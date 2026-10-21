// 手动数据源探针：用与 widget **完全相同的** FeedClient 代码去抓真实 feed，
// 用来区分"网络/代理/TLS 问题"与"widget 运行环境问题"。
//
// 用法：
//   dotnet run --project tools\FeedProbe -- [url] [cacheDir]
// 退出码：0 = 拿到数据；1 = 只能回退缓存/示例；2 = 参数/异常。

using GachaCalendar.Core.Models;
using GachaCalendar.Core.Services;

var url = args.Length > 0 ? args[0] : WidgetSettings.DefaultFeedUrl;
var cacheDir = args.Length > 1
    ? args[1]
    : Path.Combine(Path.GetTempPath(), "widbar-feed-probe-" + Guid.NewGuid().ToString("N")[..8]);

Console.WriteLine($"[probe] url       = {url}");
Console.WriteLine($"[probe] cacheDir  = {cacheDir}");
Console.WriteLine($"[probe] runtime   = {Environment.Version}");
Console.WriteLine($"[probe] proxy     = {(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HTTPS_PROXY")) ? "<none>" : Environment.GetEnvironmentVariable("HTTPS_PROXY"))}");

try
{
    using var client = new FeedClient(cacheDirectory: cacheDir);
    var started = DateTime.UtcNow;
    var result = await client.FetchAsync(url);
    var elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;

    Console.WriteLine($"[probe] status    = {result.Status}");
    Console.WriteLine($"[probe] ok        = {result.Ok}");
    Console.WriteLine($"[probe] error     = {result.Error ?? "-"}");
    Console.WriteLine($"[probe] elapsed   = {elapsed}ms");
    Console.WriteLine($"[probe] records   = {result.Document?.Games.Count ?? 0}");
    if (result.Document?.RefreshedAt > 0)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(result.Document.RefreshedAt).ToLocalTime();
        Console.WriteLine($"[probe] refreshed = {at:yyyy-MM-dd HH:mm:ss}");
    }

    Console.WriteLine($"[probe] cacheHas  = {client.TryLoadCached(out _, out var cacheError)} {cacheError}");
    Console.WriteLine($"[probe] cacheFiles= {string.Join(", ", Directory.Exists(cacheDir) ? Directory.GetFiles(cacheDir).Select(Path.GetFileName) : Array.Empty<string>())}");

    return result.Ok && result.Status == FeedStatus.Fresh ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine("[probe] EXCEPTION: " + ex);
    return 2;
}
