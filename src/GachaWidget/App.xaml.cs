using Microsoft.UI.Xaml;
using WidBar.SDK;
using WidBar.SDK.Hosting;

namespace GachaWidget;

/// <summary>WidBar 插件进程入口：宿主负责通信、任务栏预览的放置、flyout/settings 窗口。</summary>
public partial class App : WidgetHostApplication
{
	public App() => InitializeComponent();

	protected override IWidgetPlugin CreatePlugin() => new Plugin.GachaWidgetPlugin();
}
