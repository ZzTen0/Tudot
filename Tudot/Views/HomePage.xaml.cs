using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tudot.Models;
using Tudot.ViewModels;

namespace Tudot.Views;

public partial class HomePage : UserControl
{
    private MainViewModel _viewModel;

    public HomePage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Card_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int albumId)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowAlbumDetail(albumId);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Album album })
            _viewModel.ToggleFavorite(album);
    }

    private void AlbumMenu_Favorite(object sender, RoutedEventArgs e)
    {
        if (GetContextAlbum(sender) is { } album)
            _viewModel.ToggleFavorite(album);
    }

    private Album? GetContextAlbum(object sender)
        => ((sender as MenuItem)?.Parent as ContextMenu)?.Tag as Album;

    private void AlbumMenu_Edit(object sender, RoutedEventArgs e)
    {
        if (GetContextAlbum(sender) is not { } album) return;

        // 进入详情页后再打开编辑，确保 CurrentAlbumImages 已加载
        _viewModel.LoadAlbumDetail(album.Id);
        var dialog = new AlbumEditDialog(_viewModel, album)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.UpdateAlbum(
                album.Id,
                dialog.NewName,
                dialog.NewCreatorId,
                dialog.NewCoverPath,
                dialog.NewPath,
                dialog.MoveFiles,
                dialog.OldPath);
        }
    }

    private void AlbumMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (GetContextAlbum(sender) is not { } album) return;

        var newName = ModernDialog.Input(
            Window.GetWindow(this),
            $"重命名相册：{album.Name}\n输入新名称:",
            "重命名相册",
            album.Name);

        if (!string.IsNullOrWhiteSpace(newName))
            _viewModel.UpdateAlbum(album.Id, newName.Trim(), album.CreatorId, album.CoverPath, album.Path);
    }

    private void AlbumMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (GetContextAlbum(sender) is not { } album) return;

        var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(
            Window.GetWindow(this),
            $"确定要删除相册「{album.Name}」吗？\n默认仅删除程序内的索引记录，勾选后将同时删除磁盘上的源文件。",
            "删除相册");

        if (confirmed)
            _viewModel.DeleteAlbum(album.Id, deleteFiles);
    }

    private void AlbumMenu_OpenFolder(object sender, RoutedEventArgs e)
    {
        if (GetContextAlbum(sender) is not { } album) return;

        try
        {
            if (Directory.Exists(album.Path))
                System.Diagnostics.Process.Start("explorer.exe", album.Path);
        }
        catch { }
    }
}
