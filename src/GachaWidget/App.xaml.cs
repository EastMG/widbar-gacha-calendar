using Microsoft.UI.Xaml;
using WidBar.SDK;
using WidBar.SDK.Hosting;

namespace GachaWidget;

/// <summary>WidBar 插件进程入口：宿主负责通信、任务栏预览的放置、flyout/settings 窗口。</summary>
public partial class App : WidgetHostApplication
{
	public App()
	{
		Program.Log("App ctor begin");
		InitializeComponent();
		Program.Log("App ctor end");
	}

	protected override IWidgetPlugin CreatePlugin()
	{
		Program.Log("CreatePlugin");
		return new Plugin.GachaWidgetPlugin();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		Program.Log("OnLaunched");
		base.OnLaunched(args);
		Program.Log("OnLaunched returned");
	}
}
