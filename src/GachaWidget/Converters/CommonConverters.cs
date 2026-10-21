using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GachaWidget.Converters;

/// <summary>bool → Visibility。ConverterParameter 传 "invert" 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var flag = value is bool b && b;
		if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
		return flag ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>
/// 字符串与 ConverterParameter 相等 → Visible，否则 Collapsed。
/// 用于 BadgeKind（err / warn / stale）分流到两套不同颜色的 badge 视觉。
/// </summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var text = value as string ?? "";
		var expected = parameter as string ?? "";
		return string.Equals(text, expected, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>非空字符串 → Visible；空 / null → Collapsed。</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
		=> string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>枚举值名与 ConverterParameter 相等 → Visible（用于按数据状态显示"示例/离线"标记）。</summary>
public sealed class EnumEqualsToVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var name = value?.ToString() ?? "";
		var expected = parameter as string ?? "";
		return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase)
			? Visibility.Visible
			: Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}
