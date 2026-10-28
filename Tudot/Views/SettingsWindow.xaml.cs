using System.IO;
using System.Windows;
using System.Windows.Input;
using Tudot.ViewModels;
using Microsoft.Win32;
using Tudot.Updates;

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
        var preferences = UpdatePreferences.Load();
        var mode = preferences.Mode;
        UpdateSourceCombo.SelectedIndex = preferences.Source == UpdateSourceKind.Local ? 1 : 0;
        LocalUpdateDirectoryBox.Text = preferences.LocalDirectory;
        if (string.IsNullOrWhiteSpace(LocalUpdateDirectoryBox.Text))
        {
            try { LocalUpdateDirectoryBox.Text = UpdateJson.Read<UpdateSource>(Path.Combine(AppContext.BaseDirectory, "update-source.json")).SourceDirectory; }
            catch { }
        }
        AutomaticUpdateRadio.IsChecked = mode == UpdateMode.Automatic;
        NotifyUpdateRadio.IsChecked = mode == UpdateMode.Notify;
        DisabledUpdateRadio.IsChecked = mode == UpdateMode.Disabled;
        LoadUpdateInformation();
    }

    /// <summary>读取已安装构建及本地来源，仅展示配置，不访问更新源目录。</summary>
    private void LoadUpdateInformation()
    {
        try
        {
            var build = UpdateJson.Read<BuildInfo>(Path.Combine(AppContext.BaseDirectory, "build.json"));
            CurrentBuildText.Text = string.IsNullOrEmpty(build.Version) ? $"当前构建：{build.BuildId}"
                : $"当前版本：v{build.Version} · 构建 {build.BuildId}";
        }
        catch { CurrentBuildText.Text = "当前构建：开发版本"; }
        UpdateSourceText.Text = $"官方仓库：{UpdateFeed.Repository} · GitHub 每 15 分钟检查，本地每分钟检查";
    }

    /// <summary>读取界面暂存的来源和策略，保存前不改变后台检查来源。</summary>
    private UpdatePreferences SelectedUpdatePreferences => new()
    {
        Mode = SelectedUpdateMode,
        Source = UpdateSourceCombo.SelectedIndex == 1 ? UpdateSourceKind.Local : UpdateSourceKind.GitHub,
        LocalDirectory = LocalUpdateDirectoryBox.Text.Trim()
    };

    /// <summary>本地来源才显示目录选择器。</summary>
    private void UpdateSourceCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LocalUpdateSourcePanel != null)
            LocalUpdateSourcePanel.Visibility = UpdateSourceCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>选择包含 latest.json 的本地发布目录。</summary>
    private void BrowseUpdateSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择本地更新源目录" };
        if (dialog.ShowDialog(this) == true) LocalUpdateDirectoryBox.Text = dialog.FolderName;
    }

    /// <summary>手动导入新版离线更新包；与自动检查开关独立，安装前另行确认。</summary>
    private async void InstallUpdatePackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Tudot 更新包", Filter = "Tudot 更新包|*.tudotupdate", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        InstallUpdatePackageButton.IsEnabled = false;
        try { await Services.LocalUpdateService.InstallOfflineAsync(this, dialog.FileName); }
        finally { InstallUpdatePackageButton.IsEnabled = true; }
    }

    /// <summary>返回尚未保存的选项；关闭或取消设置不会提交更新偏好。</summary>
    private UpdateMode SelectedUpdateMode => AutomaticUpdateRadio.IsChecked == true ? UpdateMode.Automatic
        : DisabledUpdateRadio.IsChecked == true ? UpdateMode.Disabled : UpdateMode.Notify;

    /// <summary>同步选项说明；不更新模式下禁止手动检查。</summary>
    private void UpdateMode_Checked(object sender, RoutedEventArgs e)
    {
        if (UpdateModeHint == null || CheckUpdateButton == null) return;
        UpdateModeHint.Text = SelectedUpdateMode switch
        {
            UpdateMode.Automatic => "准备更新包后自动重启；打开子窗口时延后执行。",
            UpdateMode.Disabled => "停止检查和自动重启；仍可主动安装本地更新包。",
            _ => "发现新版本时提醒，由你确认后重启更新。"
        };
        CheckUpdateButton.IsEnabled = SelectedUpdateMode != UpdateMode.Disabled;
    }

    /// <summary>只检查清单并显示结果；更新策略保存且设置窗口关闭后才允许重启。</summary>
    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedUpdateMode == UpdateMode.Disabled) return;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新源…";
        try
        {
            var candidate = await UpdateFeed.CheckAsync(AppContext.BaseDirectory, SelectedUpdatePreferences);
            UpdateStatusText.Text = candidate == null ? "当前没有可用的兼容更新。"
                : $"发现新构建 {candidate.Manifest.BuildId}。点击「确定」保存并关闭后，按所选模式更新。";
        }
        catch (Exception ex) { UpdateStatusText.Text = "检查更新失败：" + ex.Message; }
        finally { CheckUpdateButton.IsEnabled = SelectedUpdateMode != UpdateMode.Disabled; }
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

    /// <summary>清除缩略图缓存：相册与图片缓存删除，创作者封面缓存保留</summary>
    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ModernDialog.Confirm(this, "确定清除所有相册与图片的缩略图缓存吗？\n创作者封面缓存将保留，清除后缩略图会重新生成。", "清除缓存"))
            return;

        var keepPaths = _viewModel.Creators.Select(c => c.ThumbPath);
        var (deleted, freed) = Services.ThumbnailCache.ClearDiskCache(keepPaths);
        ModernDialog.Info(this, $"已清除 {deleted} 个缓存文件，释放 {freed / 1024.0 / 1024.0:F1} MB。", "清除完成");
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
        try
        {
            var preferences = SelectedUpdatePreferences;
            if (preferences.Source == UpdateSourceKind.Local && !Directory.Exists(preferences.LocalDirectory))
                throw new IOException("请选择有效的本地更新源目录。");
            preferences.Save();
        }
        catch (Exception ex)
        {
            ModernDialog.Info(this, "无法保存更新设置：\n" + ex.Message, "保存失败");
            return;
        }
        _viewModel.CachePath = cachePath;

        _viewModel.SaveLibraryPath(path);
        ModernDialog.Info(this, "设置已保存。", "设置已保存");
        Services.LocalUpdateService.PreferencesChanged();
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
