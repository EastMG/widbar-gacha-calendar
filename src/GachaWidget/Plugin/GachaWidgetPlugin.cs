using GachaCalendar.Core.Models;
using GachaCalendar.Core.Services;
using GachaCalendar.Core.ViewModels;
using GachaWidget.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WidBar.SDK;

namespace GachaWidget.Plugin;

/// <summary>
/// WidBar 插件实现：一个实例 = 任务栏上的一个"二游日历"槽位。
///
/// 职责边界：
/// - 数据：喂给 Core 的 FeedClient / WidgetViewModel（Core 不认识 UI）；
/// - UI：只创建三块 UIElement（预览 / 展开面板 / 设置），位置与主题由 WidBar 负责；
/// - 生命周期：每分钟 tick 更新倒计时，按 settings 的间隔后台刷新，进 Smart Stack 后台时降载。
/// </summary>
public sealed class GachaWidgetPlugin : WidgetPluginBase, IConfigurableWidgetPlugin, IWidgetFlyoutLifecycle
{
	private const string PluginVersion = "0.1.12";
	private const string PluginId = "com.eastmg.gachacalendar";
	private const string PluginName = "二游日历";

	/// <summary>每个 chip 预留的逻辑宽度（含间距）；预览条总宽由当前 chip 数决定。</summary>
	private const int ChipWidth = 112;

	private IWidgetContext? _context;
	private DispatcherQueue? _queue;
	private CatalogStore? _catalogStore;
	private FeedClient? _feedClient;
	private WidgetViewModel? _vm;
	private WidgetSettings _settings = new();
	private PreviewView? _preview;
	private FlyoutView? _flyout;
	private DispatcherQueueTimer? _tickTimer;
	private CancellationTokenSource? _cts;
	private bool _refreshing;
	private bool _previewVisible = true;
	private DateTime _lastSuccessUtc = DateTime.MinValue;

	public override string Id => PluginId;

	public override string Name => PluginName;

	public override string Version => PluginVersion;

	/// <summary>预览宽度跟着当前实际显示几条变化（1~3 条）。</summary>
	public override int PreviewLogicalWidth
	{
		get
		{
			var count = _vm?.Chips.Count ?? 0;
			if (count <= 0) count = Math.Clamp(_settings.PreviewItems, 1, 3);
			return Math.Clamp(count, 1, 3) * ChipWidth + 8;
		}
	}

	public override int FlyoutWidth => 830;

	public override int FlyoutHeight => 580;

	// ---------------------------------------------------------------- 生命周期

	public override async Task InitializeAsync(IWidgetContext context)
	{
		Program.Log($"InitializeAsync begin; previewVisible={context.IsPreviewVisible}; dataDir={context.DataDirectory}");
		_context = context;

		// 宿主给的 DataDirectory 未必可写（widget 与宿主是两个包），先探出真正可写的位置。
		var workDir = GachaWidget.Diagnostics.RuntimePaths.Resolve(context.DataDirectory, context.InstanceId);
		GachaWidget.Diagnostics.PluginLog.Initialize(workDir);
		GachaWidget.Diagnostics.PluginLog.Write(
			$"dirs: host='{context.DataDirectory}' -> work='{workDir}'");

		_queue = DispatcherQueue.GetForCurrentThread();
		_catalogStore = new CatalogStore();
		_vm = new WidgetViewModel(_catalogStore.Catalog);
		_settings = WidgetSettings.FromJson(context.SettingsJson, _catalogStore.Catalog);
		_feedClient = new FeedClient(cacheDirectory: workDir);
		_cts = new CancellationTokenSource();
		_previewVisible = context.IsPreviewVisible;
		context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;
		GachaWidget.Diagnostics.PluginLog.Write(
			$"Init v{PluginVersion}: catalog={_catalogStore.Catalog.Count}; feedUrl={_settings.FeedUrl}; " +
			$"previewItems={_settings.PreviewItems}; autoRefresh={_settings.AutoRefresh}/{_settings.RefreshMinutes}min; " +
			$"previewVisible={context.IsPreviewVisible}");

		// 首帧必须立刻有内容：本地缓存 → 内置示例，任何情况下都不等网络。
		try
		{
			if (_feedClient.TryLoadCached(out var cached, out _) && cached is not null)
			{
				_vm.Apply(cached, _settings, DateTime.Now, FeedStatus.Cached);
				_lastSuccessUtc = ToUtc(cached.RefreshedAt);
				GachaWidget.Diagnostics.PluginLog.Write($"first frame: cache, records={cached.Games.Count}");
			}
			else if (EmbeddedData.TrySampleFeed(out var sampleJson, out _)
				&& FeedDocument.Parse(sampleJson) is { } sample)
			{
				_vm.Apply(sample, _settings, DateTime.Now, FeedStatus.Sample);
				GachaWidget.Diagnostics.PluginLog.Write($"first frame: sample, records={sample.Games.Count}");
			}
			else
			{
				_vm.ShowError("没有可用数据：缓存与内置示例都读取失败。");
				GachaWidget.Diagnostics.PluginLog.Write("first frame: NO DATA");
			}
		}
		catch (Exception ex)
		{
			_vm.ShowError("初始化失败：" + ex.Message);
			GachaWidget.Diagnostics.PluginLog.Write("first frame exception: " + ex);
		}
		GachaWidget.Diagnostics.PluginLog.Write($"rows={_vm.Rows.Count}; chips={_vm.Chips.Count}; status={_vm.StatusText}");

		StartTickTimer();

		Program.Log("InitializeAsync: first frame applied, starting background refresh");

		// 首次刷新放到后台，不阻塞宿主创建预览。
		_ = RefreshAsync(force: false);
		await Task.CompletedTask;
		Program.Log("InitializeAsync end");
	}

