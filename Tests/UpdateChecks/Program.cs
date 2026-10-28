using System.IO.Compression;
using System.Security.Cryptography;
using Tudot.Updates;
using System.Net;
using System.Text.Json;

// 进程级烟测入口：用生产代码暂存发布脚本实际生成的离线包，再交给真实独立更新器。
if (args.Length == 4 && args[0] == "--stage-offline")
{
    var imported = OfflineUpdatePackage.Stage(args[1], args[2], args[3]);
    imported.Manifest.VerifyPackage(imported.PackagePath);
    Console.WriteLine("PASS: Production offline update bundle staged and verified");
    return;
}

var root = Path.Combine(Path.GetTempPath(), "TudotUpdateChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
void Assert(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or IOException) { Assert(true, name); return; }
    throw new InvalidOperationException("Expected rejection: " + name);
}
void MakeInstall(string directory, long id)
{
    Directory.CreateDirectory(directory);
    var info = new BuildInfo { AppId = "Tudot", BuildId = id, PublishedUtc = DateTime.UtcNow };
    UpdateJson.Write(Path.Combine(directory, "build.json"), info);
    UpdateJson.Write(Path.Combine(directory, "tudot-install.json"), info);
    File.WriteAllText(Path.Combine(directory, "Tudot.exe"), "test-exe-" + id);
    File.WriteAllText(Path.Combine(directory, "Tudot.Updater.exe"), "test-updater");
}
UpdateManifest Manifest(string zip, long id) => new()
{
    AppId = "Tudot", BuildId = id, PackageFile = Path.GetFileName(zip), PublishedUtc = DateTime.UtcNow,
    PackageSize = new FileInfo(zip).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)))
};

var install = Path.Combine(root, "installed", "Tudot");
MakeInstall(install, 100);
Directory.CreateDirectory(Path.Combine(install, "Thumbs"));
File.WriteAllText(Path.Combine(install, "Thumbs", "cached.jpg"), "cache");
var userDatabase = Path.Combine(root, "user-data", "albums.db");
Directory.CreateDirectory(Path.GetDirectoryName(userDatabase)!);
File.WriteAllText(userDatabase, "untouched-database");

var payload = Path.Combine(root, "payload");
MakeInstall(payload, 200);
var package = Path.Combine(root, "new.zip");
ZipFile.CreateFromDirectory(payload, package);
var manifest = Manifest(package, 200);
manifest.VerifyPackage(package);
Assert(true, "正常包的哈希和长度验证通过");
Reject(() => (manifest with { Sha256 = new string('0', 64) }).VerifyPackage(package), "损坏/错误哈希被拒绝");
Reject(() => (manifest with { PackageFile = "../outside.zip" }).Validate(), "更新清单不允许路径穿越");
Reject(() => (manifest with { AppId = "Other" }).Validate(), "拒绝其它应用的更新清单");
Reject(() => PackageInstaller.Prepare(install, package, manifest with { BuildId = 100 }), "拒绝降级或重复安装");
Reject(() => PackageInstaller.Prepare(install, package, manifest with { BuildId = 201 }), "拒绝包内部构建号与清单不一致");
Assert(PackageInstaller.ValidateInstall(install).BuildId == 100, "预检失败不改变现有安装");

var unsafeZip = Path.Combine(root, "unsafe.zip");
using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
{
    using var writer = new StreamWriter(archive.CreateEntry("../escaped.txt").Open());
    writer.Write("should not escape");
}
Reject(() => PackageInstaller.Prepare(install, unsafeZip, Manifest(unsafeZip, 200)), "拒绝 ZIP 路径穿越");
Assert(!File.Exists(Path.Combine(root, "installed", "escaped.txt")), "恶意包没有写出目标目录");

