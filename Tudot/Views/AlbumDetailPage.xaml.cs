using System.Windows;
using System.Windows.Controls;
using Tudot.ViewModels;

namespace Tudot.Views;

public partial class AlbumDetailPage : UserControl
{
    private MainViewModel _viewModel;

    public AlbumDetailPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += (_, _) => UpdateModeIcons();
    }

    private void CompactModeButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ThumbViewMode = "Compact";
        UpdateModeIcons();
    }

    private void GridModeButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ThumbViewMode = "Grid";
        UpdateModeIcons();
    }

    private void UpdateModeIcons()
    {
        var active = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB"));
        var inactive = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#999999"));
        CompactModeIcon.Foreground = _viewModel.ThumbViewMode == "Compact" ? active : inactive;
        GridModeIcon.Foreground = _viewModel.ThumbViewMode == "Grid" ? active : inactive;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.ShowPage("Home");
    }

    private void ImageSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // InitializeComponent 期间就会触发，此时 _viewModel 尚未赋值
        if (_viewModel == null) return;
        if (ImageSortCombo.SelectedItem is ComboBoxItem item && item.Tag is string sort)
            _viewModel.AlbumImageSort = sort;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var album = _viewModel.SelectedAlbum;
        if (album == null) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{album.Path}\"");
        }
        catch (Exception ex)
        {
            ModernDialog.Info(Window.GetWindow(this), $"无法打开文件夹：{ex.Message}", "错误");
        }
    }

    private void SetCoverButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAlbum == null) return;
        // 使用当前第一张图作为封面
        var first = _viewModel.CurrentAlbumImages.FirstOrDefault(f => f.FileType == "image");
        if (first != null)
        {
            _viewModel.SetAlbumCover(_viewModel.SelectedAlbum.Id, first.FilePath);
            ModernDialog.Info(Window.GetWindow(this), "已将第一张图片设为封面", "设置封面");
        }
        else
        {
            ModernDialog.Info(Window.GetWindow(this), "相册中没有图片", "设置封面");
        }
    }

    private void BatchRenameButton_Click(object sender, RoutedEventArgs e)
    {
        var pattern = ModernDialog.Input(
            Window.GetWindow(this),
            "输入命名格式（如: IMG_####，# 会被序号替换）:",
            "批量重命名",
            "IMG_####");
        if (!string.IsNullOrEmpty(pattern) && _viewModel.SelectedAlbum != null)
        {
            _viewModel.BatchRenameImages(_viewModel.SelectedAlbum.Id, pattern);
            ModernDialog.Info(Window.GetWindow(this), "重命名完成", "批量重命名");
        }
    }

    private void ImageItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Tudot.Models.ImageFile file }) return;

        if (_viewModel.MultiSelectMode)
        {
            _viewModel.ToggleImageSelection(file);
            return;
        }
        OpenImage(file);
    }

    private void MultiSelectButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.MultiSelectMode = !_viewModel.MultiSelectMode;
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        // 已全选时再点则取消全选
        var all = _viewModel.CurrentAlbumImages.Count > 0
                  && _viewModel.SelectedImageCount == _viewModel.CurrentAlbumImages.Count;
        _viewModel.SelectAllImages(!all);
    }

    private void BatchRenameSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedImageCount == 0)
        {
            ModernDialog.Info(Window.GetWindow(this), "请先点击缩略图选择要重命名的文件", "批量重命名");
            return;
        }

        var pattern = ModernDialog.Input(
            Window.GetWindow(this),
            $"将重命名所选 {_viewModel.SelectedImageCount} 个文件\n输入命名格式（如: IMG_####，# 会被序号替换）:",
            "批量重命名",
            "IMG_####");
        if (!string.IsNullOrEmpty(pattern))
        {
            _viewModel.BatchRenameSelectedImages(pattern);
            ModernDialog.Info(Window.GetWindow(this), "重命名完成", "批量重命名");
        }
    }

    private void BatchDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedImageCount == 0)
        {
            ModernDialog.Info(Window.GetWindow(this), "请先点击缩略图选择要删除的文件", "批量删除");
            return;
        }

        var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(
            Window.GetWindow(this),
            $"确定要删除所选的 {_viewModel.SelectedImageCount} 个文件吗？\n默认仅删除程序内的索引记录，勾选后将同时删除磁盘上的源文件。",
            "批量删除");

        if (confirmed)
            _viewModel.DeleteSelectedImages(deleteFiles);
    }

    private void OpenImage(Tudot.Models.ImageFile file)
    {
        if (_viewModel.UseInternalViewer && file.FileType == "image")
        {
            var images = _viewModel.CurrentAlbumImages.Where(f => f.FileType == "image").ToList();
            var index = images.IndexOf(file);
            var viewer = new ImageViewerWindow(images, index >= 0 ? index : 0)
            {
                Owner = Window.GetWindow(this)
            };
            viewer.ShowDialog();
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file.FilePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ModernDialog.Info(Window.GetWindow(this), $"无法打开文件：{ex.Message}", "打开失败");
        }
    }

    private Tudot.Models.ImageFile? GetContextImage(object sender)
        => (sender as MenuItem)?.Tag as Tudot.Models.ImageFile
           ?? ((sender as MenuItem)?.Parent as ContextMenu)?.Tag as Tudot.Models.ImageFile;

    private void ImageMenu_Open(object sender, RoutedEventArgs e)
    {
        if (GetContextImage(sender) is { } file)
            OpenImage(file);
    }

    private void ImageMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (GetContextImage(sender) is not { } file) return;

        var newName = ModernDialog.Input(
            Window.GetWindow(this),
            $"重命名文件：{file.FileName}\n输入新文件名（不含扩展名）:",
            "重命名",
            System.IO.Path.GetFileNameWithoutExtension(file.FileName));

        if (!string.IsNullOrWhiteSpace(newName))
            _viewModel.RenameImageFile(file.Id, newName.Trim());
    }

    private void ImageMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (GetContextImage(sender) is not { } file) return;

        var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(
            Window.GetWindow(this),
            $"确定要删除「{file.FileName}」吗？\n默认仅删除程序内的索引记录，勾选后将同时删除磁盘上的源文件。",
            "删除图片");

        if (confirmed)
            _viewModel.DeleteImageFile(file.Id, deleteFiles);
    }

    private void ImageMenu_Properties(object sender, RoutedEventArgs e)
    {
        if (GetContextImage(sender) is not { } file) return;

        var info = new System.IO.FileInfo(file.FilePath);
        var typeText = file.FileType switch
        {
            "image" => "图片",
            "video" => "视频",
            _ => "其他"
        };
        var msg = $"文件名：{file.FileName}\n" +
                  $"类型：{typeText}\n" +
                  $"大小：{FormatSize(file.FileSize)}\n" +
                  $"路径：{file.FilePath}";
        if (info.Exists)
            msg += $"\n修改时间：{info.LastWriteTime:yyyy-MM-dd HH:mm}";

        ModernDialog.Info(Window.GetWindow(this), msg, "图片属性");
    }

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        int i = 0;
        double s = size;
        while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
        return $"{s:F1} {units[i]}";
    }

    private void EditAlbumButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAlbum == null) return;

        var dialog = new AlbumEditDialog(_viewModel, _viewModel.SelectedAlbum)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.UpdateAlbum(
                _viewModel.SelectedAlbum.Id,
                dialog.NewName,
                dialog.NewCreatorId,
                dialog.NewCoverPath,
                dialog.NewPath,
                dialog.MoveFiles,
                dialog.OldPath);
        }
    }

    private void DeleteAlbumButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedAlbum == null) return;

        var owner = Window.GetWindow(this);
        var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(
            owner,
            $"确定要删除相册「{_viewModel.SelectedAlbum.Name}」吗？\n默认仅删除程序内的索引记录，勾选后将同时删除磁盘上的源文件。",
            "删除相册");

        if (!confirmed) return;

        var albumId = _viewModel.SelectedAlbum.Id;
        _viewModel.DeleteAlbum(albumId, deleteFiles);

        if (owner is MainWindow mainWindow)
            mainWindow.ShowPage("Home");
    }
}
