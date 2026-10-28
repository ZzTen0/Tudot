using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tudot.Services;
using Tudot.Models;
using Tudot.ViewModels;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static int _passed;

    [STAThread]
    public static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try { await Check(); app.Shutdown(0); }
            catch (Exception ex) { Console.Error.WriteLine(ex); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static void Assert(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        _passed++;
        Console.WriteLine($"PASS: {description}");
    }

    private static async Task WaitFor(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Viewport thumbnail did not settle");
            await Task.Delay(25);
        }
    }

    private static async Task Check()
    {
        // 只验证集合操作，跳过会打开真实图库的构造函数及任何数据库写入。
        var model = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        void SetField(string name, object value) => typeof(MainViewModel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, value);
        SetField("<Albums>k__BackingField", new ObservableCollection<Album>(
            Enumerable.Range(1, 120).Select(i => new Album { Id = i, Name = $"Album {i}" })));
        SetField("<PagedAlbums>k__BackingField", new ObservableCollection<Album>());
        model.UpdatePaging();
        Assert(model.PagedAlbums.Count == 48 && model.Albums.Count == 120, "全部模式首批限制为48项，全集保留");
        model.SelectAllAlbums(true);
        Assert(model.SelectedAlbumCount == 120, "全部模式全选覆盖尚未生成卡片的相册");
        model.LoadMoreAlbums();
        Assert(model.PagedAlbums.Count == 96 && model.PagedAlbums.All(a => a.IsSelected), "追加卡片保留全选状态");
        model.LoadMoreAlbums();
        model.LoadMoreAlbums();
        Assert(model.PagedAlbums.Count == 120 && model.PagedAlbums.Select(a => a.Id).Distinct().Count() == 120, "滚动追加至全集后不重复或遗漏");
        SetField("_homePaged", true);
        SetField("_pageSize", 30);
        model.UpdatePaging();
        model.SelectAllAlbums(true);
        model.LoadMoreAlbums();
        Assert(model.PagedAlbums.Count == 30 && model.SelectedAlbumCount == 30, "分页模式保持每页数量与当前页选择语义");

        var directory = Path.Combine(Path.GetTempPath(), "TudotStartupChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ThumbnailCache.CurrentCacheDir = Path.Combine(directory, "cache");
        var path = Path.Combine(directory, "fixture.bmp");
        var bitmap = BitmapSource.Create(120, 80, 96, 96, PixelFormats.Bgra32, null, new byte[120 * 80 * 4], 120 * 4);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);

        var first = ThumbnailLoader.GetAsync(path, 64, CancellationToken.None);
        var second = ThumbnailLoader.GetAsync(path, 64, CancellationToken.None);
        var loaded = await first;
        Assert(loaded is { IsFrozen: true, PixelWidth: 64 }, "后台生成冻结的目标尺寸缩略图");
        Assert(ReferenceEquals(loaded, await second), "重复请求复用同一个解码结果");
        Assert(ThumbnailLoader.PendingCount == 0, "完成的请求从任务表移除");
        Assert(ReferenceEquals(loaded, await ThumbnailLoader.GetAsync(path, 64, CancellationToken.None)), "内存命中复用位图对象");

        var cancelled = new CancellationToken(true);
        var observedCancellation = false;
        try { await ThumbnailLoader.GetAsync(path, 64, cancelled); }
        catch (OperationCanceledException) { observedCancellation = true; }
        Assert(observedCancellation && ThumbnailLoader.PendingCount == 0, "取消请求不进入后台队列");

        await ThumbnailLoader.GetAsync(path, 96, CancellationToken.None);
        Assert(Directory.GetFiles(ThumbnailCache.CurrentCacheDir, "*.jpg").Length == 2, "同一媒体的不同尺寸缓存同时保留");
        Assert(ThumbnailLoader.MemoryBytes is > 0 and <= ThumbnailLoader.MemoryBudget, "内存缓存受预算约束");

        var stack = new StackPanel();
        var images = new List<Image>();
        for (var i = 0; i < 30; i++)
        {
            var image = new Image { Width = 120, Height = 100 };
            ThumbnailImage.SetPixelWidth(image, 64);
            ThumbnailImage.SetSourcePath(image, path);
            stack.Children.Add(image);
            images.Add(image);
        }
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Width = 320, Height = 240, Left = -32000, Top = -32000,
            ShowInTaskbar = false, ShowActivated = false, Content = scroll };
        window.Show();
        await Task.Delay(200);
        await WaitFor(() => images[0].Source != null);
        Assert(images[^1].Source == null, "视口外远处图片不加载");
        scroll.ScrollToEnd();
        await WaitFor(() => images[^1].Source != null && images[0].Source == null);
        Assert(true, "滚动后加载新视口并释放离屏 Image.Source");
        ThumbnailImage.SetSourcePath(images[^1], Path.Combine(directory, "missing.jpg"));
        await Task.Delay(100);
        Assert(images[^1].Source == null, "复用绑定不会残留旧图");
        window.Close();
        await WaitFor(() => ThumbnailLoader.PendingCount == 0);
        Assert(true, "卸载视图后未完成请求释放");

        ThumbnailLoader.ClearMemory();
        Assert(ThumbnailLoader.MemoryBytes == 0, "清理缓存释放内存层");
        Console.WriteLine($"{_passed} checks passed. Fixtures: {directory}");
    }
}
