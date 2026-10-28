namespace Tudot.Updates;

/// <summary>当前只读本地目录；检查、复制和校验均由调用方放到后台执行。</summary>
public static class LocalUpdateFeed
{
    public static UpdateManifest? Check(string installationDirectory, string? sourceDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) && !File.Exists(Path.Combine(installationDirectory, "update-source.json"))) return null;
        var installed = PackageInstaller.ValidateInstall(installationDirectory);
        var source = string.IsNullOrWhiteSpace(sourceDirectory) ? ReadSource(installationDirectory) : Path.GetFullPath(sourceDirectory);
        var latest = Path.Combine(source, "latest.json");
        if (!File.Exists(latest)) return null;
        var manifest = UpdateJson.Read<UpdateManifest>(latest);
        manifest.Validate();
        return manifest.BuildId > installed.BuildId ? manifest : null;
    }

    public static string Stage(string installationDirectory, UpdateManifest manifest, string stagingRoot, string? sourceDirectory = null)
    {
        manifest.Validate();
        var source = string.IsNullOrWhiteSpace(sourceDirectory) ? ReadSource(installationDirectory) : Path.GetFullPath(sourceDirectory);
        var cache = Path.Combine(stagingRoot, manifest.BuildId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(cache);
        var package = Path.Combine(cache, "package.zip");
        if (File.Exists(package))
        {
            try { manifest.VerifyPackage(package); return package; }
            catch (InvalidDataException) { }
        }
        var incoming = package + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(Path.Combine(source, manifest.PackageFile), incoming);
        manifest.VerifyPackage(incoming);
        File.Move(incoming, package, overwrite: true);
        return package;
    }

    private static string ReadSource(string installationDirectory)
    {
        var source = UpdateJson.Read<UpdateSource>(Path.Combine(installationDirectory, "update-source.json"));
        if (string.IsNullOrWhiteSpace(source.SourceDirectory))
            throw new InvalidDataException("No local update source directory is configured.");
        return Path.GetFullPath(source.SourceDirectory);
    }
}
