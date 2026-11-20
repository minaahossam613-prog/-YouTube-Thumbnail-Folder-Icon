// MainWindow.xaml.cs  —  ملف الواجهة (WPF + ثيمات + تعدد اللغات)
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace YTFolderIcon;

public partial class MainWindow : Window
{
    private readonly Config cfg;
    private string lang;
    private string? lastFolder;

    public MainWindow(Config config)
    {
        cfg = config;
        lang = Lang.Names.Any(n => n.Code == cfg.Lang) ? cfg.Lang : Lang.Default;

        InitializeComponent();

        DestBox.Text = cfg.Dest;
        PlaylistToggle.IsChecked = cfg.Playlist;
        ShortsToggle.IsChecked = cfg.Shorts;

        ThemeCombo.ItemsSource = Themes.All.Keys.OrderBy(k => k).ToList();
        ThemeCombo.SelectedItem = cfg.Theme;
        LangCombo.ItemsSource = Lang.Names.Select(n => n.Name).ToList();
        LangCombo.SelectedItem = Lang.NameOf(lang);

        // نربط الأحداث بعد ضبط القيم الأولية حتى لا تُستدعى مبكراً
        ThemeCombo.SelectionChanged += ThemeCombo_Changed;
        LangCombo.SelectionChanged += LangCombo_Changed;

        Retranslate();

        SourceInitialized += (_, _) => SetTitleBarDark(Themes.All[cfg.Theme].Dark);
        Closing += (_, _) =>
        {
            cfg.Theme = (ThemeCombo.SelectedItem as string) ?? cfg.Theme;
            cfg.Dest = DestBox.Text;
            cfg.Lang = lang;
            cfg.Playlist = PlaylistToggle.IsChecked == true;
            cfg.Shorts = ShortsToggle.IsChecked == true;
            YtCore.SaveConfig(cfg);
        };
    }

    // ------------------------------------------------------------ الترجمة
    private string T(string key, object? args = null) => Lang.T(lang, key, args);

    private void Retranslate()
    {
        Title = T("app_title");
        HeaderText.Text = T("header");
        ThemeLabel.Text = T("theme");
        LangLabel.Text = T("language");
        UrlsLabel.Text = T("urls_label");
        SaveToLabel.Text = T("save_to");
        BrowseBtn.Content = T("browse");
        PlaylistToggle.Content = T("playlist");
        ShortsToggle.Content = T("shorts");
        RunBtn.Content = T("create");
        PasteBtn.Content = T("paste");
        OpenBtn.Content = T("open_folder");
        LogGroup.Header = T("log");

        // العربية تُعرض من اليمين لليسار (حقول الروابط والمسارات تبقى LTR من الـ XAML)
        FlowDirection = lang == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void LangCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LangCombo.SelectedItem is string name)
        {
            lang = Lang.CodeFromName(name);
            Retranslate();
        }
    }

    // ------------------------------------------------------------ الثيمات
    private void ThemeCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is string name)
        {
            name = Themes.Apply(name);
            SetTitleBarDark(Themes.All[name].Dark);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>شريط العنوان الداكن في Windows 10/11 ليتماشى مع الثيم.</summary>
    private void SetTitleBarDark(bool dark)
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            int v = dark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref v, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch { /* نظام قديم: تجاهل */ }
    }

    // ------------------------------------------------------------ الأحداث
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (Directory.Exists(DestBox.Text)) dlg.InitialDirectory = DestBox.Text;
        if (dlg.ShowDialog(this) == true) DestBox.Text = dlg.FolderName;
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.GetText().Trim();
            if (text.Length > 0) UrlsBox.AppendText(text + "\n");
        }
        catch { /* الحافظة مشغولة */ }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var path = lastFolder ?? DestBox.Text;
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>تكتب رسالة في السجل بلغة الواجهة الحالية (آمنة للاستدعاء من أي Thread).</summary>
    private void Log(string key, string level = "info", object? args = null)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var run = new Run(T(key, args));
            if (level == "success") run.SetResourceReference(TextElement.ForegroundProperty, "SuccessBrush");
            else if (level == "error") run.SetResourceReference(TextElement.ForegroundProperty, "DangerBrush");
            LogBox.Document.Blocks.Add(new Paragraph(run));
            LogBox.ScrollToEnd();
        });
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var urls = UrlsBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .ToList();
        if (urls.Count == 0)
        {
            MessageBox.Show(this, T("no_urls"), T("warn_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var baseDir = DestBox.Text.Trim();
        try { Directory.CreateDirectory(baseDir); }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("bad_dest", new { error = ex.Message }), T("error_title"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RunBtn.IsEnabled = false;
        Bar.Value = 0;
        bool usePlaylist = PlaylistToggle.IsChecked == true;
        bool useShorts = ShortsToggle.IsChecked == true;
        var report = new Progress<double>(v => Bar.Value = v);   // يُنفَّذ على خيط الواجهة

        string? lastDone = null;
        await Task.Run(async () =>
        {
            int total = urls.Count;
            for (int i = 1; i <= total; i++)
            {
                double basePct = (i - 1) / (double)total * 100;
                double span = 100.0 / total;
                try
                {
                    var folder = await YtCore.ProcessUrlAsync(
                        urls[i - 1], baseDir, Log, usePlaylist,
                        (done, n) => ((IProgress<double>)report).Report(basePct + span * done / n),
                        useShorts);
                    if (folder != null) lastDone = folder;
                }
                catch (ThumbnailException) { Log("err_thumb", "error"); }
                catch (Exception ex) { Log("err_generic", "error", new { error = ex.Message }); }
                ((IProgress<double>)report).Report(i / (double)total * 100);
            }
            Log("finished", "success");
        });

        if (lastDone != null) lastFolder = lastDone;
        RunBtn.IsEnabled = true;
        OpenBtn.IsEnabled = true;
    }
}
