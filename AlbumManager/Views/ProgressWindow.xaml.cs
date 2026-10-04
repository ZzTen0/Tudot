using System.Windows;

namespace AlbumManager.Views;

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

    public void Report(int current, int total, string message)
    {
        Bar.Value = current;
        CountText.Text = $"{current} / {total}";
        StatusText.Text = message;
    }
}
