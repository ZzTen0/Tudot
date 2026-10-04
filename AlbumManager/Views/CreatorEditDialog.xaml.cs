using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AlbumManager.Models;
using Microsoft.Win32;

namespace AlbumManager.Views;

public partial class CreatorEditDialog : Window
{
    public string NewName { get; private set; } = string.Empty;
    public string NewThumbPath { get; private set; } = string.Empty;

    public CreatorEditDialog(Creator creator)
    {
        InitializeComponent();
        NameTextBox.Text = creator.Name;
        FolderPathTextBox.Text = creator.FolderPath;
        NewThumbPath = creator.ThumbPath;
        UpdateThumbPreview();
        Loaded += (_, _) => { NameTextBox.Focus(); NameTextBox.SelectAll(); };
    }

    private void PickThumbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择缩略图",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp"
        };
        if (dialog.ShowDialog() == true)
        {
            NewThumbPath = dialog.FileName;
            UpdateThumbPreview();
        }
    }

    private void ClearThumbButton_Click(object sender, RoutedEventArgs e)
    {
        NewThumbPath = string.Empty;
        UpdateThumbPreview();
    }

    private void UpdateThumbPreview()
    {
        if (!string.IsNullOrEmpty(NewThumbPath) && System.IO.File.Exists(NewThumbPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(NewThumbPath);
                bitmap.DecodePixelWidth = 120;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                ThumbPreview.Source = bitmap;
                return;
            }
            catch { }
        }
        ThumbPreview.Source = null;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ModernDialog.Info(this, "名称不能为空", "提示");
            return;
        }
        NewName = name;
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
        else if (e.Key == Key.Enter)
        {
            SaveButton_Click(sender, e);
        }
    }
}