	public override Microsoft.UI.Xaml.UIElement? CreatePreviewContent()
	{
		GachaWidget.Diagnostics.PluginLog.Write($"CreatePreviewContent; chips={_vm?.Chips.Count}; width={PreviewLogicalWidth}");
		if (_vm is null) return null;
		_preview = new PreviewView(_vm);
		return _preview;
	}

	public override Microsoft.UI.Xaml.UIElement CreateFlyoutContent()
	{
		GachaWidget.Diagnostics.PluginLog.Write($"CreateFlyoutContent; rows={_vm?.Rows.Count}");
		_flyout = new FlyoutView(_vm ?? throw new InvalidOperationException("InitializeAsync 尚未完成"));
		_flyout.RefreshRequested += (_, _) => _ = RefreshAsync(force: true);
		return _flyout;
	}

	public UIElement? CreateSettingsContent(IWidgetSettingsContext context)
	{
		if (_vm is null || _catalogStore is null) return null;

		GachaWidget.Diagnostics.PluginLog.Write("CreateSettingsContent");
		var view = new SettingsView(_settings, _catalogStore.Catalog, CheckFeedAsync);
		view.Changed += settings =>
		{
			var json = settings.ToJson();
			ApplySettingsDraft(json);
			context.SaveSettings(json);
		};
		return view;
	}

	/// <summary>设置页改动时宿主会回调这里（用户取消时会带着旧 JSON 再调一次）。</summary>
	public override void OnSettingsDraftChanged(string settingsJson) => ApplySettingsDraft(settingsJson);

	public override ValueTask DisposeAsync()
	{
		try
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;

			if (_tickTimer is not null)
			{
				_tickTimer.Tick -= OnTick;
				_tickTimer.Stop();
				_tickTimer = null;
			}

			if (_context is not null)
			{
				_context.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;
			}

			_feedClient?.Dispose();
			_feedClient = null;
			_flyout = null;
			_preview = null;
			_vm = null;
		}
		catch
		{
			// 释放阶段不再抛出：宿主正在退出。
		}