var candidate = PackageInstaller.Prepare(install, package, manifest);
var transaction = PackageInstaller.Activate(install, candidate);
Assert(PackageInstaller.ValidateInstall(install).BuildId == 200, "完整新包成功激活");
Assert(PackageInstaller.ValidateInstall(transaction.BackupDirectory).BuildId == 100, "原程序完整备份");
Assert(File.Exists(Path.Combine(install, "Thumbs", "cached.jpg")), "安装更新保留默认缩略图缓存");
transaction.Rollback();
transaction.Rollback();
Assert(PackageInstaller.ValidateInstall(install).BuildId == 100, "启动失败可回滚且回滚操作幂等");
Assert(File.Exists(Path.Combine(install, "Thumbs", "cached.jpg")), "回滚保留缩略图缓存");
Assert(File.ReadAllText(userDatabase) == "untouched-database", "安装和回滚不触碰独立用户数据库");

var unmarked = Path.Combine(root, "unmarked");
Directory.CreateDirectory(unmarked);
Reject(() => PackageInstaller.Prepare(unmarked, package, manifest), "拒绝无安装标记的目录");
var roundTripPath = Path.Combine(root, "manifest.json");
UpdateJson.Write(roundTripPath, manifest);
Assert(UpdateJson.Read<UpdateManifest>(roundTripPath) == manifest, "更新协议 JSON 跨组件往返一致");
Assert(LocalUpdateFeed.Check(install) == null, "未配置更新源时安静跳过");
var feed = Path.Combine(root, "feed");
Directory.CreateDirectory(feed);
UpdateJson.Write(Path.Combine(install, "update-source.json"), new UpdateSource { SourceDirectory = feed });
Assert(LocalUpdateFeed.Check(install) == null, "本地源尚无清单时不影响启动");
UpdateJson.Write(Path.Combine(feed, "latest.json"), manifest);
File.Copy(package, Path.Combine(feed, manifest.PackageFile));
Assert(LocalUpdateFeed.Check(install)!.BuildId == 200, "能够从本地目录发现新构建");
var stagingRoot = Path.Combine(root, "staging");
var staged = LocalUpdateFeed.Stage(install, manifest, stagingRoot);
manifest.VerifyPackage(staged);
Assert(PackageInstaller.ValidateInstall(install).BuildId == 100, "准备更新包不提前替换正在使用的程序");
var written = File.GetLastWriteTimeUtc(staged);
Assert(LocalUpdateFeed.Stage(install, manifest, stagingRoot) == staged && File.GetLastWriteTimeUtc(staged) == written, "已缓存且校验通过的包不重复复制");
File.WriteAllText(staged, "broken cache");
LocalUpdateFeed.Stage(install, manifest, stagingRoot);
manifest.VerifyPackage(staged);
Assert(true, "缓存包损坏后可从本地源重新准备");
UpdateJson.Write(Path.Combine(feed, "latest.json"), manifest with { BuildId = 100 });
Assert(LocalUpdateFeed.Check(install) == null, "相同或旧构建不反复提示更新");
// 偏好保存在独立用户目录，覆盖默认值、三种模式和损坏配置的安全回退。
var preferencesPath = Path.Combine(root, "preferences", "update-preferences.json");
Assert(UpdatePreferences.Load(preferencesPath).Mode == UpdateMode.Notify, "首次启动默认提醒更新");
Assert(!File.Exists(preferencesPath), "读取默认值不创建配置文件");
foreach (var mode in Enum.GetValues<UpdateMode>())
{
    new UpdatePreferences { Mode = mode }.Save(preferencesPath);
    Assert(UpdatePreferences.Load(preferencesPath).Mode == mode, "更新模式持久化：" + mode);
}
foreach (var invalid in new[] { "broken", "null", "{\"mode\":\"FutureMode\"}", "{\"mode\":999}" })
{
    File.WriteAllText(preferencesPath, invalid);
    Assert(UpdatePreferences.Load(preferencesPath).Mode == UpdateMode.Notify, "无效偏好回退提醒：" + invalid);
    Assert(File.ReadAllText(preferencesPath) == invalid, "安全回退保留原始配置");
}
Assert(UpdatePreferences.Load(preferencesPath).Source == UpdateSourceKind.GitHub, "默认来源为 GitHub");
File.WriteAllText(preferencesPath, "{\"mode\":\"Disabled\"}");
Assert(UpdatePreferences.Load(preferencesPath).Source == UpdateSourceKind.GitHub && UpdatePreferences.Load(preferencesPath).Mode == UpdateMode.Disabled,
    "旧偏好补默认来源且保留不更新选择");
