using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tudot.Updates;
using Tudot.ViewModels;
using Tudot.Views;

/// <summary>加载真实设置窗口，验证模式切换及取消边界；不初始化数据库、不保存用户设置。</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Tudot.App();
        app.InitializeComponent();
        var original = File.Exists(UpdatePreferences.DefaultPath) ? File.ReadAllText(UpdatePreferences.DefaultPath) : null;
        // 使用无服务的模型仅满足只读绑定，避免测试触碰真实图库数据库。
        var model = (MainViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewModel));
        var window = new SettingsWindow(model) { ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
        var automatic = (RadioButton)window.FindName("AutomaticUpdateRadio");
        var notify = (RadioButton)window.FindName("NotifyUpdateRadio");
        var disabled = (RadioButton)window.FindName("DisabledUpdateRadio");
        var check = (Button)window.FindName("CheckUpdateButton");
        var hint = (TextBlock)window.FindName("UpdateModeHint");
        var passed = 0;
        void Assert(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        disabled.IsChecked = true;
        Assert(!check.IsEnabled && hint.Text.Contains("停止检查"), "不更新禁用检查入口并显示边界");
        Assert(((Button)window.FindName("InstallUpdatePackageButton")).IsEnabled, "关闭自动检查仍可主动导入离线更新包");
        var source = (ComboBox)window.FindName("UpdateSourceCombo");
        var localPanel = (Grid)window.FindName("LocalUpdateSourcePanel");
        source.SelectedIndex = 1;
        Assert(localPanel.Visibility == Visibility.Visible, "本地来源显示目录选择器");
        source.SelectedIndex = 0;
        Assert(localPanel.Visibility == Visibility.Collapsed, "GitHub 来源隐藏本地目录选择器");
        automatic.IsChecked = true;
        Assert(check.IsEnabled && notify.IsChecked != true && disabled.IsChecked != true, "自动模式互斥且允许检查");
        Assert(hint.Text.Contains("自动重启"), "自动模式明确说明重启行为");
        notify.IsChecked = true;
        Assert(check.IsEnabled && hint.Text.Contains("确认"), "提醒模式说明用户确认要求");
        window.Show();
        window.UpdateLayout();
        Assert(window.ActualHeight <= SystemParameters.WorkArea.Height, "设置窗口受工作区高度约束");
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var screenshot = Path.Combine(Path.GetTempPath(), "Tudot-Settings-Implemented.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(screenshot)) encoder.Save(stream);
        window.Close();
        var after = File.Exists(UpdatePreferences.DefaultPath) ? File.ReadAllText(UpdatePreferences.DefaultPath) : null;
        Assert(original == after, "切换后直接关闭不会保存更新偏好");
        Console.WriteLine($"{passed} settings checks passed. Screenshot: {screenshot}");
        app.Shutdown();
    }
}
