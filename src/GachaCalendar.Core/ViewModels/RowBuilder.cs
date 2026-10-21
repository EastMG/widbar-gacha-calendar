using System.Text.RegularExpressions;
using GachaCalendar.Core.Models;

namespace GachaCalendar.Core.ViewModels;

/// <summary>一行的全部展示文案（外壳只负责排版，不再做逻辑）。</summary>
public sealed record RowData(
	string Name,
	string BadgeText,
	string BadgeKind,
	bool HasBadge,
	string GachaText,
	string GachaTip,
	string GachaDateText,
	string GachaDateTip,
	string EventText,
	string EventTip,
	string EventDateText,
	string EventDateTip,
	DateTime? ActiveEnd,
	string ActiveKind);

/// <summary>
/// 展示层纯函数：把 core 的 Result JSON 翻译成「这一行显示什么」。
/// 逐条移植自姊妹仓库 WPF 版
/// <c>gacha-calendar/src/GachaCalendar/ViewModels/RowBuilder.cs</c>，
/// 行为（含时间窗口跨年选择、失败三态、角色名精简）必须与它一致，测试锁住。
/// </summary>
public static class RowBuilder
{
	private static readonly string[] BaRoleIds = { "ba-cn", "ba-global", "ba-jp" };

	public static RowData Build(CatalogEntry game, GameRecord? rec, DateTime now)
	{
		rec ??= new GameRecord();
		var gachaText = !string.IsNullOrWhiteSpace(rec.Roles)
			? CleanRoleNames(rec.Roles, game.Id)
			: (!string.IsNullOrWhiteSpace(rec.Banner) ? rec.Banner : "—");
		var eventText = !string.IsNullOrWhiteSpace(rec.Event) ? rec.Event : "—";

		var gachaTip = WithFailNote(BuildGachaTip(rec), "gacha", rec.GachaFail, rec.GachaStale);
		var gachaDateTip = WithFailNote(RawOrDisplay(rec.BannerDatesRaw, rec.BannerDates), "gacha", rec.GachaFail, rec.GachaStale);
		var eventTip = WithFailNote(BuildEventTip(rec), "event", rec.EventFail, rec.EventStale);
		var eventDateTip = WithFailNote(RawOrDisplay(rec.EventDatesRaw, rec.EventDates), "event", rec.EventFail, rec.EventStale);

		var (badgeText, badgeKind, hasBadge) = BuildBadge(rec);

		var bannerWindow = ParseRange(rec.BannerDates, now);
		var eventWindow = ParseRange(rec.EventDates, now);
		DateTime? activeEnd = null;
		var activeKind = "";
		if (bannerWindow is { } bw && now >= bw.Start && now <= bw.End)
		{
			activeEnd = bw.End;
			activeKind = "卡池";
		}

		if (eventWindow is { } ew && now >= ew.Start && now <= ew.End && (activeEnd == null || ew.End < activeEnd))
		{
			activeEnd = ew.End;
			activeKind = "活动";
		}

		return new RowData(
			game.Name,
			badgeText,
			badgeKind,
			hasBadge,
			gachaText,
			gachaTip,
			DisplayTimeCell(rec.BannerDates, now),
			gachaDateTip,
			eventText,
			eventTip,
			DisplayTimeCell(rec.EventDates, now),
			eventDateTip,
			activeEnd,
			activeKind);
	}

	// ---------------------------------------------------------------- 时间

