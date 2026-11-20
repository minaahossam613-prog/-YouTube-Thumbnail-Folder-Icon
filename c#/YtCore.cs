// YtCore.cs  —  برمجة التطبيق (المنطق فقط، بدون واجهة)
//
// - تحميل ثمنيل يوتيوب
// - قص الحواف السوداء إلى 16:9
// - تحويله إلى .ico بخلفية شفافة
// - إنشاء فولدر وضبط desktop.ini (اسم الأيقونة فقط بدون مسار)
// - حفظ/قراءة إعدادات المستخدم (الثيم + اللغة + مكان الحفظ)
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YTFolderIcon;

/// <summary>دالة التسجيل: الواجهة تترجم المفتاح حسب اللغة وتستبدل المتغيرات من args.</summary>
public delegate void Logger(string key, string level = "info", object? args = null);

/// <summary>تعذّر تحميل الثمنيل (الواجهة تترجم الرسالة حسب اللغة).</summary>
public class ThumbnailException : Exception
{
    public ThumbnailException() : base("thumbnail download failed") { }
}

public class Config
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = "pulse-dark";
    [JsonPropertyName("lang")] public string Lang { get; set; } = "en";
    [JsonPropertyName("dest")] public string Dest { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
    [JsonPropertyName("playlist")] public bool Playlist { get; set; }
    [JsonPropertyName("shorts")] public bool Shorts { get; set; }
}

public record PlaylistResult(string Title, List<(string Id, string Title)> Videos,
                             List<string> Errors, int? Expected);

