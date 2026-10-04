using System.Windows;
using System.Windows.Input;
using Tudot.Models;
using Tudot.ViewModels;

namespace Tudot.Views;

public partial class ImportDialog : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>用户选择的创作者；null 表示归入「无创作者」目录</summary>
    public Creator? SelectedCreator { get; private set; }

    /// <summary>仅添加模式：不移动文件，仅登记到图库</summary>
    public bool AddOnly { get; private set; }

    public ImportDialog(MainViewModel viewModel, int folderCount)
    {
        InitializeComponent();
        _viewModel = viewModel;
        InfoText.Text = $"已选择 {folderCount} 个文件夹，库存储路径：{viewModel.LibraryPath}";
        CreatorList.ItemsSource = viewModel.Creators;
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (AddOnlyCheck == null || CreatorList == null) return;
        var addOnly = AddOnlyCheck.IsChecked == true;
        CreatorList.IsEnabled = !addOnly;
        CreatorHeader.Text = addOnly
            ? "归类到创作者（可选，仅作为标签，不移动文件）"
            : "整理到创作者（不选择则归入「无创作者」目录）";
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var newName = NewCreatorTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            // 同名创作者已存在则直接使用，否则创建
            var existing = _viewModel.Creators.FirstOrDefault(
                c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SelectedCreator = existing;
            }
            else
            {
                _viewModel.AddCreator(newName);
                SelectedCreator = _viewModel.Creators.FirstOrDefault(
                    c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            SelectedCreator = CreatorList.SelectedItem as Creator;
        }
        AddOnly = AddOnlyCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
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
