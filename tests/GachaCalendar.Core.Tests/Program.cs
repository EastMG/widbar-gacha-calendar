using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
				icon = "",
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

	// ================================================================ 15. FeedClient

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
