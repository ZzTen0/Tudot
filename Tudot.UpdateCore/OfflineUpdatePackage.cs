using System.IO.Compression;

namespace Tudot.Updates;

/// <summary>离线更新包：ZIP 容器只允许 manifest.json 和 payload.zip；旧安装程序不支持此格式。</summary>
public static class OfflineUpdatePackage
{
    /// <summary>校验并暂存下载包；离线来源由用户确认信任，在线来源必须匹配来自官方 Release 的清单。</summary>
    /// <returns>供独立更新器再次校验的内层程序包路径和清单。</returns>
    public static (string PackagePath, UpdateManifest Manifest) Stage(string file, string installDirectory,
        string stagingRoot, UpdateManifest? expected = null)
    {
        if (new FileInfo(file).Length > 520L * 1024 * 1024) throw new InvalidDataException("更新包超过大小限制。");
        using var archive = ZipFile.OpenRead(file);
        if (archive.Entries.Count != 2 || archive.Entries.Count(e => e.FullName == "manifest.json") != 1 ||
            archive.Entries.Count(e => e.FullName == "payload.zip") != 1)
            throw new InvalidDataException("不是受支持的 Tudot 更新包，请选择 .tudotupdate 文件。");
        var metadata = archive.GetEntry("manifest.json")!;
        if (metadata.Length > 64 * 1024) throw new InvalidDataException("更新清单过大。");
        using var input = metadata.Open();
        using var metadataBytes = new MemoryStream();
        CopyBounded(input, metadataBytes, 64 * 1024);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<UpdateManifest>(metadataBytes.ToArray(), UpdateJson.Options)
            ?? throw new InvalidDataException("更新清单为空。");
        manifest.Validate();
        if (expected != null && manifest != expected) throw new InvalidDataException("更新包与官方发布清单不一致。");
        var current = PackageInstaller.ValidateInstall(installDirectory);
        if (manifest.BuildId <= current.BuildId) throw new InvalidDataException("只能安装更新的构建，不能降级或重复更新。");
        var payload = archive.GetEntry("payload.zip")!;
        if (payload.Length != manifest.PackageSize) throw new InvalidDataException("程序包大小不匹配。");
        var directory = Path.Combine(stagingRoot, manifest.BuildId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "package.zip");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var source = payload.Open())
            using (var output = File.Create(temporary)) CopyBounded(source, output, manifest.PackageSize);
            manifest.VerifyPackage(temporary);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return (target, manifest);
    }

    /// <summary>按实际解压字节数限制输出，不信任 ZIP 中声明的长度；流由调用方释放。</summary>
    private static void CopyBounded(Stream source, Stream destination, long maximum)
    {
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            length += read;
            if (length > maximum) throw new InvalidDataException("更新包实际内容超过大小限制。");
            destination.Write(buffer, 0, read);
        }
    }
}