public static class YtCore
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0 Safari/537.36";
    private const string PublicKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";
    private const string PublicVer = "2.20250101.00.00";

    // نفس ملف الإعدادات القديم، فتبقى إعدادات نسخة بايثون صالحة
    private static readonly string ConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".yt_folder_icon.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------------------------------------------------------------- الإعدادات
    public static Config LoadConfig()
    {
        try
        {
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath, Encoding.UTF8)) ?? new Config();
        }
        catch { return new Config(); }
    }

    public static void SaveConfig(Config cfg)
    {
        try { File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, JsonOpts), Encoding.UTF8); }
        catch { /* تجاهل */ }
    }

    // ---------------------------------------------------------------- الشبكة
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UA);
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "CONSENT=YES+1; SOCS=CAI");
        return c;
    }

    public static string? ExtractVideoId(string url)
    {
        url = url.Trim();
        var m = Regex.Match(url, @"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})");
        if (m.Success) return m.Groups[1].Value;
        if (Regex.IsMatch(url, @"^[A-Za-z0-9_-]{11}$")) return url;
        return null;
    }

    public static string? ExtractPlaylistId(string url)
    {
        var m = Regex.Match(url, @"[?&]list=([A-Za-z0-9_-]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Task<byte[]> HttpGetBytes(string url) => Http.GetByteArrayAsync(url);
    private static Task<string> HttpGetString(string url) => Http.GetStringAsync(url);

    // ---------------------------------------------------------------- أدوات JSON
    private static JsonNode? G(JsonNode? n, string key) =>
        n is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;

    private static string Str(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    /// <summary>يمشي داخل JSON ويرجع كل قيمة تحت المفتاح key.</summary>
    private static IEnumerable<JsonNode> Walk(JsonNode? obj, string key)
    {
        if (obj is JsonObject o)
        {
            foreach (var kv in o)
            {
                if (kv.Key == key)
                {
                    if (kv.Value != null) yield return kv.Value;
                }
                else
                {
                    foreach (var x in Walk(kv.Value, key)) yield return x;
                }
            }
        }
        else if (obj is JsonArray a)
        {
            foreach (var i in a)
                foreach (var x in Walk(i, key)) yield return x;
        }
    }

    private static string Text(JsonNode? node)
    {
        if (node is not JsonObject o) return "";
        if (o.ContainsKey("simpleText")) return Str(o["simpleText"]);
        if (o.ContainsKey("content")) return Str(o["content"]);
        if (o["runs"] is JsonArray runs)
            return string.Concat(runs.Select(r => Str(G(r, "text"))));
        return "";
    }

    private static string? Tok(JsonNode? c)
    {
        var t = Str(G(G(G(c, "continuationEndpoint"), "continuationCommand"), "token"));
        return t == "" ? null : t;
    }

    /// <summary>توكن الصفحة التالية: نفضّل اللي في نفس القائمة مع الفيديوهات.</summary>
    private static string? PickToken(JsonNode d)
    {
        string? best = null, fallback = null;

        void Visit(JsonNode? o)
        {
            if (o is JsonArray a)
            {
                bool hasVid = a.Any(x => x is JsonObject xo &&
                    (xo.ContainsKey("playlistVideoRenderer") || xo.ContainsKey("lockupViewModel")));
                foreach (var x in a)
                {
                    if (x is JsonObject xo && xo.TryGetPropertyValue("continuationItemRenderer", out var cir))
                    {
                        var t = Tok(cir);
                        if (t != null)
                        {
                            fallback = t;
                            if (hasVid) best = t;
                        }
                    }
                }
                foreach (var x in a) Visit(x);
            }
            else if (o is JsonObject ob)
            {
                foreach (var kv in ob) Visit(kv.Value);
            }
        }

        Visit(d);
        if (best == null && fallback == null)
        {
            foreach (var c in Walk(d, "continuationCommand"))
            {
                var t = Str(G(c, "token"));
                if (t != "") fallback = t;
            }
        }
        return best ?? fallback;
    }

    /// <summary>يستخرج الفيديوهات (بالشكلين القديم والجديد) ويرجع توكن المتابعة.</summary>
    private static string? Collect(JsonNode d, List<(string Id, string Title)> videos, HashSet<string> seen)
    {
        foreach (var r in Walk(d, "playlistVideoRenderer"))
        {
            var vid = Str(G(r, "videoId"));
            if (vid != "" && seen.Add(vid))
            {
                var t = Text(G(r, "title"));
                videos.Add((vid, t == "" ? vid : t));
            }
        }
        foreach (var r in Walk(d, "lockupViewModel"))
        {
            var ct = Str(G(r, "contentType"));
            if (ct != "" && ct != "LOCKUP_CONTENT_TYPE_VIDEO") continue;
            var vid = Str(G(r, "contentId"));
            if (vid.Length == 11 && seen.Add(vid))
            {
                var meta = G(G(r, "metadata"), "lockupMetadataViewModel");
                var t = Text(G(meta, "title"));
                videos.Add((vid, t == "" ? vid : t));
            }
        }
        return PickToken(d);
    }

    /// <summary>عدد فيديوهات البلاي لست المعلن في الصفحة (للتحقق فقط).</summary>
    private static int? ExpectedCount(JsonNode d)
    {
        try
        {
            foreach (var h in Walk(d, "playlistHeaderRenderer"))
            {
                var n = Regex.Replace(Text(G(h, "numVideosText")), @"\D", "");
                if (n != "") return int.Parse(n);
            }
            foreach (var sb in Walk(d, "playlistSidebarPrimaryInfoRenderer"))
            {
                var stats = G(sb, "stats") as JsonArray;
                var n = Regex.Replace(Text(stats?.FirstOrDefault()), @"\D", "");
                if (n != "") return int.Parse(n);
            }
        }
        catch { /* تجاهل */ }
        return null;
    }

    private static string PlaylistTitle(JsonNode d, string fallback)
    {
        var t = Str(G(G(G(d, "metadata"), "playlistMetadataRenderer"), "title"));
        if (t != "") return t;
        foreach (var h in Walk(d, "playlistHeaderRenderer"))
        {
            t = Text(G(h, "title"));
            if (t != "") return t;
        }
        return fallback;
    }

    /// <summary>يستخرج كائن JSON يبدأ عند الحرف '{' في الموضع start (مع مراعاة النصوص).</summary>
    private static string ExtractJson(string s, int start)
    {
        int depth = 0;
        bool inStr = false, esc = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') inStr = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return s.Substring(start, i - start + 1);
        }
        throw new FormatException("unterminated JSON");
    }

    private record Ctx(string Key, JsonObject Client);

    private static JsonObject MakeClient(string version, string? visitorData = null)
    {
        var c = new JsonObject
        {
            ["clientName"] = "WEB",
            ["hl"] = "en",
            ["gl"] = "US",
            ["clientVersion"] = version,
        };
        if (visitorData != null) c["visitorData"] = visitorData;
        return c;
    }

    private static async Task<JsonNode> InnertubeAsync(Ctx ctx, JsonObject body, string endpoint)
    {
        var root = new JsonObject { ["context"] = new JsonObject { ["client"] = ctx.Client.DeepClone() } };
        foreach (var kv in body) root[kv.Key] = kv.Value?.DeepClone();
        using var resp = await Http.PostAsync(
            $"https://www.youtube.com/youtubei/v1/{endpoint}?key={ctx.Key}&prettyPrint=false",
            new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
    }

    // ---------------------------------------------------------------- البلاي لست
    private static async Task<PlaylistResult> GetPlaylistNativeAsync(string plId)
    {
        var errors = new List<string>();
        var title = plId;
        var videos = new List<(string Id, string Title)>();
        var seen = new HashSet<string>();
        int? expected = null;
        var ctxs = new List<Ctx>();   // الحديث من الصفحة أولاً ثم الاحتياطي
        string? token = null;

        // --- 1) صفحة البلاي لست: أول دفعة + مفتاح وإصدار حديثين
        try
        {
            var html = await HttpGetString($"https://www.youtube.com/playlist?list={plId}&hl=en");
            var kk = Regex.Match(html, "\"INNERTUBE_API_KEY\":\"([^\"]+)\"");
            var vv = Regex.Match(html, "\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"");
            var vd = Regex.Match(html, "\"visitorData\":\"([^\"]+)\"");
            if (kk.Success && vv.Success)
                ctxs.Add(new Ctx(kk.Groups[1].Value,
                                 MakeClient(vv.Groups[1].Value, vd.Success ? vd.Groups[1].Value : null)));

            var m = Regex.Match(html, @"ytInitialData\s*=\s*");
            if (!m.Success) throw new FormatException("ytInitialData not found");
            var page = JsonNode.Parse(ExtractJson(html, m.Index + m.Length))!;
            title = PlaylistTitle(page, title);
            expected = ExpectedCount(page);
            token = Collect(page, videos, seen);
        }
        catch (Exception e) { errors.Add("html: " + e.Message); }
        ctxs.Add(new Ctx(PublicKey, MakeClient(PublicVer)));

        // يجرّب كل السياقات، ومع كل واحد 3 محاولات
        async Task<JsonNode> Call(JsonObject body, string endpoint = "browse")
        {
            Exception? last = null;
            foreach (var ctx in ctxs)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { return await InnertubeAsync(ctx, body, endpoint); }
                    catch (Exception e)
                    {
                        last = e;
                        await Task.Delay(1000 * (1 + attempt));
                    }
                }
            }
            throw last ?? new Exception("no context");
        }

        // --- 2) لو الصفحة فشلت: الدفعة الأولى من الواجهة الداخلية
        if (videos.Count == 0)
        {
            try
            {
                var first = await Call(new JsonObject { ["browseId"] = "VL" + plId });
                title = PlaylistTitle(first, title);
                token = Collect(first, videos, seen);
            }
            catch (Exception e) { errors.Add("innertube: " + e.Message); }
        }

        // --- 3) باقي الدفعات (كل البلاي لست)
        int pages = 0;
        while (token != null && pages < 2000)
        {
            pages++;
            JsonNode next;
            try { next = await Call(new JsonObject { ["continuation"] = token }); }
            catch (Exception e)
            {
                errors.Add($"continuation (after {videos.Count} videos): {e.Message}");
                break;
            }
            int before = videos.Count;
            token = Collect(next, videos, seen);
            if (videos.Count == before && token == null) break;
        }

        // --- 3b) لو لسه ناقص: واجهة "next" (لوحة التشغيل) بالفهرس بدل التوكن
        if (videos.Count > 0 && expected != null && videos.Count < expected)
        {
            try
            {
                var vid = videos[^1].Id;
                int idx = videos.Count - 1;
                for (int k = 0; k < 200; k++)
                {
                    var panel = await Call(new JsonObject
                    {
                        ["playlistId"] = plId,
                        ["videoId"] = vid,
                        ["playlistIndex"] = idx,
                    }, "next");

                    int newN = 0;
                    string? lastVid = null;
                    int? lastIdx = null;
                    foreach (var r in Walk(panel, "playlistPanelVideoRenderer"))
                    {
                        var v = Str(G(r, "videoId"));
                        if (v == "") continue;
                        var ixNode = G(G(G(r, "navigationEndpoint"), "watchEndpoint"), "index");
                        int? ix = ixNode is JsonValue jv && jv.TryGetValue<int>(out var iv) ? iv : null;
                        if (seen.Add(v))
                        {
                            var t = Text(G(r, "title"));
                            videos.Add((v, t == "" ? v : t));
                            newN++;
                        }
                        if (ix != null && (lastIdx == null || ix >= lastIdx))
                        {
                            lastVid = v;
                            lastIdx = ix;
                        }
                    }
                    if (newN == 0 || (expected != null && videos.Count >= expected)) break;
                    vid = lastVid ?? videos[^1].Id;
                    idx = lastIdx ?? videos.Count - 1;
                }
            }
            catch (Exception e) { errors.Add("next: " + e.Message); }
        }

        if (videos.Count > 0 && expected != null && videos.Count < expected && errors.Count == 0)
            errors.Add($"stopped at {videos.Count}: no continuation token / empty page");

        // --- 4) RSS (آخر 15 فقط) لو ما جبنا ولا فيديو
        if (videos.Count == 0)
        {
            try
            {
                var xml = await HttpGetString("https://www.youtube.com/feeds/videos.xml?playlist_id=" + plId);
                foreach (Match m in Regex.Matches(xml,
                    @"<yt:videoId>([^<]+)</yt:videoId>\s*<yt:channelId>[^<]*</yt:channelId>\s*<title>([^<]*)</title>"))
                {
                    var vid = m.Groups[1].Value;
                    if (seen.Add(vid))
                        videos.Add((vid, WebUtility.HtmlDecode(m.Groups[2].Value)));
                }
                var tm = Regex.Match(xml, "<title>([^<]+)</title>");
                if (tm.Success && title == plId) title = WebUtility.HtmlDecode(tm.Groups[1].Value);
            }
            catch (Exception e) { errors.Add("rss: " + e.Message); }
        }

        return new PlaylistResult(title, videos, errors, expected);
    }

    /// <summary>
    /// الطريقة الأقوى: yt-dlp.exe (لو موجود بجانب البرنامج أو في PATH).
    /// ترجع null لو غير متوفر أو فشل.
    /// </summary>
    private static async Task<(string Title, List<(string Id, string Title)> Videos, int? Expected)?>
        GetPlaylistYtdlpAsync(string plId, List<string> errors)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        var exe = File.Exists(local) ? local : "yt-dlp";

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[]
        {
            "--flat-playlist", "--dump-single-json", "--no-warnings", "--ignore-errors",
            "--socket-timeout", "20", $"https://www.youtube.com/playlist?list={plId}",
        })
            psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return null;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var json = await outTask;
            await errTask;
            if (string.IsNullOrWhiteSpace(json)) return null;

            var info = JsonNode.Parse(json);
            var videos = new List<(string Id, string Title)>();
            var seen = new HashSet<string>();
            if (G(info, "entries") is JsonArray entries)
            {
                foreach (var e in entries)
                {
                    var id = Str(G(e, "id"));
                    if (id.Length == 11 && seen.Add(id))
                    {
                        var t = Str(G(e, "title"));
                        videos.Add((id, t == "" ? id : t));
                    }
                }
            }
            var title = Str(G(info, "title"));
            int? count = G(info, "playlist_count") is JsonValue cv && cv.TryGetValue<int>(out var ci) ? ci : null;
            return (title == "" ? plId : title, videos, count);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;    // yt-dlp غير منصّب: نستخدم الطريقة المدمجة بصمت
        }
        catch (Exception e)
        {
            errors.Add("yt-dlp: " + e.Message);
            return null;
        }
    }

    public static async Task<PlaylistResult> GetPlaylistAsync(string plId)
    {
        var errors = new List<string>();
        (string Title, List<(string Id, string Title)> Videos, int? Expected)? best = null;

        var y = await GetPlaylistYtdlpAsync(plId, errors);
        if (y != null)
        {
            best = y;
            if (y.Value.Videos.Count > 0 &&
                (y.Value.Expected == null || y.Value.Videos.Count >= y.Value.Expected))
                return new PlaylistResult(y.Value.Title, y.Value.Videos, new List<string>(), y.Value.Expected);
            if (y.Value.Videos.Count == 0) errors.Add("yt-dlp: no videos returned");
        }

        var n = await GetPlaylistNativeAsync(plId);
        errors.AddRange(n.Errors);
        if (best != null && best.Value.Videos.Count >= n.Videos.Count)
            return new PlaylistResult(best.Value.Title, best.Value.Videos, errors, best.Value.Expected ?? n.Expected);
        return new PlaylistResult(n.Title, n.Videos, errors, n.Expected);
    }

    public static async Task<string> GetTitleAsync(string videoId)
    {
        try
        {
            var u = "https://www.youtube.com/oembed?format=json&url=" +
                    Uri.EscapeDataString($"https://www.youtube.com/watch?v={videoId}");
            var t = Str(G(JsonNode.Parse(await HttpGetString(u)), "title"));
            return t == "" ? videoId : t;
        }
        catch { return videoId; }
    }

    public static async Task<Bitmap> DownloadThumbnailAsync(string videoId)
    {
        foreach (var name in new[] { "maxresdefault", "sddefault", "hqdefault" })
        {
            try
            {
                var data = await HttpGetBytes($"https://img.youtube.com/vi/{videoId}/{name}.jpg");
                using var ms = new MemoryStream(data);
                using var raw = new Bitmap(ms);
                if (raw.Width >= 480)
                    return new Bitmap(raw);   // نسخة مستقلة عن الـ stream
            }
            catch { /* جرّب الاسم التالي */ }
        }
        throw new ThumbnailException();
    }

    // ---------------------------------------------------------------- الصور
    public static Bitmap Crop16x9(Bitmap img)
    {
        int w = img.Width, h = img.Height;
        int targetH = (int)Math.Round(w * 9.0 / 16.0);
        if (h > targetH)
        {
            int top = (h - targetH) / 2;
            return img.Clone(new Rectangle(0, top, w, targetH), PixelFormat.Format32bppArgb);
        }
        return new Bitmap(img);
    }

    /// <summary>
    /// للشورتس: يشيل الحواف السوداء الجانبية (يوتيوب بيحط الشورت في نص صورة 16:9)
    /// ثم يضبط الصورة على 9:16 (طولية).
    /// </summary>
    public static Bitmap Crop9x16(Bitmap img)
    {
        int w = img.Width, h = img.Height;
        int left = 0, right = w - 1;

        // 1) نقرأ البكسلات ونحدد أول وآخر عمود فيه محتوى (مش أسود)
        var rect = new Rectangle(0, 0, w, h);
        var data = img.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var buf = new byte[Math.Abs(stride) * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);

            bool ColumnHasContent(int x)
            {
                for (int y = 0; y < h; y += 2)
                {
                    int i = y * stride + x * 4;      // B, G, R, A
                    if (buf[i] > 24 || buf[i + 1] > 24 || buf[i + 2] > 24) return true;
                }
                return false;
            }

            int l = 0, r = w - 1;
            while (l < w && !ColumnHasContent(l)) l++;
            while (r > l && !ColumnHasContent(r)) r--;
            // حماية: لو الصورة كلها غامقة نتجاهل القص
            if (r - l + 1 >= w * 0.2) { left = l; right = r; }
        }
        finally { img.UnlockBits(data); }

        int cw = right - left + 1;

        // 2) لو لسه أعرض من 9:16 نقص من النص
        int targetW = (int)Math.Round(h * 9.0 / 16.0);
        if (cw > targetW)
        {
            left += (cw - targetW) / 2;
            cw = targetW;
        }
        return img.Clone(new Rectangle(left, 0, cw, h), PixelFormat.Format32bppArgb);
    }

    private static Bitmap ResizeSquare(Bitmap src, int s)
    {
        var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, s, s), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        return b;
    }

    /// <summary>يحوّل الصورة إلى .ico (256 × 256 بخلفية شفافة + أحجام أصغر، بصيغة PNG داخل ICO).</summary>
    public static void MakeIcon(Bitmap img, string path)
    {
        const int size = 256;
        // نصغّر الصورة لتناسب المربع (يشتغل للعرضي 16:9 والطولي 9:16)
        double scale = Math.Min((double)size / img.Width, (double)size / img.Height);
        int newW = Math.Max(1, (int)Math.Round(img.Width * scale));
        int newH = Math.Max(1, (int)Math.Round(img.Height * scale));

        using var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using var ia = new ImageAttributes();
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(img, new Rectangle((size - newW) / 2, (size - newH) / 2, newW, newH),
                        0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
        }

        var sizes = new[] { 256, 128, 64, 48, 32, 16 };
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
        {
            using var ms = new MemoryStream();
            if (s == size) canvas.Save(ms, ImageFormat.Png);
            else
            {
                using var small = ResizeSquare(canvas, s);
                small.Save(ms, ImageFormat.Png);
            }
            pngs.Add(ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);               // reserved
        bw.Write((short)1);               // type = icon
        bw.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            bw.Write(dim);                // width
            bw.Write(dim);                // height
            bw.Write((byte)0);            // colors
            bw.Write((byte)0);            // reserved
            bw.Write((short)1);           // planes
            bw.Write((short)32);          // bit count
            bw.Write(pngs[i].Length);
            bw.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) bw.Write(png);
    }

    // ---------------------------------------------------------------- الملفات
    public static string SafeName(string name)
    {
        name = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1f]", "").Trim(' ', '.');
        if (name.Length > 100) name = name[..100].TrimEnd(' ', '.');
        return name == "" ? "video" : name;
    }

    private static void ChangeAttr(string path, FileAttributes add = 0, FileAttributes remove = 0)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        var a = File.GetAttributes(path);
        a = (a | add) & ~remove;
        File.SetAttributes(path, a);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void ApplyFolderIcon(string folder, string iconFilename)
    {
        var ini = Path.Combine(folder, "desktop.ini");
        ChangeAttr(ini, remove: FileAttributes.System | FileAttributes.Hidden);
        File.WriteAllText(ini,
            $"[.ShellClassInfo]\r\nIconResource={iconFilename},0\r\n",
            new UnicodeEncoding(false, true));   // UTF-16 LE مع BOM
        ChangeAttr(ini, add: FileAttributes.System | FileAttributes.Hidden);
        ChangeAttr(Path.Combine(folder, iconFilename), add: FileAttributes.System | FileAttributes.Hidden);
        ChangeAttr(folder, add: FileAttributes.System);
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { /* تجاهل */ }
    }

    // ---------------------------------------------------------------- الدالة الرئيسية
    public static async Task<string> MakeVideoFolderAsync(string vid, string title, string baseDir,
                                                          bool shorts = false)
    {
        using var raw = await DownloadThumbnailAsync(vid);
        using var img = shorts ? Crop9x16(raw) : Crop16x9(raw);

        var folder = Path.Combine(baseDir, SafeName(title));
        Directory.CreateDirectory(folder);

        var iconName = $"{vid}.ico";
        var iconPath = Path.Combine(folder, iconName);
        ChangeAttr(iconPath, remove: FileAttributes.System | FileAttributes.Hidden);
        MakeIcon(img, iconPath);
        ApplyFolderIcon(folder, iconName);
        return folder;
    }

    public static async Task<string?> ProcessPlaylistAsync(string plId, string baseDir, Logger log,
                                                          Action<int, int>? progress = null,
                                                          bool shorts = false)
    {
        var pl = await GetPlaylistAsync(plId);
        if (pl.Videos.Count == 0)
        {
            log("playlist_empty", "error");
            foreach (var e in pl.Errors) log("err_generic", "error", new { error = e });
            return null;
        }
        if (pl.Errors.Count > 0 || (pl.Expected != null && pl.Videos.Count < pl.Expected))
        {
            log("err_generic", "error",
                new { error = $"got {pl.Videos.Count} of {(pl.Expected?.ToString() ?? "?")} videos" });
            foreach (var e in pl.Errors) log("err_generic", "error", new { error = e });
        }

        log("playlist_found", "info", new { title = pl.Title, count = pl.Videos.Count });
        var parent = Path.Combine(baseDir, SafeName(pl.Title));
        Directory.CreateDirectory(parent);

        for (int i = 0; i < pl.Videos.Count; i++)
        {
            var (vid, vt) = pl.Videos[i];
            try
            {
                log("processing", "info", new { title = vt });
                await MakeVideoFolderAsync(vid, vt, parent, shorts);
            }
            catch (ThumbnailException) { log("err_thumb", "error"); }
            catch (Exception e) { log("err_generic", "error", new { error = e.Message }); }
            progress?.Invoke(i + 1, pl.Videos.Count);
        }

        log("done_folder", "success", new { folder = parent });
        return parent;
    }

    /// <summary>
    /// playlist=true  ← لو الرابط فيه list= يحمّل البلاي لست كاملة.
    /// playlist=false ← يتجاهل البلاي لست ويعالج فيديو واحد فقط.
    /// </summary>
    public static async Task<string?> ProcessUrlAsync(string url, string baseDir, Logger log,
                                                      bool playlist = false, Action<int, int>? progress = null,
                                                      bool shorts = false)
    {
        if (playlist)
        {
            var plId = ExtractPlaylistId(url);
            if (plId != null) return await ProcessPlaylistAsync(plId, baseDir, log, progress, shorts);
        }

        var vid = ExtractVideoId(url);
        if (vid == null)
        {
            log("invalid_url", "error", new { url });
            return null;
        }

        var title = await GetTitleAsync(vid);
        log("processing", "info", new { title });
        var folder = await MakeVideoFolderAsync(vid, title, baseDir, shorts);
        log("done_folder", "success", new { folder });
        return folder;
    }
}
