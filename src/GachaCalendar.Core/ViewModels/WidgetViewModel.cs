using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GachaCalendar.Core.Models;
using GachaCalendar.Core.Services;

namespace GachaCalendar.Core.ViewModels;

/// <summary>
/// 壳无关视图模型：把 feed + settings + catalog 变成"外壳能直接绑定"的行、chips 与顶部状态。
/// 移植自姊妹仓库 WPF 版 <c>MainViewModel.cs</c>，去掉了 Brush / ThemeManager / Schedule / EngineHost
/// 这些外壳专属内容；可见性规则与顶部状态口径保持一致。
/// </summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
	private FeedDocument? _feed;
	private WidgetSettings _settings = new();
	private GameCatalog? _catalog;
	private DateTime _lastNow = DateTime.Now;
	private int _lastMinute = -1;
	private FeedStatus _status = FeedStatus.Fresh;
	private bool _isSample;

	private string _statusText = "载入中…";
	private string _statusTip = "";
	private string _updatedText = "—";
	private string _headerCount = "";
	private string _dataStatusText = "数据：—";
	private string _scrapeText = "";
	private string _errorText = "";
	private string _feedError = "";
	private bool _hasError;
	private bool _showGacha = true;
	private bool _showEvent = true;

	public ObservableCollection<RowVm> Rows { get; } = new();

	public ObservableCollection<ChipVm> Chips { get; } = new();

	public string Title => "二游日历";

	public WidgetViewModel()
	{
	}

	/// <summary>
	/// 外壳推荐用法（docs/03 生命周期）：<c>var store = new CatalogStore(); var vm = new WidgetViewModel(store.Catalog);</c>
	/// 之后 <see cref="Apply(FeedDocument, WidgetSettings, DateTime)"/> 不必再传 catalog。
	/// </summary>
	public WidgetViewModel(GameCatalog catalog)
	{
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
	}

	/// <summary>当前 catalog（构造时注入，或最近一次 Apply 传入）。</summary>
	public GameCatalog? Catalog => _catalog;

	public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
	public string StatusTip { get => _statusTip; private set => Set(ref _statusTip, value); }
	public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }
	public string HeaderCount { get => _headerCount; private set => Set(ref _headerCount, value); }
	public string DataStatusText { get => _dataStatusText; private set => Set(ref _dataStatusText, value); }
	public string ScrapeText { get => _scrapeText; private set => Set(ref _scrapeText, value); }
	public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
	public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }

	/// <summary>最近一次取数的非致命错误（缓存/示例兜底时也非空），外壳可选择展示。</summary>
	public string FeedError { get => _feedError; private set => Set(ref _feedError, value); }

	/// <summary>当前数据的来源状态。</summary>
	public FeedStatus Status { get => _status; private set => Set(ref _status, value); }

	public bool IsSample
	{
		get => _isSample;
		private set
		{
			if (!Set(ref _isSample, value)) return;
			Raise(nameof(DataStatusText));
		}
	}

	public bool ShowGacha { get => _showGacha; private set => Set(ref _showGacha, value); }

	public bool ShowEvent { get => _showEvent; private set => Set(ref _showEvent, value); }

	public bool HasChips => Chips.Count > 0;

	public bool HasNoChips => Chips.Count == 0;

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>4 参重载：调用方未区分数据来源时按"联网新数据"处理。</summary>
	public void Apply(FeedDocument feed, WidgetSettings settings, GameCatalog catalog, DateTime now)
		=> Apply(feed, settings, catalog, now, FeedStatus.Fresh);

	public void Apply(FeedDocument feed, WidgetSettings settings, GameCatalog catalog, DateTime now, FeedStatus status)
	{
		ArgumentNullException.ThrowIfNull(feed);
		ArgumentNullException.ThrowIfNull(catalog);

		_feed = feed;
		_catalog = catalog;
		_settings = settings ?? new WidgetSettings();
		// 兜底：调用方可能还没 Normalize（例如从 JSON 直接反序列化后忘了调用）。
		_settings.Normalize(catalog);
		_status = status;
		_isSample = status == FeedStatus.Sample;
		_lastNow = now;
		ShowGacha = _settings.ShowGacha;
		ShowEvent = _settings.ShowEvent;

		Rows.Clear();
		foreach (var entry in BuildOrderedVisible(catalog, _settings))
		{
			Rows.Add(new RowVm(entry, feed.Record(entry.Id))
			{
				ShowGacha = _settings.ShowGacha,
				ShowEvent = _settings.ShowEvent,
			});
		}

		HasError = false;
		ErrorText = "";
		FeedError = "";
		HeaderCount = $"{Rows.Count} 款";
		RefreshStatus();
		Tick(now, force: true);
	}

	/// <summary>推荐入口：直接吃 FeedClient 的结果，自动带上 Fresh/NotModified/Cached/Sample 语义。</summary>
	public void Apply(FeedResult result, WidgetSettings settings, GameCatalog catalog, DateTime now)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.Document == null)
		{
			ShowError(result.Error ?? "数据源不可用");
			return;
		}

		Apply(result.Document, settings, catalog, now, result.Status);
		FeedError = result.Error ?? "";
	}

	// 以下三个重载用构造时注入的 catalog（docs/03 的用法），省得每个调用点都传。

	public void Apply(FeedDocument feed, WidgetSettings settings, DateTime now)
		=> Apply(feed, settings, RequireCatalog(), now);

	public void Apply(FeedDocument feed, WidgetSettings settings, DateTime now, FeedStatus status)
		=> Apply(feed, settings, RequireCatalog(), now, status);

	public void Apply(FeedResult result, WidgetSettings settings, DateTime now)
		=> Apply(result, settings, RequireCatalog(), now);

	private GameCatalog RequireCatalog()
		=> _catalog ?? throw new InvalidOperationException(
			"WidgetViewModel 还没有 catalog：先用 new WidgetViewModel(catalog) 构造，或调用带 GameCatalog 参数的 Apply。");

	/// <summary>设置变更后重建行（可见性/顺序/两列开关都受影响）。</summary>
	public void UpdateSettings(WidgetSettings settings)
	{
		if (_feed == null || _catalog == null)
		{
			_settings = settings ?? new WidgetSettings();
			return;
		}

		Apply(_feed, settings, _catalog, _lastNow, _status);
	}

	/// <summary>分钟级刷新：行文案 + chips 剩余时间。force 用于 Apply 后立刻算一遍。</summary>
	public void Tick(DateTime now, bool force = false)
	{
		if (_feed == null) return;
		if (!force && now.Minute == _lastMinute) return;
		_lastMinute = now.Minute;
		_lastNow = now;
		foreach (var row in Rows) row.Refresh(now);
		RebuildChips(now);
	}

	public void ShowError(string message)
	{
		_feed = null;
		Rows.Clear();
		Chips.Clear();
		HasError = true;
		ErrorText = message;
		StatusText = "载入失败";
		DataStatusText = "数据：载入失败";
		ScrapeText = message;
		StatusTip = message;
		UpdatedText = "刷新：—";
		HeaderCount = "";
		FeedError = message;
		Raise(nameof(HasChips));
		Raise(nameof(HasNoChips));
	}

	// ---------------------------------------------------------------- 内部

	/// <summary>
	/// 可见性三态（与 core/docs 口径一致）：
	/// removed/hidden 含 id → 隐藏；defaultHidden 且不在 shown → 隐藏；
	/// 顺序：settings.order 优先，其余按 catalog.order 补后。
	/// </summary>
	private static List<CatalogEntry> BuildOrderedVisible(GameCatalog catalog, WidgetSettings settings)
	{
		var hidden = new HashSet<string>(settings.Hidden, StringComparer.Ordinal);
		var shown = new HashSet<string>(settings.Shown, StringComparer.Ordinal);
		var removed = new HashSet<string>(settings.Removed, StringComparer.Ordinal);

		bool Visible(CatalogEntry entry)
			=> !removed.Contains(entry.Id)
				&& !hidden.Contains(entry.Id)
				&& !(entry.DefaultHidden && !shown.Contains(entry.Id));

		var result = new List<CatalogEntry>(catalog.Count);
		var added = new HashSet<string>(StringComparer.Ordinal);

		foreach (var id in settings.Order)
		{
			if (!catalog.TryGet(id, out var entry)) continue;
			if (!Visible(entry) || !added.Add(entry.Id)) continue;
			result.Add(entry);
		}

		foreach (var entry in catalog.Games)
		{
			if (!Visible(entry) || !added.Add(entry.Id)) continue;
			result.Add(entry);
		}

		return result;
	}

	private void RefreshStatus()
	{
		if (_feed == null) return;
		var total = Rows.Count;
		var ok = 0;
		var gachaDown = new List<string>();
		var eventDown = new List<string>();
		var gachaNomatch = new List<string>();
		var eventNomatch = new List<string>();

		foreach (var row in Rows)
		{
			var rec = _feed.Record(row.GameId);
			// 没有记录 / 本轮被跳过：不计入成功数（与 WPF 版一致）。
			if (rec == null || rec.Skipped) continue;
			var gDown = rec.GachaFail?.Kind == "down";
			var eDown = rec.EventFail?.Kind == "down";
			if (!gDown && !eDown) ok++;
			if (gDown) gachaDown.Add(row.Name);
			if (eDown) eventDown.Add(row.Name);
			if (rec.GachaFail?.Kind == "nomatch") gachaNomatch.Add(row.Name);
			if (rec.EventFail?.Kind == "nomatch") eventNomatch.Add(row.Name);
		}

		var prefix = IsSample ? "示例数据" : (ok > 0 ? "联网数据" : "尚未联网");
		StatusText = $"{prefix} · 成功 {ok}/{total}";
		DataStatusText = "数据：" + prefix;

		var lines = new List<string>();
		void Add(string label, List<string> names)
		{
			if (names.Count == 0) return;
			var shown = string.Join("、", names.Take(8));
			var more = names.Count > 8 ? $" 等 {names.Count} 个" : "";
			lines.Add($"{label}：{shown}{more}");
		}

		Add("卡池失败", gachaDown);
		Add("活动失败", eventDown);
		Add("新卡池未公布", gachaNomatch);
		Add("新活动未公布", eventNomatch);
		StatusTip = lines.Count == 0 ? "所有可见条目都抓到了当期内容" : string.Join("\n", lines);
		ScrapeText = $"成功 {ok}/{total}";
		if (lines.Count > 0) ScrapeText += "  " + string.Join("  ", lines);

		var ts = _feed.RefreshedAt;
		if (ts <= 0 && _feed.Meta?.GeneratedAtUtc is { } generated) ts = generated.ToUnixTimeMilliseconds();
		UpdatedText = ts > 0
			? "刷新：" + DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("yyyy/M/d HH:mm:ss")
			: "刷新：—";
	}

	private void RebuildChips(DateTime now)
	{
		Chips.Clear();
		if (_feed == null) return;

		var candidates = new List<(DateTime End, CatalogEntry Game, GameRecord Rec, string Kind)>();
		foreach (var row in Rows)
		{
			var rec = _feed.Record(row.GameId);
			if (rec == null) continue;
			var bannerWindow = RowBuilder.ParseRange(rec.BannerDates, now);
			if (bannerWindow is { } b && now >= b.Start && now <= b.End)
				candidates.Add((b.End, row.Game, rec, "卡池"));
			var eventWindow = RowBuilder.ParseRange(rec.EventDates, now);
			if (eventWindow is { } e && now >= e.Start && now <= e.End)
				candidates.Add((e.End, row.Game, rec, "活动"));
		}

		// 每款游戏最多一个 chip（取它最快结束的那一项），再按结束时间升序取前 N 个。
		var picked = candidates
			.GroupBy(c => c.Game.Id)
			.Select(g => g.OrderBy(c => c.End).First())
			.OrderBy(c => c.End)
			.Take(Math.Clamp(_settings.PreviewItems, WidgetSettings.MinPreviewItems, WidgetSettings.MaxPreviewItems))
			.ToList();

		for (var i = 0; i < picked.Count; i++)
		{
			var (end, game, rec, kind) = picked[i];
			var title = kind == "卡池"
				? (string.IsNullOrWhiteSpace(rec.Banner) ? "—" : rec.Banner)
				: (string.IsNullOrWhiteSpace(rec.Event) ? "—" : rec.Event);
			var dates = kind == "卡池" ? rec.BannerDates : rec.EventDates;
			var full = RowBuilder.FormatRemaining(end, now);
			Chips.Add(new ChipVm
			{
				GameId = game.Id,
				GameName = ShortName(game.Name),
				FullName = game.Name,
				Kind = kind,
				Remaining = RowBuilder.FormatShortRemaining(end - now),
				// 6 号强调色轮换，具体色值由外壳主题决定。
				AccentIndex = i % 6,
				Tip = $"{game.Name}\n{kind}：{title}\n{dates}\n还剩 {full}",
			});
		}

		Raise(nameof(HasChips));
		Raise(nameof(HasNoChips));
	}

	private static string ShortName(string name)
	{
		if (name.Length <= 9) return name;
		return name[..8] + "…";
	}

	private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return false;
		field = value;
		Raise(name);
		return true;
	}

	private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
