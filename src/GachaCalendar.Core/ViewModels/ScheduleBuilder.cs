using System.Globalization;
using System.Text;
using GachaCalendar.Core.Models;

namespace GachaCalendar.Core.ViewModels;

/// <summary>
/// 把 feed + catalog + settings 编译成「日程视图」要用的条目 / 近期列表 / 下一个截止 / 总览计数。
/// 口径逐条对齐 DSH 版 <c>newPanel</c> 的日程视图（<c>src/client/80-components.js</c> 的
/// <c>scheduleNormalize</c> / <c>scheduleWhen</c> / <c>scheduleUrgency</c> / <c>scheduleGameColor</c>），
/// 只做数据层，不含任何 UI 类型 —— 外壳负责把
/// <b>颜色=游戏 / 图案=状态 / 文字=紧迫度</b> 画出来。
/// </summary>
/// <remarks>
/// 两个刻意的取舍（与 DSH 一致，改之前先读）：
/// <list type="number">
///   <item><b>不按紧迫度重排条目</b>：<see cref="ScheduleSnapshot.Items"/> 是「用户设置的顺序 × 卡池/活动」，
///         紧迫度排序只用于 <see cref="ScheduleSnapshot.Recent"/> / <see cref="ScheduleSnapshot.NextUp"/>。</item>
///   <item><b>不按 settings.ShowGacha / ShowEvent 过滤</b>：两列开关属于渲染层（<see cref="RowVm"/> 已各自带开关），
///         快照两头都产出，外壳按需隐藏。</item>
/// </list>
/// </remarks>
public static class ScheduleBuilder
{
	/// <summary>「近期」默认条数（DSH 的近期列表默认一屏条数）。</summary>
	public const int DefaultRecentLimit = 6;

	private const long DueMs = 24L * 60 * 60 * 1000;

	private const long WeekMs = 7 * 24L * 60 * 60 * 1000;

	/// <summary>窗口超过一年 = 长期档期（pjsk 那种 2099 哨兵）。与 DSH 的 <c>SCHEDULE_LONG_MS</c> 同值。</summary>
	private const long LongWindowMs = 366L * 24 * 60 * 60 * 1000;

	/// <summary>
	/// 没有 color 的条目（自定义条目）按<b>游戏名哈希</b>落到这组固定色盘：同一款永远同色。
	/// 顺序与 DSH 的 <c>SCHEDULE_PALETTE</c> 完全一致（外壳/截图对比靠它对齐）。
	/// </summary>
	private static readonly string[] Palette =
	{
		"#E0709F", "#3FA9A0", "#E0A93B", "#4A6CF7", "#7A5AF8", "#2FB3C9", "#E06C4A", "#5C9E3F",
	};

	/// <summary>
	/// 构建一次日程快照。可见性 / 排序复用 <see cref="WidgetViewModel.VisibleOrdered"/>
	/// （hidden / shown / removed / order + catalog 默认顺序），保证与面板表格逐行一致。
	/// </summary>
	public static ScheduleSnapshot Build(
		FeedDocument feed,
		GameCatalog catalog,
		WidgetSettings settings,
		DateTime now,
		int recentLimit = DefaultRecentLimit)
	{
		ArgumentNullException.ThrowIfNull(feed);
		ArgumentNullException.ThrowIfNull(catalog);
		settings ??= new WidgetSettings();

		var items = new List<ScheduleItem>();
		foreach (var game in WidgetViewModel.VisibleOrdered(catalog, settings))
		{
			var record = feed.Record(game.Id);
			// feed 里没有这款游戏的记录 = 两侧都没有任何内容（表头状态负责汇总失败，这里不出条目）。
			if (record == null) continue;

			var gacha = BuildSide(game, record, "gacha", now);
			if (gacha != null) items.Add(gacha);

			var @event = BuildSide(game, record, "event", now);
			if (@event != null) items.Add(@event);
		}

		return new ScheduleSnapshot(items, now, recentLimit);
	}

	/// <summary>
	/// 某款游戏某一侧的日程条目；<c>null</c> = 这一侧没有内容（不占格子）。
	/// </summary>
	private static ScheduleItem? BuildSide(CatalogEntry game, GameRecord record, string side, DateTime now)
	{
		var isGacha = side == "gacha";

		// 卡池侧：角色名优先（必须走 RowBuilder.CleanRoleNames，BA 的换装括号特例不能丢），否则池名。
		var title = isGacha
			? RowBuilder.CleanRoleNames(record.Roles, game.Id)
			: record.Event;
		if (isGacha && string.IsNullOrWhiteSpace(title)) title = record.Banner;

		var datesText = isGacha ? record.BannerDates : record.EventDates;
		// 悬停一律给源站墙钟原文；raw 为空时回落到外显文本（与 DSH 的 `datesRaw || rawSrc` 同口径）。
		var datesRaw = isGacha
			? FirstNonEmpty(record.BannerDatesRaw, record.BannerDates)
			: FirstNonEmpty(record.EventDatesRaw, record.EventDates);

		// 只有「有标题或时间文本」才出条目 —— `*Fail.kind == "down"` 且无内容的情况自然被这条挡掉
		// （失败由表头状态汇总体现，不混进日程列表）。
		if (string.IsNullOrWhiteSpace(title)
			&& string.IsNullOrWhiteSpace(datesText)
			&& string.IsNullOrWhiteSpace(datesRaw))
		{
			return null;
		}

		// 状态判定一律复用 RowBuilder.ParseRange（跨年 / 年份候选的坑只在那里修），不另写解析。
		var window = RowBuilder.ParseRange(datesText, now);
		var state = ClassifyState(window, now);
		var start = window?.Start;
		var end = window?.End;
		var remain = state == ScheduleState.Live && end is { } liveEnd
			? Math.Max(0L, (long)(liveEnd - now).TotalMilliseconds)
			: 0L;

		return new ScheduleItem
		{
			GameId = game.Id,
			GameName = game.Name,
			IconUrl = game.Icon,
			Color = GameColor(game.Color, game.Name),
			Side = side,
			IsGacha = isGacha,
			Title = title,
			DatesText = datesText,
			RawTip = BuildRawTip(title, datesRaw),
			State = state,
			Start = start,
			End = end,
			UrgencyText = FormatUrgency(state, now, start, end),
			UrgencyLevel = UrgencyLevelOf(state, now, end),
			StateText = StateTextOf(state),
			RemainingMs = remain,
		};
	}

