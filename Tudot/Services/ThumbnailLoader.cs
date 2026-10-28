using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Tudot.Services;

/// <summary>所有文件探测和解码显式在线程池执行；图片最多 3 个、视频最多 1 个。</summary>
internal static class ThumbnailLoader
{
    private static readonly SemaphoreSlim Images = new(3);
    private static readonly SemaphoreSlim Videos = new(1);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Request> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LinkedListNode<CacheEntry>> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<CacheEntry> Recent = new();
    private static long _bytes;
    private static int _generation;
    internal const long MemoryBudget = 128L * 1024 * 1024;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts" };

    private sealed class Request
    {
        public readonly CancellationTokenSource Cancellation = new();
        public Task<BitmapImage?> Work = null!;
        public int Readers;
    }

    private sealed record CacheEntry(string Key, BitmapImage? Image, long Bytes, DateTime Expires);
    internal static long MemoryBytes { get { lock (Gate) return _bytes; } }
    internal static int PendingCount { get { lock (Gate) return Pending.Count; } }

    public static async Task<BitmapImage?> GetAsync(string path, int width, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        width = Math.Clamp(width, 32, 2048);
        // 路径字符串规范化不探测磁盘；文件存在性和修改时间留在后台检查。
        path = Path.GetFullPath(path);
        var key = $"{ThumbnailCache.CurrentCacheDir}|{path}|{width}";
        Request request;
        lock (Gate)
        {
            if (!Pending.TryGetValue(key, out request!))
            {
                request = new Request();
                var generation = _generation;
                request.Work = Task.Run(() => LoadAsync(path, width, key, generation, request.Cancellation.Token));
                Pending.Add(key, request);
            }
            request.Readers++;
        }

        try { return await request.Work.WaitAsync(cancellation).ConfigureAwait(false); }
        finally
        {
            lock (Gate)
            {
                if (--request.Readers == 0)
                {
                    request.Cancellation.Cancel();
                    if (Pending.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                        Pending.Remove(key);
                }
            }
        }
    }

    private static async Task<BitmapImage?> LoadAsync(string path, int width, string key, int generation, CancellationToken cancellation)
    {
        var slots = VideoExtensions.Contains(Path.GetExtension(path)) ? Videos : Images;
        await slots.WaitAsync(cancellation).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var version = $"{key}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            lock (Gate)
            {
                if (Memory.TryGetValue(version, out var cached))
                {
                    Recent.Remove(cached);
                    if (cached.Value.Expires > DateTime.UtcNow)
                    {
                        Recent.AddFirst(cached);
                        return cached.Value.Image;
                    }
                    Memory.Remove(version);
                    _bytes -= cached.Value.Bytes;
                }
            }

            cancellation.ThrowIfCancellationRequested();
            var bitmap = ReferenceEquals(slots, Videos)
                ? VideoThumbnailService.GetThumbnail(path, width)
                : ThumbnailCache.GetThumbnail(path, width);
            cancellation.ThrowIfCancellationRequested();
            var bytes = bitmap == null ? 1024 : (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
            lock (Gate)
            {
                if (generation == _generation && bytes <= MemoryBudget)
                {
                    if (Memory.Remove(version, out var previous))
                    {
                        Recent.Remove(previous);
                        _bytes -= previous.Value.Bytes;
                    }
                    while (_bytes + bytes > MemoryBudget && Recent.Last is { } oldest)
                    {
                        Recent.RemoveLast();
                        Memory.Remove(oldest.Value.Key);
                        _bytes -= oldest.Value.Bytes;
                    }
                    var entry = new CacheEntry(version, bitmap, bytes,
                        bitmap == null ? DateTime.UtcNow.AddSeconds(30) : DateTime.MaxValue);
                    Memory[version] = Recent.AddFirst(entry);
                    _bytes += bytes;
                }
            }
            return bitmap;
        }
        finally
        {
            slots.Release();
            StartupTrace.ThumbnailCompleted(watch.Elapsed.TotalMilliseconds,
                Application.Current?.Dispatcher.CheckAccess() == true);
        }
    }

    public static void ClearMemory()
    {
        lock (Gate)
        {
            _generation++;
            Memory.Clear();
            Recent.Clear();
            _bytes = 0;
        }
    }
}
