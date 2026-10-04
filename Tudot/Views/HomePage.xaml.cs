using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        UpdateModeButtons();
    }

    private void Card_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        if (_viewModel.MultiSelectAlbumsMode)
        {
            if (element.Tag is int id && _viewModel.Albums.FirstOrDefault(a => a.Id == id) is { } album)
                _viewModel.ToggleAlbumSelection(album);
            return;
        }

        if (element.Tag is int albumId)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowAlbumDetail(albumId);
        }
    }

    private void MultiSelectAlbumsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MultiSelectAlbumsMode = !_viewModel.MultiSelectAlbumsMode;
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e) => _viewModel.PrevPage();
    private void NextPageButton_Click(object sender, RoutedEventArgs e) => _viewModel.NextPage();

    private void PagedModeButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.HomePaged = true;
        UpdateModeButtons();
    }

    private void AllModeButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.HomePaged = false;
        UpdateModeButtons();
    }

    private void UpdateModeButtons()
    {
        var active = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        var inactive = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        PagedModeBtn.Foreground = _viewModel.HomePaged ? active : inactive;
        AllModeBtn.Foreground = _viewModel.HomePaged ? inactive : active;
    }

    private void AlbumsSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        var all = _viewModel.PagedAlbums.Count > 0
                  && _viewModel.SelectedAlbumCount == _viewModel.PagedAlbums.Count;
        _viewModel.SelectAllAlbums(!all);
    }

    private void FavoriteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAlbumCount == 0)
        {
            ModernDialog.Info(Window.GetWindow(this), "请先点击卡片选择相册", "批量收藏");
            return;
        }
        // 所选全部已收藏则取消收藏，否则收藏
        var selected = _viewModel.PagedAlbums.Where(a => a.IsSelected).ToList();
        var target = selected.Any(a => !a.IsFavorite);
        _viewModel.SetFavoriteSelectedAlbums(target);
    }

    private void DeleteSelectedAlbumsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAlbumCount == 0)
        {
            ModernDialog.Info(Window.GetWindow(this), "请先点击卡片选择相册", "批量删除");
            return;
        }

        var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(
            Window.GetWindow(this),
            $"确定要删除所选的 {_viewModel.SelectedAlbumCount} 个相册吗？\n默认仅删除程序内的索引记录，勾选后将同时删除磁盘上的源文件。",
            "批量删除相册");

        if (confirmed)
            _viewModel.DeleteSelectedAlbums(deleteFiles);
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
