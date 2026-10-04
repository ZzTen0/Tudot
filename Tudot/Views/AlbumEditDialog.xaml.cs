using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Tudot.Models;
using Tudot.ViewModels;
using Microsoft.Win32;

namespace Tudot.Views;

public partial class AlbumEditDialog : Window
{
    public string NewName { get; private set; } = string.Empty;
    public string NewCoverPath { get; private set; } = string.Empty;
    public int NewCreatorId { get; private set; }
    public string NewPath { get; private set; } = string.Empty;
    public bool MoveFiles { get; private set; }
    public string OldPath { get; private set; } = string.Empty;

    private readonly MainViewModel _viewModel;
    private readonly Album _album;

    public AlbumEditDialog(MainViewModel viewModel, Album album)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _album = album;

        NameTextBox.Text = album.Name;
        NewCoverPath = album.CoverPath;
        OldPath = album.Path;
        NewPath = album.Path;
        NewCreatorId = album.CreatorId;

        // 填充创作者列表
        CreatorComboBox.ItemsSource = viewModel.Creators;
        CreatorComboBox.SelectedValue = album.CreatorId;
        if (CreatorComboBox.SelectedValue == null)
            CreatorComboBox.SelectedIndex = 0;

        UpdateCoverPreview();
    }

    private void UpdateCoverPreview()
    {
        if (!string.IsNullOrEmpty(NewCoverPath) && File.Exists(NewCoverPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(NewCoverPath);
                bitmap.DecodePixelWidth = 160;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                CoverPreview.Source = bitmap;
                return;
            }
            catch { }
        }
        CoverPreview.Source = null;
    }

    private void PickCoverButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择封面图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
            InitialDirectory = _album.Path
        };
        if (dialog.ShowDialog() == true)
        {
            NewCoverPath = dialog.FileName;
            UpdateCoverPreview();
        }
    }

    private void ClearCoverButton_Click(object sender, RoutedEventArgs e)
    {
        var first = _viewModel.CurrentAlbumImages.FirstOrDefault(f => f.FileType == "image");
        NewCoverPath = first?.FilePath ?? string.Empty;
        UpdateCoverPreview();
    }

    private void AddImagesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要添加的图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|所有文件|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.AddImagesToAlbum(_album.Id, dialog.FileNames);
            ModernDialog.Info(this, $"已添加 {dialog.FileNames.Length} 个文件", "添加图片");
        }
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
        NewCreatorId = (int)(CreatorComboBox.SelectedValue ?? 0);
        MoveFiles = MoveFilesCheckBox.IsChecked == true;

        // 计算新路径（如创作者变更且用户同意移动）
        if (MoveFiles && NewCreatorId != _album.CreatorId)
        {
            var creator = _viewModel.Creators.FirstOrDefault(c => c.Id == NewCreatorId);
            var creatorName = creator?.Name ?? "无创作者";
            var folderName = Path.GetFileName(_album.Path.TrimEnd(Path.DirectorySeparatorChar));
            var newBase = Path.Combine(_viewModel.LibraryPath, creatorName);
            NewPath = Path.Combine(newBase, folderName);
        }

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