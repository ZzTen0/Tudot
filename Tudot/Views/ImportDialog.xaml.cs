using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using Tudot.Models;
using Tudot.Services;
using Tudot.ViewModels;

namespace Tudot.Views;

/// <summary>导入项：源文件夹 + 可编辑的相册名 + 同步重命名规则</summary>
public class ImportItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string SourceName => Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar));
    public string AlbumName { get; set; } = string.Empty;
    /// <summary>整理入库时：同步重命名相册内文件名</summary>
    public bool RenameFileEnabled { get; set; }
    /// <summary>文件名组合片段（不含扩展名）</summary>
    public List<NameSimilarity.FilePart> RenameFileParts { get; set; } = new();
    public string RenameFileSeparator { get; set; } = "_";
    /// <summary>相册名后缀（多文件夹批量命名时取自该行 Suffix；单文件夹取自完整相册名）</summary>
    public string RenameAlbumSuffix { get; set; } = "";
}

/// <summary>相似元素标签（也是组合中的文本块数据源）</summary>
public class SimilarTagVm : INotifyPropertyChanged
{
    /// <summary>包裹形态：无 / () / [] / {} / 双引号 / 单引号</summary>
    public static readonly (string Open, string Close)[] Wraps =
        { ("", ""), ("(", ")"), ("[", "]"), ("{", "}"), ("\"", "\""), ("'", "'") };

    private int _wrapIndex;
    private bool _active;

    public string Text { get; init; } = "";
    public bool Weak { get; init; }
    public Visibility WeakVisibility => Weak ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>包裹形态索引（0-5 循环）</summary>
    public int WrapIndex
    {
        get => _wrapIndex;
        set { if (_wrapIndex != value) { _wrapIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(WrappedText)); } }
    }

    /// <summary>已加入命名组合</summary>
    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; OnPropertyChanged(); } }
    }

    public string WrappedText => Wraps[_wrapIndex].Open + Text + Wraps[_wrapIndex].Close;

    /// <summary>指定包裹形态下的预览文本（供右键菜单使用）</summary>
    public string PreviewWrapped(int index) => Wraps[index].Open + Text + Wraps[index].Close;

    public void CycleWrap() => WrapIndex = (_wrapIndex + 1) % Wraps.Length;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>命名组合片段</summary>
public class TokenVm : INotifyPropertyChanged
{
    /// <summary>text 文本块（来自相似标签或自定义） / sep 分隔符 / num 原编号 / suf 后缀</summary>
    public string Key { get; init; } = "";

    /// <summary>text 块引用的相似标签（包裹随标签联动）；自定义文本为 null</summary>
    public SimilarTagVm? SourceTag { get; init; }

    /// <summary>自定义文本（SourceTag 为 null 时使用）</summary>
    public string CustomText { get; init; } = "";

    public virtual string Display => Key switch
    {
        "text" => SourceTag?.WrappedText ?? CustomText,
        "sep" => "分隔符",
        "num" => "原编号",
        _ => "后缀"
    };

    public Brush Bg { get; init; } = Brushes.White;
    public Brush Bd { get; init; } = Brushes.LightGray;
    public Brush Fg { get; init; } = Brushes.Black;

    public TokenVm() { }

    public TokenVm(SimilarTagVm tag)
    {
        SourceTag = tag;
        tag.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SimilarTagVm.WrappedText))
                OnPropertyChanged(nameof(Display));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    protected void NotifyDisplay() => OnPropertyChanged(nameof(Display));
}

/// <summary>「同步重命名相册内文件名」的文件名组合片段</summary>
public class FileTokenVm : TokenVm
{
    private string _label = "";
    private string _sepValue = "_";
    private int _numWidth = 4;

    /// <summary>显示文本（分隔符「_」/ 编号 #### 等），为空时回退到基类规则</summary>
    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; NotifyDisplay(); } }
    }

    /// <summary>sep 片段的分隔符值</summary>
    public string SepValue
    {
        get => _sepValue;
        set { if (_sepValue != value) { _sepValue = value; NotifyDisplay(); } }
    }

    /// <summary>num 片段的编号位数（4 = 0001）</summary>
    public int NumWidth
    {
        get => _numWidth;
        set { if (_numWidth != value) { _numWidth = value; NotifyDisplay(); } }
    }

    public override string Display => Label.Length > 0 ? Label : base.Display;
}

