using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tudot.Models;
using Tudot.ViewModels;

namespace Tudot.Views;

/// <summary>导入项：源文件夹 + 可编辑的相册名</summary>
public class ImportItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string SourceName => Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar));
    public string AlbumName { get; set; } = string.Empty;
}

public partial class ImportDialog : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>导入项列表（相册名已被用户编辑）</summary>
    public List<ImportItem> Items { get; }

    /// <summary>用户选择/新建的创作者；null 表示归入「无创作者」目录</summary>
    public Creator? SelectedCreator { get; private set; }

    /// <summary>仅添加模式：不移动文件，仅登记到图库</summary>
    public bool AddOnly { get; private set; }

    public ImportDialog(MainViewModel viewModel, string[] folderPaths)
    {
        InitializeComponent();
        _viewModel = viewModel;
        InfoText.Text = $"已选择 {folderPaths.Length} 个文件夹，库存储路径：{viewModel.LibraryPath}";
        CreatorList.ItemsSource = viewModel.Creators;

        // 填充新创作者分类下拉框
        NewCreatorCategoryCombo.Items.Add(new ComboBoxItem { Content = "无分类", Tag = 0 });
        foreach (var cat in viewModel.Categories.OrderBy(c => c.Name))
            NewCreatorCategoryCombo.Items.Add(new ComboBoxItem { Content = cat.Name, Tag = cat.Id });
        NewCreatorCategoryCombo.SelectedIndex = 0;

        Items = folderPaths.Select(p => new ImportItem
        {
            SourcePath = p,
            AlbumName = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar))
        }).ToList();
        FolderList.ItemsSource = Items;
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (AddOnlyCheck == null || CreatorList == null) return;
        var addOnly = AddOnlyCheck.IsChecked == true;
        // 仅添加模式下创作者仍可选：只作为数据库索引标签，不移动文件
        CreatorHeader.Text = addOnly
            ? "归类到创作者（可选，仅建立索引标签，不移动文件）"
            : "整理到创作者（不选择则归入「无创作者」目录）";
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        // 校验相册名：非空、无非法字符、不重复
        var invalid = Path.GetInvalidFileNameChars();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            item.AlbumName = item.AlbumName.Trim();
            if (string.IsNullOrEmpty(item.AlbumName))
            {
                ModernDialog.Info(this, $"「{item.SourceName}」的相册名不能为空", "提示");
                return;
            }
            if (item.AlbumName.Any(c => invalid.Contains(c)))
            {
                ModernDialog.Info(this, $"相册名「{item.AlbumName}」包含非法字符", "提示");
                return;
            }
            if (!names.Add(item.AlbumName))
            {
                ModernDialog.Info(this, $"相册名「{item.AlbumName}」重复，请修改", "提示");
                return;
            }
        }

        // 新建创作者（任何时候都可以）：同名已存在则直接使用
        var newName = NewCreatorTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            var existing = _viewModel.Creators.FirstOrDefault(
                c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SelectedCreator = existing;
            }
            else
            {
                var catId = NewCreatorCategoryCombo.SelectedItem is ComboBoxItem ci && ci.Tag is int id ? id : 0;
                _viewModel.AddCreator(newName, catId);
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
