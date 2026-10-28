using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Tudot.Updates;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string title, uint type);

    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--plan") return 2;
        var planPath = Path.GetFullPath(args[1]);
        var logPath = Path.Combine(Path.GetDirectoryName(planPath)!, "update.log");
        void Log(string text)
        {
            try { File.AppendAllText(logPath, $"{DateTime.UtcNow:O} {text}{Environment.NewLine}"); }
            catch { }
        }

        UpdatePlan? plan = null;
        InstallTransaction? transaction = null;
        Process? replacement = null;
        var parentExited = false;
        var validPlan = false;
        Mutex? updateMutex = null;
        var ownsMutex = false;
        try
        {
            plan = UpdateJson.Read<UpdatePlan>(planPath);
            var current = PackageInstaller.ValidateInstall(plan.InstallDirectory);
            plan.Manifest.Validate();
            if (plan.Manifest.BuildId <= current.BuildId) throw new InvalidDataException("Build is not newer.");
            if (!PackageInstaller.SamePath(Path.GetDirectoryName(plan.AckPath)!, Path.GetDirectoryName(planPath)!) ||
                Path.GetFileName(plan.AckPath) != "ready.json")
                throw new InvalidDataException("Invalid startup acknowledgement path.");
            if (plan.ParentProcessId <= 0 || plan.ParentStartTimeUtcTicks <= 0)
                throw new InvalidDataException("Invalid parent process identity.");
            if (Environment.ProcessPath!.StartsWith(Path.GetFullPath(plan.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The updater must run outside the installation directory.");
            validPlan = true;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(plan.InstallDirectory).ToUpperInvariant())))[..24];
            updateMutex = new Mutex(false, "Local\\Tudot.Update." + key);
            try { ownsMutex = updateMutex.WaitOne(TimeSpan.FromSeconds(1)); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) throw new IOException("Another update is already in progress.");

            Log("Preparing and verifying package.");
            var candidate = PackageInstaller.Prepare(plan.InstallDirectory, plan.PackagePath, plan.Manifest);
            Log("Waiting for the parent application to exit.");
            try
            {
                using var parent = Process.GetProcessById(plan.ParentProcessId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTimeUtcTicks)
                {
                    if (!PackageInstaller.SamePath(parent.MainModule!.FileName, Path.Combine(plan.InstallDirectory, "Tudot.exe")))
                        throw new InvalidDataException("Parent is not the installed Tudot process.");
                    if (!parent.WaitForExit(60000)) throw new TimeoutException("The application did not exit; no files were replaced.");
                }
            }
            catch (ArgumentException) { /* 父进程在校验包期间已经正常退出。 */ }
            parentExited = true;
            foreach (var process in Process.GetProcessesByName("Tudot"))
            {
                using (process)
                {
                    if (!process.HasExited && PackageInstaller.SamePath(process.MainModule!.FileName, Path.Combine(plan.InstallDirectory, "Tudot.exe")))
                        throw new IOException("Another instance is still using this installation.");
                }
            }

            transaction = PackageInstaller.Activate(plan.InstallDirectory, candidate);
            Log("Activated new build. Backup: " + transaction.BackupDirectory);
            replacement = StartApp(plan.InstallDirectory, plan.AckPath);
            var until = DateTime.UtcNow.AddSeconds(30);
            var ready = false;
            while (DateTime.UtcNow < until && !replacement.HasExited)
            {
                if (File.Exists(plan.AckPath))
                {
                    var ack = UpdateJson.Read<UpdateAck>(plan.AckPath);
                    if (ack.BuildId == plan.Manifest.BuildId && ack.ProcessId == replacement.Id)
                    {
                        ready = true;
                        break;
                    }
                }
                Thread.Sleep(200);
            }
            if (!ready) throw new IOException("The new application did not confirm a successful startup.");
            Log("SUCCESS: new build rendered its main window.");
            return 0;
        }
        catch (Exception ex)
        {
            Log("FAILED: " + ex);
            if (validPlan && plan != null && !parentExited)
            {
                try
                {
                    using var parent = Process.GetProcessById(plan.ParentProcessId);
                    parentExited = parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTimeUtcTicks;
                }
                catch (ArgumentException) { parentExited = true; }
            }
            if (replacement != null && !replacement.HasExited)
            {
                replacement.CloseMainWindow();
                if (!replacement.WaitForExit(5000)) replacement.Kill(entireProcessTree: true);
                replacement.WaitForExit();
            }
            try
            {
                transaction?.Rollback();
                if (parentExited && validPlan && ownsMutex && plan != null)
                {
                    StartApp(plan.InstallDirectory, null);
                    Log("ROLLBACK: previous application restarted.");
                }
            }
            catch (Exception rollback) { Log("Recovery needs attention: " + rollback); }
            if (Environment.GetEnvironmentVariable("TUDOT_UPDATER_SILENT") != "1")
                MessageBox(IntPtr.Zero, "更新未完成，已尝试恢复原程序。\n\n" + ex.Message + "\n\n日志：" + logPath, "Tudot 更新", 0x10);
            return 1;
        }
        finally
        {
            replacement?.Dispose();
            if (ownsMutex) updateMutex!.ReleaseMutex();
            updateMutex?.Dispose();
        }
    }

    private static Process StartApp(string directory, string? ackPath)
    {
        var start = new ProcessStartInfo(Path.Combine(directory, "Tudot.exe"))
        { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--skip-update-once");
        if (ackPath != null) { start.ArgumentList.Add("--update-ack"); start.ArgumentList.Add(ackPath); }
        return Process.Start(start) ?? throw new IOException("Cannot start Tudot.");
    }
}
