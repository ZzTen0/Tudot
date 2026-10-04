using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Tudot.ViewModels;
using Tudot.Views;

namespace Tudot;

public partial class MainWindow : Window
{
    private MainViewModel _viewModel;
    private HomePage _homePage;
    private CreatorsPage _creatorsPage;
    private BookmarksPage _bookmarksPage;
    private AlbumDetailPage _albumDetailPage;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainViewModel)DataContext;

        _homePage = new HomePage(_viewModel);
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
                CreatorFilterPanel.Visibility = Visibility.Visible;
                NavHome.IsChecked = true;
                break;
            case "Creators":
                MainContent.Content = _creatorsPage;
                PageTitle.Text = "创作者管理";
                CreatorFilterPanel.Visibility = Visibility.Collapsed;
                NavCreators.IsChecked = true;
                break;
            case "Bookmarks":
                MainContent.Content = _bookmarksPage;
                PageTitle.Text = "网址收藏";
                CreatorFilterPanel.Visibility = Visibility.Collapsed;
                NavBookmarks.IsChecked = true;
                break;
        }
    }

    public void ShowAlbumDetail(int albumId)
    {
        _viewModel.LoadAlbumDetail(albumId);
        MainContent.Content = _albumDetailPage;
        PageTitle.Text = "相册详情";
        CreatorFilterPanel.Visibility = Visibility.Collapsed;
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

    private void CreatorTag_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int creatorId)
        {
            _viewModel.FilterByCreator(creatorId);
        }
    }
}
