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
		var line = $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}";
		foreach (var path in LogPaths())
		{
			try
			{
				File.AppendAllText(path, line, Encoding.UTF8);
				return;
			}
			catch
			{
				// 试下一个候选位置：正式安装目录是只读的，退到用户可写目录。
			}
		}
	}

	/// <summary>日志候选路径：开发布局可写安装目录 → %LOCALAPPDATA%\WidBarGachaCalendar → %TEMP%。</summary>
	private static IEnumerable<string> LogPaths()
	{
		string? local = null;
		try
		{
			local = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"WidBarGachaCalendar");
			Directory.CreateDirectory(local);
		}
		catch
		{
			local = null;
		}

		if (!string.IsNullOrEmpty(local))
		{
			yield return Path.Combine(local, "startup.log");
		}

		yield return Path.Combine(AppContext.BaseDirectory, "startup.log");
		yield return Path.Combine(Path.GetTempPath(), "widbar-gacha-calendar-startup.log");
	}
}
