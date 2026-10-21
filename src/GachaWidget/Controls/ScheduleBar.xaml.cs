using GachaCalendar.Core.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace GachaWidget.Controls;

/// <summary>
/// 14 天迷你条（DSH newPanel 的视觉语言）：
/// - 所有行共用同一根 14 天轴：今天恒为第 3 格（索引 2），与表头刻度对齐；
/// - 颜色 = 游戏（来自 ScheduleItem.Color，缺失时按游戏名哈希）；
/// - 图案 = 状态：实心=在期 / 半透明=已结束 / 长条点线≈长期 / 空心描边=还没开始；
/// - 今天那一列用一条淡色高亮承载，不画竖线压住文字。
/// </summary>
public sealed partial class ScheduleBar : UserControl
{
	private const int Days = 14;
	private const int TodayIndex = 2;
	private const double Gap = 1.0;

	public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
		nameof(Item), typeof(ScheduleItem), typeof(ScheduleBar),
		new PropertyMetadata(null, OnItemChanged));

	public ScheduleBar()
	{
		InitializeComponent();
		Loaded += (_, _) => Redraw();
	}

	public ScheduleItem? Item
	{
		get => (ScheduleItem?)GetValue(ItemProperty);
		set => SetValue(ItemProperty, value);
	}

	private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		=> ((ScheduleBar)d).Redraw();

	private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

	private void Redraw()
	{
		Host.Children.Clear();
		var width = Host.ActualWidth;
		if (width <= 0) return;

		var item = Item;
		var now = DateTime.Now;
		var today = now.Date;
		var palette = item is null ? Colors.Gray : ParseColor(item.Color, item.GameName);
		// 颜色 = 游戏，但同一款游戏里「活动」用浅一档（DSH newPanel 的约定），
		// 这样不用看图例也能分清一条是卡池还是活动。
		if (item is { IsGacha: false })
		{
			palette = Mix(palette, Color.FromArgb(255, 255, 255, 255), 0.42);
		}
		var baseBrush = new SolidColorBrush(WithAlpha(palette, 0x1F));
		var liveBrush = new SolidColorBrush(palette);
		var endedBrush = new SolidColorBrush(WithAlpha(palette, 0x66));
		var longBrush = new SolidColorBrush(WithAlpha(palette, 0x8C));

		var cell = (width - Gap * (Days - 1)) / Days;
		if (cell <= 0.5) return;
		var radius = Math.Max(1.0, cell / 5.0);

		for (var i = 0; i < Days; i++)
		{
			var day = today.AddDays(i - TodayIndex);
			var x = i * (cell + Gap);

			// 今天列：淡高亮（上下各伸出 1px）
			if (i == TodayIndex)
			{
				var marker = new Rectangle
				{
					Width = cell,
					Height = Host.Height,
					RadiusX = radius,
					RadiusY = radius,
					Fill = new SolidColorBrush(WithAlpha(GetThemeGray(), 0x2E)),
				};
				Canvas.SetLeft(marker, x);
				Canvas.SetTop(marker, 0);
				Host.Children.Add(marker);
			}

			var over = item is not null && Overlaps(item, day);
			var rect = new Rectangle
			{
				Width = cell,
				Height = 8,
				RadiusX = radius,
				RadiusY = radius,
				Fill = baseBrush,
			};

			if (over)
			{
				switch (item!.State)
				{
					case ScheduleState.Live:
						rect.Fill = liveBrush;
						break;
					case ScheduleState.Ended:
						rect.Fill = endedBrush;
						break;
					case ScheduleState.Long:
						rect.Fill = longBrush;
						rect.Height = 4;
						break;
					case ScheduleState.Waiting:
						rect.Fill = null;
						rect.Stroke = new SolidColorBrush(WithAlpha(palette, 0xB0));
						rect.StrokeThickness = 1;
						rect.Height = 6;
						break;
				}
			}

			Canvas.SetLeft(rect, x);
			Canvas.SetTop(rect, (Host.Height - rect.Height) / 2);
			Host.Children.Add(rect);
		}
	}

	private static bool Overlaps(ScheduleItem item, DateTime day)
	{
		var dayStart = day.Date;
		var dayEnd = dayStart.AddDays(1);
		var start = item.Start ?? dayStart;
		var end = item.End ?? dayEnd;
		return start < dayEnd && end > dayStart;
	}

	private static Color GetThemeGray()
	{
		// 今天列的高亮：用系统前景色 12% 透明，深浅色都安全。
		var theme = Application.Current.RequestedTheme;
		return theme == ApplicationTheme.Dark
			? Color.FromArgb(255, 255, 255, 255)
			: Color.FromArgb(255, 32, 32, 32);
	}

	private static Color ParseColor(string? hex, string? fallbackName)
	{
		if (!string.IsNullOrWhiteSpace(hex) && hex.Length == 7 && hex[0] == '#'
			&& byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
			&& byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
			&& byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
		{
			return Color.FromArgb(255, r, g, b);
		}

		var brush = Converters.GameNameToBrushConverter.BrushFor(fallbackName);
		return brush.Color;
	}

	private static Color WithAlpha(Color color, byte alpha)
		=> Color.FromArgb(alpha, color.R, color.G, color.B);

	private static Color Mix(Color a, Color b, double t)
		=> Color.FromArgb(
			255,
			(byte)Math.Round(a.R + (b.R - a.R) * t),
			(byte)Math.Round(a.G + (b.G - a.G) * t),
			(byte)Math.Round(a.B + (b.B - a.B) * t));
}
