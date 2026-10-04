using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AlbumManager.Services;

/// <summary>
/// 视频缩略图服务：优先使用 FFmpeg（应用目录或 PATH 中），
/// 否则回退到 Windows Shell 缩略图接口（与资源管理器一致）。
/// 结果写入磁盘缓存。
/// </summary>
public static class VideoThumbnailService
{
    private static string? _ffmpegPath;
    private static bool _ffmpegSearched;

    public static BitmapImage? GetThumbnail(string videoPath, int width)
    {
        try
        {
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                return null;

            var cachePath = ThumbnailCache.GetCachePath(videoPath, width, "_v");
            if (File.Exists(cachePath))
                return ThumbnailCache.Load(cachePath, 0);

            BitmapSource? frame = TryExtractWithFfmpeg(videoPath, width)
                                  ?? TryExtractWithShell(videoPath, width);
            if (frame == null) return null;

            ThumbnailCache.SaveJpeg(frame, cachePath);
            var result = ThumbnailCache.Load(cachePath, 0);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryExtractWithFfmpeg(string videoPath, int width)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg == null) return null;

        var tempFile = Path.Combine(Path.GetTempPath(), $"am_thumb_{Guid.NewGuid():N}.jpg");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-y -ss 1 -i \"{videoPath}\" -frames:v 1 -vf scale={width}:-1 \"{tempFile}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(); } catch { }
                return null;
            }
            if (!File.Exists(tempFile)) return null;
            return ThumbnailCache.Load(tempFile, 0);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    private static string? FindFfmpeg()
    {
        if (_ffmpegSearched) return _ffmpegPath;
        _ffmpegSearched = true;

        // 应用目录
        var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return _ffmpegPath = local;

        // PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return _ffmpegPath = candidate;
            }
            catch { }
        }
        return null;
    }

    // ---- Windows Shell 缩略图回退 ----

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int SIIGBF_BIGGERSIZEOK = 0x01;
    private const int SIIGBF_THUMBNAILONLY = 0x08;

    private static BitmapSource? TryExtractWithShell(string videoPath, int width)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var guid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(videoPath, IntPtr.Zero, ref guid, out var factory);
            if (hr != 0 || factory == null) return null;

            factory.GetImage(new SIZE { cx = width, cy = width },
                SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out hBitmap);
            if (hBitmap == IntPtr.Zero) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        }
    }
}