new UpdatePreferences { Source = UpdateSourceKind.Local, LocalDirectory = feed }.Save(preferencesPath);
Assert(UpdatePreferences.Load(preferencesPath).LocalDirectory == feed && UpdatePreferences.Load(preferencesPath).Source == UpdateSourceKind.Local, "本地来源及目录持久化");

// 离线包使用临时假程序，只测试协议与边界，不启动更新器。
string Bundle(string name, UpdateManifest metadata, bool invalidPayload = false)
{
    var path = Path.Combine(root, name + ".tudotupdate");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(metadata, UpdateJson.Options));
    using (var stream = zip.CreateEntry("payload.zip").Open())
    {
        var bytes = File.ReadAllBytes(package);
        if (invalidPayload) bytes[0] ^= 1;
        stream.Write(bytes);
    }
    return path;
}
var bundlePath = Bundle("valid", manifest);
var offline = OfflineUpdatePackage.Stage(bundlePath, install, Path.Combine(root, "offline"));
Assert(offline.Manifest == manifest && File.ReadAllBytes(offline.PackagePath).SequenceEqual(File.ReadAllBytes(package)), "离线包还原完全一致的已校验程序包");
Reject(() => OfflineUpdatePackage.Stage(bundlePath, install, root, manifest with { Notes = "different" }), "在线离线清单不一致被拒绝");
Reject(() => OfflineUpdatePackage.Stage(Bundle("corrupt", manifest, true), install, root), "离线包内容篡改被拒绝");
Reject(() => OfflineUpdatePackage.Stage(Bundle("old", manifest with { BuildId = 100 }), install, root), "离线包拒绝重复或旧构建");
Reject(() => OfflineUpdatePackage.Stage(package, install, root), "完整程序 ZIP 不被误识别为离线更新包");

// HTTP 使用内存响应，确保无网络测试不会依赖 GitHub 服务状态。
var api = $"https://api.github.com/repos/{UpdateFeed.Repository}/releases/latest";
var assetBase = $"https://github.com/{UpdateFeed.Repository}/releases/download/v-test/";
var manifestUrl = assetBase + "latest.json";
var bundleUrl = assetBase + $"Tudot-{manifest.BuildId}-update.tudotupdate";
string Release(bool prerelease = false, bool compatible = true) => JsonSerializer.Serialize(new
{
    draft = false, prerelease,
    assets = compatible ? new[] {
        new { name = "latest.json", browser_download_url = manifestUrl },
        new { name = $"Tudot-{manifest.BuildId}-update.tudotupdate", browser_download_url = bundleUrl }
    } : []
});
var routes = new Dictionary<string, byte[]>
{
    [api] = System.Text.Encoding.UTF8.GetBytes(Release()),
    [manifestUrl] = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateJson.Options),
    [bundleUrl] = File.ReadAllBytes(bundlePath)
};
var handler = new FixtureHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
{ Content = new ByteArrayContent(routes[request.RequestUri!.AbsoluteUri]) });
using var http = new HttpClient(handler);
var githubPreferences = new UpdatePreferences();
var onlineCandidate = await UpdateFeed.CheckAsync(install, githubPreferences, http: http);
Assert(onlineCandidate?.Manifest == manifest && onlineCandidate.BundleUrl!.AbsoluteUri == bundleUrl, "从正式 Release 发现匹配的清单与更新包");
var onlinePath = await UpdateFeed.StageAsync(install, onlineCandidate!, Path.Combine(root, "online"), http: http);
manifest.VerifyPackage(onlinePath);
Assert(true, "下载离线更新包并按官方清单校验");
var calls = handler.Calls;
Assert(await UpdateFeed.StageAsync(install, onlineCandidate!, Path.Combine(root, "online"), http: http) == onlinePath && handler.Calls == calls,
    "已校验网络缓存不重复下载");
