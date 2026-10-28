using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tudot.Services;

/// <summary>改变图片加载机制，不改变页面布局。仅对视口及附近半屏的图片请求缩略图。</summary>
public static class ThumbnailImage
{
    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.RegisterAttached(
        "SourcePath", typeof(string), typeof(ThumbnailImage), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty PixelWidthProperty = DependencyProperty.RegisterAttached(
        "PixelWidth", typeof(int), typeof(ThumbnailImage), new PropertyMetadata(300, Changed));
    private static readonly ConditionalWeakTable<Image, State> States = new();
    private static readonly ConditionalWeakTable<ScrollViewer, Viewport> Viewports = new();

    public static void SetSourcePath(DependencyObject target, string? value) => target.SetValue(SourcePathProperty, value);
    public static string? GetSourcePath(DependencyObject target) => (string?)target.GetValue(SourcePathProperty);
    public static void SetPixelWidth(DependencyObject target, int value) => target.SetValue(PixelWidthProperty, value);
    public static int GetPixelWidth(DependencyObject target) => (int)target.GetValue(PixelWidthProperty);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is Image image) States.GetValue(image, item => new State(item)).Invalidate();
    }

    private sealed class Viewport
    {
        public readonly ScrollViewer Owner;
        public readonly HashSet<State> Items = new();
        private bool _queued;

        public Viewport(ScrollViewer owner)
        {
            Owner = owner;
            owner.ScrollChanged += (_, _) => Queue();
            owner.SizeChanged += (_, _) => Queue();
        }

        public void Queue()
        {
            if (_queued) return;
            _queued = true;
            Owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _queued = false;
                foreach (var item in Items.ToArray()) item.Update();
            }));
        }
    }

    private sealed class State
    {
        private readonly Image _image;
        private Viewport? _viewport;
        private CancellationTokenSource? _request;
        private bool _fallbackQueued;

        public State(Image image)
        {
            _image = image;
            image.Loaded += (_, _) => Attach();
            image.Unloaded += (_, _) => Detach();
            image.IsVisibleChanged += (_, _) => Queue();
            image.SizeChanged += (_, _) => Queue();
            if (image.IsLoaded) Attach();
        }

        private void Attach()
        {
            if (_viewport != null) return;
            DependencyObject? parent = VisualTreeHelper.GetParent(_image);
            while (parent != null && parent is not ScrollViewer)
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is ScrollViewer scroll)
            {
                _viewport = Viewports.GetValue(scroll, item => new Viewport(item));
                _viewport.Items.Add(this);
            }
            Queue();
        }

        private void Detach()
        {
            _viewport?.Items.Remove(this);
            _viewport = null;
            Stop();
        }

        public void Invalidate()
        {
            Stop();
            Queue();
        }

        private void Queue()
        {
            if (_viewport != null) { _viewport.Queue(); return; }
            if (_fallbackQueued || !_image.IsLoaded) return;
            _fallbackQueued = true;
            _image.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _fallbackQueued = false;
                Update();
            }));
        }

        private bool IsNearViewport()
        {
            if (!_image.IsLoaded || !_image.IsVisible)
                return false;
            // Source 为空时 Image.RenderSize 可能为零；使用父布局实际分配的区域启动首图请求。
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(_image);
            if (slot.Width <= 0 || slot.Height <= 0) return false;
            if (_viewport == null) return true;
            try
            {
                var owner = _viewport.Owner;
                if (VisualTreeHelper.GetParent(_image) is not Visual parent) return false;
                var bounds = parent.TransformToAncestor(owner).TransformBounds(slot);
                var viewport = new Rect(0, -owner.ActualHeight / 2, owner.ActualWidth, owner.ActualHeight * 2);
                return viewport.IntersectsWith(bounds);
            }
            catch (InvalidOperationException) { return false; }
        }

        public void Update()
        {
            if (!IsNearViewport()) { Stop(); return; }
            if (_request != null) return;
            var path = GetSourcePath(_image);
            if (string.IsNullOrWhiteSpace(path)) return;
            var request = new CancellationTokenSource();
            _request = request;
            _ = Load(path, GetPixelWidth(_image), request);
        }

        private async Task Load(string path, int width, CancellationTokenSource request)
        {
            try
            {
                var bitmap = await ThumbnailLoader.GetAsync(path, width, request.Token);
                // 回到图片所属 Dispatcher；容器回收、绑定变化后的旧结果不可覆盖新图。
                if (ReferenceEquals(_request, request) && !request.IsCancellationRequested)
                    _image.Source = bitmap;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"Thumbnail load failed: {ex.GetType().Name}"); }
        }

        private void Stop()
        {
            _request?.Cancel();
            _request = null;
            _image.Source = null;
        }
    }
}
