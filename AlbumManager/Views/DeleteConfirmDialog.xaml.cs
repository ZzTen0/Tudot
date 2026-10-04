using System.Windows;
using System.Windows.Input;

namespace AlbumManager.Views;

public partial class DeleteConfirmDialog : Window
{
    public bool DeleteFiles { get; private set; }

    private DeleteConfirmDialog(string message, string title, bool showFileOption)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        DeleteFilesCheckBox.Visibility = showFileOption ? Visibility.Visible : Visibility.Collapsed;
    }

    public static (bool Confirmed, bool DeleteFiles) Show(
        Window? owner, string message, string title = "确认删除", bool showFileOption = true)
    {
        var dialog = new DeleteConfirmDialog(message, title, showFileOption);
        if (owner != null)
            dialog.Owner = owner;
        var result = dialog.ShowDialog() == true;
        return (result, result && dialog.DeleteFiles);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteFiles = DeleteFilesCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
        else if (e.Key == Key.Enter)
            DeleteButton_Click(sender, e);
    }
}
