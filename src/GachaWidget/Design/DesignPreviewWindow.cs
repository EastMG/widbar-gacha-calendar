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

	public DesignPreviewWindow(string kind, string theme, WidgetViewModel vm, WidgetSettings settings, GameCatalog catalog)
	{
		Title = "二游日历 设计预览 · " + kind;
		var root = new Grid
		{
			Background = new SolidColorBrush(theme == "light" ? Color.FromArgb(255, 0xF3, 0xF3, 0xF3) : Color.FromArgb(255, 0x1A, 0x1A, 0x1A)),
			Padding = new Thickness(10),
			RequestedTheme = theme == "light" ? ElementTheme.Light : ElementTheme.Dark,
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
					Background = new SolidColorBrush(theme == "light" ? Color.FromArgb(255, 0xF9, 0xF9, 0xF9) : Color.FromArgb(255, 0x14, 0x14, 0x14)),
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
				// 模拟 WidBar.SDK.Hosting.SettingsHostWindow 的真实 chrome：
				// 1120×820dip、48dip 标题栏、内容 ScrollViewer(Padding 24,16,24,8)、底部保存/取消。
				var host = new Grid
				{
					Width = 1120,
					Height = 820,
					Background = new SolidColorBrush(theme == "light"
						? Color.FromArgb(255, 0xF3, 0xF3, 0xF3)
						: Color.FromArgb(255, 0x20, 0x20, 0x20)),
				};
				host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
				host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
				host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

				var titleBar = new Grid { Height = 48, Padding = new Thickness(16, 0, 0, 0) };
				var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
				titlePanel.Children.Add(new FontIcon { Glyph = "\uE713", FontSize = 13 });
				titlePanel.Children.Add(new TextBlock { Text = "二游日历", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
				titleBar.Children.Add(titlePanel);
				Grid.SetRow(titleBar, 0);
				host.Children.Add(titleBar);

				var scroller = new ScrollViewer
				{
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
					Padding = new Thickness(24, 16, 24, 8),
					Content = new SettingsView(settings, catalog, _ => Task.FromResult("（设计预览：未真的联网）")),
				};
				Grid.SetRow(scroller, 1);
				host.Children.Add(scroller);

				var bottom = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					HorizontalAlignment = HorizontalAlignment.Right,
					Spacing = 8,
					Margin = new Thickness(24, 12, 24, 20),
				};
				bottom.Children.Add(new Button
				{
					Content = "保存",
					MinWidth = 120,
					Style = (Style)Application.Current.Resources["AccentButtonStyle"],
				});
				bottom.Children.Add(new Button { Content = "取消", MinWidth = 120 });
				Grid.SetRow(bottom, 2);
				host.Children.Add(bottom);

				root.Children.Add(host);
				dipWidth = 1140;
				dipHeight = 840;
				break;
			}
			default:
			{
				var host = new Border
				{
					Width = 830,
					Height = 580,
					Background = new SolidColorBrush(theme == "light" ? Color.FromArgb(255, 0xFA, 0xFA, 0xFA) : Color.FromArgb(255, 0x2B, 0x2B, 0x2B)),
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
