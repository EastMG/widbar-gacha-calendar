using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace GachaWidget;

/// <summary>
/// 入口。用 DISABLE_XAML_GENERATED_MAIN 关掉 XAML 自动生成的 Main，
/// 由这里显式初始化 COM Wrappers 与 DispatcherQueue（与 WidBar 官方模板一致）。
/// </summary>
public static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		ComWrappersSupport.InitializeComWrappers();

		Application.Start(_ =>
		{
			var context = new DispatcherQueueSynchronizationContext(
				DispatcherQueue.GetForCurrentThread());
			SynchronizationContext.SetSynchronizationContext(context);
			new App();
		});
	}
}
