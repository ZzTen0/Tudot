using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlbumManager.Converters;

public class StringToColorConverter : IValueConverter
{
    private static readonly Brush[] Colors = new Brush[]
    {
        new SolidColorBrush(Color.FromRgb(64, 158, 255)),   // Blue
        new SolidColorBrush(Color.FromRgb(103, 194, 58)),   // Green
        new SolidColorBrush(Color.FromRgb(144, 101, 192)),  // Purple
        new SolidColorBrush(Color.FromRgb(245, 108, 108)),  // Red
        new SolidColorBrush(Color.FromRgb(230, 162, 60)),   // Orange
        new SolidColorBrush(Color.FromRgb(64, 204, 200)),   // Teal
        new SolidColorBrush(Color.FromRgb(255, 159, 64)),   // Yellow-Orange
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text && !string.IsNullOrEmpty(text))
        {
            var hash = text.GetHashCode();
            return Colors[Math.Abs(hash) % Colors.Length];
        }
        return Colors[0];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FileTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVideo = value is string fileType && fileType == "video";
        var invert = parameter as string == "invert";
        return (isVideo ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 文件路径 → 缩略图 BitmapImage（磁盘缓存 + 降采样）。
/// ConverterParameter 指定解码宽度（默认 300）；视频文件自动走视频缩略图服务。
/// 配合 Binding IsAsync=True 在后台线程执行，Freeze 后可跨线程使用，避免 UI 卡顿。
/// </summary>
public class ThumbnailConverter : IValueConverter
{
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts" };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        var decodeWidth = 300;
        if (parameter is string s && int.TryParse(s, out var w))
            decodeWidth = w;

        var ext = Path.GetExtension(path);
        if (VideoExts.Contains(ext))
            return Services.VideoThumbnailService.GetThumbnail(path, decodeWidth);

        return Services.ThumbnailCache.GetThumbnail(path, decodeWidth);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>布尔取反转换器</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>视图模式匹配转换器：值与 ConverterParameter 相等时 Visible，否则 Collapsed</summary>
public class ModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>数值乘法转换器，ConverterParameter 为乘数（用于按比例换算卡片高度）</summary>
public class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && double.TryParse(parameter as string, NumberStyles.Any, culture, out var factor))
            return d * factor;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long size)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            int unitIndex = 0;
            double displaySize = size;

            while (displaySize >= 1024 && unitIndex < units.Length - 1)
            {
                displaySize /= 1024;
                unitIndex++;
            }

            return $"{displaySize:F1} {units[unitIndex]}";
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 字符串为空 → Visible（显示占位）；非空 → Collapsed。用于封面占位图。
/// 传入 ConverterParameter="invert" 时反转：非空 → Visible。
/// </summary>
public class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrEmpty(value as string);
        var invert = parameter as string == "invert";
        var visible = isEmpty ^ invert;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                return new ImageBrush(bitmap);
            }
            catch { }
        }
        return new SolidColorBrush(Color.FromRgb(240, 240, 240));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
