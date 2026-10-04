using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Tudot.Models;
using Tudot.Services;
using Tudot.Views;
using Microsoft.Win32;

namespace Tudot.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _dbService;
    private readonly FileService _fileService;

    private string _searchText = string.Empty;
    private string _sortBy = "Date";
    private int? _selectedCreatorId;
    private Album? _selectedAlbum;
    private string _libraryPath = string.Empty;
    private double _albumCardWidth = 260;
    private double _creatorThumbSize = 120;
    private double _imageThumbWidth = 180;
    private bool _useInternalViewer;
    private string _thumbViewMode = "Compact";
    private string _cachePath = string.Empty;

    public static readonly string DefaultLibraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Tudot");

    public ObservableCollection<Album> Albums { get; } = new();
    public ObservableCollection<Creator> Creators { get; } = new();
    public ObservableCollection<Bookmark> Bookmarks { get; } = new();
    public ObservableCollection<ImageFile> CurrentAlbumImages { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); FilterAlbums(); }
    }

    public string SortBy
    {
        get => _sortBy;
        set { _sortBy = value; OnPropertyChanged(); SortAlbums(); }
    }

    public Album? SelectedAlbum
    {
        get => _selectedAlbum;
        set { _selectedAlbum = value; OnPropertyChanged(); }
    }

    public string LibraryPath
    {
        get => _libraryPath;
        set { _libraryPath = value; OnPropertyChanged(); }
    }

    public double AlbumCardWidth
    {
        get => _albumCardWidth;
        set { _albumCardWidth = value; OnPropertyChanged(); _dbService.SetSetting("AlbumCardWidth", value.ToString("0")); }
    }

    public double CreatorThumbSize
    {
        get => _creatorThumbSize;
        set { _creatorThumbSize = value; OnPropertyChanged(); _dbService.SetSetting("CreatorThumbSize", value.ToString("0")); }
    }

    public double ImageThumbWidth
    {
        get => _imageThumbWidth;
        set { _imageThumbWidth = value; OnPropertyChanged(); _dbService.SetSetting("ImageThumbWidth", value.ToString("0")); }
    }

    public bool UseInternalViewer
    {
        get => _useInternalViewer;
        set { _useInternalViewer = value; OnPropertyChanged(); _dbService.SetSetting("UseInternalViewer", value ? "1" : "0"); }
    }

    /// <summary>缩略图视图模式：Compact（紧凑，按原图比例）/ Grid（网格，固定比例裁切）</summary>
    public string ThumbViewMode
    {
        get => _thumbViewMode;
        set { _thumbViewMode = value; OnPropertyChanged(); _dbService.SetSetting("ThumbViewMode", value); }
    }

    /// <summary>缩略图缓存路径，留空表示应用目录下 Thumbs</summary>
    public string CachePath
    {
        get => _cachePath;
        set
        {
            _cachePath = value.Trim();
            OnPropertyChanged();
            _dbService.SetSetting("CachePath", _cachePath);
            ThumbnailCache.CurrentCacheDir = string.IsNullOrWhiteSpace(_cachePath)
                ? ThumbnailCache.DefaultCacheDir
                : _cachePath;
        }
    }

    public MainViewModel()
    {
        _dbService = new DatabaseService();
        _fileService = new FileService();
        _libraryPath = _dbService.GetSetting("LibraryPath", DefaultLibraryPath);
        _albumCardWidth = GetSizeSetting("AlbumCardWidth", 260);
        _creatorThumbSize = GetSizeSetting("CreatorThumbSize", 120);
        _imageThumbWidth = GetSizeSetting("ImageThumbWidth", 180);
        _useInternalViewer = _dbService.GetSetting("UseInternalViewer") == "1";
        var mode = _dbService.GetSetting("ThumbViewMode");
        if (mode == "Grid" || mode == "Compact") _thumbViewMode = mode;
        _cachePath = _dbService.GetSetting("CachePath");
        ThumbnailCache.CurrentCacheDir = string.IsNullOrWhiteSpace(_cachePath)
            ? ThumbnailCache.DefaultCacheDir
            : _cachePath;
        LoadData();
    }

    private double GetSizeSetting(string key, double defaultValue)
        => double.TryParse(_dbService.GetSetting(key), out var v) && v > 0 ? v : defaultValue;

    public void SaveLibraryPath(string path)
    {
        _dbService.SetSetting("LibraryPath", path);
        LibraryPath = path;
    }

    public void LoadData()
    {
        LoadAlbums();
        LoadCreators();
        LoadBookmarks();
    }

    public void LoadAlbums()
    {
        Albums.Clear();
        var albums = _dbService.GetAlbums(_selectedCreatorId, SearchText, SortBy);
        foreach (var album in albums)
            Albums.Add(album);
    }

    public void LoadCreators()
    {
        Creators.Clear();
        var creators = _dbService.GetCreators();
        foreach (var creator in creators)
            Creators.Add(creator);
    }

    public void LoadBookmarks()
    {
        Bookmarks.Clear();
        var bookmarks = _dbService.GetBookmarks();
        foreach (var bookmark in bookmarks)
            Bookmarks.Add(bookmark);
    }

    public void LoadAlbumDetail(int albumId)
    {
        SelectedAlbum = _dbService.GetAlbum(albumId);
        CurrentAlbumImages.Clear();
        if (SelectedAlbum != null)
        {
            var images = _dbService.GetAlbumImages(albumId);
            foreach (var img in images)
                CurrentAlbumImages.Add(img);
        }
    }

    public void FilterByCreator(int? creatorId)
    {
        _selectedCreatorId = creatorId;
        LoadAlbums();
    }

    private void FilterAlbums() => LoadAlbums();
    private void SortAlbums() => LoadAlbums();

    public void ShowImportDialog()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要导入的相册文件夹",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true || dialog.FolderNames.Length == 0)
            return;

        var importDialog = new ImportDialog(this, dialog.FolderNames.Length)
        {
            Owner = Application.Current.MainWindow
        };
        if (importDialog.ShowDialog() != true)
            return;

        _ = ImportFoldersAsync(dialog.FolderNames, importDialog.SelectedCreator, importDialog.AddOnly);
    }

    public async Task ImportFoldersAsync(string[] folderPaths, Creator? creator, bool addOnly)
    {
        var owner = Application.Current.MainWindow;
        var progress = new ProgressWindow(folderPaths.Length) { Owner = owner };
        progress.Show();

        int successCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        await Task.Run(() =>
        {
            for (int i = 0; i < folderPaths.Length; i++)
            {
                var folderPath = folderPaths[i];
                try
                {
                    var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));

                    progress.Dispatcher.BeginInvoke(() =>
                        progress.Report(i + 1, folderPaths.Length, $"正在处理：{folderName}"));

                    string actualPath = folderPath;
                    int creatorId = creator?.Id ?? 0;

                    if (!addOnly)
                    {
                        // 整理模式：移动到库路径下
                        if (creator != null)
                        {
                            actualPath = _fileService.OrganizeAlbums(folderPath, creator.Name, LibraryPath) ?? folderPath;
                        }
                        else
                        {
                            // 无创作者归入「无创作者」目录
                            actualPath = _fileService.OrganizeAlbums(folderPath, "无创作者", LibraryPath) ?? folderPath;
                        }
                    }

                    // 检查重复
                    if (_dbService.AlbumExists(actualPath))
                    {
                        skippedCount++;
                        continue;
                    }

                    var album = new Album
                    {
                        Name = Path.GetFileName(actualPath.TrimEnd(Path.DirectorySeparatorChar)),
                        Path = actualPath,
                        CreatorId = creatorId,
                        CreatedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now
                    };

                    // 扫描文件夹内容
                    var files = _fileService.ScanFolder(actualPath);
                    album.ImageCount = files.Count(f => f.FileType == "image");

                    var firstImage = files.FirstOrDefault(f => f.FileType == "image");
                    if (firstImage != null)
                        album.CoverPath = firstImage.FilePath;

                    var albumId = _dbService.AddAlbum(album);

                    foreach (var file in files)
                    {
                        file.AlbumId = albumId;
                        _dbService.AddImageFile(file);
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Debug.WriteLine($"导入失败 {folderPath}: {ex.Message}");
                }
            }
        });

        progress.Close();

        // 刷新数据
        LoadAlbums();
        LoadCreators();

        // 结果提示（await 后已回到 UI 线程，直接调用）
        var msg = $"导入完成：成功 {successCount} 个";
        if (skippedCount > 0) msg += $"，跳过重复 {skippedCount} 个";
        if (failedCount > 0) msg += $"，失败 {failedCount} 个";

        ModernDialog.Info(owner, msg, "导入完成");
    }

    public void AddCreator(string name)
    {
        var creator = new Creator
        {
            Name = name,
            FolderPath = Path.Combine(LibraryPath, name)
        };
        _dbService.AddCreator(creator);
        LoadCreators();
    }

    public void UpdateCreator(int id, string name, string? thumbPath = null)
    {
        _dbService.UpdateCreator(id, name, thumbPath);
        LoadCreators();
        LoadAlbums(); // 创作者名可能变化
    }

    public void DeleteCreator(int id, bool deleteFiles = false)
    {
        _dbService.DeleteCreator(id, deleteFiles);
        LoadCreators();
        LoadAlbums();
    }

    public void DeleteAlbum(int id, bool deleteFiles = false)
    {
        _dbService.DeleteAlbum(id, deleteFiles);
        LoadAlbums();
        LoadCreators();
    }

    public void DeleteBookmark(int id)
    {
        _dbService.DeleteBookmark(id);
        LoadBookmarks();
    }

    public void AddBookmark(string title, string url, string? description = null)
    {
        var bookmark = new Bookmark
        {
            Title = title,
            Url = url,
            Description = description,
            CreatedDate = DateTime.Now
        };
        _dbService.AddBookmark(bookmark);
        LoadBookmarks();
    }

    public void SetAlbumCover(int albumId, string imagePath)
    {
        _dbService.UpdateAlbumCover(albumId, imagePath);
        LoadAlbums();
    }

    public void BatchRenameImages(int albumId, string pattern)
    {
        var images = _dbService.GetAlbumImages(albumId);
        var renamed = _fileService.BatchRename(images, pattern);
        _dbService.UpdateImageFiles(renamed);
        LoadAlbumDetail(albumId);
    }

    public void RenameImageFile(int imageId, string newName)
    {
        var image = CurrentAlbumImages.FirstOrDefault(i => i.Id == imageId);
        if (image == null) return;

        var dir = Path.GetDirectoryName(image.FilePath) ?? string.Empty;
        var ext = Path.GetExtension(image.FileName);
        if (!newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            newName += ext;

        var newPath = Path.Combine(dir, newName);
        if (newPath == image.FilePath) return;

        // 冲突处理
        int conflict = 1;
        while (File.Exists(newPath) && newPath != image.FilePath)
        {
            newName = $"{Path.GetFileNameWithoutExtension(newName)}_{conflict}{ext}";
            newPath = Path.Combine(dir, newName);
            conflict++;
        }

        if (newPath != image.FilePath)
        {
            File.Move(image.FilePath, newPath);
            image.FileName = newName;
            image.FilePath = newPath;
            _dbService.UpdateImageFiles([image]);
        }

        if (SelectedAlbum?.CoverPath == image.FilePath)
        {
            SelectedAlbum.CoverPath = newPath;
            _dbService.UpdateAlbumCover(SelectedAlbum.Id, newPath);
        }
    }

    public void DeleteImageFile(int imageId, bool deleteSource = false)
    {
        var image = CurrentAlbumImages.FirstOrDefault(i => i.Id == imageId);
        if (image == null) return;

        if (deleteSource && File.Exists(image.FilePath))
        {
            try { File.Delete(image.FilePath); } catch { }
        }

        _dbService.DeleteImageFile(imageId);
        if (SelectedAlbum != null)
            _dbService.RefreshAlbumImageCount(SelectedAlbum.Id);
        LoadAlbumDetail(SelectedAlbum?.Id ?? 0);
    }

    public void AddImagesToAlbum(int albumId, string[] filePaths)
    {
        var album = _dbService.GetAlbum(albumId);
        if (album == null) return;

        foreach (var srcPath in filePaths)
        {
            try
            {
                var targetPath = Path.Combine(album.Path, Path.GetFileName(srcPath));
                // 冲突处理
                int conflict = 1;
                while (File.Exists(targetPath))
                {
                    targetPath = Path.Combine(album.Path,
                        $"{Path.GetFileNameWithoutExtension(srcPath)}_{conflict}{Path.GetExtension(srcPath)}");
                    conflict++;
                }

                File.Copy(srcPath, targetPath);

                var fileInfo = new FileInfo(targetPath);
                var ext = fileInfo.Extension.ToLowerInvariant();
                var fileType = ext switch
                {
                    ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" => "image",
                    ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv" or ".webm" => "video",
                    _ => "other"
                };

                _dbService.AddImageFile(new ImageFile
                {
                    AlbumId = albumId,
                    FileName = Path.GetFileName(targetPath),
                    FilePath = targetPath,
                    FileType = fileType,
                    FileSize = fileInfo.Length,
                    SortOrder = 0
                });
            }
            catch { }
        }

        _dbService.RefreshAlbumImageCount(albumId);
        LoadAlbumDetail(albumId);
        LoadAlbums();
    }

    public void UpdateAlbum(int albumId, string name, int creatorId, string coverPath, string path,
                            bool moveFiles = false, string? oldPath = null)
    {
        _dbService.UpdateAlbumInfo(albumId, name, creatorId, coverPath, path);
        if (moveFiles && oldPath != null && oldPath != path && Directory.Exists(oldPath))
        {
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Directory.Move(oldPath, path);
                _dbService.UpdateAlbumImagePaths(albumId, oldPath, path);
            }
            catch { }
        }
        LoadAlbums();
        LoadCreators();
        LoadAlbumDetail(albumId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
