using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GachaWidget.Converters;

/// <summary>紧迫度 → 文字颜色：1=24h 内(红) 2=7 天内(琥珀) 3=未开始(蓝) 0=次要灰。</summary>
public sealed class UrgencyToBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var level = value is int i ? i : 0;
		var key = level switch
		{
			1 => "SystemFillColorCriticalBrush",
			2 => "SystemFillColorCautionBrush",
			3 => "AccentTextFillColorPrimaryBrush",
			_ => "TextFillColorTertiaryBrush",
		};

		if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
		{
			return brush;
		}

		return new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>DSH newPanel 的「卡池 / 活动」类型胶囊配色：卡池=蓝、活动=琥珀。</summary>
public sealed class SideToBackgroundConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var isGacha = string.Equals(value as string, "gacha", StringComparison.OrdinalIgnoreCase);
		var key = isGacha ? "AccentFillColorDefaultBrush" : "SystemFillColorCautionBrush";
		if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is SolidColorBrush brush)
		{
			return new SolidColorBrush(Color.FromArgb(0x22, brush.Color.R, brush.Color.G, brush.Color.B));
		}
		return new SolidColorBrush(Color.FromArgb(0x22, 128, 128, 128));
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>DSH newPanel 的「卡池 / 活动」类型胶囊文字色。</summary>
public sealed class SideToForegroundConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var isGacha = string.Equals(value as string, "gacha", StringComparison.OrdinalIgnoreCase);
		var key = isGacha ? "AccentTextFillColorPrimaryBrush" : "SystemFillColorCautionBrush";
		if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
		{
			return brush;
		}
		return new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>ScheduleItem.State → 状态中文（表头/悬停用）。</summary>
public sealed class StateTextConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
		=> value switch
		{
			GachaCalendar.Core.ViewModels.ScheduleState.Live => "在期",
			GachaCalendar.Core.ViewModels.ScheduleState.Waiting => "未开始",
			GachaCalendar.Core.ViewModels.ScheduleState.Ended => "已结束",
			GachaCalendar.Core.ViewModels.ScheduleState.Long => "长期",
			_ => "无数据",
		};

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}

/// <summary>IsGacha → 类型标记色：卡池=强调蓝，活动=琥珀。</summary>
public sealed class SideToBrushConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
	{
		var isGacha = value is bool b && b;
		var key = isGacha ? "AccentFillColorDefaultBrush" : "SystemFillColorCautionBrush";
		if (Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
		{
			return brush;
		}
		return new SolidColorBrush(Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
	}

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException();
}
