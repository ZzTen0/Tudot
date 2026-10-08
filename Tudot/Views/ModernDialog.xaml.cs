using System.Windows;
using System.Windows.Input;

namespace Tudot.Views;

/// <summary>
/// 现代风格的通用对话框，替代系统 MessageBox / InputBox。
/// </summary>
public partial class ModernDialog : Window
{
    private ModernDialog(Window? owner, string title, string message,
                         bool showInput, string defaultInput, bool showCancel)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;

        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

        if (showInput)
        {
            InputBox.Visibility = Visibility.Visible;
            InputBox.Text = defaultInput;
            InputBox.SelectAll();
            Loaded += (_, _) => InputBox.Focus();
        }

        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>信息提示（仅确定按钮）</summary>
    public static void Info(Window? owner, string message, string title = "提示")
    {
        var dialog = new ModernDialog(owner, title, message, false, "", false);
        dialog.ShowDialog();
    }

    /// <summary>确认对话框，返回用户是否点击确定</summary>
    public static bool Confirm(Window? owner, string message, string title = "确认")
    {
        var dialog = new ModernDialog(owner, title, message, false, "", true);
        return dialog.ShowDialog() == true;
    }

    /// <summary>三态确认结果</summary>
    public enum Confirm3Result { Cancel, Ok, Alt }

    private Confirm3Result _result3 = Confirm3Result.Cancel;

    /// <summary>三态确认对话框：确定(OK) / 备选(Alt) / 取消。默认焦点在 Alt（安全默认项）。</summary>
    public static Confirm3Result Confirm3(Window? owner, string message, string title, string okText, string altText)
    {
        var dialog = new ModernDialog(owner, title, message, false, "", true);
        dialog.OkButton.Content = okText;
        dialog.AltButton.Content = altText;
        dialog.AltButton.Visibility = Visibility.Visible;
        dialog.Loaded += (_, _) => dialog.AltButton.Focus();
        dialog.ShowDialog();
        return dialog._result3;
    }

    /// <summary>输入对话框，返回输入文本；取消返回 null</summary>
    public static string? Input(Window? owner, string message, string title = "输入", string defaultValue = "")
    {
        var dialog = new ModernDialog(owner, title, message, true, defaultValue, true);
        return dialog.ShowDialog() == true ? dialog.InputBox.Text.Trim() : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _result3 = Confirm3Result.Ok;
        DialogResult = true;
        Close();
    }

    private void AltButton_Click(object sender, RoutedEventArgs e)
    {
        _result3 = Confirm3Result.Alt;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
        else if (e.Key == Key.Enter && InputBox.Visibility == Visibility.Visible)
        {
            DialogResult = true;
            Close();
        }
    }
}