/// <summary>批量命名结果行</summary>
public class BatchRowVm : INotifyPropertyChanged
{
    private string _suffix = "";
    private string _finalDisplay = "";
    private Brush _finalBrush = Brushes.Green;
    public string SourceName { get; init; } = "";
    public string Suffix
    {
        get => _suffix;
        set { if (_suffix != value) { _suffix = value; OnPropertyChanged(); } }
    }
    public string FinalDisplay
    {
        get => _finalDisplay;
        set { if (_finalDisplay != value) { _finalDisplay = value; OnPropertyChanged(); } }
    }
    public Brush FinalBrush
    {
        get => _finalBrush;
        set { if (!ReferenceEquals(_finalBrush, value)) { _finalBrush = value; OnPropertyChanged(); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class ImportDialog : Window
{
    private readonly MainViewModel _viewModel;
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x57));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F));
    private static readonly Brush TextBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFF));
    private static readonly Brush TextBd = new SolidColorBrush(Color.FromRgb(0xBC, 0xD2, 0xFF));
    private static readonly Brush TextFg = new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0xDD));
    private static readonly Brush SepBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush NumBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF2, 0xE0));
    private static readonly Brush NumBd = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0xA8));
    private static readonly Brush NumFg = new SolidColorBrush(Color.FromRgb(0xC8, 0x7A, 0x1E));
    private static readonly Brush SufBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xF8, 0xEE));
    private static readonly Brush SufBd = new SolidColorBrush(Color.FromRgb(0xB6, 0xE6, 0xC9));

    /// <summary>导入项列表（相册名已被用户编辑）</summary>
    public List<ImportItem> Items { get; }

    /// <summary>用户选择/新建的创作者；null 表示归入「无创作者」目录</summary>
    public Creator? SelectedCreator { get; private set; }

    /// <summary>仅添加模式：不移动文件，仅登记到图库</summary>
    public bool AddOnly { get; private set; }

    // 批量命名状态
    private ObservableCollection<SimilarTagVm> _tags = new();
    private ObservableCollection<TokenVm> _tokens = new();
    private ObservableCollection<BatchRowVm> _rows = new();
    private List<string> _numbers = new();
    private List<string> _orderedCommon = new();
    private string _sep = "_";
    private bool _batchReady;

    // 同步重命名相册内文件名
    private readonly ObservableCollection<FileTokenVm> _fileTokens = new();

    // token 拖拽状态（相册名组合 / 文件名组合 各一份）
    private class TokenDragState { public int Index = -1; public Point Start; }
    private readonly TokenDragState _batchDrag = new();
    private readonly TokenDragState _fileDrag = new();

    // 标签单击/双击消歧：单击延迟 200ms 生效，期间第二击判定为双击（循环包裹）
    private readonly DispatcherTimer _tagClickTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private SimilarTagVm? _pendingClickTag;

    public ImportDialog(MainViewModel viewModel, string[] folderPaths)
    {
        InitializeComponent();
        _viewModel = viewModel;
        InfoText.Text = $"已选择 {folderPaths.Length} 个文件夹，库存储路径：{viewModel.LibraryPath}";
        CreatorList.ItemsSource = viewModel.Creators;

        // 填充新创作者分类下拉框
        NewCreatorCategoryCombo.Items.Add(new ComboBoxItem { Content = "无分类", Tag = 0 });
        foreach (var cat in viewModel.Categories.OrderBy(c => c.Name))
            NewCreatorCategoryCombo.Items.Add(new ComboBoxItem { Content = cat.Name, Tag = cat.Id });
        NewCreatorCategoryCombo.SelectedIndex = 0;

        Items = folderPaths.Select(p => new ImportItem
        {
            SourcePath = p,
            AlbumName = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar))
        }).ToList();

        SimilarTagList.ItemsSource = _tags;
        TokenList.ItemsSource = _tokens;
        BatchRowList.ItemsSource = _rows;
        FileTokenList.ItemsSource = _fileTokens;

        if (Items.Count > 1)
            InitBatchNaming();
        else
            FolderList.ItemsSource = Items;

        _tagClickTimer.Tick += (s, e) =>
        {
            _tagClickTimer.Stop();
            if (_pendingClickTag != null) ToggleTagActive(_pendingClickTag);
            _pendingClickTag = null;
        };
    }

    /// <summary>多文件夹：分析相似元素并初始化批量命名区（方案 C：标签即文本块）</summary>
    private void InitBatchNaming()
    {
        SinglePanel.Visibility = Visibility.Collapsed;
        BatchPanel.Visibility = Visibility.Visible;

        var names = Items.Select(i => i.SourceName).ToList();
        var result = NameSimilarity.Analyze(names);
        _numbers = result.Numbers;

        // 公共元素按其在第一个文件夹名中的出现顺序排列
        var firstTokens = NameSimilarity.Tokenize(names[0]);
        _orderedCommon = firstTokens
            .Where(t => result.Common.Any(c => string.Equals(c, t, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        foreach (var t in _orderedCommon)
            _tags.Add(new SimilarTagVm { Text = t, Weak = false, Active = true });
        foreach (var t in result.Weak)
            _tags.Add(new SimilarTagVm { Text = t, Weak = true });

        for (var i = 0; i < names.Count; i++)
            _rows.Add(new BatchRowVm { SourceName = names[i], Suffix = result.Suffixes[i] });
        foreach (var row in _rows)
            row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BatchRowVm.Suffix)) RecomputeNames(); };

        BuildDefaultTokens();
        _batchReady = true;
        RecomputeNames();
    }

    /// <summary>默认组合：仅公共文本块 + 后缀；分隔符、原编号等按需手动添加，弱相似与自定义块不参与默认</summary>
    private void BuildDefaultTokens()
    {
        _tokens.Clear();
        foreach (var text in _orderedCommon)
        {
            var tag = _tags.First(t => !t.Weak && string.Equals(t.Text, text, StringComparison.OrdinalIgnoreCase));
            _tokens.Add(new TokenVm(tag) { Key = "text", Bg = TextBg, Bd = TextBd, Fg = TextFg });
        }
        _tokens.Add(MakeSufToken());
    }

    private static TokenVm MakeSepToken() => new() { Key = "sep", Bg = SepBg, Bd = Brushes.LightGray, Fg = Brushes.DimGray };
    private static TokenVm MakeNumToken() => new() { Key = "num", Bg = NumBg, Bd = NumBd, Fg = NumFg };
    private static TokenVm MakeSufToken() => new() { Key = "suf", Bg = SufBg, Bd = SufBd, Fg = OkBrush };
    private static TokenVm MakeCustomTextToken(string text) =>
        new() { Key = "text", CustomText = text, Bg = TextBg, Bd = TextBd, Fg = TextFg };

    /// <summary>当前 token 组合对应的片段序列</summary>
    private List<NameSimilarity.NamePart> CurrentParts() =>
        _tokens.Select<TokenVm, NameSimilarity.NamePart>(t => t.Key switch
        {
            "text" => NameSimilarity.TextPart(t.Display),
            "sep" => NameSimilarity.SepPart(_sep),
            "num" => NameSimilarity.NumberPart(),
            _ => NameSimilarity.SuffixPart()
        }).ToList();

    /// <summary>按片段序列计算某行最终名（suffix 可覆盖行后缀，供自动编号试算）</summary>
    private string BuildRowFinal(List<NameSimilarity.NamePart> parts, int rowIndex, string suffix, bool keepNum)
    {
        var number = rowIndex < _numbers.Count ? _numbers[rowIndex] : "";
        // 编号由「原编号」片段输出时，从后缀中剔除该编号，避免重复出现
        if (keepNum && number.Length > 0 && parts.Any(p => p.Kind == NameSimilarity.PartKind.Number))
            suffix = NameSimilarity.RemoveNumberOnce(suffix, number);
        return NameSimilarity.BuildName(parts, number, suffix, keepNum);
    }

    /// <summary>按当前片段组合重算每行最终名，并同步到 Items.AlbumName</summary>
    private void RecomputeNames()
    {
        if (!_batchReady) return;

        // 全部保留原命名：各行直接使用原文件夹名，组合/预览停用
        if (KeepOriginalCheck.IsChecked == true)
        {
            PatternPreviewRun.Text = "（全部保留原命名）";
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].FinalDisplay = _rows[i].SourceName;
                _rows[i].FinalBrush = OkBrush;
                Items[i].AlbumName = _rows[i].SourceName;
            }
            return;
        }
        var parts = CurrentParts();
        var keepNum = KeepNumberCheck.IsChecked == true;

        // 模板预览：文本块为实际内容，原编号→xxx、后缀→*** 占位（未勾选保留编号时编号不占位）
        var pattern = NameSimilarity.BuildName(_tokens.Select<TokenVm, NameSimilarity.NamePart>(t => t.Key switch
        {
            "text" => NameSimilarity.TextPart(t.Display),
            "sep" => NameSimilarity.SepPart(_sep),
            "num" => NameSimilarity.TextPart(keepNum ? "xxx" : ""),
            _ => NameSimilarity.TextPart("***")
        }).ToList(), "", "", false);
        PatternPreviewRun.Text = string.IsNullOrEmpty(pattern) ? "（组合为空）" : pattern;

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var final = BuildRowFinal(parts, i, row.Suffix, keepNum);

            if (string.IsNullOrEmpty(final))
            {
                row.FinalDisplay = "（命名为空）";
                row.FinalBrush = WarnBrush;
            }
            else if (final == row.SourceName)
            {
                row.FinalDisplay = final + "（与原名相同）";
                row.FinalBrush = WarnBrush;
            }
            else
            {
                row.FinalDisplay = final;
                row.FinalBrush = OkBrush;
            }
            Items[i].AlbumName = final;
        }
    }

    // ── 标签：单击加入/移出组合（200ms 延迟消歧），双击循环包裹 ──
    private void SimilarTag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SimilarTagVm tag) return;

        if (e.ClickCount >= 2)
        {
            // 双击：取消挂起的单击，循环包裹
            _tagClickTimer.Stop();
            _pendingClickTag = null;
            tag.CycleWrap();
            RecomputeNames();
            e.Handled = true;
            return;
        }

        _pendingClickTag = tag;
        _tagClickTimer.Stop();
        _tagClickTimer.Start();
    }

    private void ToggleTagActive(SimilarTagVm tag)
    {
        if (tag.Active)
        {
            tag.Active = false;
            var existing = _tokens.FirstOrDefault(t => ReferenceEquals(t.SourceTag, tag));
            if (existing != null) _tokens.Remove(existing);
        }
        else
        {
            tag.Active = true;
            _tokens.Add(new TokenVm(tag) { Key = "text", Bg = TextBg, Bd = TextBd, Fg = TextFg });
        }
        RecomputeNames();
    }

    /// <summary>标签/文本块右键：选择包裹形态</summary>
    private void SimilarTag_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SimilarTagVm tag) return;
        e.Handled = true;
        ShowWrapMenu(fe, tag);
    }

    private void Token_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        // 沿可视化树找到 DataContext 为 TokenVm 的容器
        DependencyObject d = fe;
        while (d != null && d is not ListBoxItem)
        {
            if (d is FrameworkElement { DataContext: TokenVm { SourceTag: { } tagToken } })
            {
                e.Handled = true;
                ShowWrapMenu(fe, tagToken);
                return;
            }
            d = VisualTreeHelper.GetParent(d);
        }
        if (d is ListBoxItem { DataContext: TokenVm { SourceTag: { } tag } })
        {
            e.Handled = true;
            ShowWrapMenu(fe, tag);
        }
    }

    private void ShowWrapMenu(UIElement target, SimilarTagVm tag)
    {
        var menu = new ContextMenu();
        for (var i = 0; i < SimilarTagVm.Wraps.Length; i++)
        {
            var index = i;
            var mi = new MenuItem
            {
                Header = i == 0 ? $"{tag.Text}（无包裹）" : tag.PreviewWrapped(i),
                IsCheckable = true,
                IsChecked = tag.WrapIndex == i
            };
            mi.Click += (s, e) =>
            {
                tag.WrapIndex = index;
                RecomputeNames();
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ── 自定义文本块 ──
    private void CustomTextAdd_Click(object sender, RoutedEventArgs e) => AddCustomText();

    private void CustomText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddCustomText(); e.Handled = true; }
    }

    private void AddCustomText()
    {
        var text = CustomTextBox.Text.Trim();
        if (text.Length == 0) return;
        _tokens.Add(MakeCustomTextToken(text));
        CustomTextBox.Text = "";
        RecomputeNames();
    }

    // ── 分隔符（ToggleButton 手动互斥）──
    private bool _syncingSep;

    private void Sep_Changed(object sender, RoutedEventArgs e)
    {
        if (!_batchReady) return;
        if (sender is ToggleButton current && current.IsChecked == true && !_syncingSep)
        {
            _syncingSep = true;
            foreach (var btn in new[] { SepUnderscore, SepHyphen, SepPlus, SepSpace, SepCustom })
                if (!ReferenceEquals(btn, current)) btn.IsChecked = false;
            _syncingSep = false;
        }
        var tag = (sender as ToggleButton)?.Tag as string;
        _sep = tag switch
        {
            "custom" => string.IsNullOrEmpty(CustomSepBox.Text) ? "·" : CustomSepBox.Text,
            _ => tag ?? "_"
        };
        RecomputeNames();
    }

    private void CustomSep_GotFocus(object sender, RoutedEventArgs e) => SepCustom.IsChecked = true;

    private void CustomSep_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_batchReady) return;
        if (!string.IsNullOrEmpty(CustomSepBox.Text))
        {
            SepCustom.IsChecked = true;
            _sep = CustomSepBox.Text;
        }
        RecomputeNames();
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => RecomputeNames();

    /// <summary>全部保留原命名：勾选后组合区停用，各行直接使用原文件夹名</summary>
    private void KeepOriginal_Changed(object sender, RoutedEventArgs e)
    {
        if (!_batchReady) return;
        var on = KeepOriginalCheck.IsChecked == true;
        SimilarTagList.IsEnabled = !on;
        CustomTextRow.IsEnabled = !on;
        // SepRow 内含本勾选框，不能整体停用，逐个停用组合控件
        SepUnderscore.IsEnabled = !on;
        SepHyphen.IsEnabled = !on;
        SepPlus.IsEnabled = !on;
        SepSpace.IsEnabled = !on;
        SepCustom.IsEnabled = !on;
        CustomSepBox.IsEnabled = !on;
        KeepNumberCheck.IsEnabled = !on;
        AutoNumberCheck.IsEnabled = !on;
        TokenBorder.IsEnabled = !on;
        TokenAddRow.IsEnabled = !on;
        RecomputeNames();
    }

    /// <summary>同名自动编号：勾选瞬间，把最终名重复且后缀为空的行按排列顺序填入 1、2、3…（只填一次，之后可手动修改）</summary>
    private void AutoNumber_Checked(object sender, RoutedEventArgs e)
    {
        if (!_batchReady || KeepOriginalCheck.IsChecked == true) return;
        var parts = CurrentParts();
        var keepNum = KeepNumberCheck.IsChecked == true;
        var finals = new List<string>(_rows.Count);
        for (var i = 0; i < _rows.Count; i++)
            finals.Add(BuildRowFinal(parts, i, _rows[i].Suffix, keepNum));

        var used = new HashSet<string>(finals.Where(f => f.Length > 0), StringComparer.OrdinalIgnoreCase);
        foreach (var g in finals.Select((f, i) => (f, i))
                                .Where(x => x.f.Length > 0)
                                .GroupBy(x => x.f, StringComparer.OrdinalIgnoreCase)
                                .Where(g => g.Count() > 1))
        {
            used.Remove(g.Key); // 组内空后缀行将全部改名，释放基名
            var n = 1;
            foreach (var (_, idx) in g)
            {
                if (_rows[idx].Suffix.Length > 0) continue;
                // 找到能让最终名唯一的最小序号
                string suffix, candidate;
                do
                {
                    suffix = n.ToString();
                    n++;
                    candidate = BuildRowFinal(parts, idx, suffix, keepNum);
                } while (used.Contains(candidate));
                _rows[idx].Suffix = suffix; // Suffix 变更经 PropertyChanged 触发 RecomputeNames
                used.Add(candidate);
            }
        }
    }

    private void TokenAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        switch (key)
        {
            case "sep": _tokens.Add(MakeSepToken()); break;
            case "num": _tokens.Add(MakeNumToken()); break;
            case "suf": _tokens.Add(MakeSufToken()); break;
        }
        RecomputeNames();
    }

    private void TokenReset_Click(object sender, RoutedEventArgs e)
    {
        // 重置：弱相似与自定义块全部移除，公共标签恢复激活，回到默认组合
        foreach (var t in _tags) t.Active = !t.Weak && _orderedCommon.Any(
            c => string.Equals(c, t.Text, StringComparison.OrdinalIgnoreCase));
        CustomTextBox.Text = "";
        BuildDefaultTokens();
        RecomputeNames();
    }

    private void TokenRemove_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBlock { DataContext: TokenVm token }) return;
        if (ReferenceEquals(FindOwnerListBox((DependencyObject)sender), FileTokenList))
        {
            _fileTokens.Remove((FileTokenVm)token);
            UpdateFilePreview();
        }
        else
        {
            if (token.SourceTag != null) token.SourceTag.Active = false;
            _tokens.Remove(token);
            RecomputeNames();
        }
    }

    // ── token 拖拽排序（相册名组合 / 文件名组合共用）──
    private TokenDragState DragOf(ListBox list) =>
        ReferenceEquals(list, FileTokenList) ? _fileDrag : _batchDrag;

    private void TokenDrag_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        var state = DragOf(list);
        // 点在 × 删除按钮或右键上时不启动拖拽
        if (e.RightButton == MouseButtonState.Pressed) { state.Index = -1; return; }
        if (e.OriginalSource is TextBlock tb && tb.Text == "×") { state.Index = -1; return; }
        state.Start = e.GetPosition(null);
        state.Index = FindTokenIndex(list, e.OriginalSource);
    }

    private void TokenDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox list) return;
        var state = DragOf(list);
        if (state.Index < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - state.Start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - state.Start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var idx = state.Index;
        state.Index = -1;
        var count = ReferenceEquals(list, FileTokenList) ? _fileTokens.Count : _tokens.Count;
        if (idx >= 0 && idx < count)
        {
            var fmt = ReferenceEquals(list, FileTokenList) ? "fileTokenIdx" : "tokenIdx";
            DragDrop.DoDragDrop(list, new DataObject(fmt, idx), DragDropEffects.Move);
        }
    }

    private void TokenDrag_DragOver(object sender, DragEventArgs e)
    {
        var fmt = ReferenceEquals(sender, FileTokenList) ? "fileTokenIdx" : "tokenIdx";
        e.Effects = e.Data.GetDataPresent(fmt) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void TokenDrag_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list) return;
        var fmt = ReferenceEquals(list, FileTokenList) ? "fileTokenIdx" : "tokenIdx";
        if (e.Data.GetData(fmt) is not int from) return;
        var to = FindTokenIndex(list, e.OriginalSource);

        if (ReferenceEquals(list, FileTokenList))
        {
            if (from < 0 || from >= _fileTokens.Count || to < 0 || to == from) return;
            var item = _fileTokens[from];
            _fileTokens.RemoveAt(from);
            _fileTokens.Insert(to, item);
            UpdateFilePreview();
        }
        else
        {
            if (from < 0 || from >= _tokens.Count || to < 0 || to == from) return;
            var item = _tokens[from];
            _tokens.RemoveAt(from);
            _tokens.Insert(to, item);
            RecomputeNames();
        }
    }

    private static int FindTokenIndex(ItemsControl list, object source)
    {
        if (source is not DependencyObject d) return -1;
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return d is ListBoxItem lbi ? list.ItemContainerGenerator.IndexFromContainer(lbi) : -1;
    }

    /// <summary>从子元素沿可视化树找到所属 ListBox</summary>
    private static ListBox? FindOwnerListBox(DependencyObject d)
    {
        while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return d is ListBoxItem lbi ? ItemsControl.ItemsControlFromItemContainer(lbi) as ListBox : null;
    }

    // ═══════ 同步重命名相册内文件名 ═══════
    private static readonly Brush DirBg = new SolidColorBrush(Color.FromRgb(0xF3, 0xE8, 0xFF));
    private static readonly Brush DirBd = new SolidColorBrush(Color.FromRgb(0xD9, 0xC2, 0xFF));
    private static readonly Brush DirFg = new SolidColorBrush(Color.FromRgb(0x7A, 0x3F, 0xD0));

    private void RenameFiles_Changed(object sender, RoutedEventArgs e)
    {
        var on = RenameFilesCheck.IsChecked == true;
        FileRenamePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on && _fileTokens.Count == 0)
        {
            BuildDefaultFileTokens();
            UpdateFilePreview();
        }
    }

    /// <summary>默认组合：相册后缀 + 分文件夹 + 分隔符(_) + 编号(####)</summary>
    private void BuildDefaultFileTokens()
    {
        _fileTokens.Clear();
        _fileTokens.Add(new FileTokenVm { Key = "suf", Label = "相册后缀", Bg = SufBg, Bd = SufBd, Fg = OkBrush });
        _fileTokens.Add(new FileTokenVm { Key = "dir", Label = "分文件夹", Bg = DirBg, Bd = DirBd, Fg = DirFg });
        _fileTokens.Add(MakeFileSepToken("_"));
        _fileTokens.Add(MakeFileNumToken(4));
    }

    private static string SepDisplay(string sep) => sep == " " ? "空格" : sep;

    private static FileTokenVm MakeFileSepToken(string sep) => new()
    {
        Key = "sep", SepValue = sep, Label = $"分隔符「{SepDisplay(sep)}」",
        Bg = SepBg, Bd = Brushes.LightGray, Fg = Brushes.DimGray
    };

    private static FileTokenVm MakeFileNumToken(int width) => new()
    {
        Key = "num", NumWidth = width, Label = $"编号 {new string('#', width)}",
        Bg = NumBg, Bd = NumBd, Fg = NumFg
    };

    /// <summary>模板预览：文本实际内容，相册后缀→***，分文件夹→子目录，编号→# 序列</summary>
    private void UpdateFilePreview()
    {
        if (FilePatternPreviewRun == null) return;
        var parts = _fileTokens.Select<TokenVm, NameSimilarity.NamePart>(t => t.Key switch
        {
            "text" => NameSimilarity.TextPart(t.Display),
            "sep" => NameSimilarity.SepPart(((FileTokenVm)t).SepValue),
            "dir" => NameSimilarity.TextPart("子目录"),
            "num" => NameSimilarity.TextPart(new string('#', ((FileTokenVm)t).NumWidth)),
            _ => NameSimilarity.TextPart("***")
        }).ToList();
        var pattern = NameSimilarity.BuildName(parts, "", "", false);
        FilePatternPreviewRun.Text = string.IsNullOrEmpty(pattern) ? "（组合为空）" : pattern + ".jpg";
    }

    private void FileTokenAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        switch (key)
        {
            case "suf":
                _fileTokens.Add(new FileTokenVm { Key = "suf", Label = "相册后缀", Bg = SufBg, Bd = SufBd, Fg = OkBrush });
                break;
            case "dir":
                _fileTokens.Add(new FileTokenVm { Key = "dir", Label = "分文件夹", Bg = DirBg, Bd = DirBd, Fg = DirFg });
                break;
            case "sep":
                _fileTokens.Add(MakeFileSepToken("_"));
                break;
            case "num":
                _fileTokens.Add(MakeFileNumToken(4));
                break;
            case "text":
                var text = ModernDialog.Input(this, "输入自定义文本：", "添加文本块", "");
                if (!string.IsNullOrWhiteSpace(text))
                    _fileTokens.Add(new FileTokenVm
                    {
                        Key = "text", CustomText = text.Trim(),
                        Bg = TextBg, Bd = TextBd, Fg = TextFg
                    });
                break;
        }
        UpdateFilePreview();
    }

    private void FileTokenReset_Click(object sender, RoutedEventArgs e)
    {
        BuildDefaultFileTokens();
        UpdateFilePreview();
    }

    /// <summary>编号 token 双击：循环 4→3→2→1 位</summary>
    private void FileToken_LeftDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (sender is not FrameworkElement { DataContext: FileTokenVm { Key: "num" } token }) return;
        e.Handled = true;
        SetNumWidth(token, token.NumWidth switch { 4 => 3, 3 => 2, 2 => 1, _ => 4 });
    }

    /// <summary>分隔符 / 编号 token 右键菜单</summary>
    private void FileToken_RightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FileTokenVm token) return;
        if (token.Key is not ("sep" or "num")) return;
        e.Handled = true;
        if (token.Key == "sep") ShowFileSepMenu(fe, token);
        else ShowFileNumMenu(fe, token);
    }

    private void SetFileSep(FileTokenVm token, string sep)
    {
        token.SepValue = sep;
        token.Label = $"分隔符「{SepDisplay(sep)}」";
        UpdateFilePreview();
    }

    private void SetNumWidth(FileTokenVm token, int width)
    {
        token.NumWidth = width;
        token.Label = $"编号 {new string('#', width)}";
        UpdateFilePreview();
    }

    private void ShowFileSepMenu(UIElement target, FileTokenVm token)
    {
        var menu = new ContextMenu();
        foreach (var (label, val) in new[] { ("_", "_"), ("-", "-"), ("+", "+"), ("空格", " "), ("·", "·") })
        {
            var mi = new MenuItem { Header = $"「{label}」", IsCheckable = true, IsChecked = token.SepValue == val };
            mi.Click += (s, _) => SetFileSep(token, val);
            menu.Items.Add(mi);
        }
        var custom = new MenuItem { Header = "自定…" };
        custom.Click += (s, _) =>
        {
            var input = ModernDialog.Input(this, "输入分隔符（最多 3 个字符）：", "自定义分隔符", token.SepValue);
            if (!string.IsNullOrEmpty(input))
            {
                var v = input.Trim();
                SetFileSep(token, v.Length > 3 ? v[..3] : v);
            }
        };
        menu.Items.Add(custom);
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void ShowFileNumMenu(UIElement target, FileTokenVm token)
    {
        var menu = new ContextMenu();
        foreach (var w in new[] { 4, 3, 2, 1 })
        {
            var width = w;
            var mi = new MenuItem
            {
                Header = $"{new string('#', w)}（{w} 位）",
                IsCheckable = true,
                IsChecked = token.NumWidth == w
            };
            mi.Click += (s, _) => SetNumWidth(token, width);
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateUi();
    }

    private void UpdateUi()
    {
        if (AddOnlyCheck == null || CreatorList == null) return;
        var addOnly = AddOnlyCheck.IsChecked == true;
        // 仅添加模式下创作者仍可选：只作为数据库索引标签，不移动文件
        CreatorHeader.Text = addOnly
            ? "归类到创作者（可选，仅建立索引标签，不移动文件）"
            : "整理到创作者（不选择则归入「无创作者」目录）";

        // 外部相册只读：同步重命名仅「整理入库」模式可用
        if (RenameFilesCheck != null)
        {
            RenameFilesCheck.IsEnabled = !addOnly;
            RenameFilesModeHint.Opacity = addOnly ? 1 : 0.6;
            if (addOnly) RenameFilesCheck.IsChecked = false;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        // 校验相册名：非空、无非法字符、不重复
        var invalid = Path.GetInvalidFileNameChars();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            item.AlbumName = item.AlbumName.Trim();
            if (string.IsNullOrEmpty(item.AlbumName))
            {
                ModernDialog.Info(this, $"「{item.SourceName}」的相册名不能为空", "提示");
                return;
            }
            if (item.AlbumName.Any(c => invalid.Contains(c)))
            {
                ModernDialog.Info(this, $"相册名「{item.AlbumName}」包含非法字符", "提示");
                return;
            }
            if (!names.Add(item.AlbumName))
            {
                ModernDialog.Info(this, $"相册名「{item.AlbumName}」重复，请修改", "提示");
                return;
            }
        }

        // 新建创作者（任何时候都可以）：同名已存在则直接使用
        var newName = NewCreatorTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            var existing = _viewModel.Creators.FirstOrDefault(
                c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SelectedCreator = existing;
            }
            else
            {
                var catId = NewCreatorCategoryCombo.SelectedItem is ComboBoxItem ci && ci.Tag is int id ? id : 0;
                _viewModel.AddCreator(newName, catId);
                SelectedCreator = _viewModel.Creators.FirstOrDefault(
                    c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            SelectedCreator = CreatorList.SelectedItem as Creator;
        }

        AddOnly = AddOnlyCheck.IsChecked == true;

        // 同步重命名相册内文件名：把组合片段与每行后缀写入各导入项
        if (!AddOnly && RenameFilesCheck.IsChecked == true && _fileTokens.Count > 0)
        {
            NameSimilarity.FilePart ToPart(FileTokenVm t) => t.Key switch
            {
                "text" => new(NameSimilarity.FilePartKind.Text, t.CustomText),
                "sep" => new(NameSimilarity.FilePartKind.Sep, t.SepValue),
                "dir" => new(NameSimilarity.FilePartKind.SubDir),
                "num" => new(NameSimilarity.FilePartKind.Number, "", t.NumWidth),
                _ => new(NameSimilarity.FilePartKind.AlbumSuffix)
            };
            var parts = _fileTokens.Select(ToPart).ToList();

            for (var i = 0; i < Items.Count; i++)
            {
                Items[i].RenameFileEnabled = true;
                Items[i].RenameFileParts = parts;
                // 后缀：批量命名行后缀；为空时回退完整相册名；单文件夹直接用相册名
                Items[i].RenameAlbumSuffix = _batchReady && i < _rows.Count
                    ? (_rows[i].Suffix.Length > 0 ? _rows[i].Suffix : Items[i].AlbumName)
                    : Items[i].AlbumName;
            }
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
    }
}
