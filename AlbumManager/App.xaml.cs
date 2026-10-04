using System.Windows;

namespace AlbumManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初始化数据库
        var dbService = new Services.DatabaseService();
        dbService.Initialize();
    }
}
