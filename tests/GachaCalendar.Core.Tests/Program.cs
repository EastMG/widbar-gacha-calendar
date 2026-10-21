using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GachaCalendar.Core.Models;
using GachaCalendar.Core.Services;
using GachaCalendar.Core.ViewModels;

namespace GachaCalendar.Core.Tests;

/// <summary>
/// 零依赖 console 测试运行器。约定：退出码 0 = 全部通过；非 0 = 有失败（CI 门禁）。
/// 覆盖范围：catalog 覆盖率 / feed 解析 / 抓取缓存三态 / RowBuilder 逐条行为 /
/// 可见性与排序 / 顶部状态口径 / chips 选择 / 设置归一化。
/// </summary>
internal static class Program
{
	private static readonly List<string> Failures = new();
	private static int _passed;
	private static string _section = "";

	private static async Task<int> Main()
	{
		Console.OutputEncoding = Encoding.UTF8;
		try
		{
			TestCatalog();
			TestFeedParsing();
			TestParseRange();
			TestFormatRemaining();
			TestDisplayTimeCell();
			TestCleanRoleNames();
			TestBadges();
			TestRowBuilder();
			TestVisibility();
			TestStatus();
			TestStatusNotifications();
			TestChips();
			TestTick();
			TestSettings();
			TestShellApi();
			TestCatalogColor();
			TestScheduleItemShape();
			TestScheduleStates();
			TestScheduleUrgency();
			TestScheduleLists();
			TestScheduleGlance();
			TestScheduleColors();
			TestScheduleVisibility();
			TestScheduleViewModel();
			TestScheduleRealFeed();
			await TestFeedClient();
		}
		catch (Exception ex)
		{
			Failures.Add($"[未捕获异常] {ex}");
			Console.WriteLine($"[未捕获异常] {ex}");
		}

		Console.WriteLine();
		if (Failures.Count == 0)
		{
			Console.WriteLine($"✓ 全部通过：{_passed} 条断言");
			return 0;
		}

		Console.WriteLine($"✗ 失败 {Failures.Count} 条 / 通过 {_passed} 条：");
		foreach (var failure in Failures) Console.WriteLine("  - " + failure);
		return 1;
	}

	// ================================================================ 断言

	private static void Section(string name)
	{
		_section = name;
		Console.WriteLine();
		Console.WriteLine($"== {name} ==");
	}

	private static void Check(string name, bool ok, string? detail = null)
	{
		if (ok)
		{
			_passed++;
			Console.WriteLine($"  ✓ {name}");
			return;
		}

		var message = $"{_section} / {name}" + (detail == null ? "" : $"（{detail}）");
		Failures.Add(message);
		Console.WriteLine($"  ✗ {name}" + (detail == null ? "" : $"（{detail}）"));
	}