Assert(await UpdateFeed.CheckAsync("invalid-install", githubPreferences with { Mode = UpdateMode.Disabled }, http: http) == null && handler.Calls == calls,
    "不更新模式不访问网络或安装目录");
routes[api] = System.Text.Encoding.UTF8.GetBytes(Release(prerelease: true));
Assert(await UpdateFeed.CheckAsync(install, githubPreferences, http: http) == null, "预发布版本不自动更新");
routes[api] = System.Text.Encoding.UTF8.GetBytes(Release(compatible: false));
Assert(await UpdateFeed.CheckAsync(install, githubPreferences, http: http) == null, "旧 Release 没有兼容资产时不误报");
routes[api] = System.Text.Encoding.UTF8.GetBytes(Release());
routes[manifestUrl] = JsonSerializer.SerializeToUtf8Bytes(manifest with { BuildId = 100 }, UpdateJson.Options);
Assert(await UpdateFeed.CheckAsync(install, githubPreferences, http: http) == null, "GitHub 相同构建不更新");
using var missingHttp = new HttpClient(new FixtureHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
Assert(await UpdateFeed.CheckAsync(install, githubPreferences, http: missingHttp) == null, "尚未发布 Release 的 404 安静跳过");
using var redirectHttp = new HttpClient(new FixtureHttpHandler(_ =>
{
    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
    response.Headers.Location = new Uri("https://untrusted.example/package");
    return response;
}));
try { await UpdateFeed.CheckAsync(install, githubPreferences, http: redirectHttp); throw new Exception("Unsafe redirect accepted"); }
catch (InvalidDataException) { Assert(true, "拒绝非 GitHub 服务器重定向"); }
using var rateHttp = new HttpClient(new FixtureHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
try { await UpdateFeed.CheckAsync(install, githubPreferences, http: rateHttp); throw new Exception("Rate limit ignored"); }
catch (HttpRequestException) { Assert(true, "限流或权限错误向调用方报告且不安装"); }
UpdateJson.Write(Path.Combine(feed, "latest.json"), manifest);
Assert((await UpdateFeed.CheckAsync(install, githubPreferences with { Source = UpdateSourceKind.Local, LocalDirectory = feed }))?.Manifest == manifest,
    "统一入口保留本地目录更新");
Assert(PackageInstaller.ValidateInstall(install).BuildId == 100, "检查下载和导入均不提前改变安装");
using var cancelledToken = new CancellationTokenSource();
cancelledToken.Cancel();
try { await UpdateFeed.CheckAsync(install, githubPreferences, cancelledToken.Token, http); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Assert(true, "取消在线检查不安装程序"); }
File.WriteAllText(onlinePath, "corrupt network cache");
routes[bundleUrl] = File.ReadAllBytes(Bundle("network-corrupt", manifest, true));
try { await UpdateFeed.StageAsync(install, onlineCandidate!, Path.Combine(root, "online"), http: http); throw new Exception("Corrupt download accepted"); }
catch (InvalidDataException) { Assert(true, "损坏的网络更新包拒绝进入安装"); }
Assert(!Directory.GetFiles(Path.GetDirectoryName(onlinePath)!, "*.download").Any(), "失败下载不残留临时文件");
Console.WriteLine($"{passed} update checks passed. Fixtures: {root}");

/// <summary>返回内存 HTTP 响应并计数，隔离真实网络和速率限制。</summary>
sealed class FixtureHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(respond(request));
    }
}
