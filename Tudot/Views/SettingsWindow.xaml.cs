using System.IO;
using System.Windows;
using System.Windows.Input;
using Tudot.ViewModels;
using Microsoft.Win32;

namespace Tudot.Views;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        PathTextBox.Text = viewModel.LibraryPath;
        CachePathTextBox.Text = viewModel.CachePath;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择库存储路径" };
        if (dialog.ShowDialog() == true)
        {
            PathTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择缩略图缓存路径" };
        if (dialog.ShowDialog() == true)
        {
            CachePathTextBox.Text = dialog.FolderName;
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        PathTextBox.Text = MainViewModel.DefaultLibraryPath;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            ModernDialog.Info(this, "路径不能为空", "提示");
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            ModernDialog.Info(this, "路径无效或无权限创建该目录", "错误");
            return;
        }

        // 缓存路径（留空则恢复默认）
        var cachePath = CachePathTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(cachePath))
        {
            try
            {
                Directory.CreateDirectory(cachePath);
            }
            catch
            {
                ModernDialog.Info(this, "缓存路径无效或无权限创建该目录", "错误");
                return;
            }
        }
        _viewModel.CachePath = cachePath;

        _viewModel.SaveLibraryPath(path);
        ModernDialog.Info(this, "设置已保存。", "设置已保存");
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
