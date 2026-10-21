using System.Text.Json;

namespace GachaCalendar.Core.Models;

/// <summary>
/// widget 本地设置（由外壳负责持久化到磁盘；这里只管形状与归一化）。
/// 字段口径见任务定义：预览条数 / 自动刷新 / 隐藏、打开、删除、排序 / feed 地址 / 两列开关。
/// </summary>
public sealed class WidgetSettings
{
	public const string DefaultFeedUrl = "https://eastmg.github.io/gacha-calendar-app/data/latest.json";

	public const int MinPreviewItems = 1;
	public const int MaxPreviewItems = 3;
	public const int MinRefreshMinutes = 15;
	public const int MaxRefreshMinutes = 720;

	/// <summary>任务栏预览条同时显示几款游戏（1–3）。</summary>
	public int PreviewItems { get; set; } = 3;

	public bool AutoRefresh { get; set; } = true;

	/// <summary>自动刷新间隔（分钟，15–720）。</summary>
	public int RefreshMinutes { get; set; } = 60;

	/// <summary>用户手动隐藏的条目 id。</summary>
	public List<string> Hidden { get; set; } = new();

	/// <summary>出厂默认隐藏的条目被用户打开后记在这里。</summary>
	public List<string> Shown { get; set; } = new();

	/// <summary>用户删除的条目 id（比隐藏更强，永久不展示）。</summary>
	public List<string> Removed { get; set; } = new();

	/// <summary>展示顺序（优先级高于 catalog.order；未列出的按 catalog 顺序补后）。</summary>
	public List<string> Order { get; set; } = new();

	/// <summary>数据源地址；为空时回默认快照 URL。</summary>
	public string FeedUrl { get; set; } = DefaultFeedUrl;

	public bool ShowGacha { get; set; } = true;

	public bool ShowEvent { get; set; } = true;

	/// <summary>
	/// 归一化：clamp 数值、去重、裁掉 catalog 里不存在的 id、空 feedUrl 回默认。
	/// 传入 null 时只做 clamp / 去重 / 默认值（不裁 id），便于在 catalog 尚未加载时先规整。
	/// </summary>
	public void Normalize(GameCatalog? catalog = null)
	{
		PreviewItems = Math.Clamp(PreviewItems, MinPreviewItems, MaxPreviewItems);
		RefreshMinutes = Math.Clamp(RefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes);
		FeedUrl = string.IsNullOrWhiteSpace(FeedUrl) ? DefaultFeedUrl : FeedUrl.Trim();

		HashSet<string>? known = null;
		if (catalog != null)
		{
			known = new HashSet<string>(StringComparer.Ordinal);
			foreach (var entry in catalog.Games) known.Add(entry.Id);
		}

		Hidden = CleanIds(Hidden, known);
		Shown = CleanIds(Shown, known);
		Removed = CleanIds(Removed, known);
		Order = CleanIds(Order, known);
	}

	public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Write);

	public static WidgetSettings FromJson(string? json, GameCatalog? catalog = null)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			var fresh = new WidgetSettings();
			fresh.Normalize(catalog);
			return fresh;
		}

		WidgetSettings? settings = null;
		try
		{
			settings = JsonSerializer.Deserialize<WidgetSettings>(json, JsonDefaults.Read);
		}
		catch (JsonException)
		{
			// 设置文件坏了就用默认值，不让 widget 起不来。
		}

		settings ??= new WidgetSettings();
		settings.Hidden ??= new();
		settings.Shown ??= new();
		settings.Removed ??= new();
		settings.Order ??= new();
		settings.Normalize(catalog);
		return settings;
	}

	public static WidgetSettings Load(string path, GameCatalog? catalog = null)
	{
		try
		{
			if (File.Exists(path)) return FromJson(File.ReadAllText(path), catalog);
		}
		catch (Exception)
		{
			// 读失败按默认设置继续：设置文件不是关键路径。
		}

		var fresh = new WidgetSettings();
		fresh.Normalize(catalog);
		return fresh;
	}

	/// <summary>先写 .tmp 再替换，避免断电/崩溃留下半个 JSON。</summary>
	public void Save(string path)
	{
		var directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		var tmp = path + ".tmp";
		File.WriteAllText(tmp, ToJson(), System.Text.Encoding.UTF8);
		File.Move(tmp, path, overwrite: true);
	}

	private static List<string> CleanIds(List<string>? ids, HashSet<string>? known)
	{
		var result = new List<string>();
		if (ids == null) return result;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var raw in ids)
		{
			var id = raw?.Trim() ?? "";
			if (id.Length == 0) continue;
			if (known != null && !known.Contains(id)) continue;
			if (!seen.Add(id)) continue;
			result.Add(id);
		}

		return result;
	}
}
