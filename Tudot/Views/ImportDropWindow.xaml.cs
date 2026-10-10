using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Tudot.Views;

/// <summary>
/// 导入入口窗口：支持从资源管理器拖入文件夹，或点击区域走系统文件夹选择对话框。
/// 关闭后通过 SelectedFolders 取结果：null = 未选择或点击取消；空数组 = 点击区域（走系统对话框）；非空 = 拖入的文件夹。
/// </summary>
public partial class ImportDropWindow : Window
{
    /// <summary>拖入的文件夹列表；null 表示取消；空数组表示用户选择点击流程</summary>
    public string[]? SelectedFolders { get; private set; }

    public ImportDropWindow()
    {
        InitializeComponent();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static bool TryGetDropFolders(DragEventArgs e, out string[] folders)
    {
        folders = Array.Empty<string>();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return false;
        folders = paths.Where(Directory.Exists).ToArray();
        return folders.Length > 0;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (TryGetDropFolders(e, out _))
        {
            e.Effects = DragDropEffects.Copy;
            DropRect.Stroke = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF));
            DropRect.StrokeThickness = 2;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropRect.Stroke = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xC9));
        DropRect.StrokeThickness = 1.5;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        DropZone_DragLeave(sender, e);
        if (!TryGetDropFolders(e, out var folders))
            return;

        SelectedFolders = folders;
        DialogResult = true;
        Close();
    }

    private void DropZone_Click(object sender, MouseButtonEventArgs e)
    {
        // 空数组约定为「点击选择」：由调用方继续打开系统文件夹对话框
        SelectedFolders = Array.Empty<string>();
        DialogResult = true;
        Close();
    }
}
