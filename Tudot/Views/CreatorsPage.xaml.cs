using System.Windows;
using System.Windows.Controls;
using Tudot.Models;
using Tudot.ViewModels;

namespace Tudot.Views;

public partial class CreatorsPage : UserControl
{
    private MainViewModel _viewModel;

    public CreatorsPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void AddCreatorButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ModernDialog.Input(
            Window.GetWindow(this), "输入创作者名称:", "添加创作者");
        if (!string.IsNullOrEmpty(name))
        {
            _viewModel.AddCreator(name);
        }
    }

    private void ViewAlbumsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int creatorId)
        {
            _viewModel.FilterByCreator(creatorId);
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowPage("Home");
        }
    }

    private void EditCreatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Creator creator)
        {
            var dialog = new CreatorEditDialog(creator) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true &&
                (dialog.NewName != creator.Name || dialog.NewThumbPath != creator.ThumbPath))
            {
                _viewModel.UpdateCreator(creator.Id, dialog.NewName, dialog.NewThumbPath);
            }
        }
    }

    private void DeleteCreatorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Creator creator)
        {
            var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(
                Window.GetWindow(this),
                $"确定要删除创作者「{creator.Name}」吗？\n默认仅移除创作者标签（其相册将归入未分类），勾选后将同时删除该创作者名下的所有相册源文件。",
                "删除创作者");

            if (confirmed)
                _viewModel.DeleteCreator(creator.Id, deleteFiles);
        }
    }
}
