using System.Diagnostics;
using System.IO;
using System.Windows;
using Tudot.Updates;
using Tudot.Views;

namespace Tudot.Services;

internal static class LocalUpdateService
{
    private static bool _started;
    private static long _offeredBuild;
    private static bool _busy;
    private static UpdateMode _lastMode = UpdateMode.Notify;
    private static readonly SemaphoreSlim Wake = new(0, 1);
    private static readonly string UpdatesRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tudot", "updates");

    /// <summary>设置保存后重新评估已暂缓的更新，并唤醒检查循环；调用方须位于 UI 线程。</summary>
    public static void PreferencesChanged()
    {
        _offeredBuild = 0;
        if (Wake.CurrentCount == 0) Wake.Release();
    }

    public static async Task OnRenderedAsync(Window owner)
    {
        if (_started) return;
        _started = true;
        var args = Environment.GetCommandLineArgs();
        try { await Task.Run(() => Acknowledge(args)); }
        catch (Exception ex) { Debug.WriteLine("Update acknowledgement failed: " + ex); }
        if (StartupTrace.Enabled) return;
        using var closed = new CancellationTokenSource();
        EventHandler onClosed = (_, _) => closed.Cancel();
        owner.Closed += onClosed;
        try
        {
            if (args.Contains("--skip-update-once") && UpdatePreferences.Load().Mode != UpdateMode.Disabled)
            {
                // 更新/回滚刚重启时，跳过当时已知构建，避免重复提醒；以后发布的新构建仍可发现。
                try { _offeredBuild = (await UpdateFeed.CheckAsync(AppContext.BaseDirectory, UpdatePreferences.Load(), closed.Token))?.Manifest.BuildId ?? 0; }
                catch (Exception ex) { Debug.WriteLine("Deferred update check: " + ex.Message); }
                await Wake.WaitAsync(TimeSpan.FromMinutes(1), closed.Token);
            }
            else await Task.Delay(1000, closed.Token);
            while (!closed.IsCancellationRequested)
            {
                await CheckAndOfferAsync(owner, closed.Token);
                await Wake.WaitAsync(TimeSpan.FromMinutes(UpdatePreferences.Load().Source == UpdateSourceKind.GitHub ? 15 : 1), closed.Token);
            }
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
        finally { owner.Closed -= onClosed; }
    }

    private static async Task CheckAndOfferAsync(Window owner, CancellationToken token)
    {
        if (_busy) return;
        _busy = true;
        var installRequested = false;
        try
        {
            var preferences = UpdatePreferences.Load();
            var mode = preferences.Mode;
            // 从提醒切换到自动后，允许处理本次运行中曾被用户暂缓的同一个构建。
            if (mode != _lastMode) _offeredBuild = 0;
            _lastMode = mode;
            if (mode == UpdateMode.Disabled) return;
            var candidate = await UpdateFeed.CheckAsync(AppContext.BaseDirectory, preferences, token);
            if (candidate == null || candidate.Manifest.BuildId == _offeredBuild) return;
            var manifest = candidate.Manifest;
            var packagePath = await UpdateFeed.StageAsync(AppContext.BaseDirectory, candidate, UpdatesRoot, token);
            if (!owner.IsLoaded || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
            // 拷贝期间用户可能更改设置；子窗口打开时延后操作，避免打断编辑、导入或设置保存。
            var currentPreferences = UpdatePreferences.Load();
            if (preferences.Source != currentPreferences.Source || preferences.LocalDirectory != currentPreferences.LocalDirectory) return;
            mode = currentPreferences.Mode;
            if (mode == UpdateMode.Disabled || owner.OwnedWindows.Cast<Window>().Any(w => w.IsVisible)) return;
            _offeredBuild = manifest.BuildId;
            if (mode == UpdateMode.Notify && !ModernDialog.Confirm(owner,
                $"更新包已准备好。\n构建时间：{manifest.PublishedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n\n是否退出并更新？完成后会自动重新打开。\n相册与数据库不会被更新程序替换。",
                "重启更新"))
                return;

            if (UpdatePreferences.Load().Mode == UpdateMode.Disabled) return;

            installRequested = true;
            if (!await StartInstallerAsync(packagePath, manifest, () =>
            {
                var latest = UpdatePreferences.Load();
                return owner.IsLoaded && owner.IsVisible && owner.WindowState != WindowState.Minimized &&
                    !owner.OwnedWindows.Cast<Window>().Any(w => w.IsVisible) && latest.Mode == mode &&
                    latest.Source == preferences.Source && latest.LocalDirectory == preferences.LocalDirectory;
            })) _offeredBuild = 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(UpdatesRoot);
                await File.AppendAllTextAsync(Path.Combine(UpdatesRoot, "check.log"), $"{DateTime.UtcNow:O} {ex}\n");
            }
            catch { }
            Debug.WriteLine("Update check failed: " + ex.Message);
            if (installRequested && owner.IsLoaded)
                ModernDialog.Info(owner, "无法开始更新，当前程序仍可继续使用。\n\n" + ex.Message, "更新失败");
        }
        finally { _busy = false; }
    }

    /// <summary>手动选择离线包是独立的显式操作，允许在关闭自动检查时使用；始终要求用户确认重启。</summary>
    public static async Task InstallOfflineAsync(Window owner, string file)
    {
        if (_busy)
        {
            ModernDialog.Info(owner, "正在准备其它更新，请稍后再试。", "更新进行中");
            return;
        }
        _busy = true;
        try
        {
            var staged = await Task.Run(() => OfflineUpdatePackage.Stage(file, AppContext.BaseDirectory, UpdatesRoot));
            if (!owner.IsLoaded) return;
            if (!ModernDialog.Confirm(owner,
                $"已校验更新包，构建 {staged.Manifest.BuildId}。\n请确认文件来自官方 Release 或你信任的发布者。\n\n立即退出、更新并重新打开？", "安装本地更新包")) return;
            await StartInstallerAsync(staged.PackagePath, staged.Manifest, () => owner.IsLoaded);
        }
        catch (Exception ex)
        {
            if (owner.IsLoaded) ModernDialog.Info(owner, "无法安装更新包：\n" + ex.Message, "更新失败");
        }
        finally { _busy = false; }
    }

    /// <summary>启动独立更新器并退出当前应用；安装和失败回滚共用现有协议，不替换用户数据库。</summary>
    private static async Task<bool> StartInstallerAsync(string packagePath, UpdateManifest manifest, Func<bool> stillAllowed)
    {
        var requestDirectory = Path.Combine(Path.GetDirectoryName(packagePath)!, "attempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(requestDirectory);
        var updaterPath = Path.Combine(requestDirectory, "Tudot.Updater.exe");
        await Task.Run(() => File.Copy(Path.Combine(AppContext.BaseDirectory, "Tudot.Updater.exe"), updaterPath));
        // 复制期间 UI 仍可操作，必须在实际退出前复核模式和窗口状态。
        if (!stillAllowed()) return false;
        using var current = Process.GetCurrentProcess();
        var plan = new UpdatePlan
        {
            InstallDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            PackagePath = packagePath, Manifest = manifest,
            ParentProcessId = Environment.ProcessId,
            ParentStartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks,
            AckPath = Path.Combine(requestDirectory, "ready.json")
        };
        var planPath = Path.Combine(requestDirectory, "plan.json");
        UpdateJson.Write(planPath, plan);
        var start = new ProcessStartInfo(updaterPath)
        { WorkingDirectory = requestDirectory, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--plan");
        start.ArgumentList.Add(planPath);
        using var helper = Process.Start(start) ?? throw new IOException("无法启动更新程序。");
        Application.Current.Shutdown();
        return true;
    }

    private static void Acknowledge(string[] args)
    {
        var index = Array.IndexOf(args, "--update-ack");
        if (index < 0 || index + 1 >= args.Length) return;
        var ackPath = Path.GetFullPath(args[index + 1]);
        if (Path.GetFileName(ackPath) != "ready.json") return;
        var plan = UpdateJson.Read<UpdatePlan>(Path.Combine(Path.GetDirectoryName(ackPath)!, "plan.json"));
        var installed = PackageInstaller.ValidateInstall(AppContext.BaseDirectory);
        if (!PackageInstaller.SamePath(plan.InstallDirectory, AppContext.BaseDirectory) ||
            !PackageInstaller.SamePath(plan.AckPath, ackPath) || plan.Manifest.BuildId != installed.BuildId)
            throw new InvalidDataException("Startup acknowledgement does not match this installation.");
        UpdateJson.Write(ackPath, new UpdateAck { BuildId = installed.BuildId, ProcessId = Environment.ProcessId });
    }
}
