namespace GachaCalendar.Core.ViewModels;

/// <summary>
/// 一条日程的状态。口径移植自 DSH 版 <c>newPanel</c> 的日程视图
/// （<c>src/client/80-components.js</c> 的 <c>scheduleNormalize</c>），五态一一对应：
/// <list type="bullet">
///   <item><see cref="Live"/>：在期（now 落在窗口内）→ 实心</item>
///   <item><see cref="Waiting"/>：还没开始 → 空心描边</item>
///   <item><see cref="Ended"/>：已结束 → 斜纹</item>
///   <item><see cref="Long"/>：长期 / 无结束时间（解析不出窗口，或窗口超过一年）→ 点线</item>
///   <item><see cref="Unknown"/>：该侧无数据（<see cref="ScheduleBuilder.Build"/> 不会产出，
///         留着给外壳在"格子里没有条目"时表达"无数据"）</item>
/// </list>
/// </summary>
public enum ScheduleState
{
	Live,
	Waiting,
	Ended,
	Long,
	Unknown,
}

/// <summary>
/// 一条日程 = 某款游戏的<strong>卡池侧或活动侧</strong>（一款游戏最多两条）。
/// 纯数据、无 UI 类型：颜色是 <c>#RRGGBB</c> 字符串，图标是 URL，由外壳自己决定怎么渲染。
/// </summary>
/// <remarks>
/// 三条互不抢的视觉分工（与 DSH 版一致，外壳照这个渲染）：
/// <list type="bullet">
///   <item><b>颜色 = 游戏</b>：<see cref="Color"/></item>
///   <item><b>图案 = 状态</b>：<see cref="State"/></item>
///   <item><b>文字 = 紧迫度</b>：<see cref="UrgencyText"/>（<see cref="UrgencyLevel"/> 决定文字色档位）</item>
/// </list>
/// </remarks>
public sealed class ScheduleItem
{
	public string GameId { get; init; } = "";

	public string GameName { get; init; } = "";

	public string IconUrl { get; init; } = "";

	/// <summary>游戏代表色 <c>#RRGGBB</c>：catalog 的 <see cref="Models.CatalogEntry.Color"/> 优先，空则按游戏名哈希色盘。</summary>
	public string Color { get; init; } = "";

	/// <summary><c>"gacha"</c>（卡池）或 <c>"event"</c>（活动）。</summary>
	public string Side { get; init; } = "";

	public bool IsGacha { get; init; }

	/// <summary>卡池侧优先「去修饰的角色名」，否则池名；活动侧为活动名。</summary>
	public string Title { get; init; } = "";

	/// <summary>外显档期短文本（<c>bannerDates</c> / <c>eventDates</c>）。</summary>
	public string DatesText { get; init; } = "";

	/// <summary>悬停文本：<see cref="Title"/> + 换行 + 源站原文（<c>bannerDatesRaw</c> / <c>eventDatesRaw</c>）。</summary>
	public string RawTip { get; init; } = "";

	public ScheduleState State { get; init; }

	/// <summary>解析出的窗口起点；解析不出为 <c>null</c>（长期 / 无数据）。</summary>
	public DateTime? Start { get; init; }

	/// <summary>解析出的窗口终点；解析不出为 <c>null</c>。</summary>
	public DateTime? End { get; init; }

	/// <summary>紧迫度文案：今天到期 / 还剩 X 天 / MM-DD HH:mm 开始 / 已结束 / 长期 / 空串。</summary>
	public string UrgencyText { get; init; } = "";

	/// <summary>0=普通 1=24 小时内 2=7 天内 3=未开始（外壳按它上文字色）。</summary>
	public int UrgencyLevel { get; init; }

	/// <summary>在期 / 已结束 / 长期 / 未开始 / 无数据。</summary>
	public string StateText { get; init; } = "";

	/// <summary>在期时的剩余毫秒（其余状态恒 0，与 DSH 的 <c>remain</c> 同口径）。</summary>
	public long RemainingMs { get; init; }
}

/// <summary>
/// 一次构建出的完整日程快照：全部条目 + 「近期」+ 「下一个截止」+ 14 天迷你条要的时间窗口。
/// 由 <see cref="ScheduleBuilder.Build"/> 产出；派生字段在构造时一次算好（纯只读）。
/// </summary>
public sealed class ScheduleSnapshot
{
	private const long DueMs = 24L * 60 * 60 * 1000;
	private const long WeekMs = 7 * 24L * 60 * 60 * 1000;

