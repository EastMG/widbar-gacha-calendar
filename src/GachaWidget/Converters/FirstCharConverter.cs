using Microsoft.UI.Xaml.Data;

namespace GachaWidget.Converters;

/// <summary>游戏名 → 首字（GameIcon 的兜底字形只该显示一个字，不是整名）。</summary>
public sealed class FirstCharConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var text = value as string;
		if (string.IsNullOrWhiteSpace(text)) return "?";
		var trimmed = text.TrimStart();
		return trimmed.Length == 0 ? "?" : trimmed[..1];
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}
