using System.ComponentModel;
using GachaCalendar.Core.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GachaWidget.Views;

/// <summary>
/// 展开面板（DSH newPanel 的日程视图语言）：
/// 头部总览 + 近期（按紧迫度）+ 下一个截止卡 + 14 天迷你条游戏表 + 图例/状态底栏。
/// 内容只创建一次、复用（WidBar 会把 flyout 保持"热"）。
/// </summary>
public sealed partial class FlyoutView : UserControl
{
	private readonly DispatcherQueueTimer? _minuteTimer;
	private DateTime _rulerDay = DateTime.MinValue;

	public FlyoutView(WidgetViewModel vm)
	{
		Vm = vm;
		InitializeComponent();

		Vm.PropertyChanged += OnVmPropertyChanged;
		Loaded += (_, _) =>
		{
			UpdateDateCapsule();
			RebuildRulers();
		};

		if (DispatcherQueue.GetForCurrentThread() is { } queue)
		{
			_minuteTimer = queue.CreateTimer();
			_minuteTimer.Interval = TimeSpan.FromSeconds(30);
			_minuteTimer.IsRepeating = true;
			_minuteTimer.Tick += (_, _) =>
			{
				UpdateDateCapsule();
				if (_rulerDay != DateTime.Today) RebuildRulers();
			};
			_minuteTimer.Start();
		}

		UpdateErrorState();
	}

	/// <summary>用户点了刷新按钮（由插件真正执行，避免 View 自己发网络请求）。</summary>
	public event EventHandler? RefreshRequested;

	public WidgetViewModel Vm { get; }

	/// <summary>刷新中：禁用按钮 + 显示 ProgressRing。</summary>
	public void SetRefreshing(bool refreshing)
	{
		RefreshButton.IsEnabled = !refreshing;
		BusyRing.IsActive = refreshing;
		BusyRing.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;
	}

	private void OnRefreshClick(object sender, RoutedEventArgs e)
		=> RefreshRequested?.Invoke(this, EventArgs.Empty);

	private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(WidgetViewModel.HasError) or nameof(WidgetViewModel.ErrorText))
		{
			UpdateErrorState();
		}
	}

	private void UpdateErrorState()
	{
		var hasError = Vm.HasError;
		ErrorPanel.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
		ErrorText.Text = Vm.ErrorText;
	}

	private void UpdateDateCapsule()
	{
		var now = DateTime.Now;
		var weekdays = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
		DateCapsuleText.Text = $"{now.Month}月{now.Day}日 · {weekdays[(int)now.DayOfWeek]}";
	}

	/// <summary>14 天刻度：与 ScheduleBar 共用同一根轴（今天 = 第 3 格）。</summary>
	private void RebuildRulers()
	{
		var today = DateTime.Today;
		_rulerDay = today;
		FillRuler(RulerGacha);
		FillRuler(RulerEvent);

		void FillRuler(Grid ruler)
		{
			ruler.Children.Clear();
			ruler.ColumnDefinitions.Clear();
			for (var i = 0; i < 14; i++)
			{
				ruler.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				var day = today.AddDays(i - 2);
				var isToday = i == 2;
				var label = new TextBlock
				{
					Text = day.Day.ToString(),
					FontSize = 8,
					HorizontalAlignment = HorizontalAlignment.Center,
					Foreground = (Brush)Application.Current.Resources[
						isToday ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"],
					FontWeight = isToday ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
				};
				Grid.SetColumn(label, i);
				ruler.Children.Add(label);
			}
		}
	}
}