		return ValueTask.CompletedTask;
	}

	// ---------------------------------------------------------------- flyout 生命周期

	public void OnFlyoutShown()
	{
		// 面板打开时顺手检查一次是否到期（便宜：只看时间）。
		_ = RefreshAsync(force: false);
	}

	public void OnFlyoutHidden()
	{
		// 面板没有常驻动画/轮播，无需暂停；将来加动画时在这里停。
	}

	// ---------------------------------------------------------------- 内部

	private void ApplySettingsDraft(string settingsJson)
	{
		if (_catalogStore is null || _vm is null) return;

		_settings = WidgetSettings.FromJson(settingsJson, _catalogStore.Catalog);
		_vm.UpdateSettings(_settings);
		_context?.RequestPreviewRefresh();
	}

	private void OnPreviewVisibilityChanged(object? sender, bool visible)
	{
		_previewVisible = visible;
		if (visible)
		{
			// 回到前台：补一次 tick，避免显示上一分钟的倒计时。
			_vm?.Tick(DateTime.Now, force: true);
		}
	}

	private void StartTickTimer()
	{
		if (_queue is null) return;

		_tickTimer = _queue.CreateTimer();
		_tickTimer.Interval = TimeSpan.FromSeconds(20);
		_tickTimer.IsRepeating = true;
		_tickTimer.Tick += OnTick;
		_tickTimer.Start();
	}

	private void OnTick(DispatcherQueueTimer sender, object args)
	{
		_vm?.Tick(DateTime.Now);

		if (_settings.AutoRefresh && IsRefreshDue())
		{
			_ = RefreshAsync(force: false);
		}
	}

	private bool IsRefreshDue()
	{
		if (_lastSuccessUtc == DateTime.MinValue) return true;
		return DateTime.UtcNow - _lastSuccessUtc >= TimeSpan.FromMinutes(_settings.RefreshMinutes);
	}

	private static DateTime ToUtc(long unixMs)
	{
		if (unixMs <= 0) return DateTime.MinValue;
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return DateTime.MinValue;
		}
	}

	private async Task RefreshAsync(bool force)
	{
		if (_refreshing || _context is null || _feedClient is null || _vm is null) return;
		if (!force && !IsRefreshDue()) return;

		_refreshing = true;
		try
		{
			_flyout?.SetRefreshing(true);
			var token = _cts?.Token ?? CancellationToken.None;
			var started = DateTime.UtcNow;
			GachaWidget.Diagnostics.PluginLog.Write($"refresh start: {_settings.FeedUrl} (force={force})");
			var result = await _feedClient.FetchAsync(_settings.FeedUrl, token);
			var elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;

			var beforeCount = _vm.Chips.Count;
			_vm.Apply(result, _settings, DateTime.Now);
			if (result.Ok)
			{
				_lastSuccessUtc = DateTime.UtcNow;
			}

			GachaWidget.Diagnostics.PluginLog.Write(
				$"refresh done: status={result.Status}; ok={result.Ok}; error={result.Error ?? "-"}; " +
				$"records={result.Document?.Games.Count ?? 0}; {elapsed}ms; chips={_vm.Chips.Count}; statusText={_vm.StatusText}");

			// chip 数可能变了（预览宽度跟着变），请求宿主重排一次。
			if (_vm.Chips.Count != beforeCount) _context.RequestPreviewRefresh();
			_flyout?.SetRefreshing(false);
		}
		catch (OperationCanceledException)
		{
			// 退出中，忽略。
		}
		catch (Exception ex)
		{
			GachaWidget.Diagnostics.PluginLog.Write("refresh exception: " + ex);
			_vm?.ShowError("刷新失败：" + ex.Message);
			_context?.RequestPreviewRefresh();
		}
		finally
		{
			_refreshing = false;
			_flyout?.SetRefreshing(false);
		}
	}

	// ---------------------------------------------------------------- 设置页：数据检查

	private async Task<string> CheckFeedAsync(string url)
	{
		if (_context is null) return "不可用：widget 尚未初始化";

		try
		{
			// 独立缓存目录：自检不应覆盖正常缓存。
			using var probe = new FeedClient(cacheDirectory: Path.Combine(GachaWidget.Diagnostics.RuntimePaths.Chosen ?? _context.DataDirectory, "selfcheck"));
			var result = await probe.FetchAsync(url);
			if (!result.Ok || result.Document is null)
			{
				return $"✗ 拉取失败：{result.Error ?? "未知原因"}";
			}

			var doc = result.Document;
			var statusLabel = result.Status switch
			{
				FeedStatus.Fresh => "最新（已下载）",
				FeedStatus.NotModified => "未变化（304）",
				FeedStatus.Cached => "网络失败，用的是缓存",
				FeedStatus.Sample => "网络失败，用的是示例数据",
				_ => "未知",
			};

			var refreshed = doc.RefreshedAt > 0
				? DateTimeOffset.FromUnixTimeMilliseconds(doc.RefreshedAt).ToLocalTime().ToString("yyyy/M/d HH:mm:ss")
				: "—";

			var down = doc.Games.Where(kv => kv.Value.GachaFail?.Kind == "down" || kv.Value.EventFail?.Kind == "down")
				.Select(kv => kv.Value.Name)
				.ToList();
			var nomatch = doc.Games.Count(kv => kv.Value.GachaFail?.Kind == "nomatch" || kv.Value.EventFail?.Kind == "nomatch");

			var lines = new List<string>
			{
				$"✓ 数据可用：{statusLabel}",
				$"源站抓取时间：{refreshed}（本机已解析 {doc.Games.Count} 款）",
				$"报错条目：{down.Count} 个{(down.Count > 0 ? "（" + string.Join("、", down.Take(6)) + (down.Count > 6 ? " 等" : "") + "）" : "")}",
				$"未公布当期：{nomatch} 个",
			};
			if (result.IsOffline) lines.Add("提示：当前展示的是缓存/示例数据，请检查网络或数据源地址。");
			return string.Join("\n", lines);
		}
		catch (Exception ex)
		{
			return "✗ 检查异常：" + ex.Message;
		}
	}
}
