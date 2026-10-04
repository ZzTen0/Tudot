using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AlbumManager.Models;

namespace AlbumManager.Views;

public partial class ImageViewerWindow : Window
{
    private readonly List<ImageFile> _images;
    private int _currentIndex;
    private double _zoom = 1.0;

    public ImageViewerWindow(List<ImageFile> images, int startIndex)
    {
        InitializeComponent();
        _images = images;
        _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, images.Count - 1));
        LoadImage();
    }

    private void LoadImage()
    {
        if (_images.Count == 0) return;
        var file = _images[_currentIndex];
        ResetZoom();
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(file.FilePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            MainImage.Source = bitmap;
            InfoText.Text = $"{_currentIndex + 1} / {_images.Count}  {file.FileName}";
        }
        catch
        {
            InfoText.Text = $"{_currentIndex + 1} / {_images.Count}  无法加载";
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) => ShowNext();
    private void PrevButton_Click(object sender, RoutedEventArgs e) => ShowPrev();

    private void ShowNext()
    {
        if (_images.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _images.Count;
        LoadImage();
    }

    private void ShowPrev()
    {
        if (_images.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + _images.Count) % _images.Count;
        LoadImage();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ZoomIn() => SetZoom(_zoom * 1.15);
    private void ZoomOut() => SetZoom(_zoom / 1.15);

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 10.0);
        ImgScale.ScaleX = _zoom;
        ImgScale.ScaleY = _zoom;
        InfoText.Text = $"{_currentIndex + 1} / {_images.Count}  {_images[_currentIndex].FileName}  ({_zoom:P0})";
    }

    private void ResetZoom()
    {
        _zoom = 1.0;
        ImgScale.ScaleX = 1;
        ImgScale.ScaleY = 1;
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl + 滚轮：缩放
            if (e.Delta > 0) ZoomIn(); else ZoomOut();
        }
        else
        {
            // 普通滚轮：翻页
            if (e.Delta < 0) ShowNext(); else ShowPrev();
        }
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left: ShowPrev(); break;
            case Key.Right: ShowNext(); break;
            case Key.Add:
            case Key.OemPlus: ZoomIn(); break;
            case Key.Subtract:
            case Key.OemMinus: ZoomOut(); break;
            case Key.D0:
            case Key.NumPad0: ResetZoom(); break;
        }
    }

    private void ImageArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainImage);
        var width = MainImage.ActualWidth > 0 ? MainImage.ActualWidth : ActualWidth;
        if (pos.X < width * 0.3)
            ShowPrev();
        else if (pos.X > width * 0.7)
            ShowNext();
    }
}