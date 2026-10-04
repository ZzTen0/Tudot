using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace AlbumManager.Services;

/// <summary>
/// 图片缩略图磁盘缓存。键 = 路径哈希 + 宽度 + 文件修改时间。
/// 缓存目录默认为应用目录下 Thumbs 文件夹，可通过 CurrentCacheDir 自定义。
/// </summary>
public static class ThumbnailCache
{
    /// <summary>默认缓存目录：应用目录\Thumbs</summary>
    public static readonly string DefaultCacheDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Thumbs");

    /// <summary>当前缓存目录（可在设置中自定义）</summary>
    public static string CurrentCacheDir { get; set; } = DefaultCacheDir;

    /// <summary>获取缩略图（命中缓存则直接读取，否则解码原图并写入缓存）。可在后台线程调用。</summary>
    public static BitmapImage? GetThumbnail(string path, int width)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            var cacheDir = CurrentCacheDir;
            Directory.CreateDirectory(cacheDir);
            var fi = new FileInfo(path);
            var key = Convert.ToHexString(SHA1.HashData(
                Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..16];
            var cachePath = Path.Combine(cacheDir, $"{key}_{width}_{fi.LastWriteTimeUtc.Ticks}.jpg");

            if (File.Exists(cachePath))
                return Load(cachePath, 0);

            var bitmap = Load(path, width);
            if (bitmap == null) return null;

            // 清理同一路径的旧缓存
            try
            {
                foreach (var old in Directory.GetFiles(cacheDir, $"{key}_*.jpg"))
                {
                    if (old != cachePath) File.Delete(old);
                }
            }
            catch { }

            SaveJpeg(bitmap, cachePath);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按修改时间获取缓存文件路径（供视频缩略图服务复用）</summary>
    public static string GetCachePath(string sourcePath, int width, string tag)
    {
        Directory.CreateDirectory(CurrentCacheDir);
        var key = Convert.ToHexString(SHA1.HashData(
            Encoding.UTF8.GetBytes(sourcePath.ToLowerInvariant())))[..16];
        var ticks = File.Exists(sourcePath)
            ? new FileInfo(sourcePath).LastWriteTimeUtc.Ticks
            : 0;
        return Path.Combine(CurrentCacheDir, $"{key}{tag}_{width}_{ticks}.jpg");
    }

    public static BitmapImage? Load(string path, int decodeWidth)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveJpeg(BitmapSource source, string path)
    {
        try
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }
        catch { }
    }
}
