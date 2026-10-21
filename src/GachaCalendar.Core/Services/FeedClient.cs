using System.Net;
using System.Text;
using GachaCalendar.Core.Models;

namespace GachaCalendar.Core.Services;

/// <summary>一次取数的结果状态（决定顶部"示例数据/联网数据"文案与是否算错误）。</summary>
public enum FeedStatus
{
	/// <summary>200 + 解析成功（已写缓存）。</summary>
	Fresh,

	/// <summary>304：服务器说没变，用的是本地缓存。</summary>
	NotModified,

	/// <summary>网络/HTTP/解析失败，回退到本地缓存。</summary>
	Cached,

	/// <summary>没有任何可用缓存，回退到内置示例 feed。</summary>
	Sample,

	/// <summary>连内置示例都不可用（正常情况下不会发生）。</summary>
	Failed,
}

public sealed record FeedResult(FeedDocument? Document, FeedStatus Status, string? Error, DateTimeOffset FetchedAtUtc)
{
	public bool Ok => Document is not null;

	public bool IsSample => Status == FeedStatus.Sample;

	/// <summary>展示的是非实时数据（缓存或示例）。</summary>
	public bool IsOffline => Status is FeedStatus.Cached or FeedStatus.Sample;
}

/// <summary>
/// feed 抓取 + 缓存。设计约束：
/// - 单例 HttpClient、15s 超时、自定义 UA（GitHub Pages 对 UA 不敏感，但便于排障）；
/// - 带 If-None-Match / If-Modified-Since，命中 304 复用缓存，省流量；
/// - 任何失败都不抛给调用方（除了调用方自己取消）：缓存 → 内置示例 feed 逐级兜底；
/// - 与 UI 线程无关，纯 async，不引用任何 UI 类型。
/// </summary>
public sealed class FeedClient : IFeedProvider
{
	public const string UserAgent = "WidBarGachaCalendar/0.1 (+https://github.com/EastMG/gacha-calendar)";

	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

	private const string CacheFile = "feed.json";
	private const string ETagFile = "feed.etag";
	private const string LastModifiedFile = "feed.lastmod";

	// HttpClient 单例：避免每个 widget 实例都开连接池 / 耗尽 socket。
	private static readonly Lazy<HttpClient> SharedClient = new(() => BuildClient(null, DefaultTimeout), isThreadSafe: true);

	private readonly HttpClient _client;
	private readonly bool _ownsClient;

	public FeedClient(HttpMessageHandler? handler = null, string? cacheDirectory = null, TimeSpan? timeout = null)
	{
		CacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
			? DefaultCacheDirectory
			: Path.GetFullPath(cacheDirectory!);

		if (handler == null)
		{
			_client = SharedClient.Value;
			_ownsClient = false;
		}
		else
		{
			// 测试注入：不能让 handler 被共享 HttpClient 的生命周期绑死。
			_client = BuildClient(handler, timeout ?? DefaultTimeout);
			_ownsClient = true;
		}
	}

	public string CacheDirectory { get; }

