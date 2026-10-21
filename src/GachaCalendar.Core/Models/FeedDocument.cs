using System.Text.Json;

namespace GachaCalendar.Core.Models;

/// <summary>
/// 数据源文档：gacha-calendar-core 的 Result JSON + app 仓库附加的 meta。
/// 形状（2026-10-21 实测）：
/// { schemaVersion, refreshedAt, parserVersions, games{id: GameRecord},
///   meta{generatedAt(ISO), okCount, failCount, total, generator} }
/// </summary>
public sealed class FeedDocument
{
	public int SchemaVersion { get; set; }

	/// <summary>本轮抓取完成时间（Unix 毫秒，0 = 未联网）。</summary>
	public long RefreshedAt { get; set; }

	public Dictionary<string, int> ParserVersions { get; set; } = new();

	public Dictionary<string, GameRecord> Games { get; set; } = new();

	public FeedMeta? Meta { get; set; }

	/// <summary>更新时间：优先 refreshedAt，其次 meta.generatedAt（ISO 文本）。</summary>
	public DateTimeOffset? LastUpdatedUtc
	{
		get
		{
			if (RefreshedAt > 0) return DateTimeOffset.FromUnixTimeMilliseconds(RefreshedAt);
			return Meta?.GeneratedAtUtc;
		}
	}

	public GameRecord? Record(string? id)
	{
		if (string.IsNullOrWhiteSpace(id)) return null;
		return Games.TryGetValue(id!, out var rec) ? rec : null;
	}

	public static FeedDocument? Parse(string? json)
		=> TryParse(json, out var document, out _) ? document : null;

	public static bool TryParse(string? json, out FeedDocument? document, out string? error)
	{
		document = null;
		error = null;
		if (string.IsNullOrWhiteSpace(json))
		{
			error = "feed JSON 为空";
			return false;
		}

		try
		{
			var parsed = JsonSerializer.Deserialize<FeedDocument>(json, JsonDefaults.Read);
			if (parsed == null)
			{
				error = "feed JSON 反序列化结果为空";
				return false;
			}

			// 兼容"缺字段"：字典 / meta / 每条记录都补默认值，下游不再判空。
			parsed.ParserVersions ??= new();
			parsed.Games ??= new();
			foreach (var record in parsed.Games.Values) record?.Normalize();
			document = parsed;
			return true;
		}
		catch (JsonException ex)
		{
			error = "feed JSON 解析失败：" + ex.Message;
			return false;
		}
	}
}

/// <summary>app 仓库抓取流水线附加的元信息；仅用于诊断，不参与 UI 逻辑。</summary>
public sealed class FeedMeta
{
	/// <summary>生成时间（ISO 8601 文本，例如 2026-10-21T03:53:54.055Z）。</summary>
	public string GeneratedAt { get; set; } = "";

	public int OkCount { get; set; }
	public int FailCount { get; set; }
	public int Total { get; set; }
	public string Generator { get; set; } = "";

	public DateTimeOffset? GeneratedAtUtc
	{
		get
		{
			if (string.IsNullOrWhiteSpace(GeneratedAt)) return null;
			return DateTimeOffset.TryParse(
				GeneratedAt,
				System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
				out var value)
				? value
				: null;
		}
	}
}