	/// <summary>
	/// 解析源站墙钟文本 <c>MM-dd HH:mm ~ MM-dd HH:mm</c>（年份缺失）。
	/// 跨年（结束早于开始）把结束算到下一年；再同时试 去年/今年/明年，
	/// 取离 now 最近的一个窗口，避免 12 月/1 月附近把"明年才开"误判成"已结束"。
	/// </summary>
	public static (DateTime Start, DateTime End)? ParseRange(string? raw, DateTime now)
	{
		if (string.IsNullOrWhiteSpace(raw)) return null;
		var parts = raw.Split('~');
		if (parts.Length < 2) return null;
		var a = ParsePoint(parts[0].Trim(), now);
		var b = ParsePoint(parts[1].Trim(), now);
		if (a == null || b == null) return null;

		var start = a.Value;
		var end = b.Value;
		if (end < start) end = AddYearsSafe(end, 1);

		var bestStart = start;
		var bestEnd = end;
		var bestDistance = long.MaxValue;
		foreach (var shift in new[] { -1, 0, 1 })
		{
			if (!TryAddYears(start, shift, out var s) || !TryAddYears(end, shift, out var e)) continue;
			var distance = now < s ? (s - now).Ticks : now > e ? (now - e).Ticks : 0;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestStart = s;
				bestEnd = e;
			}
		}

