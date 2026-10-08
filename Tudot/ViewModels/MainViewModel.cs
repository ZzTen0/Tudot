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
    public ObservableCollection<Album> FavoriteAlbums { get; } = new();
    public ObservableCollection<Creator> Creators { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<Bookmark> Bookmarks { get; } = new();
    public ObservableCollection<ImageFile> CurrentAlbumImages { get; } = new();
    public ObservableCollection<TreeNode> TreeNodes { get; } = new();

    // ===== 资源管理器式导航状态 =====
    private string _homeViewMode = "Albums"; // "Albums" | "Creators"
    public string HomeViewMode
    {
        get => _homeViewMode;
        set { _homeViewMode = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Creator> FilteredCreators { get; } = new();

    private string _homeViewTitle = "全部相册";
    public string HomeViewTitle
    {
        get => _homeViewTitle;
        set { _homeViewTitle = value; OnPropertyChanged(); }
    }

    private int? _explorerCategoryId;
    private int? _explorerCreatorId;

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
        _homePaged = _dbService.GetSetting("HomePaged", "1") != "0";
        _pageSize = (int)GetSizeSetting("PageSize", 24);
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
        LoadCategories();
        LoadAlbums();
        LoadCreators();
        LoadBookmarks();
        LoadFavorites();
        BuildTree();
    }

    public void LoadCategories()
    {
        Categories.Clear();
        foreach (var c in _dbService.GetCategories())
            Categories.Add(c);
    }

    public void AddCategory(string name)
    {
        var cat = new Category { Name = name.Trim() };
        if (string.IsNullOrEmpty(cat.Name)) return;
        _dbService.AddCategory(cat);
        LoadCategories();
        BuildTree();
    }

    public void RenameCategory(int id, string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return;
        _dbService.UpdateCategory(id, name);
        LoadCategories();
        BuildTree();
    }

    public void DeleteCategory(int id)
    {
        _dbService.DeleteCategory(id);
        LoadCategories();
        LoadCreators();
        BuildTree();
    }

    /// <summary>构建侧边栏目录树：分类 → 创作者 → 相册</summary>
    public void BuildTree()
    {
        // 保存现有展开状态
        var expandedStates = new Dictionary<(TreeNodeType, int), bool>();
        foreach (var node in TreeNodes)
            SaveExpandedState(node, expandedStates);

        TreeNodes.Clear();
        var noCategoryCreators = Creators.Where(c => c.CategoryId == 0).ToList();
        if (noCategoryCreators.Count > 0)
        {
            var noCat = new TreeNode { Type = TreeNodeType.Category, Id = 0, Name = "无分类" };
            foreach (var c in noCategoryCreators)
                noCat.Children.Add(BuildCreatorNode(c));
            TreeNodes.Add(noCat);
        }

        foreach (var cat in Categories)
        {
            var catNode = new TreeNode { Type = TreeNodeType.Category, Id = cat.Id, Name = cat.Name };
            foreach (var c in Creators.Where(x => x.CategoryId == cat.Id))
                catNode.Children.Add(BuildCreatorNode(c));
            TreeNodes.Add(catNode);
        }

        // 恢复展开状态
        foreach (var node in TreeNodes)
            RestoreExpandedState(node, expandedStates);
    }

    private static void SaveExpandedState(TreeNode node, Dictionary<(TreeNodeType, int), bool> states)
    {
        states[(node.Type, node.Id)] = node.IsExpanded;
        foreach (var child in node.Children)
            SaveExpandedState(child, states);
    }

    private static void RestoreExpandedState(TreeNode node, Dictionary<(TreeNodeType, int), bool> states)
    {
        if (states.TryGetValue((node.Type, node.Id), out var expanded))
            node.IsExpanded = expanded;
        foreach (var child in node.Children)
            RestoreExpandedState(child, states);
    }

    private TreeNode BuildCreatorNode(Creator c)
    {
        var node = new TreeNode
        {
            Type = TreeNodeType.Creator,
            Id = c.Id,
            Name = c.Name,
            Extra = c.AlbumCount.ToString()
        };
        foreach (var a in Albums.Where(x => x.CreatorId == c.Id))
        {
            node.Children.Add(new TreeNode
            {
                Type = TreeNodeType.Album,
                Id = a.Id,
                Name = a.Name,
                Extra = a.CoverPath
            });
        }
        return node;
    }

    public void LoadFavorites()
    {
        FavoriteAlbums.Clear();
        foreach (var album in _dbService.GetAlbums().Where(a => a.IsFavorite))
            FavoriteAlbums.Add(album);
    }

    public void ToggleFavorite(Album album)
    {
        _dbService.SetFavorite(album.Id, !album.IsFavorite);
        LoadAlbums();
        LoadFavorites();
    }

    // ===== 相册多选模式 =====

    private bool _multiSelectAlbumsMode;
    public bool MultiSelectAlbumsMode
    {
        get => _multiSelectAlbumsMode;
        set
        {
            _multiSelectAlbumsMode = value;
            if (!value)
            {
                foreach (var a in Albums) a.IsSelected = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAlbumCount));
        }
    }

    public int SelectedAlbumCount => PagedAlbums.Count(a => a.IsSelected);

    public void ToggleAlbumSelection(Album album)
    {
        album.IsSelected = !album.IsSelected;
        OnPropertyChanged(nameof(SelectedAlbumCount));
    }

    public void SelectAllAlbums(bool select)
    {
        foreach (var a in PagedAlbums) a.IsSelected = select;
        OnPropertyChanged(nameof(SelectedAlbumCount));
    }

    /// <summary>批量删除所选相册</summary>
    public void DeleteSelectedAlbums(bool deleteFiles)
    {
        var selected = PagedAlbums.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var album in selected)
            _dbService.DeleteAlbum(album.Id, deleteFiles);

        MultiSelectAlbumsMode = false;
        LoadAlbums();
        LoadFavorites();
        LoadCreators();
    }

    /// <summary>批量收藏/取消收藏所选相册</summary>
    public void SetFavoriteSelectedAlbums(bool favorite)
    {
        var selected = PagedAlbums.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var album in selected)
            _dbService.SetFavorite(album.Id, favorite);

        LoadAlbums();
        LoadFavorites();
    }

    public ObservableCollection<Album> PagedAlbums { get; } = new();

    public void LoadAlbums()
    {
        Albums.Clear();
        IEnumerable<Album> albums;
        if (_selectedCreatorIds != null && _selectedCreatorIds.Count > 0)
        {
            albums = _dbService.GetAlbums(null, SearchText, SortBy)
                .Where(a => _selectedCreatorIds.Contains(a.CreatorId));
        }
        else
        {
            albums = _dbService.GetAlbums(_selectedCreatorId, SearchText, SortBy);
        }
        foreach (var album in albums)
            Albums.Add(album);
        UpdatePaging();
    }

    // ===== 主页分页 =====

    private bool _homePaged = true;
    /// <summary>主页分页显示（false = 全部平铺显示）</summary>
    public bool HomePaged
    {
        get => _homePaged;
        set { _homePaged = value; _dbService.SetSetting("HomePaged", value ? "1" : "0"); OnPropertyChanged(); UpdatePaging(); }
    }

    private int _pageSize = 24;
    /// <summary>每页相册数</summary>
    public int PageSize
    {
        get => _pageSize;
        set { _pageSize = Math.Clamp(value, 4, 200); _dbService.SetSetting("PageSize", _pageSize.ToString()); OnPropertyChanged(); UpdatePaging(); }
    }

    private int _currentPage = 1;
    public int CurrentPage
    {
        get => _currentPage;
        private set { _currentPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageInfo)); OnPropertyChanged(nameof(CanPrevPage)); OnPropertyChanged(nameof(CanNextPage)); }
    }

    private int _totalPages = 1;
    public int TotalPages
    {
        get => _totalPages;
        private set { _totalPages = value; OnPropertyChanged(); OnPropertyChanged(nameof(PageInfo)); OnPropertyChanged(nameof(CanPrevPage)); OnPropertyChanged(nameof(CanNextPage)); }
    }

    public string PageInfo => $"{CurrentPage} / {TotalPages}";
    public bool CanPrevPage => HomePaged && CurrentPage > 1;
    public bool CanNextPage => HomePaged && CurrentPage < TotalPages;

    public void PrevPage() { if (CanPrevPage) { CurrentPage--; UpdatePaging(); } }
    public void NextPage() { if (CanNextPage) { CurrentPage++; UpdatePaging(); } }

    /// <summary>根据分页设置刷新当前页内容</summary>
    public void UpdatePaging()
    {
        MultiSelectAlbumsMode = false;

        if (!HomePaged)
        {
            TotalPages = 1;
            CurrentPage = 1;
            PagedAlbums.Clear();
            foreach (var a in Albums) PagedAlbums.Add(a);
        }
        else
        {
            TotalPages = Math.Max(1, (int)Math.Ceiling(Albums.Count / (double)PageSize));
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;

            PagedAlbums.Clear();
            foreach (var a in Albums.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
                PagedAlbums.Add(a);
        }
    }

    public void LoadCreators()
    {
        Creators.Clear();
        var creators = _dbService.GetCreators();
        foreach (var creator in creators)
            Creators.Add(creator);
        BuildTree();
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
        SortAlbumImages();
    }

    private string _albumImageSort = "Name";

    /// <summary>相册内排序规则：Name（名称，默认）/ Date（修改时间）/ Size（大小）；视频永远排在最后</summary>
    public string AlbumImageSort
    {
        get => _albumImageSort;
        set { _albumImageSort = value; OnPropertyChanged(); SortAlbumImages(); }
    }

    private void SortAlbumImages()
    {
        if (CurrentAlbumImages.Count == 0) return;

        IEnumerable<ImageFile> sorted = _albumImageSort switch
        {
            "Date" => CurrentAlbumImages
                .OrderBy(f => f.FileType == "video" ? 1 : 0)
                .ThenBy(f => { try { return File.GetLastWriteTime(f.FilePath); } catch { return DateTime.MinValue; } }),
            "Size" => CurrentAlbumImages
                .OrderBy(f => f.FileType == "video" ? 1 : 0)
                .ThenBy(f => f.FileSize),
            _ => CurrentAlbumImages
                .OrderBy(f => f.FileType == "video" ? 1 : 0)
                .ThenBy(f => f.FileName, NaturalStringComparer.Instance)
        };

        var list = sorted.ToList();
        CurrentAlbumImages.Clear();
        foreach (var img in list)
            CurrentAlbumImages.Add(img);
    }

    public void FilterByCreator(int? creatorId)
    {
        _selectedCreatorId = creatorId;
        _selectedCreatorIds = null;
        LoadAlbums();
    }

    private List<int>? _selectedCreatorIds;
    /// <summary>按多个创作者筛选（用于分类筛选）</summary>
    public void FilterByCreators(List<int> creatorIds)
    {
        _selectedCreatorId = null;
        _selectedCreatorIds = creatorIds;
        LoadAlbums();
    }

    // ===== 资源管理器式导航方法 =====

    /// <summary>显示所有相册（重置导航）</summary>
    public void ShowAllAlbums()
    {
        _explorerCategoryId = null;
        _explorerCreatorId = null;
        _selectedCreatorId = null;
        _selectedCreatorIds = null;
        HomeViewMode = "Albums";
        HomeViewTitle = "全部相册";
        LoadAlbums();
    }

    /// <summary>显示指定分类下的创作者列表</summary>
    public void ShowCategoryCreators(int categoryId)
    {
        _explorerCategoryId = categoryId;
        _explorerCreatorId = null;
        HomeViewMode = "Creators";

        FilteredCreators.Clear();
        var creators = Creators.Where(c => c.CategoryId == categoryId).ToList();
        foreach (var c in creators)
            FilteredCreators.Add(c);

        HomeViewTitle = categoryId == 0 ? "无分类" : Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "分类";
    }

    /// <summary>显示指定创作者的所有相册</summary>
    public void ShowCreatorAlbums(int creatorId)
    {
        _explorerCreatorId = creatorId;
        _explorerCategoryId = Creators.FirstOrDefault(c => c.Id == creatorId)?.CategoryId;
        _selectedCreatorId = creatorId;
        _selectedCreatorIds = null;
        HomeViewMode = "Albums";
        HomeViewTitle = Creators.FirstOrDefault(c => c.Id == creatorId)?.Name ?? "创作者";
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

        var importDialog = new ImportDialog(this, dialog.FolderNames)
        {
            Owner = Application.Current.MainWindow
        };
        if (importDialog.ShowDialog() != true)
            return;

        _ = ImportFoldersAsync(importDialog.Items, importDialog.SelectedCreator, importDialog.AddOnly);
    }

    public async Task ImportFoldersAsync(List<ImportItem> items, Creator? creator, bool addOnly)
    {
        var owner = Application.Current.MainWindow;
        var progress = new ProgressWindow(items.Count) { Owner = owner };
        progress.Show();

        int successCount = 0;
        int skippedCount = 0;
        int failedCount = 0;
        bool cancelled = false;

        await Task.Run(() =>
        {
            for (int i = 0; i < items.Count; i++)
            {
                // 每项开始前检查取消标记，中断后续导入
                if (progress.Dispatcher.Invoke(() => progress.IsCancelRequested))
                {
                    cancelled = true;
                    break;
                }

                var item = items[i];
                try
                {
                    progress.Dispatcher.BeginInvoke(() =>
                        progress.Report(i + 1, items.Count, $"正在处理：{item.AlbumName}"));

                    string actualPath = item.SourcePath;
                    int creatorId = creator?.Id ?? 0;

                    if (!addOnly)
                    {
                        // 整理模式：递归提取媒体文件到 库/[分类/]创作者/相册名，其余归入「非视图文件」
                        var creatorName = creator?.Name ?? "无创作者";
                        var categoryName = creator?.CategoryId > 0
                            ? Categories.FirstOrDefault(c => c.Id == creator.CategoryId)?.Name
                            : null;
                        var organized = _fileService.OrganizeAlbumRecursive(
                            item.SourcePath, creatorName, item.AlbumName, LibraryPath, categoryName);
                        if (organized == null)
                        {
                            failedCount++;
                            continue;
                        }
                        actualPath = organized;
                    }

                    // 检查重复
                    if (_dbService.AlbumExists(actualPath))
                    {
                        skippedCount++;
                        continue;
                    }

                    var album = new Album
                    {
                        Name = item.AlbumName,
                        Path = actualPath,
                        CreatorId = creatorId,
                        StorageMode = addOnly ? StorageMode.External : StorageMode.Managed,
                        CreatedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now
                    };

                    // 扫描媒体文件（整理模式媒体已平铺到根部；仅添加模式递归索引，多二级目录时索引名加前缀）
                    var files = addOnly
                        ? _fileService.ScanMediaRecursive(actualPath, subDirPrefix: true)
                        : _fileService.ScanFolder(actualPath)
                            .Where(f => f.FileType is "image" or "video").ToList();
                    album.ImageCount = files.Count(f => f.FileType == "image");
                    album.VideoCount = files.Count(f => f.FileType == "video");

                    var firstImage = files.FirstOrDefault(f => f.FileType == "image")
                                     ?? files.FirstOrDefault(f => f.FileType == "video");
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
                    Debug.WriteLine($"导入失败 {item.SourcePath}: {ex.Message}");
                }
            }
        });

        progress.Close();

        // 刷新数据
        LoadAlbums();
        LoadCreators();
        LoadFavorites();

        // 结果提示（await 后已回到 UI 线程，直接调用）
        var msg = cancelled
            ? $"已取消导入：成功 {successCount} 个"
            : $"导入完成：成功 {successCount} 个";
        if (skippedCount > 0) msg += $"，跳过重复 {skippedCount} 个";
        if (failedCount > 0) msg += $"，失败 {failedCount} 个";

        ModernDialog.Info(owner, msg, "导入完成");
    }

    public void AddCreator(string name, int categoryId = 0)
    {
        var creator = new Creator
        {
            Name = name,
            FolderPath = Path.Combine(LibraryPath, name),
            CategoryId = categoryId
        };
        _dbService.AddCreator(creator);
        LoadCreators();
        BuildTree();
    }

    public void UpdateCreator(int id, string name, string? thumbPath = null, int? categoryId = null)
    {
        _dbService.UpdateCreator(id, name, thumbPath, categoryId);
        LoadCreators();
        LoadAlbums(); // 创作者名可能变化
        BuildTree();
    }

    public void DeleteCreator(int id, bool deleteFiles = false)
    {
        _dbService.DeleteCreator(id, deleteFiles);
        LoadCreators();
        LoadAlbums();
        BuildTree();
    }

    public void DeleteAlbum(int id, bool deleteFiles = false)
    {
        _dbService.DeleteAlbum(id, deleteFiles);
        LoadAlbums();
        LoadCreators();
        BuildTree();
    }

    public void DeleteBookmark(int id)
    {
        _dbService.DeleteBookmark(id);
        LoadBookmarks();
    }

    // ===== 拖拽移动 =====

    /// <summary>把相册移动到目标创作者（仅更新逻辑归属，不移动磁盘文件）</summary>
    public bool MoveAlbumToCreator(int albumId, int targetCreatorId)
    {
        var album = _dbService.GetAlbum(albumId);
        if (album == null || album.CreatorId == targetCreatorId) return false;

        var targetCreator = Creators.FirstOrDefault(c => c.Id == targetCreatorId);
        if (targetCreator == null) return false;

        // 安全规则：普通拖放只改逻辑分类，绝不移动磁盘文件
        _dbService.UpdateAlbumCreator(albumId, targetCreatorId);
        LoadAlbums();
        LoadCreators();
        return true;
    }

    /// <summary>把创作者移动到目标分类（仅更新逻辑归属，不移动磁盘文件）</summary>
    public bool MoveCreatorToCategory(int creatorId, int targetCategoryId)
    {
        var creator = Creators.FirstOrDefault(c => c.Id == creatorId);
        if (creator == null || creator.CategoryId == targetCategoryId) return false;

        // 安全规则：普通拖放只改逻辑分类，绝不移动磁盘文件
        _dbService.UpdateCreatorCategory(creatorId, targetCategoryId);
        LoadCreators();
        LoadAlbums();
        return true;
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
        var album = _dbService.GetAlbum(albumId);
        if (album == null) return;

        // 外部相册禁止重命名文件（只读保护）
        if (album.IsExternal)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                "外部相册为只读模式，不允许重命名文件。\n如需编辑，请先使用「整理入库」将相册转为托管模式。",
                "操作受限");
            return;
        }

        var images = _dbService.GetAlbumImages(albumId);
        var renamed = _fileService.BatchRename(images, pattern);
        _dbService.UpdateImageFiles(renamed);
        LoadAlbumDetail(albumId);
    }

    public void RenameImageFile(int imageId, string newName)
    {
        var image = CurrentAlbumImages.FirstOrDefault(i => i.Id == imageId);
        if (image == null) return;

        // 外部相册禁止重命名文件（只读保护）
        if (SelectedAlbum?.IsExternal == true)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                "外部相册为只读模式，不允许重命名文件。\n如需编辑，请先使用「整理入库」将相册转为托管模式。",
                "操作受限");
            return;
        }

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
            try
            {
                File.Move(image.FilePath, newPath);
                image.FileName = newName;
                image.FilePath = newPath;
                _dbService.UpdateImageFiles([image]);
            }
            catch (Exception ex)
            {
                ModernDialog.Info(Application.Current.MainWindow,
                    $"重命名失败：{ex.Message}", "错误");
            }
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

        // 外部相册禁止删除源文件（只读保护）
        if (deleteSource && SelectedAlbum?.IsExternal == true)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                "外部相册为只读模式，不允许删除源文件。\n仅会从索引中移除该文件。",
                "操作受限");
            deleteSource = false;
        }

        if (deleteSource && File.Exists(image.FilePath))
        {
            try { File.Delete(image.FilePath); }
            catch (Exception ex)
            {
                ModernDialog.Info(Application.Current.MainWindow,
                    $"删除文件失败：{ex.Message}\n但已从索引中移除。", "警告");
            }
        }

        _dbService.DeleteImageFile(imageId);
        if (SelectedAlbum != null)
            _dbService.RefreshAlbumImageCount(SelectedAlbum.Id);
        LoadAlbumDetail(SelectedAlbum?.Id ?? 0);
    }

    // ===== 多选模式 =====

    private bool _multiSelectMode;
    public bool MultiSelectMode
    {
        get => _multiSelectMode;
        set
        {
            _multiSelectMode = value;
            if (!value)
            {
                foreach (var f in CurrentAlbumImages) f.IsSelected = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedImageCount));
        }
    }

    public int SelectedImageCount => CurrentAlbumImages.Count(f => f.IsSelected);

    public void ToggleImageSelection(ImageFile file)
    {
        file.IsSelected = !file.IsSelected;
        OnPropertyChanged(nameof(SelectedImageCount));
    }

    public void SelectAllImages(bool select)
    {
        foreach (var f in CurrentAlbumImages) f.IsSelected = select;
        OnPropertyChanged(nameof(SelectedImageCount));
    }

    /// <summary>批量删除所选图片</summary>
    public void DeleteSelectedImages(bool deleteSource)
    {
        var selected = CurrentAlbumImages.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        // 外部相册禁止删除源文件（只读保护）
        if (deleteSource && SelectedAlbum?.IsExternal == true)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                "外部相册为只读模式，不允许删除源文件。\n仅会从索引中移除这些文件。",
                "操作受限");
            deleteSource = false;
        }

        int failCount = 0;
        if (deleteSource)
        {
            foreach (var f in selected)
            {
                try { if (File.Exists(f.FilePath)) File.Delete(f.FilePath); }
                catch { failCount++; }
            }
        }

        _dbService.DeleteImageFiles(selected.Select(f => f.Id));
        if (SelectedAlbum != null)
            _dbService.RefreshAlbumImageCount(SelectedAlbum.Id);
        LoadAlbumDetail(SelectedAlbum?.Id ?? 0);
        LoadAlbums();
        LoadFavorites();

        if (failCount > 0)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                $"已删除索引，但 {failCount} 个文件的物理删除失败。", "警告");
        }
    }

    /// <summary>批量重命名所选图片</summary>
    public void BatchRenameSelectedImages(string pattern)
    {
        var selected = CurrentAlbumImages.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        // 外部相册禁止重命名文件（只读保护）
        if (SelectedAlbum?.IsExternal == true)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                "外部相册为只读模式，不允许重命名文件。\n如需编辑，请先使用「整理入库」将相册转为托管模式。",
                "操作受限");
            return;
        }

        var renamed = _fileService.BatchRename(selected, pattern);
        _dbService.UpdateImageFiles(renamed);
        LoadAlbumDetail(SelectedAlbum?.Id ?? 0);
    }

    /// <summary>将「仅添加」相册整理入库：移动文件到库目录并重建索引。成功返回 true</summary>
    public bool OrganizeExternalAlbum(int albumId)
    {
        var album = _dbService.GetAlbum(albumId);
        if (album == null || !Directory.Exists(album.Path)) return false;

        var creatorName = Creators.FirstOrDefault(c => c.Id == album.CreatorId)?.Name ?? "无创作者";
        var albumCreator = Creators.FirstOrDefault(c => c.Id == album.CreatorId);
        var categoryName = albumCreator?.CategoryId > 0
            ? Categories.FirstOrDefault(c => c.Id == albumCreator.CategoryId)?.Name
            : null;
        var newPath = _fileService.OrganizeAlbumRecursive(album.Path, creatorName, album.Name, LibraryPath, categoryName);
        if (newPath == null) return false;

        // 重建索引
        _dbService.DeleteImageFilesByAlbum(albumId);
        var files = _fileService.ScanFolder(newPath)
            .Where(f => f.FileType is "image" or "video").ToList();
        foreach (var file in files)
        {
            file.AlbumId = albumId;
            _dbService.AddImageFile(file);
        }

        _dbService.UpdateAlbumPath(albumId, newPath);
        _dbService.SetAlbumStorageMode(albumId, StorageMode.Managed);
        _dbService.RefreshAlbumImageCount(albumId);

        // 封面可能已失效，重置为第一张图（无图则用第一个视频）
        var firstImage = files.FirstOrDefault(f => f.FileType == "image")
                         ?? files.FirstOrDefault(f => f.FileType == "video");
        if (firstImage != null)
            _dbService.UpdateAlbumCover(albumId, firstImage.FilePath);

        LoadAlbumDetail(albumId);
        LoadAlbums();
        LoadFavorites();
        return true;
    }

    /// <summary>刷新「仅添加」相册：按导入规则（递归 + 多二级目录前缀）重建索引，不移动文件</summary>
    public void RefreshExternalAlbum(int albumId)
    {
        var album = _dbService.GetAlbum(albumId);
        if (album == null || !Directory.Exists(album.Path)) return;

        _dbService.DeleteImageFilesByAlbum(albumId);
        var files = _fileService.ScanMediaRecursive(album.Path, subDirPrefix: true);
        RebuildAlbumIndex(albumId, album, files);
    }

    /// <summary>刷新任意相册索引：仅添加相册按递归+前缀规则，已入库相册扫描目录根部；均不移动文件</summary>
    public bool RefreshAlbum(int albumId)
    {
        var album = _dbService.GetAlbum(albumId);
        if (album == null || !Directory.Exists(album.Path)) return false;

        _dbService.DeleteImageFilesByAlbum(albumId);
        var files = album.IsExternal
            ? _fileService.ScanMediaRecursive(album.Path, subDirPrefix: true)
            : _fileService.ScanFolder(album.Path)
                .Where(f => f.FileType is "image" or "video").ToList();
        RebuildAlbumIndex(albumId, album, files);
        return true;
    }

    /// <summary>重建相册文件索引并更新封面/计数</summary>
    private void RebuildAlbumIndex(int albumId, Album album, List<ImageFile> files)
    {
        foreach (var file in files)
        {
            file.AlbumId = albumId;
            _dbService.AddImageFile(file);
        }

        _dbService.RefreshAlbumImageCount(albumId);

        // 封面文件已不存在时重置（无图则用第一个视频）
        if (string.IsNullOrEmpty(album.CoverPath) || !File.Exists(album.CoverPath))
        {
            var firstImage = files.FirstOrDefault(f => f.FileType == "image")
                             ?? files.FirstOrDefault(f => f.FileType == "video");
            if (firstImage != null)
                _dbService.UpdateAlbumCover(albumId, firstImage.FilePath);
        }

        LoadAlbumDetail(albumId);
        LoadAlbums();
        LoadFavorites();
    }

    public void AddImagesToAlbum(int albumId, string[] filePaths)
    {
        var album = _dbService.GetAlbum(albumId);
        if (album == null) return;

        // 外部相册禁止添加文件（只读保护）
        if (album.IsExternal)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                "外部相册为只读模式，不允许向其中添加文件。\n如需编辑，请先使用「整理入库」将相册转为托管模式。",
                "操作受限");
            return;
        }

        int failCount = 0;
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
            catch (Exception ex)
            {
                failCount++;
                Debug.WriteLine($"AddImagesToAlbum failed for {srcPath}: {ex.Message}");
            }
        }

        _dbService.RefreshAlbumImageCount(albumId);
        LoadAlbumDetail(albumId);
        LoadAlbums();

        if (failCount > 0)
        {
            ModernDialog.Info(Application.Current.MainWindow,
                $"添加完成，但有 {failCount} 个文件复制失败。", "部分失败");
        }
    }

    public void UpdateAlbum(int albumId, string name, int creatorId, string coverPath, string path,
                            bool moveFiles = false, string? oldPath = null)
    {
        // 先移动文件，成功后再更新数据库（避免数据库指向不存在的位置）
        if (moveFiles && oldPath != null && oldPath != path && Directory.Exists(oldPath))
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Directory.Move(oldPath, path);
                _dbService.UpdateAlbumImagePaths(albumId, oldPath, path);
            }
            catch (Exception ex)
            {
                ModernDialog.Info(Application.Current.MainWindow,
                    $"移动文件夹失败：{ex.Message}\n数据库未更新，请检查目标路径是否可写。",
                    "移动失败");
                return; // 不更新数据库，保持原有状态
            }
        }

        _dbService.UpdateAlbumInfo(albumId, name, creatorId, coverPath, path);
        LoadAlbums();
        LoadCreators();
        LoadAlbumDetail(albumId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
