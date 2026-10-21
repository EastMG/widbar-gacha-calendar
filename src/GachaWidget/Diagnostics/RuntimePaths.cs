using Windows.Storage;

namespace GachaWidget.Diagnostics;

/// <summary>
/// 找一个**真的可写**的目录给缓存与诊断日志用。
///
/// 背景：宿主通过 initInstance 给的 DataDirectory 在
/// <c>…\Packages\&lt;WidBar PFN&gt;\LocalCache\Roaming\WidBar\Data\&lt;plugin&gt;\&lt;instance&gt;</c>，
/// 那是"宿主包"的数据目录。widget 是另一个 MSIX 包，某些机器上对该目录只有读权限
/// （实测：settings.json 是宿主写的，widget 自己的 feed 缓存写不进去 → 一直显示内置示例）。
/// 所以按顺序试：宿主目录 → 本包 LocalState → 临时目录。
/// </summary>
internal static class RuntimePaths
{
	private static string? _chosen;

	public static string? Chosen => _chosen;

	public static string Resolve(string? hostDataDirectory, string? instanceId)
	{
		var leaf = string.IsNullOrWhiteSpace(instanceId) ? "default" : instanceId;

		foreach (var candidate in Candidates(hostDataDirectory, leaf))
		{
			if (string.IsNullOrWhiteSpace(candidate)) continue;
			if (TryPrepare(candidate))
			{
				_chosen = candidate;
				return candidate;
			}
		}

		// 全都写不进去：至少返回一个字符串，让上层逻辑别崩；写操作自己会失败并被吞掉。
		return Path.Combine(Path.GetTempPath(), "widbar-gacha-calendar", leaf);
	}

	private static IEnumerable<string?> Candidates(string? hostDataDirectory, string leaf)
	{
		yield return hostDataDirectory;

		string? packageLocal = null;
		try
		{
			packageLocal = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Data", leaf);
		}
		catch
		{
			packageLocal = null;
		}
		yield return packageLocal;

		yield return Path.Combine(Path.GetTempPath(), "widbar-gacha-calendar", leaf);
	}

	private static bool TryPrepare(string directory)
	{
		try
		{
			Directory.CreateDirectory(directory);
			var probe = Path.Combine(directory, ".write-probe");
			File.WriteAllText(probe, DateTime.Now.ToString("O"));
			File.Delete(probe);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
