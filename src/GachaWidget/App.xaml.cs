using GachaCalendar.Core.Models;
using GachaCalendar.Core.Services;
using GachaCalendar.Core.ViewModels;
using GachaWidget.Design;
using Microsoft.UI.Xaml;
using WidBar.SDK;
using WidBar.SDK.Hosting;

namespace GachaWidget;

/// <summary>WidBar 插件进程入口：宿主负责通信、任务栏预览的放置、flyout/settings 窗口。</summary>
public partial class App : WidgetHostApplication
{
	private DesignPreviewWindow? _designWindow;

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
		// 设计冒烟：`GachaWidget.exe --ui-preview=flyout|settings|preview`，不连 WidBar、直接按真实尺寸渲染。
		var previewArg = Environment.GetCommandLineArgs()
			.FirstOrDefault(a => a.StartsWith("--ui-preview", StringComparison.OrdinalIgnoreCase));
		if (previewArg is not null)
		{
			ShowDesignPreview(ParseKind(previewArg), ParseTheme());
			return;
		}

		Program.Log("OnLaunched");
		base.OnLaunched(args);
		Program.Log("OnLaunched returned");
	}

	private static string ParseTheme()
	{
		var arg = Environment.GetCommandLineArgs()
			.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));
		if (arg is null) return "dark";
		var value = arg[(arg.IndexOf('=') + 1)..].Trim().ToLowerInvariant();
		return value is "light" or "dark" ? value : "dark";
	}

	private static string ParseKind(string arg)
	{
		var index = arg.IndexOf('=');
		var kind = index >= 0 ? arg[(index + 1)..].Trim().ToLowerInvariant() : "flyout";
		return kind is "preview" or "settings" or "flyout" ? kind : "flyout";
	}

	private void ShowDesignPreview(string kind, string theme)
	{
		try
		{
			var catalog = new CatalogStore().Catalog;
			var settings = new WidgetSettings();
			settings.Normalize(catalog);
			var vm = new WidgetViewModel(catalog);

			if (EmbeddedData.TrySampleFeed(out var feedJson, out _)
				&& FeedDocument.Parse(feedJson) is { } feed)
			{
				vm.Apply(feed, settings, catalog, DateTime.Now, FeedStatus.Sample);
			}

			_designWindow = new DesignPreviewWindow(kind, theme, vm, settings, catalog);
			_designWindow.Activate();
			Program.Log("design preview activated: " + kind);
		}
		catch (Exception ex)
		{
			Program.Log("design preview failed: " + ex);
			throw;
		}
	}
}
