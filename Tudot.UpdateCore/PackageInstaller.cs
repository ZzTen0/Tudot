using System.IO.Compression;

namespace Tudot.Updates;

public static class PackageInstaller
{
    public static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public static BuildInfo ValidateInstall(string directory)
    {
        directory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (SamePath(directory, Path.GetPathRoot(directory)!))
            throw new InvalidDataException("A filesystem root cannot be an application installation.");
        // 不允许更新程序穿过联接/符号链接，避免将未知目录作为安装目标。
        for (var ancestor = new DirectoryInfo(directory); ancestor != null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not supported for the application installation.");
        var info = UpdateJson.Read<BuildInfo>(Path.Combine(directory, "build.json"));
        var marker = UpdateJson.Read<BuildInfo>(Path.Combine(directory, "tudot-install.json"));
        if (info.AppId != "Tudot" || marker.AppId != "Tudot" || info.BuildId <= 0 || marker.BuildId != info.BuildId ||
            !File.Exists(Path.Combine(directory, "Tudot.exe")) || !File.Exists(Path.Combine(directory, "Tudot.Updater.exe")))
            throw new InvalidDataException("The target is not a complete, marked Tudot installation.");
        return info;
    }

    public static string Prepare(string installDirectory, string zipPath, UpdateManifest manifest)
    {
        var current = ValidateInstall(installDirectory);
        manifest.VerifyPackage(zipPath);
        if (manifest.BuildId <= current.BuildId)
            throw new InvalidDataException("Only a newer build can be installed.");
        var full = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = full + ".pending." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(candidate);
        var root = Path.GetFullPath(candidate) + Path.DirectorySeparatorChar;
        long extracted = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 2048) throw new InvalidDataException("Too many package entries.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(name) || name.Contains(':') || name.Split('/').Any(s => s is "." or "..") ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Unsafe path or symlink in update package.");
            var output = Path.GetFullPath(Path.Combine(candidate, name));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A package entry escapes its destination.");
            if (name.EndsWith('/')) { Directory.CreateDirectory(output); continue; }
            if (!seen.Add(output)) throw new InvalidDataException("Duplicate package entry.");
            extracted += entry.Length;
            if (extracted > 1024L * 1024 * 1024) throw new InvalidDataException("The extracted package is too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = entry.Open();
            using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(target);
        }
        var incoming = ValidateInstall(candidate);
        if (incoming.BuildId != manifest.BuildId)
            throw new InvalidDataException("The package build does not match the feed.");
        return candidate;
    }

    public static InstallTransaction Activate(string installDirectory, string candidate)
    {
        ValidateInstall(installDirectory);
        ValidateInstall(candidate);
        var full = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!Path.GetFullPath(candidate).StartsWith(full + ".pending.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate is not a prepared sibling of the installation.");
        var backup = full + ".backup." + Guid.NewGuid().ToString("N");
        Directory.Move(full, backup);
        try
        {
            Directory.Move(candidate, full);
            var previousCache = Path.Combine(backup, "Thumbs");
            if (Directory.Exists(previousCache) && !Directory.Exists(Path.Combine(full, "Thumbs")) &&
                (File.GetAttributes(previousCache) & FileAttributes.ReparsePoint) == 0)
                Directory.Move(previousCache, Path.Combine(full, "Thumbs"));
            return new InstallTransaction(full, backup);
        }
        catch
        {
            if (Directory.Exists(full))
                Directory.Move(full, full + ".failed." + Guid.NewGuid().ToString("N"));
            Directory.Move(backup, full);
            throw;
        }
    }
}

public sealed class InstallTransaction
{
    public string InstallDirectory { get; }
    public string BackupDirectory { get; }
    private bool _rolledBack;

    internal InstallTransaction(string installDirectory, string backupDirectory)
        => (InstallDirectory, BackupDirectory) = (installDirectory, backupDirectory);

    public void Rollback()
    {
        if (_rolledBack) return;
        var failed = InstallDirectory + ".failed." + Guid.NewGuid().ToString("N");
        var cache = Path.Combine(InstallDirectory, "Thumbs");
        if (Directory.Exists(cache) && !Directory.Exists(Path.Combine(BackupDirectory, "Thumbs")) &&
            (File.GetAttributes(cache) & FileAttributes.ReparsePoint) == 0)
            Directory.Move(cache, Path.Combine(BackupDirectory, "Thumbs"));
        Directory.Move(InstallDirectory, failed);
        Directory.Move(BackupDirectory, InstallDirectory);
        _rolledBack = true;
    }
}
