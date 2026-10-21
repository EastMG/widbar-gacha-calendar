using GachaCalendar.Core.Models;

namespace GachaCalendar.Core.Services;

/// <summary>
/// 数据源抽象：widget 只依赖这个接口，不关心数据来自哪里。
///
/// v0 的实现是 <see cref="FeedClient"/>（读公开静态快照 JSON）；
/// v1 计划换成"内嵌 gacha-calendar-core 的 EngineFeedProvider"（各端自行抓 28 个源）。
/// 两者都返回 <see cref="FeedResult"/>，命中 <c>schemaVersion 1</c> 的 Result JSON，
/// 所以 <c>WidgetViewModel</c> 与 UI 一行都不用改。
/// </summary>
public interface IFeedProvider : IDisposable
{
	/// <summary>缓存目录（用于诊断与"数据检查"展示）。</summary>
	string CacheDirectory { get; }

	/// <summary>读上次成功缓存（供首帧立即出内容）。</summary>
	bool TryLoadCached(out FeedDocument? document, out string? error);

	/// <summary>抓一次最新数据；任何失败都应回退缓存/示例而不是抛给调用方。</summary>
	Task<FeedResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}
