using GachaCalendar.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace GachaWidget.Views;

/// <summary>
/// 任务栏预览条：只显示"最近结束的 1–3 项"的游戏简称 + 短倒计时。
/// 高度由宿主按任务栏高度给，这里不写死高度；节奏交给 WidBar 的 Smart Stack。
/// </summary>
public sealed partial class PreviewView : UserControl
{
	public PreviewView(WidgetViewModel vm)
	{
		Vm = vm;
		InitializeComponent();
	}

	public WidgetViewModel Vm { get; }
}
