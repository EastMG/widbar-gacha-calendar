using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace GachaWidget;

/// <summary>
/// 入口。用 DISABLE_XAML_GENERATED_MAIN 关掉 XAML 自动生成的 Main，
/// 由这里显式初始化 COM Wrappers 与 DispatcherQueue（与 WidBar 官方模板一致）。
///
/// 额外的启动诊断（仅本地开发布局可写时生效）：
/// 把关键阶段与未处理异常追加到可执行文件目录的 startup.log，
/// 否则宿主只报 "Plugin process exited"，看不到进程内部到底走到哪一步。
/// </summary>
public static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		Log($"Main entered; args=[{string.Join(" ", args)}]");
		Log("token: " + Diagnostics.TokenInfo.Describe());

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			Log("UNHANDLED(AppDomain): " + e.ExceptionObject);
		TaskScheduler.UnobservedTaskException += (_, e) =>
			Log("UNOBSERVED(Task): " + e.Exception);

		// 诊断：宿主用 --widbar-pipe=<name> 传管道名，SDK 启动后会在 OnLaunched 里连它。
		// 这里先自己连一次，把"连不上"的具体异常（访问被拒 / 找不到管道）记下来。
		ProbeHostPipe(args);

		try
		{
			Log("InitializeComWrappers ...");
			ComWrappersSupport.InitializeComWrappers();

			Log("Application.Start ...");
			Application.Start(_ =>
			{
				Log("Application.Start callback: creating App");
				var context = new DispatcherQueueSynchronizationContext(
					DispatcherQueue.GetForCurrentThread());
				SynchronizationContext.SetSynchronizationContext(context);
				try
				{
					new App();
					Log("App constructed");
				}
				catch (Exception ex)
				{
					Log("FATAL in callback: " + ex);
					throw;
				}
			});
			Log("Application.Start returned");
		}
		catch (Exception ex)
		{
			Log("FATAL: " + ex);
			throw;
		}
	}

	internal static void Log(string message)
	{
		try
		{
			var path = Path.Combine(AppContext.BaseDirectory, "startup.log");
			File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
		}
		catch
		{
			// 安装目录不可写（正式包）时忽略：这只是开发期诊断。
		}
	}

	private static void ProbeHostPipe(string[] args)
	{
		var pipeArg = args.FirstOrDefault(a =>
			a.StartsWith("--widbar-pipe=", StringComparison.OrdinalIgnoreCase));
		if (pipeArg is null)
		{
			Log("no --widbar-pipe argument (standalone launch)");
			return;
		}

		var name = pipeArg["--widbar-pipe=".Length..];
		try
		{
			using var client = new System.IO.Pipes.NamedPipeClientStream(
				".", name, System.IO.Pipes.PipeDirection.InOut,
				System.IO.Pipes.PipeOptions.Asynchronous);
			client.Connect(3000);
			Log($"pipe probe OK: {name}");
		}
		catch (Exception ex)
		{
			Log($"pipe probe FAILED: {name}: {ex.GetType().Name}: {ex.Message}");
		}
	}
}