	// ---------------------------------------------------------------- 状态

	/// <summary>
	/// 窗口 → 状态。<c>null</c> 窗口 = 长期（DSH 把"长期 / 无结束时间"并成同一档）；
	/// 窗口超过 366 天也算长期；其余按 now 与窗口的相对位置。
	/// </summary>
	public static ScheduleState ClassifyState((DateTime Start, DateTime End)? window, DateTime now)
	{
		if (window is not { } w) return ScheduleState.Long;
		var span = w.End - w.Start;
		if (span.TotalMilliseconds > LongWindowMs) return ScheduleState.Long;
		if (now < w.Start) return ScheduleState.Waiting;
		if (now > w.End) return ScheduleState.Ended;
		return ScheduleState.Live;
	}

	/// <summary>状态 → 文案：在期 / 未开始 / 已结束 / 长期 / 无数据。</summary>
	public static string StateTextOf(ScheduleState state)
		=> state switch
		{
			ScheduleState.Live => "在期",
			ScheduleState.Waiting => "未开始",
			ScheduleState.Ended => "已结束",
			ScheduleState.Long => "长期",
			_ => "无数据",
		};

	// ---------------------------------------------------------------- 紧迫度

	/// <summary>
	/// 紧迫度文案（只有文字表达紧迫度，颜色留给"认游戏"）：
	/// 在期 → 今天到期（剩余 &lt; 24h）/ 还剩 X 天；未开始 → <c>MM-DD HH:mm 开始</c>；
	/// 已结束 → 已结束；长期 → 长期；其余 → 空串。
	/// </summary>
	public static string FormatUrgency(ScheduleState state, DateTime now, DateTime? start, DateTime? end)
	{
		switch (state)
		{
			case ScheduleState.Live:
				if (end is not { } liveEnd) return "";
				var remain = liveEnd - now;
				return remain.TotalMilliseconds < DueMs
					? "今天到期"
					: "还剩 " + FormatRemainingText(remain);

			case ScheduleState.Waiting:
				return start is { } startAt
					? startAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " 开始"
					: "";

			case ScheduleState.Ended:
				return "已结束";

			case ScheduleState.Long:
				return "长期";

			default:
				return "";
		}
	}

	/// <summary>
	/// 文字色档位：0=普通 1=24 小时内到期 2=7 天内 3=未开始。
	/// 边界都取<b>严格小于</b>（正好 24h → 2 档、正好 7d → 普通），与 DSH 的 <c>&lt;=</c> 判断在整点上的观感一致。
	/// </summary>
	public static int UrgencyLevelOf(ScheduleState state, DateTime now, DateTime? end)
	{
		if (state == ScheduleState.Waiting) return 3;
		if (state != ScheduleState.Live || end is not { } liveEnd) return 0;
		var remain = (liveEnd - now).TotalMilliseconds;
		if (remain < DueMs) return 1;
		return remain < WeekMs ? 2 : 0;
	}

	/// <summary>
	/// 倒计时文本（不含前缀，"还剩 " / "之后开始" 由调用方写）：
	/// 有整日写天、否则整小时写小时、否则写分钟（不足 1 分钟写 0 分钟）。
	/// </summary>
	public static string FormatRemainingText(TimeSpan remain)
	{
		if (remain <= TimeSpan.Zero) return "0 分钟";
		var minutes = (long)Math.Floor(remain.TotalMinutes);
		if (minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + " 分钟";
		if (remain.TotalDays < 1)
			return ((long)Math.Floor(remain.TotalHours)).ToString(CultureInfo.InvariantCulture) + " 小时";
		return ((long)Math.Floor(remain.TotalDays)).ToString(CultureInfo.InvariantCulture) + " 天";
	}

	// ---------------------------------------------------------------- 颜色 / 悬停

	/// <summary>
	/// 游戏代表色：catalog 自带 <paramref name="catalogColor"/> 优先（非空即用，不回验格式），
	/// 空则按<b>游戏名哈希</b>落到固定色盘（同名恒同色）。
	/// </summary>
	public static string GameColor(string? catalogColor, string? gameName)
	{
		var own = catalogColor?.Trim() ?? "";
		if (own.Length > 0) return own;

		uint hash = 0;
		foreach (var rune in (gameName ?? "").EnumerateRunes())
			hash = unchecked(hash * 31u + (uint)rune.Value);
		return Palette[hash % (uint)Palette.Length];
	}

	private static string BuildRawTip(string title, string datesRaw)
	{
		if (string.IsNullOrWhiteSpace(datesRaw)) return title;
		if (string.IsNullOrWhiteSpace(title)) return datesRaw;
		return title + "\n" + datesRaw;
	}

	private static string FirstNonEmpty(string? preferred, string? fallback)
		=> !string.IsNullOrWhiteSpace(preferred) ? preferred! : (fallback ?? "");
}