		return (bestStart, bestEnd);
	}

	private static DateTime? ParsePoint(string text, DateTime now)
	{
		var m = Regex.Match(text, @"^(\d{1,2})-(\d{1,2})(?:\s+(\d{1,2}):(\d{2}))?$");
		if (!m.Success) return null;
		var mo = int.Parse(m.Groups[1].Value);
		var d = int.Parse(m.Groups[2].Value);
		if (mo is < 1 or > 12 || d is < 1 or > 31) return null;
		var h = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
		var mi = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
		try
		{
			return new DateTime(now.Year, mo, d, h, mi, 0);
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	/// <summary>
	/// 跨年修正时用：2 月 29 日往非闰年平移会抛异常，此时保持原值（原 WPF 版会崩，
	/// 这里只做健壮性兜底，正常数据行为不变）。
	/// </summary>
	private static DateTime AddYearsSafe(DateTime value, int years)
		=> TryAddYears(value, years, out var result) ? result : value;

	private static bool TryAddYears(DateTime value, int years, out DateTime result)
	{
		try
		{
			result = value.AddYears(years);
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			result = value;
			return false;
		}
	}

	public static string FormatRemaining(DateTime target, DateTime now)
	{
		var diff = target - now;
		if (diff < TimeSpan.Zero) return "";
		var days = (int)Math.Floor(diff.TotalDays);
		var hours = diff.Hours;
		var mins = diff.Minutes;
		return $"{days}天{hours}小时{mins}分钟";
	}

	/// <summary>任务栏常驻条用的短倒计时：只保留最有信息量的一档。</summary>
	public static string FormatShortRemaining(TimeSpan diff)
	{
		if (diff <= TimeSpan.Zero) return "已结束";
		if (diff.TotalDays >= 1) return $"{(int)Math.Floor(diff.TotalDays)}天";
		if (diff.TotalHours >= 1) return $"{(int)Math.Floor(diff.TotalHours)}时";
		return $"{Math.Max(1, (int)Math.Floor(diff.TotalMinutes))}分";
	}

	public static string DisplayTimeCell(string? raw, DateTime now)
	{
		var window = ParseRange(raw, now);
		if (window == null) return raw ?? "";
		var (start, end) = window.Value;
		if (now < start)
		{
			var t = FormatRemaining(start, now);
			return t.Length == 0 ? raw ?? "" : $"还有 {t} 开始";
		}

		if (now > end) return "已结束";
		var remain = FormatRemaining(end, now);
		return remain.Length == 0 ? raw ?? "" : $"还剩 {remain}";
	}

	// ---------------------------------------------------------------- 文案

	public static string CleanRoleNames(string? roles, string gameId)
	{
		if (string.IsNullOrWhiteSpace(roles)) return "";
		var isBa = BaRoleIds.Contains(gameId);
		var list = new List<string>();
		foreach (var raw in Regex.Split(roles, @"[、,，]"))
		{
			var s = Regex.Replace(raw, @"[「」【】]", "");
			if (isBa) s = s.Replace('（', '(').Replace('）', ')');
			else s = Regex.Replace(s, @"[（(][^）)]*[）)]", "");
			s = s.Trim();
			if (s.Length == 0) continue;

			// 称号·名字 → 取 · 后面的名字（BA 的「乐队」括号已在上面保留）
			var m1 = Regex.Match(s, @"^([\u4e00-\u9fff]{2,10})·(.+)$");
			if (m1.Success && m1.Groups[2].Value.Trim().Length > 0)
			{
				s = m1.Groups[2].Value.Trim();
			}
			else
			{
				// 4 字前缀 + • 的名字（例：虚无之刃•主角）
				var m2 = Regex.Match(s, @"^([\u4e00-\u9fff]{4})•(.+)$");
				if (m2.Success) s = m2.Groups[2].Value.Trim();
			}

			if (s.Length > 0) list.Add(s);
		}

		return string.Join("、", list);
	}

	private static string BuildGachaTip(GameRecord rec)
	{
		var head = !string.IsNullOrWhiteSpace(rec.Roles) ? $"{rec.Banner}：{rec.Roles}" : rec.Banner;
		if (!string.IsNullOrWhiteSpace(rec.BannerDates))
			head = string.IsNullOrEmpty(head)
				? RawOrDisplay(rec.BannerDatesRaw, rec.BannerDates)
				: head + "\n" + RawOrDisplay(rec.BannerDatesRaw, rec.BannerDates);
		return head;
	}

	private static string BuildEventTip(GameRecord rec)
	{
		if (string.IsNullOrWhiteSpace(rec.Event)) return RawOrDisplay(rec.EventDatesRaw, rec.EventDates);
		if (string.IsNullOrWhiteSpace(rec.EventDates)) return rec.Event;
		return rec.Event + "\n" + RawOrDisplay(rec.EventDatesRaw, rec.EventDates);
	}

	private static string RawOrDisplay(string raw, string display) => !string.IsNullOrWhiteSpace(raw) ? raw : display;

	private static (string Text, string Kind, bool Has) BuildBadge(GameRecord rec)
	{
		var parts = new List<(string Kind, string Text)>();
		var g = NormalizeFail(rec.GachaFail);
		var e = NormalizeFail(rec.EventFail);
		if (g != null) parts.Add((g.Kind, SideFailText("gacha", g)));
		if (e != null) parts.Add((e.Kind, SideFailText("event", e)));

		// down 优先于 nomatch：有真失败就显示失败原因。
		var down = parts.FirstOrDefault(p => p.Kind == "down");
		if (down != default) return (down.Text, "err", true);
		if (parts.Count > 0) return (parts[0].Text, "warn", true);
		if (rec.GachaStale || rec.EventStale) return ("沿用旧数据", "stale", true);
		return ("", "", false);
	}

	/// <summary>把任意 kind 归一成三态之一；down 缺 reason 时补「抓取异常」。</summary>
	public static SideFail? NormalizeFail(SideFail? fail)
	{
		if (fail == null) return null;
		if (fail.Kind == "down")
		{
			if (string.IsNullOrWhiteSpace(fail.Reason)) fail.Reason = "抓取异常";
			return fail;
		}

		return fail.Kind == "nomatch" ? fail : null;
	}

	public static string SideFailText(string side, SideFail fail)
	{
		var label = side == "gacha" ? "卡池" : "活动";
		if (fail.Kind == "down") return $"{label}失败：{fail.Reason ?? "抓取异常"}";
		return side == "gacha" ? "新卡池未公布" : "新活动未公布";
	}

	/// <summary>
	/// 失败注记：只有旧数据（stale）才追加在原文本后；否则注记就是全部内容。
	/// 与 WPF 版逐字一致。
	/// </summary>
	public static string WithFailNote(string? baseText, string side, SideFail? fail, bool stale)
	{
		var f = NormalizeFail(fail);
		var text = f == null ? "" : SideFailText(side, f);
		var note = text.Length == 0 ? "" : $"（{text}）";
		if (note.Length == 0) return baseText ?? "";
		return stale && !string.IsNullOrEmpty(baseText) ? baseText + "\n" + note : note;
	}
}
