using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tudot.Models;

/// <summary>相册存储模式：Managed=托管入库（软件可管理物理文件）；External=外部引用（只读，不删源文件）</summary>
public enum StorageMode
{
    Managed,
    External
}

public class Album : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int CreatorId { get; set; }
    public string CreatorName { get; set; } = string.Empty;
    private string _coverPath = string.Empty;
    public string CoverPath
    {
        get => _coverPath;
        set { _coverPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CoverIsVideo)); }
    }

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

    /// <summary>封面是否为视频文件（用于显示播放角标）</summary>
    public bool CoverIsVideo =>
        !string.IsNullOrEmpty(CoverPath) && VideoExts.Contains(System.IO.Path.GetExtension(CoverPath));
    public int ImageCount { get; set; }
    public int VideoCount { get; set; }

    /// <summary>相册内容计数显示：有图有视频→"3P+2V"；仅图→"3P"；仅视频→"2V"；空→"0P"</summary>
    public string CountLabel
    {
        get
        {
            if (ImageCount > 0 && VideoCount > 0)
                return $"{ImageCount}P+{VideoCount}V";
            if (ImageCount > 0)
                return $"{ImageCount}P";
            if (VideoCount > 0)
                return $"{VideoCount}V";
            return "0P";
        }
    }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set { _isFavorite = value; OnPropertyChanged(); }
    }

    private StorageMode _storageMode = StorageMode.Managed;
    /// <summary>存储模式：Managed=托管入库；External=外部引用（文件保留原位置，只读）</summary>
    public StorageMode StorageMode
    {
        get => _storageMode;
        set { _storageMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsExternal)); }
    }

    /// <summary>是否外部引用相册（便捷只读属性，供 XAML 布尔绑定）</summary>
    public bool IsExternal => StorageMode == StorageMode.External;

    private string _healthStatus = "Healthy";
    /// <summary>健康状态：Healthy / Missing / MigrationFailed 等（阶段 B 仅入列，检查逻辑属阶段 C）</summary>
    public string HealthStatus
    {
        get => _healthStatus;
        set { _healthStatus = value; OnPropertyChanged(); }
    }

    private bool _isSelected;
    /// <summary>多选模式下的选中状态（仅 UI，不入库）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class Creator : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ThumbPath { get; set; } = string.Empty;
    public int CategoryId { get; set; } = 0; // 0 表示无分类
    public string CategoryName { get; set; } = string.Empty;
    public int AlbumCount { get; set; }
    public int TotalImages { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class Category : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#999999";
    public int SortOrder { get; set; }
    public int CreatorCount { get; set; }
    public int AlbumCount { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ImageFile : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int AlbumId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty; // image, video, other
    public long FileSize { get; set; }
    public int SortOrder { get; set; }

    private bool _isSelected;
    /// <summary>多选模式下的选中状态（仅 UI，不入库）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class Bookmark : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedDate { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
