using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Tudot.Updates;

/// <summary>一次检查的不可变结果，绑定来源快照，避免用户切换来源后下载错误的同号包。</summary>
public sealed record UpdateCandidate(UpdateManifest Manifest, UpdatePreferences Preferences, Uri? BundleUrl = null);

/// <summary>统一官方 GitHub 和本地来源；只读取正式 Release，不回退到旧版不兼容资产。</summary>
public static class UpdateFeed
{
    public const string Repository = "ZzTen0/Tudot";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>检查比当前安装更新的兼容构建；禁用时不访问任何来源，404 或尚无协议资产时返回空。</summary>
    /// <param name="http">仅供隔离 HTTP 测试注入；生产使用限制重定向的共享客户端。</param>
    public static async Task<UpdateCandidate?> CheckAsync(string installDirectory, UpdatePreferences preferences,
        CancellationToken cancellationToken = default, HttpClient? http = null)
    {
        if (preferences.Mode == UpdateMode.Disabled) return null;
        if (preferences.Source == UpdateSourceKind.Local)
        {
            var local = await Task.Run(() => LocalUpdateFeed.Check(installDirectory, preferences.LocalDirectory), cancellationToken);
            return local == null ? null : new(local, preferences);
        }
        var installed = PackageInstaller.ValidateInstall(installDirectory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await GetAsync(http ?? Client, new Uri($"https://api.github.com/repos/{Repository}/releases/latest"), timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await ReadLimitedAsync(response, 2 * 1024 * 1024, timeout.Token));
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var metadata = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "latest.json");
        if (metadata.ValueKind == JsonValueKind.Undefined) return null;
        using var manifestResponse = await GetAsync(http ?? Client, AssetUrl(metadata), timeout.Token);
        manifestResponse.EnsureSuccessStatusCode();
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(await ReadLimitedAsync(manifestResponse, 64 * 1024, timeout.Token), UpdateJson.Options)
            ?? throw new InvalidDataException("官方更新清单为空。");
        manifest.Validate();
        if (manifest.BuildId <= installed.BuildId) return null;
        var bundle = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == $"Tudot-{manifest.BuildId}-update.tudotupdate");
        if (bundle.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Release 缺少对应更新包，请稍后重试。");
        return new(manifest, preferences, AssetUrl(bundle));
    }

    /// <summary>后台准备程序包；在线内容必须匹配清单 SHA-256，失败或取消不进入安装。</summary>
    public static async Task<string> StageAsync(string installDirectory, UpdateCandidate candidate, string stagingRoot,
        CancellationToken cancellationToken = default, HttpClient? http = null)
    {
        if (candidate.Preferences.Source == UpdateSourceKind.Local)
            return await Task.Run(() => LocalUpdateFeed.Stage(installDirectory, candidate.Manifest, stagingRoot, candidate.Preferences.LocalDirectory), cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        cancellationToken = deadline.Token;
        var directory = Path.Combine(stagingRoot, candidate.Manifest.BuildId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var cached = Path.Combine(directory, "package.zip");
        if (File.Exists(cached))
        {
            try { await Task.Run(() => candidate.Manifest.VerifyPackage(cached), cancellationToken); return cached; }
            catch (InvalidDataException) { }
        }
        var incoming = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using var response = await GetAsync(http ?? Client, candidate.BundleUrl ?? throw new InvalidDataException("缺少下载地址。"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var output = File.Create(incoming))
            {
                var buffer = new byte[81920];
                long length = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    length += read;
                    if (length > 520L * 1024 * 1024) throw new InvalidDataException("下载的更新包过大。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            return (await Task.Run(() => OfflineUpdatePackage.Stage(incoming, installDirectory, stagingRoot, candidate.Manifest), cancellationToken)).PackagePath;
        }
        finally { if (File.Exists(incoming)) File.Delete(incoming); }
    }

    /// <summary>只接受指定官方仓库的 Release 资产地址，不能由清单指定任意服务器。</summary>
    private static Uri AssetUrl(JsonElement asset)
    {
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        if (!uri.AbsoluteUri.StartsWith($"https://github.com/{Repository}/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("更新资产不是官方仓库的 Release 地址。");
        return uri;
    }

    /// <summary>逐跳检查 HTTPS 和 GitHub 官方下载域名，不向第三方地址发送请求。</summary>
    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri uri, CancellationToken token)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            if (uri.Scheme != "https" || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("不安全的更新下载地址。");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Tudot-Updater/1.0");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }
            return response;
        }
        throw new InvalidDataException("更新下载重定向次数过多。");
    }

    /// <summary>限制元数据实际读取量，不信任响应头宣称的长度。</summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("更新元数据超过大小限制。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
