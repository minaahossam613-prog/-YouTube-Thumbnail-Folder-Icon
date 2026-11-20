// Themes.cs  —  الثيمات (تغيير الألوان فورياً بدون إعادة تشغيل)
using System.Windows;
using System.Windows.Media;

namespace YTFolderIcon;

public record Theme(string Name, bool Dark,
                    string Bg, string Fg, string Input, string Line,
                    string Primary, string Secondary,
                    string Success, string Info, string Warning, string Danger);

public static class Themes
{
    public const string Fallback = "darkly";

    private static Theme T(string name, bool dark, string bg, string fg, string input, string line,
                           string primary, string secondary, string success, string info,
                           string warning, string danger)
        => new(name, dark, bg, fg, input, line, primary, secondary, success, info, warning, danger);

    public static readonly Dictionary<string, Theme> All = new[]
    {
        //   name          dark   bg        fg        input     line      primary   secondary success   info      warning   danger
        T("pulse-dark",  true,  "1B1A26", "E6E6EE", "21202F", "4A4860", "7A55B8", "5B5870", "3DC77A", "1E9BE0", "E8A33D", "E5534B"),
        T("darkly",      true,  "222222", "FFFFFF", "2D2D2D", "444444", "375A7F", "555555", "00BC8C", "3498DB", "F39C12", "E74C3C"),
        T("superhero",   true,  "2B3E50", "FFFFFF", "20374C", "4E5D6C", "DF691A", "4E5D6C", "5CB85C", "5BC0DE", "F0AD4E", "D9534F"),
        T("cyborg",      true,  "060606", "ADAFAE", "222222", "555555", "2A9FD6", "555555", "77B300", "9933CC", "FF8800", "CC0000"),
        T("solar",       true,  "002B36", "839496", "073642", "586E75", "B58900", "586E75", "2AA198", "268BD2", "CB4B16", "D33682"),
        T("vapor",       true,  "1A0141", "E0D7FF", "2A0F5C", "6E3CBC", "EA39B8", "5A3A9E", "3CF281", "27C5FF", "FFB84D", "FF3D6A"),
        T("flatly",      false, "FFFFFF", "2C3E50", "FFFFFF", "CED4DA", "2C3E50", "95A5A6", "18BC9C", "3498DB", "F39C12", "E74C3C"),
        T("cosmo",       false, "FFFFFF", "373A3C", "FFFFFF", "CED4DA", "2780E3", "8A8D8F", "3FB618", "9954BB", "FF7518", "FF0039"),
        T("litera",      false, "FFFFFF", "343A40", "FFFFFF", "CED4DA", "4582EC", "868E96", "02B875", "17A2B8", "F0AD4E", "D9534F"),
        T("minty",       false, "FFFFFF", "5A5A5A", "FFFFFF", "CED4DA", "78C2AD", "F3969A", "56CC9D", "6CC3D5", "FFCE67", "FF7851"),
        T("pulse",       false, "FFFFFF", "444444", "FFFFFF", "CED4DA", "593196", "A991D4", "13B955", "009CDC", "EFA31D", "FC3939"),
    }.ToDictionary(t => t.Name);

    private static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + hex));
        b.Freeze();
        return b;
    }

    /// <summary>يطبّق الثيم على موارد التطبيق (كل العناصر تستخدم DynamicResource فتتغير فوراً).</summary>
    public static string Apply(string name)
    {
        if (!All.TryGetValue(name, out var t))
        {
            name = Fallback;
            t = All[name];
        }

        var r = Application.Current.Resources;
        r["BgBrush"] = B(t.Bg);
        r["FgBrush"] = B(t.Fg);
        r["InputBrush"] = B(t.Input);
        r["LineBrush"] = B(t.Line);
        r["PrimaryBrush"] = B(t.Primary);
        r["SecondaryBrush"] = B(t.Secondary);
        r["SuccessBrush"] = B(t.Success);
        r["InfoBrush"] = B(t.Info);
        r["WarningBrush"] = B(t.Warning);
        r["DangerBrush"] = B(t.Danger);
        return name;
    }
}
