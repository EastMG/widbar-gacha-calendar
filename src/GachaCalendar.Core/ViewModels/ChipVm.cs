namespace GachaCalendar.Core.ViewModels;

/// <summary>
/// 任务栏预览条上的一个 chip（一款游戏当前最快结束的一项）。
/// 不含画刷：<see cref="AccentIndex"/> 只给"第几号强调色"，具体色值由外壳主题决定。
/// </summary>
public sealed class ChipVm
{
	public string GameId { get; init; } = "";

	/// <summary>展示名：超过 9 字符时截断为 8 字符 + …（与 WPF/JS 口径一致）。</summary>
	public string GameName { get; init; } = "";

	/// <summary>完整游戏名（tooltip 用）。</summary>
	public string FullName { get; init; } = "";

	/// <summary>卡池 / 活动。</summary>
	public string Kind { get; init; } = "";

	/// <summary>短倒计时：X天 / X时 / X分。</summary>
	public string Remaining { get; init; } = "";

	/// <summary>多行 tooltip：游戏名 / 类别：池名或活动名 / 时间 / 还剩 X天X小时X分钟。</summary>
	public string Tip { get; init; } = "";

	/// <summary>强调色索引 0..5（外壳按主题取色）。</summary>
	public int AccentIndex { get; init; }

	/// <summary>内容标题：卡池=UP 角色（BA 等特例已清洗），活动=活动名。</summary>
	public string Title { get; init; } = "";

	/// <summary>true=卡池，false=活动（外壳据此换左侧图案与颜色）。</summary>
	public bool IsGacha { get; init; }

	/// <summary>游戏代表色 #RRGGBB（来自 catalog；空串时外壳按游戏名哈希兜底）。</summary>
	public string Color { get; init; } = "";

	/// <summary>紧迫度：1 = 24 小时内，2 = 7 天内，0 = 更远（外壳据此给倒计时上色）。</summary>
	public int UrgencyLevel { get; init; }

	/// <summary>截止日期短文案：今天 / 明天 / MM-dd。</summary>
	public string Deadline { get; init; } = "";

	/// <summary>游戏图标 URL（任务栏 chip 用图标代替文字承担"是哪款游戏"）。</summary>
	public string IconUrl { get; init; } = "";

	/// <summary>类型图案：◆ 卡池 / ⚑ 活动（任务栏上不占文字宽度也能分辨）。</summary>
	public string SideGlyph => IsGacha ? "◆" : "⚑";
}
