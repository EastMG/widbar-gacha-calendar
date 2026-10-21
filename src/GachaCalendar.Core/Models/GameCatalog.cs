using System.Text.Json;
using GachaCalendar.Core.Services;

namespace GachaCalendar.Core.Models;

/// <summary>catalog 里的一款游戏元信息（feed 里没有这些字段，必须本地补齐）。</summary>
public sealed class CatalogEntry
{
	/// <summary>core 的稳定条目 id，同时是 feed.games 的 key。</summary>
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	/// <summary>外链图标 URL；可能为空串（外壳退化为首字/色块）。</summary>
	public string Icon { get; set; } = "";

	/// <summary>
	/// 该游戏的代表色（<c>#RRGGBB</c>，来自 core 的来源注册表），日程视图里"颜色 = 游戏"用它。
	/// 空串表示拿不到 —— 由 <see cref="ViewModels.ScheduleBuilder"/> 按游戏名哈希落到固定色盘。
	/// </summary>
	public string Color { get; set; } = "";

	/// <summary>core 默认展示顺序（0 起）。</summary>
	public int Order { get; set; }

	/// <summary>出厂默认隐藏：未出现在 settings.shown 里时不展示。</summary>
	public bool DefaultHidden { get; set; }

	/// <summary>该条目官方时区的 UTC 偏移分钟数（Asia/Shanghai = 480）。</summary>
	public int Tz { get; set; }
}

/// <summary>
/// 游戏元信息目录：编进程序集（EmbeddedResource catalog.json）作为唯一来源，
/// 同时支持磁盘覆盖（调试 / 手工测试用），见 <see cref="Load"/>。
/// </summary>
public sealed class GameCatalog
{
	private readonly Dictionary<string, CatalogEntry> _byId;

	private GameCatalog(List<CatalogEntry> games, int schemaVersion, long generatedAt, string coreVersion, string source)
	{
		Games = games;
		SchemaVersion = schemaVersion;
		GeneratedAt = generatedAt;
		CoreVersion = coreVersion;
		Source = source;
		_byId = games.ToDictionary(g => g.Id, StringComparer.Ordinal);
	}

	public int SchemaVersion { get; }
	public long GeneratedAt { get; }
	public string CoreVersion { get; }

	/// <summary>catalog 实际来自哪里（嵌入资源名 / 文件路径），排障用。</summary>
	public string Source { get; }

	/// <summary>按 order 升序排好的条目。</summary>
	public IReadOnlyList<CatalogEntry> Games { get; }

	public IReadOnlyDictionary<string, CatalogEntry> ById => _byId;

	public int Count => Games.Count;

	public bool TryGet(string? id, out CatalogEntry entry)
	{
		if (!string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id!, out var found))
		{
			entry = found;
			return true;
		}
		entry = null!;
		return false;
	}

	public CatalogEntry? Find(string? id) => TryGet(id, out var entry) ? entry : null;

	/// <summary>
	/// 加载顺序：显式路径 → 环境变量 GACHA_CALENDAR_CATALOG → 内置 EmbeddedResource。
	/// 显式路径或环境变量文件解析失败时回退内置资源（调试覆盖不应让 widget 起不来）。
	/// </summary>
	public static GameCatalog Load(string? overridePath = null)
	{
		var candidates = new List<string>();
		if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath!);
		var env = Environment.GetEnvironmentVariable("GACHA_CALENDAR_CATALOG");
		if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env!);

		foreach (var path in candidates)
		{
			try
			{
				if (!File.Exists(path)) continue;
				return FromJson(File.ReadAllText(path), Path.GetFullPath(path));
			}
			catch (Exception)
			{
				// 覆盖文件坏了就退回内置 catalog：宁可展示旧顺序，也不要整个 widget 挂掉。
			}
		}

		return FromJson(EmbeddedData.CatalogJson(), "embedded:catalog.json");
	}

	public static GameCatalog FromJson(string json, string source = "(inline)")
	{
		CatalogDocument? doc;
		try
		{
			doc = JsonSerializer.Deserialize<CatalogDocument>(json, JsonDefaults.Read);
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException($"catalog JSON 解析失败（{source}）：{ex.Message}", ex);
		}

		if (doc?.Games == null || doc.Games.Count == 0)
			throw new InvalidDataException($"catalog 为空或缺少 games（{source}）");

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var games = new List<CatalogEntry>(doc.Games.Count);
		foreach (var raw in doc.Games)
		{
			if (raw == null) continue;
			var entry = new CatalogEntry
			{
				Id = raw.Id?.Trim() ?? "",
				Name = raw.Name ?? "",
				Icon = raw.Icon ?? "",
				Color = raw.Color?.Trim() ?? "",
				Order = raw.Order,
				DefaultHidden = raw.DefaultHidden,
				Tz = raw.Tz,
			};
			if (entry.Id.Length == 0) continue;
			if (!seen.Add(entry.Id))
				throw new InvalidDataException($"catalog 里 id 重复：{entry.Id}（{source}）");
			games.Add(entry);
		}

		games.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
		return new GameCatalog(games, doc.SchemaVersion, doc.GeneratedAt, doc.CoreVersion ?? "", source);
	}

	private sealed class CatalogDocument
	{
		public int SchemaVersion { get; set; }
		public long GeneratedAt { get; set; }
		public string? CoreVersion { get; set; }
		public List<CatalogEntry>? Games { get; set; }
	}
}
