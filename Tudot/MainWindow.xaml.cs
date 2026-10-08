using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Tudot.Models;
using Tudot.ViewModels;
using Tudot.Views;

namespace Tudot;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel;
    private HomePage _homePage;
    private FavoritesPage _favoritesPage;
    private CreatorsPage _creatorsPage;
    private BookmarksPage _bookmarksPage;
    private AlbumDetailPage _albumDetailPage;

    // 拖拽相关
    private Point _dragStartPoint;
    private TreeNode? _dragSource;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainViewModel)DataContext;

        _homePage = new HomePage(_viewModel);
        _favoritesPage = new FavoritesPage(_viewModel);
        _creatorsPage = new CreatorsPage(_viewModel);
        _bookmarksPage = new BookmarksPage(_viewModel);
        _albumDetailPage = new AlbumDetailPage(_viewModel);

        MainContent.Content = _homePage;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryApplyRoundedCorners();
    }

    // Windows 11 下为无边框窗口启用圆角；失败则保持直角
    private void TryApplyRoundedCorners()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private void MinButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (MaxButtonGlyph != null)
            MaxButtonGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string page)
        {
            ShowPage(page);
        }
    }

    public void ShowPage(string page)
    {
        switch (page)
        {
            case "Home":
                MainContent.Content = _homePage;
                PageTitle.Text = "全部相册";
                NavHome.IsChecked = true;
                break;
            case "Favorites":
                _viewModel.LoadFavorites();
                MainContent.Content = _favoritesPage;
                PageTitle.Text = "收藏相册";
                NavFavorites.IsChecked = true;
                break;
            case "Creators":
                MainContent.Content = _creatorsPage;
                PageTitle.Text = "创作者管理";
                NavCreators.IsChecked = true;
                break;
            case "Bookmarks":
                MainContent.Content = _bookmarksPage;
                PageTitle.Text = "网址收藏";
                NavBookmarks.IsChecked = true;
                break;
        }
    }

    public void ShowAlbumDetail(int albumId)
    {
        _viewModel.LoadAlbumDetail(albumId);
        MainContent.Content = _albumDetailPage;
        PageTitle.Text = "相册详情";
    }

    private void SearchBar_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.SearchText = ((TextBox)sender).Text;
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string sort)
        {
            _viewModel.SortBy = sort;
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowImportDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new Views.SettingsWindow(_viewModel) { Owner = this };
        settingsWindow.ShowDialog();
    }

    // ===== 侧边栏目录树 =====

    private void AddCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ModernDialog.Input(this, "输入新分类名称：", "新建分类", "");
        if (!string.IsNullOrWhiteSpace(name))
            _viewModel.AddCategory(name);
    }

    /// <summary>树节点点击：分类→显示分类下创作者；创作者→显示其相册；相册→打开详情</summary>
    private void TreeItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item) return;

        // 隧道事件沿可视化树从外层节点向内传递，只有事件源真正属于当前节点时才处理，
        // 否则点击子级节点会被最外层分类节点截获
        var clickedItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (clickedItem != item) return;

        if (item.DataContext is not TreeNode node) return;

        // 点击的是展开/收起箭头（ToggleButton 或其内部图形元素）时，只展开，不触发跳转
        if (IsInsideToggleButton(e.OriginalSource as DependencyObject))
            return;

        _dragStartPoint = e.GetPosition(null);
        _dragSource = node;

        item.IsSelected = true;
        HandleNodeClick(node);
        e.Handled = true;
    }

    /// <summary>沿可视化树向上查找最近的指定类型祖先</summary>
    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T t) return t;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>沿可视化树向上检查点击源是否位于 ToggleButton（展开箭头）内</summary>
    private static bool IsInsideToggleButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ToggleButton) return true;
            if (source is TreeViewItem) return false;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void HandleNodeClick(TreeNode node)
    {
        switch (node.Type)
        {
            case TreeNodeType.Category:
                // 显示该分类下的所有创作者
                _viewModel.ShowCategoryCreators(node.Id);
                ShowPage("Home");
                break;
            case TreeNodeType.Creator:
                // 显示该创作者的所有相册
                _viewModel.ShowCreatorAlbums(node.Id);
                ShowPage("Home");
                break;
            case TreeNodeType.Album:
                // 打开相册详情
                ShowAlbumDetail(node.Id);
                break;
        }
    }

    /// <summary>树节点鼠标移动：达到拖拽阈值时启动拖拽</summary>
    private void TreeItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;

        var current = e.GetPosition(null);
        var diff = _dragStartPoint - current;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is TreeViewItem item)
            {
                var data = new DataObject("TreeNode", _dragSource);
                DragDrop.DoDragDrop(item, data, DragDropEffects.Move);
                _dragSource = null;
            }
        }
    }

    private void TreeItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TreeNode")) { e.Effects = DragDropEffects.None; return; }
        if (sender is TreeViewItem item && item.DataContext is TreeNode target)
        {
            e.Effects = CanDrop(_dragSource, target) ? DragDropEffects.Move : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DirTree_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TreeNode")) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TreeItem_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TreeNode")) return;
        var source = e.Data.GetData("TreeNode") as TreeNode;
        if (sender is TreeViewItem item && item.DataContext is TreeNode target)
        {
            PerformDrop(source, target);
        }
        e.Handled = true;
    }

    private void DirTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TreeNode")) return;
        var source = e.Data.GetData("TreeNode") as TreeNode;
        // 拖到空白处：移到无分类
        if (source?.Type == TreeNodeType.Creator)
        {
            _viewModel.MoveCreatorToCategory(source.Id, 0);
        }
        e.Handled = true;
    }

    private bool CanDrop(TreeNode? source, TreeNode target)
    {
        if (source == null || source == target) return false;
        // 相册可拖到创作者
        if (source.Type == TreeNodeType.Album && target.Type == TreeNodeType.Creator) return true;
        // 创作者可拖到分类（含无分类）
        if (source.Type == TreeNodeType.Creator && target.Type == TreeNodeType.Category) return true;
        return false;
    }

    private void PerformDrop(TreeNode? source, TreeNode target)
    {
        if (source == null) return;
        if (source.Type == TreeNodeType.Album && target.Type == TreeNodeType.Creator)
        {
            _viewModel.MoveAlbumToCreator(source.Id, target.Id);
        }
        else if (source.Type == TreeNodeType.Creator && target.Type == TreeNodeType.Category)
        {
            _viewModel.MoveCreatorToCategory(source.Id, target.Id);
        }
    }

    /// <summary>树空白处点击：重置为显示全部相册</summary>
    private void DirTree_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            _viewModel.ShowAllAlbums();
            ShowPage("Home");
        }
    }

    /// <summary>树节点右键菜单：分类→重命名/删除；创作者→编辑/删除；相册→打开/删除</summary>
    private void TreeItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item) return;

        // 同为隧道事件，需确认事件源属于当前节点，避免外层节点截获
        var clickedItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (clickedItem != item) return;

        if (item.DataContext is not TreeNode node) return;
        item.IsSelected = true;
        e.Handled = true;

        var menu = new ContextMenu();

        switch (node.Type)
        {
            case TreeNodeType.Category:
                if (node.Id > 0) // 无分类不允许重命名/删除
                {
                    var renameCat = new MenuItem { Header = "重命名分类" };
                    renameCat.Click += (_, _) =>
                    {
                        var name = ModernDialog.Input(this, "输入新分类名称：", "重命名分类", node.Name);
                        if (!string.IsNullOrWhiteSpace(name)) _viewModel.RenameCategory(node.Id, name);
                    };
                    menu.Items.Add(renameCat);

                    var delCat = new MenuItem { Header = "删除分类" };
                    delCat.Click += (_, _) =>
                    {
                        if (ModernDialog.Confirm(this,
                            $"删除分类「{node.Name}」？该分类下的创作者将变为无分类。", "确认删除"))
                            _viewModel.DeleteCategory(node.Id);
                    };
                    menu.Items.Add(delCat);
                }
                break;

            case TreeNodeType.Creator:
                var editCreator = new MenuItem { Header = "编辑创作者" };
                editCreator.Click += (_, _) =>
                {
                    var creator = _viewModel.Creators.FirstOrDefault(c => c.Id == node.Id);
                    if (creator != null)
                    {
                        var dialog = new CreatorEditDialog(creator, _viewModel.Categories.ToList())
                        { Owner = this };
                        if (dialog.ShowDialog() == true)
                            _viewModel.UpdateCreator(creator.Id, dialog.NewName, dialog.NewThumbPath, dialog.NewCategoryId);
                    }
                };
                menu.Items.Add(editCreator);

                var delCreator = new MenuItem { Header = "删除创作者" };
                delCreator.Click += (_, _) =>
                {
                    var creator = _viewModel.Creators.FirstOrDefault(c => c.Id == node.Id);
                    if (creator != null)
                    {
                        var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(this,
                            $"删除创作者「{creator.Name}」？", "确认删除", showFileOption: true);
                        if (confirmed)
                        {
                            _viewModel.DeleteCreator(creator.Id, deleteFiles);
                        }
                    }
                };
                menu.Items.Add(delCreator);
                break;

            case TreeNodeType.Album:
                var openAlbum = new MenuItem { Header = "打开相册" };
                openAlbum.Click += (_, _) => ShowAlbumDetail(node.Id);
                menu.Items.Add(openAlbum);

                var delAlbum = new MenuItem { Header = "删除相册" };
                delAlbum.Click += (_, _) =>
                {
                    var (confirmed, deleteFiles) = DeleteConfirmDialog.Show(this,
                        "删除此相册？", "确认删除", showFileOption: true);
                    if (confirmed)
                    {
                        _viewModel.DeleteAlbum(node.Id, deleteFiles);
                        if (MainContent.Content is AlbumDetailPage)
                            ShowPage("Home");
                    }
                };
                menu.Items.Add(delAlbum);
                break;
        }

        if (menu.Items.Count > 0)
        {
            menu.PlacementTarget = item;
            menu.IsOpen = true;
        }
    }
}
