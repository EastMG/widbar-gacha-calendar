using System.Runtime.InteropServices;
using GachaCalendar.Core.Models;
using GachaCalendar.Core.ViewModels;
using GachaWidget.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace GachaWidget.Design;

/// <summary>
/// 设计冒烟 harness（仅开发期）：用 `GachaWidget.exe --ui-preview=flyout|settings|preview`
/// 在独立窗口里按真实尺寸渲染对应界面，便于在拿不到 WidBar 交互时截图迭代视觉。
/// </summary>
public sealed class DesignPreviewWindow : Window
{
	private const double ScaleFallback = 1.5;

	public DesignPreviewWindow(string kind, WidgetViewModel vm, WidgetSettings settings, GameCatalog catalog)
	{
		Title = "二游日历 设计预览 · " + kind;
		var root = new Grid
		{
			Background = new SolidColorBrush(Color.FromArgb(255, 0x1A, 0x1A, 0x1A)),
			Padding = new Thickness(10),
			RequestedTheme = ElementTheme.Dark,
		};

		double dipWidth;
		double dipHeight;

		switch (kind)
		{
			case "preview":
			{
				var strip = new Border
				{
					Width = 372,
					Height = 48,
					Background = new SolidColorBrush(Color.FromArgb(255, 0x14, 0x14, 0x14)),
					CornerRadius = new CornerRadius(8),
					Child = new PreviewView(vm) { VerticalAlignment = VerticalAlignment.Center },
				};
				root.Children.Add(strip);
				dipWidth = 392;
				dipHeight = 68;
				break;
			}
			case "settings":
			{
				var host = new Border
				{
					Width = 900,
					Height = 940,
					Background = new SolidColorBrush(Color.FromArgb(255, 0x2B, 0x2B, 0x2B)),
					CornerRadius = new CornerRadius(8),
					Child = new SettingsView(settings, catalog, _ => Task.FromResult("（设计预览：未真的联网）")),
				};
				root.Children.Add(host);
				dipWidth = 920;
				dipHeight = 960;
				break;
			}
			default:
			{
				var host = new Border
				{
					Width = 830,
					Height = 580,
					Background = new SolidColorBrush(Color.FromArgb(255, 0x2B, 0x2B, 0x2B)),
					CornerRadius = new CornerRadius(8),
					Child = new FlyoutView(vm),
				};
				root.Children.Add(host);
				dipWidth = 850;
				dipHeight = 600;
				break;
			}
		}

		Content = root;
		ResizeToDips(dipWidth, dipHeight);
	}

	private void ResizeToDips(double width, double height)
	{
		try
		{
			var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
			var dpi = GetDpiForWindow(hwnd);
			var scale = dpi > 0 ? dpi / 96.0 : ScaleFallback;
			AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)));
		}
		catch
		{
			// 尺寸失败不影响内容；截图脚本可自行按窗口实际大小抓。
		}
	}

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hwnd);
}
