using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GachaWidget.Converters;

/// <summary>
/// 游戏名 → 代表色（DSH newPanel 的固定色盘，同名恒同色）。
/// 与 DSH 版一致：颜色代表"是哪款游戏"，不表达紧迫度。
/// </summary>
public sealed class GameNameToBrushConverter : IValueConverter
{
	private static readonly Color[] Palette =
	{
		Color.FromArgb(255, 0xE0, 0x70, 0x9F),
		Color.FromArgb(255, 0x3F, 0xA9, 0xA0),
		Color.FromArgb(255, 0xE0, 0xA9, 0x3B),
		Color.FromArgb(255, 0x4A, 0x6C, 0xF7),
		Color.FromArgb(255, 0x7A, 0x5A, 0xF8),
		Color.FromArgb(255, 0x2F, 0xB3, 0xC9),
		Color.FromArgb(255, 0xE0, 0x6C, 0x4A),
		Color.FromArgb(255, 0x5C, 0x9E, 0x3F),
	};

	private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.Ordinal);

	public object Convert(object value, Type targetType, object parameter, string language)
		=> BrushFor(value as string);

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();

	/// <summary>给代码里复用（例如 ScheduleBar 取色）。</summary>
	public static SolidColorBrush BrushFor(string? name)
	{
		var key = name ?? "";
		if (Cache.TryGetValue(key, out var cached)) return cached;

		var hash = 0u;
		foreach (var ch in key) hash = unchecked(hash * 31 + ch);
		var brush = new SolidColorBrush(Palette[hash % (uint)Palette.Length]);
		Cache[key] = brush;
		return brush;
	}
}