	/// <summary>
	/// 从条目列表算出全部派生字段。
	/// </summary>
	/// <param name="items">已按「游戏顺序 × 卡池/活动」排好的全部日程条目。</param>
	/// <param name="now">计算紧迫度的基准时间（与构建条目时用同一个，保证自洽）。</param>
	/// <param name="recentLimit">「近期」最多几条（默认 6）；&lt;=0 表示不列。</param>
	public ScheduleSnapshot(IReadOnlyList<ScheduleItem> items, DateTime now, int recentLimit = ScheduleBuilder.DefaultRecentLimit)
	{
		Items = items?.ToArray() ?? Array.Empty<ScheduleItem>();

		var limit = Math.Max(0, recentLimit);
		var live = new List<ScheduleItem>();
		foreach (var item in Items)
		{
			if (item.State == ScheduleState.Live) live.Add(item);
		}

		// End 升序；同 End 再按 GameId / Side 兜底，保证跨次构建顺序稳定（UI 不会莫名换行）。
		live.Sort(CompareByEnd);

		LiveCount = live.Count;
		Recent = limit == 0 || live.Count == 0
			? Array.Empty<ScheduleItem>()
			: live.Take(limit).ToArray();
		NextUp = live.Count > 0 ? live[0] : null;
		NextTwo = live.Count > 1
			? live.Skip(1).Take(2).ToArray()
			: Array.Empty<ScheduleItem>();

		var within24 = 0;
		var within7 = 0;
		foreach (var item in live)
		{
			if (item.End is not { } end) continue;
			var remain = (end - now).TotalMilliseconds;
			if (remain <= DueMs) within24++;
			if (remain <= WeekMs) within7++;
		}

		// 格式严格（n/m 为 0 也照写）："24 小时内 3 条 · 7 天内 22 条"
		GlanceText = $"24 小时内 {within24} 条 · 7 天内 {within7} 条";
	}

	/// <summary>本次可见的全部日程条目（「游戏顺序 × 卡池/活动」）。</summary>
	public IReadOnlyList<ScheduleItem> Items { get; }

	/// <summary>跨游戏的「近期」：只取在期条目，按 <see cref="ScheduleItem.End"/> 升序，最多 recentLimit 条。</summary>
	public IReadOnlyList<ScheduleItem> Recent { get; }

	/// <summary>下一个截止：在期条目里 End 最小的一条（跨游戏）；没有在期条目时为 null。</summary>
	public ScheduleItem? NextUp { get; }

	/// <summary>NextUp 之后最近的 2 条（不含 NextUp，只取在期）。</summary>
	public IReadOnlyList<ScheduleItem> NextTwo { get; }

	/// <summary>总览一行：<c>"24 小时内 N 条 · 7 天内 M 条"</c>。</summary>
	public string GlanceText { get; }

	public int LiveCount { get; }

	/// <summary>
	/// 值相等：条目逐字段比 + <see cref="GlanceText"/>。
	/// 供视图模型判断"这一分钟的快照与上次有没有变"，避免每分钟空发 PropertyChanged。
	/// </summary>
	internal static bool SameValue(ScheduleSnapshot? a, ScheduleSnapshot? b)
	{
		if (ReferenceEquals(a, b)) return true;
		if (a == null || b == null) return false;
		if (a.Items.Count != b.Items.Count) return false;
		if (!string.Equals(a.GlanceText, b.GlanceText, StringComparison.Ordinal)) return false;

		for (var i = 0; i < a.Items.Count; i++)
		{
			var x = a.Items[i];
			var y = b.Items[i];
			if (!string.Equals(x.GameId, y.GameId, StringComparison.Ordinal)) return false;
			if (!string.Equals(x.Side, y.Side, StringComparison.Ordinal)) return false;
			if (x.IsGacha != y.IsGacha) return false;
			if (x.State != y.State) return false;
			if (x.UrgencyLevel != y.UrgencyLevel) return false;
			if (x.RemainingMs != y.RemainingMs) return false;
			if (x.Start != y.Start || x.End != y.End) return false;
			if (!string.Equals(x.Title, y.Title, StringComparison.Ordinal)) return false;
			if (!string.Equals(x.UrgencyText, y.UrgencyText, StringComparison.Ordinal)) return false;
			if (!string.Equals(x.StateText, y.StateText, StringComparison.Ordinal)) return false;
			if (!string.Equals(x.Color, y.Color, StringComparison.Ordinal)) return false;
		}

		return true;
	}

	private static int CompareByEnd(ScheduleItem a, ScheduleItem b)
	{
		var byEnd = Nullable.Compare(a.End, b.End);
		if (byEnd != 0) return byEnd;
		var byGame = string.CompareOrdinal(a.GameId, b.GameId);
		return byGame != 0 ? byGame : string.CompareOrdinal(a.Side, b.Side);
	}
}
