using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tudot.Models;

/// <summary>侧边栏目录树节点类型</summary>
public enum TreeNodeType { Root, Category, Creator, Album }

/// <summary>侧边栏目录树节点（分类 → 创作者 → 相册）</summary>
public class TreeNode : INotifyPropertyChanged
{
    public TreeNodeType Type { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Extra { get; set; } // 创作者存分类名；相册存封面路径

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TreeNode> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