	public static string DefaultCacheDirectory =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"WidBarGachaCalendar");

	private string CachePath => Path.Combine(CacheDirectory, CacheFile);
	private string ETagPath => Path.Combine(CacheDirectory, ETagFile);
	private string LastModifiedPath => Path.Combine(CacheDirectory, LastModifiedFile);

	/// <summary>缓存 feed 的完整路径（外壳首帧直接读它时可用）。</summary>
	public string CacheFilePath => CachePath;

	/// <summary>
	/// 只读缓存、不联网：给"启动首帧先展示上次数据"用（docs/03 生命周期的第一步）。
	/// 返回 false 表示没有可用缓存，调用方应回退示例 feed 或等待首次刷新。
	/// </summary>
	public bool TryLoadCached(out FeedDocument? document, out string? error)
		=> TryReadCache(out document, out error);

	/// <summary>
	/// 拉取 feed：成功写缓存；304 用缓存；失败回退缓存；缓存也没有则回退内置示例。
	/// 调用方的取消会原样抛 OperationCanceledException，其余异常都折叠进 FeedResult.Error。
	/// </summary>
	public async Task<FeedResult> FetchAsync(string url, CancellationToken cancellationToken = default)
	{
		var fetchedAt = DateTimeOffset.UtcNow;
		if (string.IsNullOrWhiteSpace(url)) url = WidgetSettings.DefaultFeedUrl;

		string? error = null;
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			var meta = ReadCacheMeta();
			if (!string.IsNullOrWhiteSpace(meta.ETag))
				request.Headers.TryAddWithoutValidation("If-None-Match", meta.ETag);
			else if (meta.LastModified is { } lastModified)
				request.Headers.IfModifiedSince = lastModified;

			using var response = await _client
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (response.StatusCode == HttpStatusCode.NotModified)
			{
				if (TryReadCache(out var cached, out var cacheError))
					return new FeedResult(cached, FeedStatus.NotModified, null, fetchedAt);

				error = "服务器返回 304，但本地缓存不可用：" + (cacheError ?? "未知原因");
				return Fallback(fetchedAt, error);
			}

			if (!response.IsSuccessStatusCode)
			{
				error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim();
				return Fallback(fetchedAt, error);
			}

			var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
			var json = DecodeUtf8(payload);
			if (!FeedDocument.TryParse(json, out var document, out var parseError) || document == null)
			{
				error = parseError ?? "feed 解析失败";
				return Fallback(fetchedAt, error);
			}

			// 缓存写失败不影响这次返回（磁盘满 / 只读目录都可能发生）。
			var etag = response.Headers.ETag?.ToString();
			var responseLastModified = response.Content.Headers.LastModified;
			TryWriteCache(payload, etag, responseLastModified);
			return new FeedResult(document, FeedStatus.Fresh, null, fetchedAt);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// 超时（TaskCanceledException）也走这里：ct 没取消就不是调用方取消。
			error = ex is TaskCanceledException ? "请求超时" : $"{ex.GetType().Name}：{ex.Message}";
			return Fallback(fetchedAt, error);
		}
	}

	private FeedResult Fallback(DateTimeOffset fetchedAt, string? primaryError)
	{
		string? cacheError = null;
		if (TryReadCache(out var cached, out cacheError))
			return new FeedResult(cached, FeedStatus.Cached, primaryError, fetchedAt);

		string? sampleError = null;
		if (EmbeddedData.TrySampleFeed(out var json, out sampleError)
			&& FeedDocument.TryParse(json, out var sample, out var sampleParseError)
			&& sample != null)
			return new FeedResult(sample, FeedStatus.Sample, primaryError, fetchedAt);

		var details = new[] { primaryError, cacheError, sampleError, "无法加载内置示例 feed" }
			.Where(x => !string.IsNullOrWhiteSpace(x));
		return new FeedResult(null, FeedStatus.Failed, string.Join("；", details), fetchedAt);
	}

	// ---------------------------------------------------------------- 缓存

	private bool TryReadCache(out FeedDocument? document, out string? error)
	{
		document = null;
		error = null;
		try
		{
			if (!File.Exists(CachePath))
			{
				error = "没有本地缓存";
				return false;
			}

			var json = DecodeUtf8(File.ReadAllBytes(CachePath));
			if (!FeedDocument.TryParse(json, out document, out var parseError) || document == null)
			{
				error = "本地缓存无法解析：" + (parseError ?? "未知原因");
				document = null;
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			error = "读取本地缓存失败：" + ex.Message;
			document = null;
			return false;
		}
	}

	private void TryWriteCache(byte[] payload, string? etag, DateTimeOffset? lastModified)
	{
		try
		{
			Directory.CreateDirectory(CacheDirectory);
			WriteAtomic(CachePath, payload);
			WriteOrDelete(ETagPath, etag);
			WriteOrDelete(LastModifiedPath, lastModified?.ToUniversalTime().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			// 缓存是尽力而为：失败下次还会重新拉。
		}
	}

	private (string? ETag, DateTimeOffset? LastModified) ReadCacheMeta()
	{
		string? etag = null;
		DateTimeOffset? lastModified = null;
		try
		{
			if (File.Exists(ETagPath)) etag = File.ReadAllText(ETagPath).Trim();
		}
		catch (Exception)
		{
			etag = null;
		}

		try
		{
			if (File.Exists(LastModifiedPath)
				&& DateTimeOffset.TryParse(
					File.ReadAllText(LastModifiedPath).Trim(),
					System.Globalization.CultureInfo.InvariantCulture,
					System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
					out var parsed))
				lastModified = parsed;
		}
		catch (Exception)
		{
			lastModified = null;
		}

		return (string.IsNullOrWhiteSpace(etag) ? null : etag, lastModified);
	}

	private static void WriteOrDelete(string path, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			if (File.Exists(path)) File.Delete(path);
			return;
		}

		WriteAtomic(path, Encoding.UTF8.GetBytes(value!));
	}

	private static void WriteAtomic(string path, byte[] bytes)
	{
		var tmp = path + ".tmp";
		File.WriteAllBytes(tmp, bytes);
		File.Move(tmp, path, overwrite: true);
	}

	private static string DecodeUtf8(byte[] bytes)
	{
		// 用 StreamReader 走 BOM 检测：System.Text.Json 的 byte→string 路径不认 BOM。
		using var stream = new MemoryStream(bytes, writable: false);
		using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		return reader.ReadToEnd();
	}

	private static HttpClient BuildClient(HttpMessageHandler? handler, TimeSpan timeout)
	{
		var client = handler == null
			? new HttpClient()
			: new HttpClient(handler, disposeHandler: false);
		client.Timeout = timeout;
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return client;
	}

	public void Dispose()
	{
		if (_ownsClient) _client.Dispose();
	}
}
