using System.Windows;
using System.Windows.Controls;
using AlbumManager.ViewModels;

namespace AlbumManager.Views;

public partial class BookmarksPage : UserControl
{
    private MainViewModel _viewModel;

    public BookmarksPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void AddBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ModernDialog.Input(Window.GetWindow(this), "输入网址:", "添加网址收藏");
        if (string.IsNullOrEmpty(url)) return;

        var title = ModernDialog.Input(Window.GetWindow(this), "输入标题:", "添加网址收藏");
        if (!string.IsNullOrEmpty(title))
        {
            _viewModel.AddBookmark(title, url);
        }
    }

    private void BookmarkItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }

    private void DeleteBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AlbumManager.Models.Bookmark bookmark })
        {
            var (confirmed, _) = DeleteConfirmDialog.Show(
                Window.GetWindow(this),
                $"确定要删除收藏「{bookmark.Title}」吗？",
                "删除收藏",
                showFileOption: false);

            if (confirmed)
                _viewModel.DeleteBookmark(bookmark.Id);
        }
    }
}
