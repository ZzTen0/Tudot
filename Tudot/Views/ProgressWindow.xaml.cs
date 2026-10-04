using System.Windows;

namespace Tudot.Views;

/// <summary>
/// 导入进度窗口，需在 UI 线程调用 Report。
/// </summary>
public partial class ProgressWindow : Window
{
    public ProgressWindow(int total)
    {
        InitializeComponent();
        Bar.Maximum = total;
        CountText.Text = $"0 / {total}";
    }

    public bool IsCancelRequested { get; private set; }

    private void CancelImportButton_Click(object sender, RoutedEventArgs e)
    {
        IsCancelRequested = true;
        CancelImportButton.IsEnabled = false;
        StatusText.Text = "正在取消，完成当前项后停止...";
    }

    public void Report(int current, int total, string message)
    {
        Bar.Value = current;
        CountText.Text = $"{current} / {total}";
        StatusText.Text = message;
    }
}