	private static void Eq<T>(string name, T expected, T actual)
		=> Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"期望 <{expected}> 实际 <{actual}>");

	private static void True(string name, bool value, string? detail = null) => Check(name, value, detail ?? "期望 true，实际 false");

	private static void False(string name, bool value) => Check(name, !value, "期望 false，实际 true");

	private static void Null(string name, object? value) => Check(name, value == null, $"期望 null，实际 <{value}>");

	private static void NotNull(string name, object? value) => Check(name, value != null, "期望非 null，实际 null");

	private static void Contains(string name, string expected, string? actual)
		=> Check(name, actual != null && actual.Contains(expected, StringComparison.Ordinal), $"期望包含 <{expected}>，实际 <{actual}>");

	private static void StartsWith(string name, string expected, string? actual)
		=> Check(name, actual != null && actual.StartsWith(expected, StringComparison.Ordinal), $"期望以 <{expected}> 开头，实际 <{actual}>");

	private static void SeqEq(string name, IEnumerable<string> expected, IEnumerable<string> actual)
	{
		var e = string.Join("、", expected);
		var a = string.Join("、", actual);
		Check(name, e == a, $"期望 [{e}] 实际 [{a}]");
	}

	private static void Throws<T>(string name, Action action) where T : Exception
	{
		try
		{
			action();
			Check(name, false, $"期望抛 {typeof(T).Name}，实际没抛");
		}
		catch (T)
		{
			Check(name, true);
		}
		catch (Exception ex)
		{
			Check(name, false, $"期望抛 {typeof(T).Name}，实际抛 {ex.GetType().Name}");
		}
	}

	// ================================================================ fixtures

	private static readonly DateTime Now = new(2026, 10, 21, 19, 8, 0);

	private static GameCatalog MakeCatalog(params (string Id, string Name, bool DefaultHidden)[] games)
		=> MakeCatalogFull(games.Select(g => (g.Id, g.Name, "", "", g.DefaultHidden)).ToArray());

	/// <summary>带代表色 / 图标的 catalog（日程快照要验颜色透传与哈希回退，默认全部可见）。</summary>
	private static GameCatalog MakeCatalogRich(params (string Id, string Name, string Color, string Icon)[] games)
		=> MakeCatalogFull(games.Select(g => (g.Id, g.Name, g.Color, g.Icon, false)).ToArray());

	private static GameCatalog MakeCatalogFull(
		params (string Id, string Name, string Color, string Icon, bool DefaultHidden)[] games)
	{
		var payload = new
		{
			schemaVersion = 1,
			generatedAt = 0L,
			coreVersion = "test",
			games = games.Select((g, i) => new
			{
				id = g.Id,
				name = g.Name,
				icon = g.Icon,
				color = g.Color,
				order = i,
				defaultHidden = g.DefaultHidden,
				tz = 480,
			}).ToArray(),
		};
		return GameCatalog.FromJson(JsonSerializer.Serialize(payload, JsonDefaults.Write));
	}

	private static FeedDocument MakeFeed(params (string Id, GameRecord Rec)[] records)
	{
		var feed = new FeedDocument { SchemaVersion = 1, RefreshedAt = 0 };
		foreach (var (id, rec) in records) feed.Games[id] = rec;
		return feed;
	}

	private static GameRecord Rec(
		string banner = "",
		string roles = "",
		string bannerDates = "",
		string bannerDatesRaw = "",
		string eventText = "",
		string eventDates = "",
		string eventDatesRaw = "",
		SideFail? gachaFail = null,
		SideFail? eventFail = null,
		bool gachaStale = false,
		bool eventStale = false,
		bool skipped = false)
		=> new()
		{
			Banner = banner,
			Roles = roles,
			BannerDates = bannerDates,
			BannerDatesRaw = bannerDatesRaw,
			Event = eventText,
			EventDates = eventDates,
			EventDatesRaw = eventDatesRaw,
			GachaFail = gachaFail,
			EventFail = eventFail,
			GachaStale = gachaStale,
			EventStale = eventStale,
			Skipped = skipped,
		};

	private static SideFail Down(string? reason) => new() { Kind = "down", Reason = reason };

	private static SideFail NoMatch() => new() { Kind = "nomatch" };

	private static string TempRoot => Path.Combine(AppContext.BaseDirectory, "test-tmp");

	private static string NewTempDir()
	{
		// 放在 bin 目录下（已被 .gitignore 的 bin/ 覆盖）：不依赖 %TEMP% 的 ACL，
		// 也能在受限沙箱 / CI 里稳定创建。
		var dir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private static void TryCleanup(string dir)
	{
		try
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
		catch
		{
			// 清理失败不影响测试结论。
		}
	}

	// ================================================================ 1. catalog

	private static void TestCatalog()
	{
		Section("catalog（嵌入资源，28 款元信息）");
		var catalog = GameCatalog.Load();
		Eq("条目总数 28", 28, catalog.Count);
		Eq("catalog schemaVersion", 1, catalog.SchemaVersion);
		Eq("core 版本", "0.12.5", catalog.CoreVersion);
		Eq("来源为嵌入资源", "embedded:catalog.json", catalog.Source);
		True("是否 28 个唯一 id", catalog.Games.Select(g => g.Id).Distinct().Count() == 28);
		True("order 为 0..27", catalog.Games.Select((g, i) => g.Order == i).All(x => x));
		True("id/name 全部非空", catalog.Games.All(g => g.Id.Length > 0 && g.Name.Length > 0));

		var hidden = catalog.Games.Where(g => g.DefaultHidden).Select(g => g.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
		SeqEq("出厂默认隐藏 5 款与 core 核对一致", new[] { "bh3", "czn", "ddlezj", "ournotes", "ournotes-global" }, hidden);
		Eq("icon 覆盖率 28/28", 28, catalog.Games.Count(g => !string.IsNullOrWhiteSpace(g.Icon)));
		Eq("genshin 时区 480", 480, catalog.Find("genshin")!.Tz);
		Eq("ba-jp 时区 540", 540, catalog.Find("ba-jp")!.Tz);
		Eq("ba-global 时区 0", 0, catalog.Find("ba-global")!.Tz);
		Eq("ddlezj 时区 +540", 540, catalog.Find("ddlezj")!.Tz);
		Eq("genshin 名称", "原神", catalog.Find("genshin")!.Name);
		Eq("hsr 名称", "崩坏：星穹铁道", catalog.Find("hsr")!.Name);
		True("TryGet 未知 id 返回 false", !catalog.TryGet("nope", out _));

		// 磁盘覆盖（调试）：临时文件只有 2 款时，Loader 应优先用它。
		var dir = NewTempDir();
		try
		{
			var path = Path.Combine(dir, "catalog.json");
			File.WriteAllText(path, """{"schemaVersion":1,"coreVersion":"override","games":[{"id":"a","name":"A","order":0},{"id":"b","name":"B","order":1}]}""", Encoding.UTF8);
			var overridden = GameCatalog.Load(path);
			Eq("磁盘覆盖生效", 2, overridden.Count);
			Eq("磁盘覆盖 coreVersion", "override", overridden.CoreVersion);
			Eq("磁盘覆盖 Source 为路径", Path.GetFullPath(path), overridden.Source);
		}
		finally
		{
			TryCleanup(dir);
		}

		var missingOverride = GameCatalog.Load(Path.Combine(TempRoot, "not-exists.json"));
		Eq("覆盖文件不存在时回退嵌入 catalog", 28, missingOverride.Count);

		Throws<InvalidDataException>("catalog id 重复必须报错", () =>
			GameCatalog.FromJson("""{"schemaVersion":1,"games":[{"id":"x","order":0},{"id":"x","order":1}]}"""));
		Throws<InvalidDataException>("catalog 无 games 必须报错", () =>
			GameCatalog.FromJson("""{"schemaVersion":1}"""));
	}

	// ================================================================ 2. feed 解析

	private static void TestFeedParsing()
	{
		Section("feed 解析（真实 sample-feed）");
		var json = EmbeddedData.SampleFeedJson();
		var feed = FeedDocument.Parse(json);
		NotNull("真实 feed 可解析", feed);
		if (feed == null) return;

		Eq("schemaVersion", 1, feed.SchemaVersion);
		True("refreshedAt 有效", feed.RefreshedAt > 0);
		Eq("28 款记录", 28, feed.Games.Count);
		NotNull("meta 存在", feed.Meta);
		Eq("meta.total", 28, feed.Meta!.Total);
		Eq("meta.okCount", 23, feed.Meta.OkCount);
		Eq("meta.failCount", 3, feed.Meta.FailCount);
		Contains("meta.generator", "refresh-data", feed.Meta.Generator);
		NotNull("LastUpdatedUtc 可算", feed.LastUpdatedUtc);

		var genshin = feed.Games["genshin"];
		Eq("genshin 名称", "原神", genshin.Name);
		Contains("genshin banner", "虚星临渡", genshin.Banner);
		Contains("genshin roles", "丝柯克", genshin.Roles);
		Eq("genshin 卡池时间", "10-13 18:00 ~ 11-03 14:59", genshin.BannerDates);
		Null("genshin 无失败状态", genshin.GachaFail);

		Eq("uma-global 活动失败 kind", "down", feed.Games["uma-global"].EventFail?.Kind);
		Eq("uma-global 活动失败 reason", "HTTP 403", feed.Games["uma-global"].EventFail?.Reason);
		Eq("kedr 卡池 nomatch", "nomatch", feed.Games["kedr"].GachaFail?.Kind);
		Eq("pjsk 活动 nomatch", "nomatch", feed.Games["pjsk"].EventFail?.Kind);
		Eq("parserVersions[genshin]", 1, feed.ParserVersions["genshin"]);
		Eq("parserVersions[nte]", 3, feed.ParserVersions["nte"]);

		var nullContent = feed.Games.Values.Count(r =>
			r.Name == null || r.Banner == null || r.Roles == null || r.BannerDates == null ||
			r.BannerDatesRaw == null || r.BannerHover == null || r.Event == null ||
			r.EventDates == null || r.EventDatesRaw == null || r.EventHover == null);
		Eq("内容字段没有 null（缺失按空串）", 0, nullContent);

		// 兼容性：未知字段忽略、缺失/显式 null 内容字段按空串、缺失状态字段按 null。
		var ok = FeedDocument.TryParse(
			"""{"schemaVersion":1,"futureTopLevel":true,"games":{"x":{"name":null,"futureField":123,"bannerDates":"10-13 ~ 11-03"}}}""",
			out var minimal,
			out var minimalError);
		True("兼容性 JSON 可解析", ok && minimal != null, minimalError);
		Eq("显式 null 的 name 归一为空串", "", minimal!.Games["x"].Name);
		Eq("缺失的 banner 归一为空串", "", minimal.Games["x"].Banner);
		Null("缺失的 gachaFail 为 null", minimal.Games["x"].GachaFail);
		False("缺失的 skipped 为 false", minimal.Games["x"].Skipped);
		Eq("未知字段被忽略", "10-13 ~ 11-03", minimal.Games["x"].BannerDates);

		var badOk = FeedDocument.TryParse("{ not json", out _, out var badError);
		False("坏 JSON 解析失败", badOk);
		NotNull("坏 JSON 有错误信息", badError);
		False("空 JSON 解析失败", FeedDocument.TryParse("", out _, out _));
	}

	// ================================================================ 3. ParseRange

	private static void TestParseRange()
	{
		Section("ParseRange（跨年 / 年份候选 / 脏文本）");
		var normal = RowBuilder.ParseRange("10-13 18:00 ~ 11-03 14:59", Now);
		NotNull("普通区间可解析", normal);
		Eq("普通区间 start", new DateTime(2026, 10, 13, 18, 0, 0), normal!.Value.Start);
		Eq("普通区间 end", new DateTime(2026, 11, 3, 14, 59, 0), normal.Value.End);

		var cross = RowBuilder.ParseRange("12-30 12:00 ~ 01-05 12:00", new DateTime(2026, 1, 2, 12, 0, 0));
		NotNull("跨年区间（1 月视角）可解析", cross);
		Eq("跨年（1 月）start 落在去年", new DateTime(2025, 12, 30, 12, 0, 0), cross!.Value.Start);
		Eq("跨年（1 月）end 落在今年", new DateTime(2026, 1, 5, 12, 0, 0), cross.Value.End);

		var dec = RowBuilder.ParseRange("12-30 12:00 ~ 01-05 12:00", new DateTime(2026, 12, 28, 0, 0, 0));
		NotNull("跨年区间（12 月视角）可解析", dec);
		Eq("跨年（12 月）start 落在今年", new DateTime(2026, 12, 30, 12, 0, 0), dec!.Value.Start);
		Eq("跨年（12 月）end 落在明年", new DateTime(2027, 1, 5, 12, 0, 0), dec.Value.End);

		var dateOnly = RowBuilder.ParseRange("10-13 ~ 11-03", Now);
		NotNull("只有日期（无时刻）可解析", dateOnly);
		Eq("只有日期时 start 为 0 点", new DateTime(2026, 10, 13, 0, 0, 0), dateOnly!.Value.Start);

		var nextYear = RowBuilder.ParseRange("01-02 ~ 01-10", new DateTime(2026, 12, 31, 12, 0, 0));
		NotNull("跨年边界选最近窗口可解析", nextYear);
		Eq("12-31 视角下 01-02 选明年", new DateTime(2027, 1, 2, 0, 0, 0), nextYear!.Value.Start);
		Eq("同一窗口 end 为明年 01-10", new DateTime(2027, 1, 10, 0, 0, 0), nextYear.Value.End);

		Null("脏文本（中文日期）返回 null", RowBuilder.ParseRange("10月13日 ~ 11月3日", Now));
		Null("缺少 ~ 返回 null", RowBuilder.ParseRange("10-13", Now));
		Null("月份越界返回 null", RowBuilder.ParseRange("13-01 ~ 02-02", Now));
		Null("非法日期（02-30）返回 null", RowBuilder.ParseRange("02-30 ~ 03-02", Now));
		Null("小时越界返回 null", RowBuilder.ParseRange("10-13 25:00 ~ 11-03 14:59", Now));
		Null("空串返回 null", RowBuilder.ParseRange("", Now));
		Null("null 返回 null", RowBuilder.ParseRange(null, Now));
	}

	// ================================================================ 4. FormatRemaining

	private static void TestFormatRemaining()
	{
		Section("FormatRemaining / FormatShortRemaining 边界");
		Eq("目标=现在 → 0 天 0 小时 0 分钟", "0天0小时0分钟", RowBuilder.FormatRemaining(Now, Now));
		Eq("59 秒 → 0 天 0 小时 0 分钟", "0天0小时0分钟", RowBuilder.FormatRemaining(Now.AddSeconds(59), Now));
		Eq("1 小时", "0天1小时0分钟", RowBuilder.FormatRemaining(Now.AddHours(1), Now));
		Eq("1 天 2 小时 3 分钟", "1天2小时3分钟", RowBuilder.FormatRemaining(Now.AddDays(1).AddHours(2).AddMinutes(3).AddSeconds(4), Now));
		Eq("已结束返回空串", "", RowBuilder.FormatRemaining(Now.AddSeconds(-1), Now));
		Eq("13 天整", "13天0小时0分钟", RowBuilder.FormatRemaining(Now.AddDays(13), Now));

		Eq("short 已结束", "已结束", RowBuilder.FormatShortRemaining(TimeSpan.Zero));
		Eq("short 负值", "已结束", RowBuilder.FormatShortRemaining(TimeSpan.FromMinutes(-1)));
		Eq("short 59 秒", "1分", RowBuilder.FormatShortRemaining(TimeSpan.FromSeconds(59)));
		Eq("short 60 秒", "1分", RowBuilder.FormatShortRemaining(TimeSpan.FromSeconds(60)));
		Eq("short 90 秒", "1分", RowBuilder.FormatShortRemaining(TimeSpan.FromSeconds(90)));
		Eq("short 1 小时", "1时", RowBuilder.FormatShortRemaining(TimeSpan.FromHours(1)));
		Eq("short 23 小时 59 分", "23时", RowBuilder.FormatShortRemaining(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59)));
		Eq("short 24 小时", "1天", RowBuilder.FormatShortRemaining(TimeSpan.FromHours(24)));
		Eq("short 3 天 15 小时", "3天", RowBuilder.FormatShortRemaining(TimeSpan.FromDays(3) + TimeSpan.FromHours(15)));
	}

	// ================================================================ 5. DisplayTimeCell

	private static void TestDisplayTimeCell()
	{
		Section("DisplayTimeCell（还有 / 还剩 / 已结束）");
		Eq("进行中：还剩", "还剩 12天19小时51分钟", RowBuilder.DisplayTimeCell("10-13 18:00 ~ 11-03 14:59", Now));
		Eq("未开始：还有", "还有 0天14小时52分钟 开始", RowBuilder.DisplayTimeCell("10-22 10:00 ~ 10-30 10:00", Now));
		Eq("已结束", "已结束", RowBuilder.DisplayTimeCell("09-01 10:00 ~ 09-30 10:00", Now));
		Eq("解析失败原样显示", "10月13日 ~ 11月3日", RowBuilder.DisplayTimeCell("10月13日 ~ 11月3日", Now));
		Eq("空串原样显示", "", RowBuilder.DisplayTimeCell("", Now));
		Eq("null 显示空串", "", RowBuilder.DisplayTimeCell(null, Now));
	}

	// ================================================================ 6. CleanRoleNames

	private static void TestCleanRoleNames()
	{
		Section("CleanRoleNames（BA 特例 / 称号剥离）");
		Eq("普通游戏去括号属性", "丝柯克、爱可菲",
			RowBuilder.CleanRoleNames("「虚渊暗星·丝柯克(冰)」、「明绚千韵·爱可菲(冰)」", "genshin"));
		Eq("普通游戏去全角括号", "希儿",
			RowBuilder.CleanRoleNames("希儿（量子）", "hsr"));
		Eq("BA 保留换装括号", "白子(乐队)、和纱(乐队)",
			RowBuilder.CleanRoleNames("「白子(乐队)」、「和纱(乐队)」", "ba-cn"));
		Eq("BA 全角括号转半角", "白子(泳装)",
			RowBuilder.CleanRoleNames("白子（泳装）", "ba-jp"));
		Eq("4 字前缀• 取后半", "主角",
			RowBuilder.CleanRoleNames("虚无之刃•主角", "other"));
		Eq("非 4 字前缀• 不剥离", "虚无之刃锋•主角",
			RowBuilder.CleanRoleNames("虚无之刃锋•主角", "other"));
		Eq("1 字前缀· 不剥离", "影·雷",
			RowBuilder.CleanRoleNames("影·雷", "other"));
		Eq("逗号分隔也支持", "A、B",
			RowBuilder.CleanRoleNames("「A(火)」,「B(冰)」", "genshin"));
		Eq("空串", "", RowBuilder.CleanRoleNames("", "genshin"));
		Eq("null", "", RowBuilder.CleanRoleNames(null, "genshin"));
	}

	// ================================================================ 7. 失败三态

	private static void TestBadges()
	{
		Section("BuildBadge / NormalizeFail / WithFailNote（down > nomatch > stale）");
		var game = MakeCatalog(("g", "测试游戏", false)).Games[0];

		var normal = RowBuilder.Build(game, Rec(banner: "池", bannerDates: "10-13 18:00 ~ 11-03 14:59"), Now);
		False("正常无 badge", normal.HasBadge);
		Eq("正常 badgeKind 为空", "", normal.BadgeKind);

		var nomatch = RowBuilder.Build(game, Rec(banner: "池", gachaFail: NoMatch()), Now);
		True("nomatch 有 badge", nomatch.HasBadge);
		Eq("nomatch kind=warn", "warn", nomatch.BadgeKind);
		Eq("nomatch 文案", "新卡池未公布", nomatch.BadgeText);

		var eventNomatch = RowBuilder.Build(game, Rec(banner: "池", eventFail: NoMatch()), Now);
		Eq("活动 nomatch 文案", "新活动未公布", eventNomatch.BadgeText);

		var down = RowBuilder.Build(game, Rec(banner: "池", eventFail: Down("HTTP 403")), Now);
		Eq("down kind=err", "err", down.BadgeKind);
		Eq("down 文案", "活动失败：HTTP 403", down.BadgeText);

		var noReason = RowBuilder.Build(game, Rec(banner: "池", gachaFail: Down(null)), Now);
		Eq("down 缺 reason 回退文案", "卡池失败：抓取异常", noReason.BadgeText);

		var both = RowBuilder.Build(game, Rec(banner: "池", gachaFail: Down("HTTP 500"), eventFail: Down("超时")), Now);
		Eq("双失败时卡池优先", "卡池失败：HTTP 500", both.BadgeText);

		var mixed = RowBuilder.Build(game, Rec(banner: "池", gachaFail: Down("HTTP 500"), eventFail: NoMatch()), Now);
		Eq("down 优先于 nomatch", "卡池失败：HTTP 500", mixed.BadgeText);

		var stale = RowBuilder.Build(game, Rec(banner: "旧池", bannerDates: "10-01 12:00 ~ 10-20 12:00", eventStale: true), Now);
		True("stale 有 badge", stale.HasBadge);
		Eq("stale kind", "stale", stale.BadgeKind);
		Eq("stale 文案", "沿用旧数据", stale.BadgeText);

		Null("NormalizeFail 未知 kind → null", RowBuilder.NormalizeFail(new SideFail { Kind = "weird" }));
		var normalized = RowBuilder.NormalizeFail(Down(""));
		Eq("NormalizeFail 补 reason", "抓取异常", normalized!.Reason);
		Eq("SideFailText nomatch", "新卡池未公布", RowBuilder.SideFailText("gacha", NoMatch()));
		Eq("WithFailNote 非 stale 只有注记", "（新卡池未公布）", RowBuilder.WithFailNote("旧内容", "gacha", NoMatch(), false));
		Eq("WithFailNote stale 追加", "旧内容\n（新卡池未公布）", RowBuilder.WithFailNote("旧内容", "gacha", NoMatch(), true));
		Eq("WithFailNote 无失败原样返回", "旧内容", RowBuilder.WithFailNote("旧内容", "gacha", null, true));
	}

	// ================================================================ 8. RowBuilder 真实数据

	private static void TestRowBuilder()
	{
		Section("RowBuilder（真实 sample-feed）");
		var catalog = GameCatalog.Load();
		var feed = FeedDocument.Parse(EmbeddedData.SampleFeedJson())!;

		var genshin = RowBuilder.Build(catalog.Find("genshin")!, feed.Games["genshin"], Now);
		Eq("genshin 外显角色名", "丝柯克、爱可菲", genshin.GachaText);
		Eq("genshin 卡池倒计时", "还剩 12天19小时51分钟", genshin.GachaDateText);
		Contains("genshin 卡池 tip 含池名", "「虚星临渡」107期角色活动祈愿：", genshin.GachaTip);
		Contains("genshin 活动 tip 换行", "\n", genshin.GachaTip);
		Eq("genshin 活动外显", "「往冥府的安魂歌」", genshin.EventText);
		NotNull("genshin activeEnd", genshin.ActiveEnd);
		Eq("genshin activeKind（两侧同结束时卡池优先）", "卡池", genshin.ActiveKind);

		var umaGlobal = RowBuilder.Build(catalog.Find("uma-global")!, feed.Games["uma-global"], Now);
		True("uma-global 有失败 badge", umaGlobal.HasBadge);
		Eq("uma-global badge kind", "err", umaGlobal.BadgeKind);
		Eq("uma-global badge 文案", "活动失败：HTTP 403", umaGlobal.BadgeText);
		Contains("uma-global 活动日期 tip 带失败注记", "（活动失败：HTTP 403）", umaGlobal.EventDateTip);

		var kedr = RowBuilder.Build(catalog.Find("kedr")!, feed.Games["kedr"], Now);
		Eq("kedr nomatch badge", "新卡池未公布", kedr.BadgeText);
		Eq("kedr 无卡池内容显示破折号", "—", kedr.GachaText);
		True("kedr 活动有文案", kedr.EventText.Length > 0);
		True("kedr 活动倒计时有还剩", kedr.EventDateText.StartsWith("还剩", StringComparison.Ordinal));

		var pjsk = RowBuilder.Build(catalog.Find("pjsk")!, feed.Games["pjsk"], Now);
		Eq("pjsk 活动 nomatch badge", "新活动未公布", pjsk.BadgeText);

		var missing = RowBuilder.Build(catalog.Find("genshin")!, null, Now);
		Eq("无记录时卡池列 —", "—", missing.GachaText);
		Eq("无记录时活动列 —", "—", missing.EventText);
		Eq("无记录时时间列空", "", missing.GachaDateText);
		False("无记录时无 badge", missing.HasBadge);
	}

	// ================================================================ 9. 可见性 / 排序

	private static void TestVisibility()
	{
		Section("可见性（hidden / defaultHidden / shown / removed / order）");
		var catalog = MakeCatalog(
			("a", "阿尔法", false),
			("b", "贝塔", true),
			("c", "伽马", false),
			("d", "德尔塔", false),
			("e", "艾普西龙", false));
		var feed = new FeedDocument();

		var vm = new WidgetViewModel();
		vm.Apply(feed, new WidgetSettings(), catalog, Now);
		Eq("默认可见 4 行（b 出厂隐藏）", 4, vm.Rows.Count);
		SeqEq("默认顺序 a,c,d,e", new[] { "a", "c", "d", "e" }, vm.Rows.Select(r => r.GameId));
		Eq("HeaderCount", "4 款", vm.HeaderCount);

		var settings2 = new WidgetSettings();
		settings2.Shown.Add("b");
		vm.Apply(feed, settings2, catalog, Now);
		SeqEq("shown 打开出厂隐藏条目", new[] { "a", "b", "c", "d", "e" }, vm.Rows.Select(r => r.GameId));

		var settings3 = new WidgetSettings();
		settings3.Hidden.Add("a");
		vm.Apply(feed, settings3, catalog, Now);
		SeqEq("hidden 隐藏 a", new[] { "c", "d", "e" }, vm.Rows.Select(r => r.GameId));

		var settings4 = new WidgetSettings();
		settings4.Removed.Add("c");
		vm.Apply(feed, settings4, catalog, Now);
		SeqEq("removed 隐藏 c", new[] { "a", "d", "e" }, vm.Rows.Select(r => r.GameId));

		var settings5 = new WidgetSettings();
		settings5.Order.AddRange(new[] { "d", "a", "zzz" });
		vm.Apply(feed, settings5, catalog, Now);
		SeqEq("order 优先，未知 id 被裁掉", new[] { "d", "a", "c", "e" }, vm.Rows.Select(r => r.GameId));

		var settings6 = new WidgetSettings { ShowGacha = false, ShowEvent = false };
		vm.Apply(feed, settings6, catalog, Now);
		False("showGacha=false 透传到 VM", vm.ShowGacha);
		False("showEvent=false 透传到 VM", vm.ShowEvent);
		True("行也带开关", vm.Rows.All(r => !r.ShowGacha && !r.ShowEvent));

		var settings7 = new WidgetSettings();
		settings7.Hidden.Add("a");
		settings7.Removed.Add("a");
		vm.Apply(feed, settings7, catalog, Now);
		SeqEq("同时 hidden+removed 只隐藏一次", new[] { "c", "d", "e" }, vm.Rows.Select(r => r.GameId));
	}

	// ================================================================ 10. 顶部状态

	private static void TestStatus()
	{
		Section("顶部状态（成功 N/M / 失败分类 / 示例数据前缀）");
		var catalog = MakeCatalog(
			("g1", "游戏1", false),
			("g2", "游戏2", false),
			("g3", "游戏3", false),
			("g4", "游戏4", false),
			("g5", "游戏5", false));
		var feed = MakeFeed(
			("g1", Rec(banner: "池1")),
			("g2", Rec(banner: "池2", eventFail: Down("HTTP 403"))),
			("g3", Rec(banner: "池3", gachaFail: NoMatch())),
			("g4", Rec(banner: "池4", skipped: true)));
		feed.RefreshedAt = 1792554834054;

		var vm = new WidgetViewModel();
		vm.Apply(feed, new WidgetSettings(), catalog, Now);
		Eq("StatusText", "联网数据 · 成功 2/5", vm.StatusText);
		Eq("DataStatusText", "数据：联网数据", vm.DataStatusText);
		Eq("StatusTip 失败分组", "活动失败：游戏2\n新卡池未公布：游戏3", vm.StatusTip);
		Eq("ScrapeText", "成功 2/5  活动失败：游戏2  新卡池未公布：游戏3", vm.ScrapeText);
		StartsWith("UpdatedText 有刷新时间", "刷新：2026/", vm.UpdatedText);
		False("无错误时 HasError=false", vm.HasError);

		vm.Apply(feed, new WidgetSettings(), catalog, Now, FeedStatus.Sample);
		StartsWith("示例数据前缀", "示例数据 · 成功 2/5", vm.StatusText);
		Eq("示例数据 DataStatusText", "数据：示例数据", vm.DataStatusText);
		True("IsSample", vm.IsSample);

		var downFeed = MakeFeed(
			("g1", Rec(gachaFail: Down("HTTP 500"))),
			("g2", Rec(eventFail: Down("超时"))));
		vm.Apply(downFeed, new WidgetSettings(), catalog, Now);
		StartsWith("全部失败 → 尚未联网", "尚未联网 · 成功 0/5", vm.StatusText);

		vm.Apply(new FeedDocument(), new WidgetSettings(), catalog, Now);
		Eq("无失败无记录时提示语", "所有可见条目都抓到了当期内容", vm.StatusTip);
	}

	/// <summary>
	/// 预览条的「示例 / 缓存」标记依赖 VM 的变更通知；这里锁住 Apply 时 Status/IsSample/IsCached
	/// 必须真的发 PropertyChanged（0.1.14 之前直接改字段没通知，导致刷新成功后标记不消失）。
	/// </summary>
	private static void TestStatusNotifications()
	{
		Section("状态变更通知（示例/缓存标记）");
		var catalog = MakeCatalog(("g1", "游戏1", false));
		var feed = MakeFeed(("g1", Rec(banner: "池1")));
		var vm = new WidgetViewModel();
		var changed = new List<string>();
		vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

		vm.Apply(feed, new WidgetSettings(), catalog, Now, FeedStatus.Sample);
		True("示例状态通知 IsSample", changed.Contains(nameof(WidgetViewModel.IsSample)));
		True("示例状态通知 Status", changed.Contains(nameof(WidgetViewModel.Status)));
		True("示例状态下 IsSample=true", vm.IsSample);

		changed.Clear();
		vm.Apply(feed, new WidgetSettings(), catalog, Now, FeedStatus.Fresh);
		True("刷新成功通知 IsSample", changed.Contains(nameof(WidgetViewModel.IsSample)));
		False("刷新成功后 IsSample=false", vm.IsSample);
		False("刷新成功后 IsCached=false", vm.IsCached);

		changed.Clear();
		vm.Apply(feed, new WidgetSettings(), catalog, Now, FeedStatus.Cached);
		True("缓存状态通知 IsCached", changed.Contains(nameof(WidgetViewModel.IsCached)));
		True("缓存状态下 IsCached=true", vm.IsCached);
	}

	// ================================================================ 11. chips

	private static void TestChips()
	{
		Section("chips（同游戏取最快结束侧 / 升序 / N 上限 / 空）");
		var catalog = MakeCatalog(
			("genshin", "原神", false),
			("hsr", "崩坏：星穹铁道", false),
			("zzz", "绝区零", false),
			("wuwa", "鸣潮", false));
		var now = new DateTime(2026, 10, 21, 12, 0, 0);
		var feed = MakeFeed(
			("genshin", Rec(banner: "「虚星临渡」107期", bannerDates: "10-13 18:00 ~ 11-03 14:59",
				eventText: "「往冥府的安魂歌」", eventDates: "10-15 12:00 ~ 10-30 12:00")),
			("hsr", Rec(banner: "「皓色轶英」", bannerDates: "10-12 04:00 ~ 10-25 03:59")),
			("zzz", Rec(banner: "「倾涛浮生」", bannerDates: "10-21 06:00 ~ 11-11 11:59",
				eventText: "天使应援大作战", eventDates: "09-09 06:00 ~ 09-30 03:59")),
			("wuwa", Rec(banner: "旧池", bannerDates: "09-01 10:00 ~ 09-20 10:00")));

		var vm = new WidgetViewModel();
		catalog.Games.First(g => g.Id == "hsr").Color = "#6C6FE0";
		vm.Apply(feed, new WidgetSettings { PreviewItems = 3 }, catalog, now);
		Eq("3 个 chip（wuwa 无进行中）", 3, vm.Chips.Count);
		True("HasChips", vm.HasChips);
		False("HasNoChips", vm.HasNoChips);
		SeqEq("按结束时间升序", new[] { "hsr", "genshin", "zzz" }, vm.Chips.Select(c => c.GameId));
		Eq("hsr 短倒计时", "3天", vm.Chips[0].Remaining);
		Eq("同游戏多侧取最早结束（活动）", "活动", vm.Chips[1].Kind);
		Eq("活动侧剩余", "9天", vm.Chips[1].Remaining);
		Eq("zzz 只取进行中的卡池", "卡池", vm.Chips[2].Kind);
		Contains("chip Tip 多行：游戏名换行", "\n", vm.Chips[0].Tip);
		Contains("chip Tip 含类别", "卡池：", vm.Chips[0].Tip);
		Contains("chip Tip 含还剩", "\n还剩 ", vm.Chips[0].Tip);
		Eq("AccentIndex 从 0 开始", 0, vm.Chips[0].AccentIndex);
		Eq("AccentIndex 递增", 1, vm.Chips[1].AccentIndex);
		Eq("chip 游戏名（<=9 字符不截断）", "原神", vm.Chips[1].GameName);
		Eq("chip 全名", "原神", vm.Chips[1].FullName);

		// 任务栏 chip 的"信息量"断言：游戏之外还要有 内容标题 / 类型 / 截止 / 紧迫度 / 游戏色
		Eq("chip 标题（卡池无 roles 时用 banner）", "「皓色轶英」", vm.Chips[0].Title);
		True("卡池 chip IsGacha", vm.Chips[0].IsGacha);
		Eq("卡池类型图案", "◆", vm.Chips[0].SideGlyph);
		Eq("卡池截止日", "10-25", vm.Chips[0].Deadline);
		Eq("3 天内 → 紧迫度 2", 2, vm.Chips[0].UrgencyLevel);
		Eq("活动 chip 标题", "「往冥府的安魂歌」", vm.Chips[1].Title);
		False("活动 chip IsGacha", vm.Chips[1].IsGacha);
		Eq("活动类型图案", "⚑", vm.Chips[1].SideGlyph);
		Eq("7 天外 → 紧迫度 0", 0, vm.Chips[1].UrgencyLevel);
		Eq("chip 游戏代表色", "#6C6FE0", vm.Chips[0].Color);
		Contains("chip Tip 含截止时间", "\n截止：", vm.Chips[0].Tip);

		vm.Apply(feed, new WidgetSettings { PreviewItems = 2 }, catalog, now);
		Eq("PreviewItems=2 上限生效", 2, vm.Chips.Count);
		SeqEq("N 上限后仍按时间升序", new[] { "hsr", "genshin" }, vm.Chips.Select(c => c.GameId));

		var hiddenSettings = new WidgetSettings { PreviewItems = 3 };
		hiddenSettings.Hidden.Add("hsr");
		vm.Apply(feed, hiddenSettings, catalog, now);
		SeqEq("隐藏条目不进 chips", new[] { "genshin", "zzz" }, vm.Chips.Select(c => c.GameId));

		var idle = MakeFeed(("genshin", Rec(banner: "旧池", bannerDates: "09-01 10:00 ~ 09-20 10:00")));
		vm.Apply(idle, new WidgetSettings(), catalog, now);
		Eq("没有进行中项时 chips 为空", 0, vm.Chips.Count);
		True("HasNoChips", vm.HasNoChips);

		var longCatalog = MakeCatalog(("bandori", "BanG Dream！少女乐团派对·国服", false));
		var longFeed = MakeFeed(("bandori", Rec(banner: "纪念招募", bannerDates: "10-20 12:00 ~ 10-25 12:00")));
		vm.Apply(longFeed, new WidgetSettings(), longCatalog, now);
		Eq("超长游戏名截断为 8 字符 + …", "BanG Dre…", vm.Chips[0].GameName);
		Eq("FullName 保留全名", "BanG Dream！少女乐团派对·国服", vm.Chips[0].FullName);
	}

	// ================================================================ 12. Tick

	private static void TestTick()
	{
		Section("Tick（分钟级刷新 / force）");
		var catalog = MakeCatalog(("g", "计时游戏", false));
		var now = new DateTime(2026, 10, 21, 12, 0, 0);
		var feed = MakeFeed(("g", Rec(banner: "卡池", bannerDates: "10-20 12:00 ~ 11-03 12:00")));
		var vm = new WidgetViewModel();
		vm.Apply(feed, new WidgetSettings(), catalog, now);
		Eq("初始倒计时", "还剩 13天0小时0分钟", vm.Rows[0].GachaDateText);

		vm.Tick(now.AddSeconds(30));
		Eq("同一分钟内不刷新", "还剩 13天0小时0分钟", vm.Rows[0].GachaDateText);

		vm.Tick(now.AddMinutes(1));
		Eq("跨分钟刷新", "还剩 12天23小时59分钟", vm.Rows[0].GachaDateText);

		vm.Tick(now.AddMinutes(5), force: true);
		Eq("force 立刻重算", "还剩 12天23小时55分钟", vm.Rows[0].GachaDateText);
	}

	// ================================================================ 13. 设置

	private static void TestSettings()
	{
		Section("WidgetSettings（camelCase / Normalize）");
		var catalog = MakeCatalog(
			("a", "A", false),
			("b", "B", false),
			("c", "C", false),
			("d", "D", false));

		var json = """
		{"previewItems":9,"autoRefresh":false,"refreshMinutes":1,
		 "hidden":["a","a","bogus"],"shown":[],"removed":["bogus","c"],
		 "order":["d","bogus","a"],"feedUrl":"   ","showGacha":false}
		""";
		var settings = WidgetSettings.FromJson(json, catalog);
		Eq("previewItems 上限 3", 3, settings.PreviewItems);
		Eq("refreshMinutes 下限 15", 15, settings.RefreshMinutes);
		SeqEq("hidden 去重 + 裁未知", new[] { "a" }, settings.Hidden);
		SeqEq("removed 裁未知", new[] { "c" }, settings.Removed);
		SeqEq("order 裁未知并保序", new[] { "d", "a" }, settings.Order);
		Eq("空 feedUrl 回默认", WidgetSettings.DefaultFeedUrl, settings.FeedUrl);
		False("autoRefresh 反序列化", settings.AutoRefresh);
		False("showGacha 反序列化", settings.ShowGacha);
		True("showEvent 默认 true", settings.ShowEvent);

		var low = WidgetSettings.FromJson("""{"previewItems":0,"refreshMinutes":5000}""", catalog);
		Eq("previewItems 下限 1", 1, low.PreviewItems);
		Eq("refreshMinutes 上限 720", 720, low.RefreshMinutes);

		var noCatalog = WidgetSettings.FromJson("""{"order":["a","zzz"]}""");
		SeqEq("无 catalog 时不裁 id，只去重", new[] { "a", "zzz" }, noCatalog.Order);

		var fresh = WidgetSettings.FromJson(null);
		Eq("空 JSON 用默认预览数", 3, fresh.PreviewItems);
		Eq("空 JSON 用默认 feedUrl", WidgetSettings.DefaultFeedUrl, fresh.FeedUrl);
		Eq("空 JSON 默认刷新 60 分钟", 60, fresh.RefreshMinutes);

		var roundTripJson = new WidgetSettings { Hidden = { "a" } }.ToJson();
		Contains("ToJson camelCase previewItems", "\"previewItems\": 3", roundTripJson);
		Contains("ToJson camelCase feedUrl", "\"feedUrl\":", roundTripJson);
		Contains("ToJson 序列化 hidden", "\"hidden\"", roundTripJson);
		Contains("ToJson 序列化 showGacha", "\"showGacha\"", roundTripJson);
		var roundTrip = WidgetSettings.FromJson(roundTripJson, catalog);
		SeqEq("ToJson/FromJson 往返", new[] { "a" }, roundTrip.Hidden);

		var dir = NewTempDir();
		try
		{
			var path = Path.Combine(dir, "settings.json");
			var saved = new WidgetSettings { PreviewItems = 2, AutoRefresh = false, Hidden = { "b" } };
			saved.Save(path);
			True("Save 落盘", File.Exists(path));
			var loaded = WidgetSettings.Load(path, catalog);
			Eq("Load previewItems", 2, loaded.PreviewItems);
			False("Load autoRefresh", loaded.AutoRefresh);
			SeqEq("Load hidden", new[] { "b" }, loaded.Hidden);
			var missing = WidgetSettings.Load(Path.Combine(dir, "nope.json"), catalog);
			Eq("缺失设置文件回默认", 3, missing.PreviewItems);
			var bad = Path.Combine(dir, "bad.json");
			File.WriteAllText(bad, "not json", Encoding.UTF8);
			Eq("坏设置文件回默认", 3, WidgetSettings.Load(bad, catalog).PreviewItems);
		}
		finally
		{
			TryCleanup(dir);
		}
	}

	// ================================================================ 14. 外壳接口

	private static void TestShellApi()
	{
		Section("外壳接口（docs/03 生命周期：CatalogStore / ctor 注入 / 首帧读缓存）");
		var store = new CatalogStore();
		Eq("CatalogStore.Catalog 28 款", 28, store.Catalog.Count);
		Eq("CatalogStore.Reload 返回同一口径", 28, store.Reload().Count);

		var catalog = MakeCatalog(("g", "游戏", false));
		var vm = new WidgetViewModel(catalog);
		Eq("构造时注入 catalog", 1, vm.Catalog!.Count);
		vm.Apply(MakeFeed(("g", Rec(banner: "池"))), new WidgetSettings(), Now);
		Eq("3 参 Apply 可用（用注入的 catalog）", 1, vm.Rows.Count);
		Eq("3 参 Apply 默认按联网数据处理", "联网数据 · 成功 1/1", vm.StatusText);

		var vmNoCatalog = new WidgetViewModel();
		Throws<InvalidOperationException>("未注入 catalog 时 3 参 Apply 明确报错", () =>
			vmNoCatalog.Apply(new FeedDocument(), new WidgetSettings(), Now));

		var sample = new FeedResult(new FeedDocument(), FeedStatus.Sample, "离线", DateTimeOffset.UtcNow);
		var vmSample = new WidgetViewModel(catalog);
		vmSample.Apply(sample, new WidgetSettings(), Now);
		StartsWith("FeedResult 3 参 Apply 保留 Sample 前缀", "示例数据", vmSample.StatusText);
		Eq("FeedResult 错误透出到 FeedError", "离线", vmSample.FeedError);
	}

	// ================================================================ 15. catalog 代表色

	/// <summary>
	/// 日程视图「颜色 = 游戏」的唯一真源：gen-catalog 从 core 的 SOURCES 抽 color 写进 catalog，
	/// Core 侧经 <see cref="CatalogEntry.Color"/> 透出。这里锁住抽取结果与"缺字段按空串"的兼容性。
	/// </summary>
	private static void TestCatalogColor()
	{
		Section("catalog 代表色（core SOURCES → data/catalog.json → CatalogEntry.Color）");
		var catalog = GameCatalog.Load();
		Eq("genshin 代表色", "#3FA9A0", catalog.Find("genshin")!.Color);
		Eq("hsr 代表色", "#6C6FE0", catalog.Find("hsr")!.Color);
		Eq("zzz 代表色", "#E0A93B", catalog.Find("zzz")!.Color);
		Eq("颜色覆盖率 28/28", 28, catalog.Games.Count(g => Regex.IsMatch(g.Color, "^#[0-9A-Fa-f]{6}$")));
		Eq("CatalogEntry.Color 默认空串", "", new CatalogEntry().Color);
		var legacy = GameCatalog.FromJson("""{"schemaVersion":1,"games":[{"id":"x","name":"X","order":0}]}""");
		Eq("旧 catalog 缺 color 字段按空串（运行期哈希回退）", "", legacy.Find("x")!.Color);
	}

	// ================================================================ 16. 日程条目形状

	private static void TestScheduleItemShape()
	{
		Section("ScheduleItem 形状（Title / DatesText / RawTip / 双侧 / down 无内容不出条目）");
		var catalog = MakeCatalogRich(
			("ba-cn", "蔚蓝档案", "", ""),
			("genshin", "原神", "#3FA9A0", "https://icon/genshin.png"),
			("multi", "双列游戏", "", ""),
			("empty", "无内容游戏", "", ""),
			("down", "抓取失败游戏", "", ""),
			("stalegame", "沿用旧数据游戏", "", ""));

		var feed = MakeFeed(
			("ba-cn", Rec(roles: "「白子(乐队)」、「和纱(乐队)」", banner: "「XX」招募",
				bannerDates: "10-19 00:00 ~ 10-25 19:08", bannerDatesRaw: "10-19 00:00 ~ 10-25 19:08")),
			("genshin", Rec(banner: "「虚星临渡」", bannerDates: "10-19 00:00 ~ 10-25 19:08")),
			("multi", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08",
				eventText: "活动名", eventDates: "10-20 00:00 ~ 10-27 00:00")),
			("empty", Rec()),
			("down", Rec(gachaFail: Down("HTTP 500"), eventFail: Down("HTTP 403"))),
			("stalegame", Rec(banner: "旧池", bannerDates: "10-19 00:00 ~ 10-25 19:08",
				gachaFail: Down("超时"), gachaStale: true)));

		var snap = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now);
		ScheduleItem Of(string id, string side = "gacha") => snap.Items.First(i => i.GameId == id && i.Side == side);

		Eq("BA 卡池 Title 走 CleanRoleNames 且保留换装括号", "白子(乐队)、和纱(乐队)", Of("ba-cn").Title);
		Eq("卡池 Side", "gacha", Of("ba-cn").Side);
		True("卡池 IsGacha", Of("ba-cn").IsGacha);
		Eq("DatesText 取 bannerDates", "10-19 00:00 ~ 10-25 19:08", Of("ba-cn").DatesText);
		Eq("RawTip 用 raw 时间字段", "白子(乐队)、和纱(乐队)\n10-19 00:00 ~ 10-25 19:08", Of("ba-cn").RawTip);
		Eq("缺角色名时 Title 退回池名", "「虚星临渡」", Of("genshin").Title);
		Eq("活动侧 Title 取 event", "活动名", Of("multi", "event").Title);
		Eq("活动侧 Side", "event", Of("multi", "event").Side);
		False("活动侧 IsGacha", Of("multi", "event").IsGacha);
		Eq("双侧都有内容 → 2 条", 2, snap.Items.Count(i => i.GameId == "multi"));
		Eq("两侧都无内容 → 不出条目", 0, snap.Items.Count(i => i.GameId == "empty"));
		Eq("down 且无内容 → 两侧都不出条目", 0, snap.Items.Count(i => i.GameId == "down"));
		Eq("down 但沿用旧数据 → 仍出条目", 1, snap.Items.Count(i => i.GameId == "stalegame"));
		Eq("IconUrl 透传 catalog 图标", "https://icon/genshin.png", Of("genshin").IconUrl);
		Eq("GameName 透传", "原神", Of("genshin").GameName);

		// raw 为空时回落到外显文本（悬停不能空白）
		var fallback = ScheduleBuilder.Build(
			MakeFeed(("genshin", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08"))),
			catalog, new WidgetSettings(), Now);
		Eq("raw 为空时 RawTip 回落外显文本", "池\n10-19 00:00 ~ 10-25 19:08", fallback.Items[0].RawTip);

		// 只有标题、没有时间文本：条目仍在（长期），时间字段为 null
		var onlyTitleCatalog = MakeCatalogRich(("t", "只有标题", "", ""));
		var onlyTitle = ScheduleBuilder.Build(MakeFeed(("t", Rec(banner: "仅标题"))), onlyTitleCatalog, new WidgetSettings(), Now);
		Eq("只有标题也出条目", 1, onlyTitle.Items.Count);
		Eq("只有标题时 RawTip 就是标题", "仅标题", onlyTitle.Items[0].RawTip);
		Eq("无时间文本 → 长期", ScheduleState.Long, onlyTitle.Items[0].State);
		Eq("无时间文本 → DatesText 空串", "", onlyTitle.Items[0].DatesText);
		Null("无窗口 Start 为 null", onlyTitle.Items[0].Start);
		Null("无窗口 End 为 null", onlyTitle.Items[0].End);

		// feed 里没有这款游戏的记录 → 不出条目
		Eq("feed 无记录 → 不出条目", 0,
			ScheduleBuilder.Build(new FeedDocument(), catalog, new WidgetSettings(), Now).Items.Count);
	}

	// ================================================================ 17. 状态五态

	private static void TestScheduleStates()
	{
		Section("ScheduleState 五态 + 跨年窗口 + >366 天长期");
		var catalog = MakeCatalogRich(
			("live", "在期", "", ""),
			("wait", "未开始", "", ""),
			("ended", "已结束", "", ""),
			("longtext", "长期文本", "", ""));

		var feed = MakeFeed(
			("live", Rec(banner: "池", bannerDates: "10-13 18:00 ~ 11-03 14:59")),
			("wait", Rec(banner: "池", bannerDates: "11-01 10:00 ~ 11-10 10:00")),
			("ended", Rec(banner: "池", bannerDates: "09-01 10:00 ~ 09-30 10:00")),
			("longtext", Rec(banner: "池", bannerDates: "长期")));

		var snap = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now);
		ScheduleItem Of(string id) => snap.Items.First(i => i.GameId == id);

		Eq("在期 → Live", ScheduleState.Live, Of("live").State);
		Eq("未开始 → Waiting", ScheduleState.Waiting, Of("wait").State);
		Eq("已结束 → Ended", ScheduleState.Ended, Of("ended").State);
		Eq("解析不出窗口 → Long", ScheduleState.Long, Of("longtext").State);
		Eq("Long 文案「长期」", "长期", Of("longtext").UrgencyText);
		True("Live RemainingMs > 0", Of("live").RemainingMs > 0);
		Eq("Ended RemainingMs = 0", 0L, Of("ended").RemainingMs);
		Eq("Live StateText 在期", "在期", Of("live").StateText);
		Eq("Waiting StateText 未开始", "未开始", Of("wait").StateText);
		Eq("Ended StateText 已结束", "已结束", Of("ended").StateText);
		Eq("Long StateText 长期", "长期", Of("longtext").StateText);
		Eq("Unknown StateText 无数据", "无数据", ScheduleBuilder.StateTextOf(ScheduleState.Unknown));

		// 窗口 > 366 天 → 长期。ParseRange 只产出「MM-dd」窗口（最多 366 天），
		// 所以这条规则直接喂合成窗口锁住（pjsk 那种 2099 哨兵在 DSH 里走的就是它）。
		Eq("窗口 > 366 天 → Long", ScheduleState.Long,
			ScheduleBuilder.ClassifyState((Now.AddDays(-10), Now.AddDays(400)), Now));
		Eq("窗口正好 366 天 → 按位置判定", ScheduleState.Live,
			ScheduleBuilder.ClassifyState((Now.AddDays(-1), Now.AddDays(365)), Now));
		Eq("null 窗口 → Long", ScheduleState.Long, ScheduleBuilder.ClassifyState(null, Now));

		// 跨年窗口：1 月视角下 12-30 ~ 01-05 必须在期（起在去年、止在今年）
		var jan = new DateTime(2026, 1, 2, 12, 0, 0);
		var crossCatalog = MakeCatalogRich(("x", "跨年", "", ""));
		var cross = ScheduleBuilder.Build(
			MakeFeed(("x", Rec(banner: "池", bannerDates: "12-30 12:00 ~ 01-05 12:00"))),
			crossCatalog, new WidgetSettings(), jan).Items[0];
		Eq("跨年窗口 → Live", ScheduleState.Live, cross.State);
		Eq("跨年 Start 落在去年", new DateTime(2025, 12, 30, 12, 0, 0), cross.Start);
		Eq("跨年 End 落在今年", new DateTime(2026, 1, 5, 12, 0, 0), cross.End);
	}

	// ================================================================ 18. 紧迫度

	private static void TestScheduleUrgency()
	{
		Section("紧迫度文案 / 档位边界（24h、7d、59s / 1min / 1h / 1d）");
		var now = Now;

		Eq("Live 59 秒 → 今天到期", "今天到期",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddSeconds(59)));
		Eq("Live 1 小时 → 今天到期", "今天到期",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddHours(1)));
		Eq("Live 23 小时 59 分 → 今天到期", "今天到期",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddHours(23).AddMinutes(59)));
		Eq("Live 正好 24 小时 → 还剩 1 天", "还剩 1 天",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddHours(24)));
		Eq("Live 25 小时 → 还剩 1 天（按天取整）", "还剩 1 天",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddHours(25)));
		Eq("Live 3 天 → 还剩 3 天", "还剩 3 天",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddDays(3)));
		Eq("Live 6 天 23 小时 → 还剩 6 天", "还剩 6 天",
			ScheduleBuilder.FormatUrgency(ScheduleState.Live, now, now, now.AddDays(6).AddHours(23)));
		Eq("Waiting →「MM-DD HH:mm 开始」", "10-22 10:00 开始",
			ScheduleBuilder.FormatUrgency(ScheduleState.Waiting, now,
				new DateTime(2026, 10, 22, 10, 0, 0), new DateTime(2026, 10, 30, 10, 0, 0)));
		Eq("Waiting 月/日/时/分补零", "11-05 09:07 开始",
			ScheduleBuilder.FormatUrgency(ScheduleState.Waiting, now,
				new DateTime(2026, 11, 5, 9, 7, 0), new DateTime(2026, 11, 10, 9, 7, 0)));
		Eq("Ended → 已结束", "已结束",
			ScheduleBuilder.FormatUrgency(ScheduleState.Ended, now, now.AddDays(-3), now.AddDays(-1)));
		Eq("Long → 长期", "长期",
			ScheduleBuilder.FormatUrgency(ScheduleState.Long, now, null, null));
		Eq("Unknown → 空串", "",
			ScheduleBuilder.FormatUrgency(ScheduleState.Unknown, now, null, null));

		Eq("59 秒倒计时写 0 分钟", "0 分钟", ScheduleBuilder.FormatRemainingText(TimeSpan.FromSeconds(59)));
		Eq("正好 1 分钟", "1 分钟", ScheduleBuilder.FormatRemainingText(TimeSpan.FromMinutes(1)));
		Eq("59 分钟", "59 分钟", ScheduleBuilder.FormatRemainingText(TimeSpan.FromMinutes(59)));
		Eq("1 小时", "1 小时", ScheduleBuilder.FormatRemainingText(TimeSpan.FromHours(1)));
		Eq("23 小时 59 分", "23 小时",
			ScheduleBuilder.FormatRemainingText(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59)));
		Eq("1 天", "1 天", ScheduleBuilder.FormatRemainingText(TimeSpan.FromDays(1)));
		Eq("1 天半按天取整", "1 天", ScheduleBuilder.FormatRemainingText(TimeSpan.FromDays(1.5)));
		Eq("零/负值倒计时", "0 分钟", ScheduleBuilder.FormatRemainingText(TimeSpan.FromMinutes(-5)));

		Eq("24h 内 → 档位 1", 1,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Live, now, now.AddHours(23)));
		Eq("正好 24h → 档位 2", 2,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Live, now, now.AddHours(24)));
		Eq("6 天 23 小时 → 档位 2", 2,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Live, now, now.AddDays(6).AddHours(23)));
		Eq("正好 7 天 → 档位 0", 0,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Live, now, now.AddDays(7)));
		Eq("超过 7 天 → 档位 0", 0,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Live, now, now.AddDays(8)));
		Eq("未开始 → 档位 3", 3,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Waiting, now, now.AddDays(2)));
		Eq("已结束 → 档位 0", 0,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Ended, now, now.AddDays(-1)));
		Eq("长期 → 档位 0", 0,
			ScheduleBuilder.UrgencyLevelOf(ScheduleState.Long, now, null));
	}

	// ================================================================ 19. 近期 / 下一个截止

	private static void TestScheduleLists()
	{
		Section("Recent / NextUp / NextTwo（只取 Live、End 升序、上限）");
		var catalog = MakeCatalogRich(
			("a", "甲", "", ""), ("b", "乙", "", ""), ("c", "丙", "", ""),
			("d", "丁", "", ""), ("e", "戊", "", ""));
		var feed = MakeFeed(
			// +3 天
			("a", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-24 19:08")),
			// 卡池 +1 天、活动 +4 天（同一款游戏两条都在期）
			("b", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-22 19:08",
				eventText: "活动", eventDates: "10-19 00:00 ~ 10-25 19:08")),
			// +2 天
			("c", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-23 19:08")),
			// 已结束
			("d", Rec(banner: "池", bannerDates: "10-01 00:00 ~ 10-18 00:00")),
			// 未开始
			("e", Rec(banner: "池", bannerDates: "11-01 00:00 ~ 11-05 00:00")));

		var snap = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now);
		Eq("LiveCount 只数在期", 4, snap.LiveCount);
		Eq("Items 仍按游戏顺序（不按紧迫度重排）", 4 + 1 + 1, snap.Items.Count);
		Eq("Recent 只含在期", 4, snap.Recent.Count);
		SeqEq("Recent 跨游戏按 End 升序", new[] { "b:gacha", "c:gacha", "a:gacha", "b:event" },
			snap.Recent.Select(i => i.GameId + ":" + i.Side));
		SeqEq("Items 顺序 = catalog 顺序 × 卡池/活动",
			new[] { "a:gacha", "b:gacha", "b:event", "c:gacha", "d:gacha", "e:gacha" },
			snap.Items.Select(i => i.GameId + ":" + i.Side));
		Eq("NextUp 是 End 最小的一条", "b:gacha", snap.NextUp!.GameId + ":" + snap.NextUp.Side);
		SeqEq("NextTwo 是 NextUp 之后的两条", new[] { "c:gacha", "a:gacha" },
			snap.NextTwo.Select(i => i.GameId + ":" + i.Side));
		False("NextTwo 不含 NextUp", snap.NextTwo.Contains(snap.NextUp));

		var two = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now, recentLimit: 2);
		Eq("recentLimit 生效", 2, two.Recent.Count);
		SeqEq("截断后仍按 End 升序", new[] { "b:gacha", "c:gacha" },
			two.Recent.Select(i => i.GameId + ":" + i.Side));
		Eq("recentLimit 不影响 NextUp", "b:gacha", two.NextUp!.GameId + ":" + two.NextUp.Side);
		Eq("recentLimit 不影响 NextTwo 条数", 2, two.NextTwo.Count);
		Eq("recentLimit=0 → 不列近期", 0,
			ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now, 0).Recent.Count);

		var idle = ScheduleBuilder.Build(
			MakeFeed(("a", Rec(banner: "池", bannerDates: "10-01 00:00 ~ 10-18 00:00"))),
			catalog, new WidgetSettings(), Now);
		Null("没有在期条目 → NextUp 为 null", idle.NextUp);
		Eq("没有在期条目 → Recent 为空", 0, idle.Recent.Count);
		Eq("没有在期条目 → NextTwo 为空", 0, idle.NextTwo.Count);
		Eq("只在期一条 → NextTwo 为空", 0, ScheduleBuilder.Build(
			MakeFeed(("a", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-24 19:08"))),
			catalog, new WidgetSettings(), Now).NextTwo.Count);
	}

	// ================================================================ 20. 总览计数

	private static void TestScheduleGlance()
	{
		Section("GlanceText（24 小时内 / 7 天内计数与严格格式）");
		var catalog = MakeCatalogRich(
			("g1", "一", "", ""), ("g2", "二", "", ""), ("g3", "三", "", ""),
			("g4", "四", "", ""), ("g5", "五", "", ""), ("g6", "六", "", ""));
		var feed = MakeFeed(
			("g1", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-21 21:08")), // +2 小时
			("g2", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-22 19:07")), // +23 小时 59 分
			("g3", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-22 19:08")), // 正好 +24 小时
			("g4", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-22 20:08")), // +25 小时
			("g5", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-27 19:08")), // +6 天
			("g6", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-29 19:08"))); // +8 天

		var snap = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now);
		Eq("LiveCount 6", 6, snap.LiveCount);
		Eq("24h 含正好 24h；7d 只到 7 天内", "24 小时内 3 条 · 7 天内 5 条", snap.GlanceText);

		Eq("零也照写格式", "24 小时内 0 条 · 7 天内 0 条",
			ScheduleBuilder.Build(new FeedDocument(), catalog, new WidgetSettings(), Now).GlanceText);
		Eq("全已结束时两个 0 也照写", "24 小时内 0 条 · 7 天内 0 条",
			ScheduleBuilder.Build(
				MakeFeed(("g1", Rec(banner: "池", bannerDates: "09-01 00:00 ~ 09-30 00:00"))),
				catalog, new WidgetSettings(), Now).GlanceText);
	}

	// ================================================================ 21. 颜色

	private static void TestScheduleColors()
	{
		Section("颜色：catalog 优先 / 空串按游戏名哈希落固定色盘");
		string[] palette = { "#E0709F", "#3FA9A0", "#E0A93B", "#4A6CF7", "#7A5AF8", "#2FB3C9", "#E06C4A", "#5C9E3F" };

		Eq("catalog 颜色优先", "#3FA9A0", ScheduleBuilder.GameColor("#3FA9A0", "任意名字"));
		Eq("catalog 颜色非空即用（不回验格式）", "#abcdef", ScheduleBuilder.GameColor("#abcdef", "原神"));
		Eq("空串 → 名字哈希", "#5C9E3F", ScheduleBuilder.GameColor("", "原神"));
		Eq("null → 名字哈希", "#E0709F", ScheduleBuilder.GameColor(null, "崩坏：星穹铁道"));
		Eq("纯空白视为未指定", "#5C9E3F", ScheduleBuilder.GameColor("   ", "原神"));
		Eq("emoji 按码点哈希（与 DSH 的 for...of 同口径）", "#2FB3C9", ScheduleBuilder.GameColor("", "🎮游戏"));
		Eq("空名字也有确定色", "#E0709F", ScheduleBuilder.GameColor("", ""));
		Eq("同名恒同色", ScheduleBuilder.GameColor("", "自定义条目"), ScheduleBuilder.GameColor("", "自定义条目"));
		True("哈希结果落在固定色盘内", palette.Contains(ScheduleBuilder.GameColor("", "自定义条目")));
		True("不同名字可落不同色",
			ScheduleBuilder.GameColor("", "原神") != ScheduleBuilder.GameColor("", "崩坏：星穹铁道"));

		var catalog = MakeCatalogRich(("with", "带色游戏", "#123456", ""), ("without", "原神", "", ""));
		var feed = MakeFeed(
			("with", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08")),
			("without", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08")));
		var snap = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now);
		Eq("条目用 catalog 色", "#123456", snap.Items.First(i => i.GameId == "with").Color);
		Eq("条目空色回退哈希", "#5C9E3F", snap.Items.First(i => i.GameId == "without").Color);
	}

	// ================================================================ 22. 可见性

	private static void TestScheduleVisibility()
	{
		Section("日程条目复用可见性 / 排序规则（hidden / shown / removed / order）");
		var catalog = MakeCatalogFull(
			("a", "甲", "", "", false),
			("b", "乙", "", "", true),
			("c", "丙", "", "", false),
			("d", "丁", "", "", false));
		var feed = MakeFeed(
			("a", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08")),
			("b", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08")),
			("c", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08")),
			("d", Rec(banner: "池", bannerDates: "10-19 00:00 ~ 10-25 19:08")));

		SeqEq("默认隐藏的游戏不进日程", new[] { "a", "c", "d" },
			ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now).Items.Select(i => i.GameId));

		var shown = new WidgetSettings();
		shown.Shown.Add("b");
		SeqEq("shown 打开后进日程", new[] { "a", "b", "c", "d" },
			ScheduleBuilder.Build(feed, catalog, shown, Now).Items.Select(i => i.GameId));

		var hidden = new WidgetSettings();
		hidden.Hidden.Add("a");
		SeqEq("hidden 不进日程", new[] { "c", "d" },
			ScheduleBuilder.Build(feed, catalog, hidden, Now).Items.Select(i => i.GameId));

		var removed = new WidgetSettings();
		removed.Removed.Add("c");
		SeqEq("removed 不进日程", new[] { "a", "d" },
			ScheduleBuilder.Build(feed, catalog, removed, Now).Items.Select(i => i.GameId));

		var ordered = new WidgetSettings();
		ordered.Order.AddRange(new[] { "d", "a" });
		SeqEq("order 优先（日程顺序与表格一致）", new[] { "d", "a", "c" },
			ScheduleBuilder.Build(feed, catalog, ordered, Now).Items.Select(i => i.GameId));
	}

	// ================================================================ 23. 视图模型集成

	private static void TestScheduleViewModel()
	{
		Section("WidgetViewModel 暴露日程快照（属性 / 通知 / RowVm 挂载 / Tick 重算）");
		var catalog = MakeCatalogRich(("g1", "计时游戏", "#3FA9A0", ""), ("g2", "仅活动游戏", "", ""));
		var vm = new WidgetViewModel(catalog);
		var changed = new List<string>();
		vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

		var feed = MakeFeed(
			("g1", Rec(banner: "卡池", bannerDates: "10-19 00:00 ~ 11-03 19:08")),         // +13 天
			("g2", Rec(eventText: "活动", eventDates: "10-19 00:00 ~ 10-22 19:08")));      // 正好 +24 小时

		vm.Apply(feed, new WidgetSettings(), Now);
		NotNull("Apply 后有快照", vm.Schedule);
		Eq("Recent 数量与快照一致", vm.Schedule!.Recent.Count, vm.Recent.Count);
		Eq("NextUp 与快照一致", vm.Schedule.NextUp!.GameId, vm.NextUp!.GameId);
		Eq("NextTwo 数量与快照一致", vm.Schedule.NextTwo.Count, vm.NextTwo.Count);
		Eq("GlanceText 与快照一致", vm.Schedule.GlanceText, vm.GlanceText);
		Eq("GlanceText 文案", "24 小时内 1 条 · 7 天内 1 条", vm.GlanceText);
		True("Apply 通知 Schedule", changed.Contains(nameof(WidgetViewModel.Schedule)));
		True("Apply 通知 Recent", changed.Contains(nameof(WidgetViewModel.Recent)));
		True("Apply 通知 NextUp", changed.Contains(nameof(WidgetViewModel.NextUp)));
		True("Apply 通知 NextTwo", changed.Contains(nameof(WidgetViewModel.NextTwo)));
		True("Apply 通知 GlanceText", changed.Contains(nameof(WidgetViewModel.GlanceText)));

		var row1 = vm.Rows.First(r => r.GameId == "g1");
		var row2 = vm.Rows.First(r => r.GameId == "g2");
		NotNull("RowVm.Gacha 已挂载", row1.Gacha);
		Eq("RowVm.Gacha 就是快照里的那一条", vm.Schedule.Items.First(i => i.GameId == "g1" && i.Side == "gacha"), row1.Gacha);
		Eq("RowVm.Gacha.Side", "gacha", row1.Gacha!.Side);
		Eq("RowVm.Gacha 颜色来自 catalog", "#3FA9A0", row1.Gacha.Color);
		Eq("RowVm.Gacha 紧迫度（13 天）", "还剩 13 天", row1.Gacha.UrgencyText);
		Null("g1 无活动侧 → RowVm.Event 为 null", row1.Event);
		Null("g2 无卡池侧 → RowVm.Gacha 为 null", row2.Gacha);
		NotNull("g2 活动侧已挂载", row2.Event);
		Eq("RowVm.Event.Side", "event", row2.Event!.Side);

		// Tick 跨分钟：紧迫度文案要变、要发通知、RowVm 要跟着换成新条目
		changed.Clear();
		var before = row1.Gacha;
		vm.Tick(Now.AddMinutes(1));
		Eq("Tick 后紧迫度重算", "还剩 12 天",
			vm.Schedule!.Items.First(i => i.GameId == "g1").UrgencyText);
		True("Tick 通知 Schedule", changed.Contains(nameof(WidgetViewModel.Schedule)));
		True("Tick 通知 GlanceText", changed.Contains(nameof(WidgetViewModel.GlanceText)));
		True("Tick 后 RowVm.Gacha 指向新条目", !ReferenceEquals(before, row1.Gacha));
		Eq("RowVm 与快照保持同步",
			vm.Schedule.Items.First(i => i.GameId == "g1" && i.Side == "gacha"), row1.Gacha);

		changed.Clear();
		vm.Tick(Now.AddMinutes(1).AddSeconds(20));
		False("同一分钟内不重算、不通知", changed.Contains(nameof(WidgetViewModel.Schedule)));

		vm.ShowError("网络挂了");
		Null("ShowError 后快照为 null", vm.Schedule);
		Eq("ShowError 后 Recent 为空", 0, vm.Recent.Count);
		Eq("ShowError 后 NextTwo 为空", 0, vm.NextTwo.Count);
		Null("ShowError 后 NextUp 为 null", vm.NextUp);
		Eq("ShowError 后 GlanceText 空串", "", vm.GlanceText);
	}

	// ================================================================ 24. 真实数据

	private static void TestScheduleRealFeed()
	{
		Section("日程快照（真实 sample-feed 冒烟）");
		var catalog = GameCatalog.Load();
		var feed = FeedDocument.Parse(EmbeddedData.SampleFeedJson())!;
		var snap = ScheduleBuilder.Build(feed, catalog, new WidgetSettings(), Now);

		True("真实 feed 有条目", snap.Items.Count > 20);
		True("条目数不超过「可见游戏 × 2」", snap.Items.Count <= 23 * 2);
		Eq("每条都有 #RRGGBB 颜色", snap.Items.Count,
			snap.Items.Count(i => Regex.IsMatch(i.Color, "^#[0-9A-Fa-f]{6}$")));
		Eq("每条都有 GameName", snap.Items.Count, snap.Items.Count(i => i.GameName.Length > 0));
		True("GlanceText 格式正确", Regex.IsMatch(snap.GlanceText, @"^24 小时内 \d+ 条 · 7 天内 \d+ 条$"));
		True("Recent 不超过 6 条", snap.Recent.Count <= 6);
		True("Recent 全是在期", snap.Recent.All(i => i.State == ScheduleState.Live));
		True("Recent 按 End 升序", snap.Recent.Select(i => i.End)
			.SequenceEqual(snap.Recent.Select(i => i.End).OrderBy(x => x)));
		True("NextTwo 至多 2 条", snap.NextTwo.Count <= 2);
		True("在期条目的紧迫度文案只有两种前缀",
			snap.Items.Where(i => i.State == ScheduleState.Live).All(i =>
				i.UrgencyText.StartsWith("还剩 ", StringComparison.Ordinal) || i.UrgencyText == "今天到期"));
		True("条目颜色都来自 catalog 或固定色盘",
			snap.Items.All(i => catalog.Games.Any(g => g.Id == i.GameId && g.Color == i.Color)
				|| i.Color.Length == 7));

		var vm = new WidgetViewModel(catalog);
		vm.Apply(feed, new WidgetSettings(), Now);
		Eq("真实数据 23 行（5 款出厂默认隐藏）", 23, vm.Rows.Count);
		True("每行两侧条目的类型正确",
			vm.Rows.All(r => (r.Gacha == null || r.Gacha.IsGacha) && (r.Event == null || !r.Event.IsGacha)));
		True("每行至少一侧挂了日程条目", vm.Rows.Any(r => r.Gacha != null || r.Event != null));
	}

	// ================================================================ 25. FeedClient

	private static async Task TestFeedClient()
	{
		Section("FeedClient（ETag/304/缓存/示例兜底/可注入 handler）");
		var url = "https://example.test/latest.json";
		var sampleJson = EmbeddedData.SampleFeedJson();

		// A. 200 + ETag + Last-Modified → Fresh，写缓存三件套
		var dirA = NewTempDir();
		try
		{
			var handlerA = new FakeHandler(_ =>
			{
				var response = JsonResponse(sampleJson);
				response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
				response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-10-21T03:53:54Z");
				return response;
			});
			using (var client = new FeedClient(handlerA, dirA))
			{
				False("首次启动无缓存时 TryLoadCached=false", client.TryLoadCached(out _, out var missError));
				NotNull("无缓存时给出原因", missError);

				var result = await client.FetchAsync(url);
				Eq("200 → Fresh", FeedStatus.Fresh, result.Status);
				True("200 → 有文档", result.Ok);
				Eq("200 → 28 款", 28, result.Document!.Games.Count);
				Null("200 → 无错误", result.Error);
				True("UA 自定义", handlerA.LastUserAgent?.Contains("WidBarGachaCalendar", StringComparison.Ordinal) == true,
					handlerA.LastUserAgent);
				True("首次请求没有条件头", handlerA.LastIfNoneMatch == null && handlerA.LastIfModifiedSince == null);

				True("首帧读缓存命中", client.TryLoadCached(out var cachedDoc, out _));
				Eq("首帧读缓存 28 款", 28, cachedDoc!.Games.Count);
			}

			True("缓存 feed.json 落盘", File.Exists(Path.Combine(dirA, "feed.json")));
			True("缓存 feed.etag 落盘", File.Exists(Path.Combine(dirA, "feed.etag")));
			True("缓存 feed.lastmod 落盘", File.Exists(Path.Combine(dirA, "feed.lastmod")));
			Eq("ETag 内容原样写入", "\"v1\"", File.ReadAllText(Path.Combine(dirA, "feed.etag")).Trim());

			// B. 304 → NotModified，用缓存；请求带 If-None-Match
			var handlerB = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
			using (var clientB = new FeedClient(handlerB, dirA))
			{
				var result = await clientB.FetchAsync(url);
				Eq("304 → NotModified", FeedStatus.NotModified, result.Status);
				Eq("304 → 用缓存文档", 28, result.Document!.Games.Count);
				Null("304 不算错误", result.Error);
				Eq("304 请求带 If-None-Match", "\"v1\"", handlerB.LastIfNoneMatch);
				Null("304 请求不再带 If-Modified-Since", handlerB.LastIfModifiedSince);
			}

			// C. 500 → 回退缓存
			var handlerC = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
			using (var clientC = new FeedClient(handlerC, dirA))
			{
				var result = await clientC.FetchAsync(url);
				Eq("500 → Cached", FeedStatus.Cached, result.Status);
				Eq("500 仍返回缓存文档", 28, result.Document!.Games.Count);
				Contains("500 错误信息含状态码", "500", result.Error);
			}

			// J. 断网 → 回退缓存
			var handlerJ = new FakeHandler(_ => throw new HttpRequestException("模拟断网"));
			using (var clientJ = new FeedClient(handlerJ, dirA))
			{
				var result = await clientJ.FetchAsync(url);
				Eq("断网 → Cached", FeedStatus.Cached, result.Status);
				Contains("断网错误信息", "模拟断网", result.Error);
			}

			// I. 新版 200 不再带 ETag → 旧 etag 文件被清掉（避免拿着过期校验器打服务器）
			var handlerI = new FakeHandler(_ => JsonResponse(sampleJson));
			using (var clientI = new FeedClient(handlerI, dirA))
			{
				var result = await clientI.FetchAsync(url);
				Eq("无 ETag 的 200 仍 Fresh", FeedStatus.Fresh, result.Status);
			}

			False("服务器不再给 ETag 时清掉本地 etag", File.Exists(Path.Combine(dirA, "feed.etag")));
		}
		finally
		{
			TryCleanup(dirA);
		}

		// D. 无缓存 + 断网 → 内置示例
		var dirD = NewTempDir();
		try
		{
			var handlerD = new FakeHandler(_ => throw new HttpRequestException("模拟断网"));
			using var clientD = new FeedClient(handlerD, dirD);
			var result = await clientD.FetchAsync(url);
			Eq("无缓存断网 → Sample", FeedStatus.Sample, result.Status);
			Eq("示例 feed 也是 28 款", 28, result.Document!.Games.Count);
			Contains("示例兜底保留原始错误", "模拟断网", result.Error);
			Eq("示例数据 meta.total", 28, result.Document.Meta!.Total);
		}
		finally
		{
			TryCleanup(dirD);
		}

		// E. 无缓存 + 500 → 内置示例
		var dirE = NewTempDir();
		try
		{
			var handlerE = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
			using var clientE = new FeedClient(handlerE, dirE);
			var result = await clientE.FetchAsync(url);
			Eq("无缓存 503 → Sample", FeedStatus.Sample, result.Status);
			Contains("503 错误信息", "503", result.Error);
		}
		finally
		{
			TryCleanup(dirE);
		}

		// F. 200 但内容坏 → 无缓存回退示例，错误说明解析失败
		var dirF = NewTempDir();
		try
		{
			var handlerF = new FakeHandler(_ => JsonResponse("{ broken json"));
			using var clientF = new FeedClient(handlerF, dirF);
			var result = await clientF.FetchAsync(url);
			Eq("坏 JSON → Sample", FeedStatus.Sample, result.Status);
			Contains("坏 JSON 错误信息", "解析失败", result.Error);
			False("坏 JSON 不写 feed.json", File.Exists(Path.Combine(dirF, "feed.json")));
		}
		finally
		{
			TryCleanup(dirF);
		}

		// G. 带 UTF-8 BOM 的响应也能解析
		var dirG = NewTempDir();
		try
		{
			var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(sampleJson)).ToArray();
			var handlerG = new FakeHandler(_ =>
			{
				var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(withBom) };
				response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
				return response;
			});
			using var clientG = new FeedClient(handlerG, dirG);
			var result = await clientG.FetchAsync(url);
			Eq("BOM 响应 → Fresh", FeedStatus.Fresh, result.Status);
			Eq("BOM 响应解析出 28 款", 28, result.Document!.Games.Count);
		}
		finally
		{
			TryCleanup(dirG);
		}

		// H. 只有 Last-Modified 时用 If-Modified-Since（没有 ETag）
		var dirH = NewTempDir();
		try
		{
			var handlerH1 = new FakeHandler(_ =>
			{
				var response = JsonResponse(sampleJson);
				response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-10-20T00:00:00Z");
				return response;
			});
			using (var clientH1 = new FeedClient(handlerH1, dirH))
			{
				var first = await clientH1.FetchAsync(url);
				Eq("只有 Last-Modified 的 200 → Fresh", FeedStatus.Fresh, first.Status);
			}

			var handlerH2 = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
			using (var clientH2 = new FeedClient(handlerH2, dirH))
			{
				var second = await clientH2.FetchAsync(url);
				Eq("Last-Modified 命中 304", FeedStatus.NotModified, second.Status);
				NotNull("第二次请求带 If-Modified-Since", handlerH2.LastIfModifiedSince);
				Eq("If-Modified-Since 值正确",
					DateTimeOffset.Parse("2026-10-20T00:00:00Z"),
					handlerH2.LastIfModifiedSince!.Value.ToUniversalTime());
			}
		}
		finally
		{
			TryCleanup(dirH);
		}
	}

	private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
	{
		var response = new HttpResponseMessage(status)
		{
			Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json)),
		};
		response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		return response;
	}

	/// <summary>可模拟 200/304/500/断网的 handler，并记录请求上的条件头，供断言使用。</summary>
	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

		public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

		public string? LastIfNoneMatch { get; private set; }

		public DateTimeOffset? LastIfModifiedSince { get; private set; }

		public string? LastUserAgent { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastIfNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values)
				? string.Join(",", values)
				: null;
			LastIfModifiedSince = request.Headers.IfModifiedSince;
			LastUserAgent = request.Headers.UserAgent.ToString();
			try
			{
				return Task.FromResult(_responder(request));
			}
			catch (Exception ex)
			{
				return Task.FromException<HttpResponseMessage>(ex);
			}
		}
	}
}
