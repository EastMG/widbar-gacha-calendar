using GachaCalendar.Core.Models;

namespace GachaCalendar.Core.Services;

/// <summary>
/// catalog 的持有者（外壳启动时 new 一次，传给 WidgetViewModel）。
/// 只是 <see cref="GameCatalog"/> 的一层薄封装：让插件层不需要知道 catalog 的来源
/// （嵌入资源 / 磁盘覆盖），也方便将来做"热重载 catalog"。
/// </summary>
public sealed class CatalogStore
{
	public CatalogStore(string? overridePath = null)
	{
		Catalog = GameCatalog.Load(overridePath);
	}

	/// <summary>28 款元信息（已按 order 排好）。</summary>
	public GameCatalog Catalog { get; private set; }

	/// <summary>重新加载（调试覆盖文件变化后可用）。</summary>
	public GameCatalog Reload(string? overridePath = null)
	{
		Catalog = GameCatalog.Load(overridePath);
		return Catalog;
	}
}
