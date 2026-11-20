using System.Windows;

namespace YTFolderIcon;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var cfg = YtCore.LoadConfig();
        cfg.Theme = Themes.Apply(cfg.Theme);   // يرجع الثيم الفعلي (الاحتياطي لو المحفوظ غير موجود)

        var win = new YTFolderIcon.MainWindow(cfg);
        MainWindow = win;
        win.Show();
    }
}
