using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Tudot.Services;

/// <summary>仅设置 TUDOT_STARTUP_REPORT 时启用，用于比较实际启动路径。</summary>
internal static class StartupTrace
{
    private static readonly string? ReportPath = Environment.GetEnvironmentVariable("TUDOT_STARTUP_REPORT");
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, double> Stages = new();
    private static int _thumbnailCount;
    private static int _uiThumbnailCount;
    private static double _thumbnailMs;
    private static double _lastWrite;

    public static bool Enabled => !string.IsNullOrWhiteSpace(ReportPath);

    public static void Mark(string stage)
    {
        if (!Enabled) return;
        lock (Gate)
        {
            Stages[stage] = Clock.Elapsed.TotalMilliseconds;
            Write();
        }
    }

    public static void ThumbnailCompleted(double milliseconds, bool onUiThread)
    {
        if (!Enabled) return;
        lock (Gate)
        {
            _thumbnailCount++;
            if (onUiThread) _uiThumbnailCount++;
            _thumbnailMs += milliseconds;
            if (Clock.Elapsed.TotalMilliseconds - _lastWrite > 1000) Write();
        }
    }

    private static void Write()
    {
        try
        {
            File.WriteAllText(ReportPath!, JsonSerializer.Serialize(new
            {
                stagesMs = Stages, thumbnailCount = _thumbnailCount,
                uiThumbnailCount = _uiThumbnailCount, thumbnailWorkMs = _thumbnailMs,
                workingSetBytes = Environment.WorkingSet,
                pendingThumbnails = ThumbnailLoader.PendingCount,
                thumbnailMemoryBytes = ThumbnailLoader.MemoryBytes,
                executablePath = Environment.ProcessPath
            }, new JsonSerializerOptions { WriteIndented = true }));
            _lastWrite = Clock.Elapsed.TotalMilliseconds;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
