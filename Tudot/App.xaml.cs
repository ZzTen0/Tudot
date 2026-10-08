using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Tudot;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "tudot_error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常捕获：写入日志文件，便于诊断卡死/崩溃
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 初始化数据库
        var dbService = new Services.DatabaseService();
        dbService.Initialize();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("UI线程", e.Exception);
        e.Handled = true; // 阻止直接崩溃，先看日志
        MessageBox.Show($"发生错误：{e.Exception.Message}\n\n详细堆栈已写入：\n{LogPath}",
            "Tudot 错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log("后台线程", ex);
    }

    private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        Log("异步任务", e.Exception);
        e.SetObserved();
    }

    private static void Log(string source, Exception ex)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }
    }
}
