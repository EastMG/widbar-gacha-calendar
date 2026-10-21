using System.ComponentModel;
using GachaCalendar.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GachaWidget.Views;

/// <summary>
/// 展开面板：28 款游戏的六列表格 + 状态条 + 刷新按钮。
/// 内容只创建一次、复用（WidBar 会把 flyout 保持"热"），所以这里不要做一次性初始化假设。
/// </summary>
public sealed partial class FlyoutView : UserControl
{
	public FlyoutView(WidgetViewModel vm)
	{
		Vm = vm;
		InitializeComponent();

		Vm.PropertyChanged += OnVmPropertyChanged;
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
}
