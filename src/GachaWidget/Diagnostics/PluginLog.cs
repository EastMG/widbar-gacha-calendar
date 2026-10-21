using System.Text;

namespace GachaWidget.Diagnostics;

/// <summary>
/// 运行期诊断日志：写到宿主分配的 <c>DataDirectory\widget.log</c>（该目录对 widget 可写）。
/// 为什么不用 Program.Log：正式安装目录只读、%LOCALAPPDATA% 在打包进程里也可能写不进去；
/// DataDirectory 是宿主明确给 widget 用的可写目录，最可靠。
/// </summary>
internal static class PluginLog
{
	private static readonly object Gate = new();
	private static string? _path;

	public static string? Path => _path;

	public static void Initialize(string? dataDirectory)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(dataDirectory)) return;
			Directory.CreateDirectory(dataDirectory);
			_path = System.IO.Path.Combine(dataDirectory, "widget.log");
			Write($"---- widget session start (pid {Environment.ProcessId}) ----");
		}
		catch
		{
			_path = null;
		}
	}

	public static void Write(string message)
	{
		var path = _path;
		if (path is null) return;
		try
		{
			lock (Gate)
			{
				File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
			}
		}
		catch
		{
			// 诊断日志永远不能影响 widget 本身。
		}
	}
}
