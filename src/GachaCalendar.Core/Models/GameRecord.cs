namespace GachaCalendar.Core.Models;

/// <summary>
/// 单个条目（游戏）的抓取结果，字段口径对齐 gacha-calendar-core 的 Result JSON
/// 与姊妹仓库 docs/06-数据契约.md：
/// 内容字段恒在（缺失按空串）、状态字段三态（null = 正常 / nomatch / down）。
/// </summary>
public sealed class GameRecord
{
	public string Name { get; set; } = "";
	public string Banner { get; set; } = "";
	public string Roles { get; set; } = "";
	public string BannerDates { get; set; } = "";
	public string BannerDatesRaw { get; set; } = "";
	public string BannerHover { get; set; } = "";
	public string Event { get; set; } = "";
	public string EventDates { get; set; } = "";
	public string EventDatesRaw { get; set; } = "";
	public string EventHover { get; set; } = "";

	/// <summary>卡池侧状态；null = 正常。</summary>
	public SideFail? GachaFail { get; set; }

	/// <summary>活动侧状态；null = 正常。</summary>
	public SideFail? EventFail { get; set; }

	/// <summary>该列显示的是沿用的上次成功值。</summary>
	public bool GachaStale { get; set; }

	public bool EventStale { get; set; }

	/// <summary>本轮被跳过（未配置来源 / 隐藏），不计入成功数。</summary>
	public bool Skipped { get; set; }

	/// <summary>最近一次两侧都拿到新数据的时间（Unix 毫秒，0 = 从未成功）。</summary>
	public long OkAt { get; set; }

	/// <summary>
	/// 反序列化后的兜底：JSON 里显式写 null 会把内容字段置 null，
	/// 所以统一在 Parse 收口处补空串，避免下游到处判空。
	/// </summary>
	internal void Normalize()
	{
		Name = Coalesce(Name);
		Banner = Coalesce(Banner);
		Roles = Coalesce(Roles);
		BannerDates = Coalesce(BannerDates);
		BannerDatesRaw = Coalesce(BannerDatesRaw);
		BannerHover = Coalesce(BannerHover);
		Event = Coalesce(Event);
		EventDates = Coalesce(EventDates);
		EventDatesRaw = Coalesce(EventDatesRaw);
		EventHover = Coalesce(EventHover);
		GachaFail?.Normalize();
		EventFail?.Normalize();
	}

	internal static string Coalesce(string? value) => value ?? "";
}

/// <summary>某一侧的失败状态：kind = down（抓取/解析报错）或 nomatch（源站无当期）。</summary>
public sealed class SideFail
{
	public string Kind { get; set; } = "";

	/// <summary>仅 down 时有意义；缺失时 UI 文案回退到「抓取异常」。</summary>
	public string? Reason { get; set; }

	internal void Normalize()
	{
		Kind = GameRecord.Coalesce(Kind);
	}
}
