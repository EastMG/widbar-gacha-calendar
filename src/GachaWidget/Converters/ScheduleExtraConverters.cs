using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GachaWidget.Converters;

/// <summary>"#RRGGBB" → 画刷（非法/空 → 灰色）。ScheduleItem.Color 已由 Core 兜底成哈希色。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var hex = value as string;
		if (!string.IsNullOrWhiteSpace(hex) && hex.Length == 7 && hex[0] == '#'
			&& byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
			&& byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
			&& byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
		{
			return new SolidColorBrush(Color.FromArgb(255, r, g, b));
		}

		return new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>ScheduleItem.IsGacha → 「卡池」/「活动」。</summary>
public sealed class SideLabelConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
		=> value is bool b && b ? "卡池" : "活动";

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>null → Collapsed（parameter=invert 时反过来），用于「该来源无数据」占位。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var isNull = value is null;
		if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) isNull = !isNull;
		return isNull ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}
