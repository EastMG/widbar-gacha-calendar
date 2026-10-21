using System.Reflection;

namespace GachaCalendar.Core.Services;

/// <summary>
/// 读取编译进程序集的 JSON（catalog / 示例 feed）。
/// 为什么走嵌入资源：首次启动与断网时不能依赖任何外部文件（见 AGENTS 硬边界）。
/// </summary>
public static class EmbeddedData
{
	public const string CatalogResource = "catalog.json";
	public const string SampleFeedResource = "sample-feed.json";

	private static readonly Lazy<string> Catalog = new(() => ReadRequired(CatalogResource), isThreadSafe: true);
	private static readonly Lazy<string> SampleFeed = new(() => ReadRequired(SampleFeedResource), isThreadSafe: true);

	public static string CatalogJson() => Catalog.Value;

	public static string SampleFeedJson() => SampleFeed.Value;

	public static bool TrySampleFeed(out string json, out string? error)
		=> TryRead(SampleFeedResource, out json, out error);

	public static bool TryCatalog(out string json, out string? error)
		=> TryRead(CatalogResource, out json, out error);

	private static string ReadRequired(string name)
	{
		if (TryRead(name, out var json, out var error)) return json;
		throw new InvalidOperationException(error ?? $"缺少嵌入资源 {name}");
	}

	private static bool TryRead(string name, out string json, out string? error)
	{
		json = "";
		error = null;
		try
		{
			var assembly = typeof(EmbeddedData).Assembly;
			var stream = assembly.GetManifestResourceStream(name)
				?? FindBySuffix(assembly, name);
			if (stream == null)
			{
				error = $"程序集里找不到嵌入资源 {name}（现有：{string.Join(", ", assembly.GetManifestResourceNames())}）";
				return false;
			}

			using (stream)
			using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
			{
				json = reader.ReadToEnd();
			}

			if (json.Length == 0)
			{
				error = $"嵌入资源 {name} 内容为空";
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			error = $"读取嵌入资源 {name} 失败：{ex.Message}";
			return false;
		}
	}

	/// <summary>LogicalName 未生效时（例如被改成 RootNamespace.名字）仍能按后缀找到资源。</summary>
	private static Stream? FindBySuffix(Assembly assembly, string name)
	{
		foreach (var resource in assembly.GetManifestResourceNames())
		{
			if (resource.EndsWith("." + name, StringComparison.Ordinal))
				return assembly.GetManifestResourceStream(resource);
		}

		return null;
	}
}
