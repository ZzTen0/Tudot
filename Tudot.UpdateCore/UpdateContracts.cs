using System.Security.Cryptography;
using System.Text.Json;

namespace Tudot.Updates;

public sealed record BuildInfo
{
    /// <summary>可选语义版本，由正式发布标签生成；旧包和本地开发构建可为空。</summary>
    public string Version { get; init; } = "";
    public string AppId { get; init; } = "";
    public long BuildId { get; init; }
    public DateTime PublishedUtc { get; init; }
}

public sealed record UpdateManifest
{
    public string AppId { get; init; } = "";
    public long BuildId { get; init; }
    public DateTime PublishedUtc { get; init; }
    public string PackageFile { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long PackageSize { get; init; }
    public string Notes { get; init; } = "";

    public void Validate()
    {
        if (AppId != "Tudot" || BuildId <= 0 || PackageSize <= 0 || PackageSize > 512L * 1024 * 1024)
            throw new InvalidDataException("Invalid Tudot update manifest.");
        if (string.IsNullOrWhiteSpace(PackageFile) || PackageFile.IndexOfAny(['/', '\\', ':']) >= 0 ||
            Path.GetFileName(PackageFile) != PackageFile || !PackageFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package must be a ZIP file directly inside the update source.");
        if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Missing or invalid SHA-256.");
    }

    public void VerifyPackage(string path)
    {
        Validate();
        using var stream = File.OpenRead(path);
        if (stream.Length != PackageSize || !Convert.ToHexString(SHA256.HashData(stream)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update package checksum or length does not match.");
    }
}

public sealed record UpdateSource
{
    public string SourceDirectory { get; init; } = "";
}

public sealed record UpdatePlan
{
    public string InstallDirectory { get; init; } = "";
    public string PackagePath { get; init; } = "";
    public UpdateManifest Manifest { get; init; } = new();
    public int ParentProcessId { get; init; }
    public long ParentStartTimeUtcTicks { get; init; }
    public string AckPath { get; init; } = "";
}

public sealed record UpdateAck
{
    public long BuildId { get; init; }
    public int ProcessId { get; init; }
}

public static class UpdateJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Empty update metadata.");
    public static void Write<T>(string path, T value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
