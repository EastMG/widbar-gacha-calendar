using System.Collections.ObjectModel;
using System.ComponentModel;
using GachaCalendar.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GachaWidget.Views;

/// <summary>设置页里"条目"列表的一行。可见性由设置页算好，勾选改动回调回设置页。</summary>
public sealed class SettingsEntryVm : INotifyPropertyChanged
{
	private bool _isVisible;

	public string Id { get; init; } = "";
	public string Name { get; init; } = "";
	public bool DefaultHidden { get; init; }

	public bool IsVisible
	{
		get => _isVisible;
		set
		{
			if (_isVisible == value) return;
			_isVisible = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// 设置页：显示 / 刷新 / 数据源 / 条目显隐与排序 / 关于。
/// 宿主（WidBar）负责提供 Mica 窗口和保存/取消按钮，这里只负责"改草稿 + 通知"。
/// </summary>
public sealed partial class SettingsView : UserControl
{
	private readonly WidgetSettings _settings;
	private readonly GameCatalog _catalog;
	private readonly Func<string, Task<string>> _checkFeed;
	private readonly ObservableCollection<SettingsEntryVm> _entries = new();
	private bool _loading = true;

	/// <summary>任何设置改动都会带着归一化后的完整设置触发一次（宿主用于实时预览）。</summary>
	public event Action<WidgetSettings>? Changed;

	public SettingsView(WidgetSettings settings, GameCatalog catalog, Func<string, Task<string>> checkFeed)
	{
		_settings = WidgetSettings.FromJson(settings.ToJson());
		_catalog = catalog;
		_checkFeed = checkFeed;

		InitializeComponent();
		EntriesList.ItemsSource = _entries;
		Populate();
		_loading = false;
	}

	private void Populate()
	{
		PreviewItemsBox.Value = _settings.PreviewItems;
		ShowGachaSwitch.IsOn = _settings.ShowGacha;
		ShowEventSwitch.IsOn = _settings.ShowEvent;
		AutoRefreshSwitch.IsOn = _settings.AutoRefresh;
		FeedUrlBox.Text = _settings.FeedUrl;

		BuildIntervalItems();
		BuildEntries();

		AboutText.Text =
			$"二游日历 for WidBar · 版本 {Version()}\n" +
			$"游戏目录：{_catalog.Count} 款（core {_catalog.CoreVersion}）\n" +
			"v0 数据源：公开静态快照（每 6 小时更新一轮）。v1 将支持各端自行抓取与来源切换。";
	}

	private static string Version()
	{
		try
		{
			var asm = typeof(SettingsView).Assembly;
			var info = asm.GetName().Version;
			return info is null ? "0.1.0" : $"{info.Major}.{info.Minor}.{info.Build}";
		}
		catch
		{
			return "0.1.0";
		}
	}

	private void BuildIntervalItems()
	{
		var options = new (int Minutes, string Label)[]
		{
			(15, "15 分钟"),
			(30, "30 分钟"),
			(60, "1 小时"),
			(120, "2 小时"),
			(360, "6 小时"),
			(720, "12 小时"),
		};

		RefreshIntervalBox.Items.Clear();
		var selected = 0;
		for (var i = 0; i < options.Length; i++)
		{
			RefreshIntervalBox.Items.Add(new ComboBoxItem { Content = options[i].Label, Tag = options[i].Minutes });
			if (options[i].Minutes == _settings.RefreshMinutes) selected = i;
		}

		// 用户存了非法/非列表内的值：选最接近的一项，避免出现空白。
		if (selected == 0)
		{
			var best = int.MaxValue;
			for (var i = 0; i < options.Length; i++)
			{
				var distance = Math.Abs(options[i].Minutes - _settings.RefreshMinutes);
				if (distance >= best) continue;
				best = distance;
				selected = i;
			}
		}

		RefreshIntervalBox.SelectedIndex = selected;
	}

	/// <summary>条目顺序：settings.order 优先，其余按 catalog 默认顺序补后。</summary>
	private List<CatalogEntry> BuildOrderedCatalog()
	{
		var result = new List<CatalogEntry>(_catalog.Count);
		var added = new HashSet<string>(StringComparer.Ordinal);
		foreach (var id in _settings.Order)
		{
			if (!_catalog.TryGet(id, out var entry) || !added.Add(entry.Id)) continue;
			result.Add(entry);
		}

		foreach (var entry in _catalog.Games)
		{
			if (!added.Add(entry.Id)) continue;
			result.Add(entry);
		}

		return result;
	}

	private void BuildEntries()
	{
		var ordered = BuildOrderedCatalog();

		// 第一次进入：order 为空时把当前顺序落成显式顺序，后续上下移动才好保存。
		if (_settings.Order.Count == 0)
		{
			_settings.Order = ordered.Select(e => e.Id).ToList();
		}

		_entries.Clear();
		foreach (var entry in ordered)
		{
			_entries.Add(new SettingsEntryVm
			{
				Id = entry.Id,
				Name = entry.Name,
				DefaultHidden = entry.DefaultHidden,
				IsVisible = ComputeVisible(entry),
			});
		}
	}

	private bool ComputeVisible(CatalogEntry entry)
	{
		if (_settings.Removed.Contains(entry.Id)) return false;
		if (_settings.Hidden.Contains(entry.Id)) return false;
		if (entry.DefaultHidden && !_settings.Shown.Contains(entry.Id)) return false;
		return true;
	}

	private void SetVisible(SettingsEntryVm entry, bool visible)
	{
		var s = _settings;
		s.Removed.Remove(entry.Id);

		if (entry.DefaultHidden)
		{
			if (visible)
			{
				if (!s.Shown.Contains(entry.Id)) s.Shown.Add(entry.Id);
				s.Hidden.Remove(entry.Id);
			}
			else
			{
				s.Shown.Remove(entry.Id);
			}
		}
		else
		{
			if (visible)
			{
				s.Hidden.Remove(entry.Id);
			}
			else if (!s.Hidden.Contains(entry.Id))
			{
				s.Hidden.Add(entry.Id);
			}
		}
	}

	private void RaiseChanged()
	{
		_settings.Normalize(_catalog);
		Changed?.Invoke(_settings);
	}

	// ---------------------------------------------------------------- 事件

	private void OnPreviewItemsChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_loading || double.IsNaN(args.NewValue)) return;
		_settings.PreviewItems = (int)Math.Round(args.NewValue);
		RaiseChanged();
	}

	private void OnSimpleToggled(object sender, RoutedEventArgs e)
	{
		if (_loading) return;
		_settings.ShowGacha = ShowGachaSwitch.IsOn;
		_settings.ShowEvent = ShowEventSwitch.IsOn;
		_settings.AutoRefresh = AutoRefreshSwitch.IsOn;
		RaiseChanged();
	}

	private void OnRefreshIntervalChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_loading) return;
		if (RefreshIntervalBox.SelectedItem is not ComboBoxItem item) return;
		if (item.Tag is not int minutes) return;
		_settings.RefreshMinutes = minutes;
		RaiseChanged();
	}

	private void OnFeedUrlChanged(object sender, TextChangedEventArgs e)
	{
		if (_loading) return;
		_settings.FeedUrl = FeedUrlBox.Text;
		RaiseChanged();
	}

	private async void OnCheckFeedClick(object sender, RoutedEventArgs e)
	{
		CheckFeedButton.IsEnabled = false;
		CheckResultText.Visibility = Visibility.Visible;
		CheckResultText.Text = "检查中…";
		try
		{
			var result = await _checkFeed(FeedUrlBox.Text.Trim());
			CheckResultText.Text = result;
		}
		catch (Exception ex)
		{
			CheckResultText.Text = "检查失败：" + ex.Message;
		}
		finally
		{
			CheckFeedButton.IsEnabled = true;
		}
	}

	private void OnResetFeedClick(object sender, RoutedEventArgs e)
	{
		FeedUrlBox.Text = WidgetSettings.DefaultFeedUrl;
	}

	private void OnEntryCheckBoxClick(object sender, RoutedEventArgs e)
	{
		if (sender is not CheckBox { DataContext: SettingsEntryVm entry } box) return;
		var visible = box.IsChecked == true;
		entry.IsVisible = visible;
		SetVisible(entry, visible);
		RaiseChanged();
	}

	private void OnMoveUpClick(object sender, RoutedEventArgs e) => MoveEntry(sender, -1);

	private void OnMoveDownClick(object sender, RoutedEventArgs e) => MoveEntry(sender, +1);

	private void MoveEntry(object sender, int delta)
	{
		if (sender is not FrameworkElement { DataContext: SettingsEntryVm entry }) return;
		var index = _entries.IndexOf(entry);
		var target = index + delta;
		if (index < 0 || target < 0 || target >= _entries.Count) return;

		_entries.Move(index, target);
		_settings.Order = _entries.Select(e => e.Id).ToList();
		RaiseChanged();
	}

	private void OnResetEntriesClick(object sender, RoutedEventArgs e)
	{
		_settings.Hidden.Clear();
		_settings.Shown.Clear();
		_settings.Removed.Clear();
		_settings.Order.Clear();
		BuildEntries();
		RaiseChanged();
	}
}
