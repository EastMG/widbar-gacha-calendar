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
}
