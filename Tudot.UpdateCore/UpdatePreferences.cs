using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tudot.Updates;

/// <summary>自动检查使用的来源；首次运行和旧配置默认使用官方 GitHub Release。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UpdateSourceKind>))]
public enum UpdateSourceKind
{
    /// <summary>官方 ZzTen0/Tudot 仓库的最新正式 Release。</summary>
    GitHub,
    /// <summary>用户指定的本地发布目录，留空兼容安装目录中的旧来源配置。</summary>
    Local
}

/// <summary>用户选择的更新策略；配置以枚举名称保存，不能依赖数值顺序。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UpdateMode>))]
public enum UpdateMode
{
    /// <summary>发现更新后准备更新包，在没有打开子窗口时重启更新。</summary>
    Automatic,
    /// <summary>默认策略：准备更新包后提醒，由用户确认重启。</summary>
    Notify,
    /// <summary>不自动检查来源或准备更新；用户主动导入离线包不受此开关限制。</summary>
    Disabled
}

/// <summary>安装目录之外的用户偏好，更新和回滚程序文件时均保留。</summary>
public sealed record UpdatePreferences
{
    /// <summary>默认提醒；缺失、损坏或无法识别的配置不得开启自动重启。</summary>
    public UpdateMode Mode { get; init; } = UpdateMode.Notify;

    /// <summary>更新来源，默认 GitHub；不会因网络失败自动切换到其它来源。</summary>
    public UpdateSourceKind Source { get; init; } = UpdateSourceKind.GitHub;

    /// <summary>本地来源目录，仅 Source=Local 时使用；空值沿用旧 update-source.json。</summary>
    public string LocalDirectory { get; init; } = "";

    /// <summary>日常配置位置，独立于安装包及本地源码目录。</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tudot", "update-preferences.json");

    /// <summary>只读加载偏好；不可读取或无效时回退提醒模式，不覆盖原文件。</summary>
    public static UpdatePreferences Load(string? path = null)
    {
        try
        {
            var value = UpdateJson.Read<UpdatePreferences>(path ?? DefaultPath);
            return Enum.IsDefined(value.Mode) && Enum.IsDefined(value.Source) && value.LocalDirectory != null ? value : new();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    /// <summary>原子保存有效偏好；写入失败向调用方报告，不静默忽略。</summary>
    public void Save(string? path = null)
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (!Enum.IsDefined(Source) || LocalDirectory == null) throw new ArgumentException("无效更新来源。");
        var target = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        UpdateJson.Write(target, this);
    }
}
