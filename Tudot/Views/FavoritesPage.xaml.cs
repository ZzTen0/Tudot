using System.Windows;
using System.Windows.Controls;
using Tudot.Models;
using Tudot.ViewModels;

namespace Tudot.Views;

public partial class FavoritesPage : UserControl
{
    private readonly MainViewModel _viewModel;

    public FavoritesPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Card_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is int albumId)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.ShowAlbumDetail(albumId);
        }
    }

    private void UnfavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Album album })
            _viewModel.ToggleFavorite(album);
    }
}
