using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GachaWidget.Controls;

/// <summary>
/// 游戏图标：优先外链 PNG（BitmapImage 走系统 HTTP 缓存），失败退化为"首字 + 色块"。
/// 不阻塞行渲染：Image 默认 Collapsed，加载成功才显示。
/// </summary>
public sealed partial class GameIcon : UserControl
{
	public static readonly DependencyProperty IconUrlProperty = DependencyProperty.Register(
		nameof(IconUrl), typeof(string), typeof(GameIcon), new PropertyMetadata("", OnIconUrlChanged));

	public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
		nameof(Glyph), typeof(string), typeof(GameIcon), new PropertyMetadata("?", OnGlyphChanged));

	public GameIcon()
	{
		InitializeComponent();
		Unloaded += (_, _) => Release();
	}

	public string IconUrl
	{
		get => (string)GetValue(IconUrlProperty);
		set => SetValue(IconUrlProperty, value);
	}

	public string Glyph
	{
		get => (string)GetValue(GlyphProperty);
		set => SetValue(GlyphProperty, value);
	}

	private static void OnIconUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		=> ((GameIcon)d).LoadIcon(e.NewValue as string);

	private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		=> ((GameIcon)d).FallbackGlyph.Text = string.IsNullOrWhiteSpace(e.NewValue as string) ? "?" : (string)e.NewValue;

	private void LoadIcon(string? url)
	{
		Release();
		if (string.IsNullOrWhiteSpace(url)) return;

		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
		if (uri.Scheme is not ("http" or "https")) return;

		try
		{
			var bitmap = new BitmapImage
			{
				DecodePixelWidth = 64,
				DecodePixelType = DecodePixelType.Logical,
				UriSource = uri,
			};
			IconImage.Source = bitmap;
		}
		catch
		{
			// 非法 URI / 解码失败：保持兜底。
		}
	}

	private void OnImageOpened(object sender, RoutedEventArgs e)
	{
		FallbackHost.Visibility = Visibility.Collapsed;
		IconImage.Visibility = Visibility.Visible;
	}

	private void OnImageFailed(object sender, ExceptionRoutedEventArgs e)
	{
		Release();
	}

	private void Release()
	{
		IconImage.Visibility = Visibility.Collapsed;
		IconImage.Source = null;
		FallbackHost.Visibility = Visibility.Visible;
	}
}
