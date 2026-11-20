"""
yt_core.py  —  ملف برمجة التطبيق (المنطق فقط، بدون واجهة)

- تحميل ثمنيل يوتيوب
- قص الحواف السوداء إلى 16:9
- تحويله إلى .ico بخلفية شفافة
- إنشاء فولدر وضبط desktop.ini (اسم الأيقونة فقط بدون مسار)
- حفظ/قراءة إعدادات المستخدم (الثيم + مكان الحفظ)
"""
import ctypes
import io
import json
import os
import re
import subprocess
import time
import urllib.parse
import urllib.request

from PIL import Image

UA = {
    "User-Agent": ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
                   "(KHTML, like Gecko) Chrome/124.0 Safari/537.36"),
    "Accept-Language": "en-US,en;q=0.9",
    "Cookie": "CONSENT=YES+1; SOCS=CAI",
}
_PUBLIC_KEY = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8"
_PUBLIC_VER = "2.20250101.00.00"


class ThumbnailError(Exception):
    """تعذّر تحميل الثمنيل (الواجهة تترجم الرسالة حسب اللغة)."""
CONFIG_PATH = os.path.join(os.path.expanduser("~"), ".yt_folder_icon.json")
DEFAULT_CONFIG = {
    "theme": "darkly",
    "lang": "en",
    "dest": os.path.join(os.path.expanduser("~"), "Videos"),
    "playlist": False,
    "shorts": False,
}


# ---------------------------------------------------------------- الإعدادات
def load_config() -> dict:
    cfg = dict(DEFAULT_CONFIG)
    try:
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            cfg.update(json.load(f))
    except Exception:
        pass
    return cfg


def save_config(cfg: dict):
    try:
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump(cfg, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


# ---------------------------------------------------------------- الشبكة
def extract_video_id(url: str):
    url = url.strip()
    m = re.search(r"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})", url)
    if m:
        return m.group(1)
    if re.fullmatch(r"[A-Za-z0-9_-]{11}", url):
        return url
    return None


def http_get(url: str) -> bytes:
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=20) as r:
        return r.read()


def http_post_json(url: str, payload: dict) -> dict:
    req = urllib.request.Request(
        url, data=json.dumps(payload).encode("utf-8"),
        headers={**UA, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=20) as r:
        return json.loads(r.read().decode("utf-8"))


def extract_playlist_id(url: str):
    m = re.search(r"[?&]list=([A-Za-z0-9_-]+)", url)
    return m.group(1) if m else None


def _walk(obj, key):
    """يمشي داخل JSON ويرجع كل قيمة تحت المفتاح key."""
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == key:
                yield v
            else:
                yield from _walk(v, key)
    elif isinstance(obj, list):
        for i in obj:
            yield from _walk(i, key)


def _text(node) -> str:
    if not isinstance(node, dict):
        return ""
    if "simpleText" in node:
        return node["simpleText"]
    if "content" in node:
        return node["content"]
    return "".join(r.get("text", "") for r in node.get("runs", []))


def _tok(c):
    return (c.get("continuationEndpoint", {})
             .get("continuationCommand", {}).get("token"))


def _pick_token(d):
    """توكن الصفحة التالية: نفضّل اللي في نفس القائمة مع الفيديوهات."""
    best, fallback = [None], [None]

    def visit(o):
        if isinstance(o, list):
            has_vid = any(isinstance(x, dict) and
                          ("playlistVideoRenderer" in x or "lockupViewModel" in x)
                          for x in o)
            for x in o:
                if isinstance(x, dict) and "continuationItemRenderer" in x:
                    t = _tok(x["continuationItemRenderer"])
                    if t:
                        fallback[0] = t
                        if has_vid:
                            best[0] = t
            for x in o:
                visit(x)
        elif isinstance(o, dict):
            for v in o.values():
                visit(v)

    visit(d)
    if not (best[0] or fallback[0]):
        for c in _walk(d, "continuationCommand"):
            if isinstance(c, dict) and c.get("token"):
                fallback[0] = c["token"]
    return best[0] or fallback[0]


def _collect(d, videos, seen):
    """يستخرج الفيديوهات (بالشكلين القديم والجديد) ويرجع توكن المتابعة."""
    for r in _walk(d, "playlistVideoRenderer"):
        vid = r.get("videoId")
        if vid and vid not in seen:
            seen.add(vid)
            videos.append((vid, _text(r.get("title")) or vid))
    for r in _walk(d, "lockupViewModel"):
        if r.get("contentType") not in (None, "LOCKUP_CONTENT_TYPE_VIDEO"):
            continue
        vid = r.get("contentId")
        if vid and len(vid) == 11 and vid not in seen:
            seen.add(vid)
            meta = r.get("metadata", {}).get("lockupMetadataViewModel", {})
            videos.append((vid, _text(meta.get("title")) or vid))
    return _pick_token(d)


def _expected_count(d):
    """عدد فيديوهات البلاي لست المعلن في الصفحة (للتحقق فقط)."""
    try:
        for h in _walk(d, "playlistHeaderRenderer"):
            n = re.sub(r"\D", "", _text(h.get("numVideosText")))
            if n:
                return int(n)
        for sb in _walk(d, "playlistSidebarPrimaryInfoRenderer"):
            n = re.sub(r"\D", "", _text(sb.get("stats", [{}])[0]))
            if n:
                return int(n)
    except Exception:
        pass
    return None


def _playlist_title(d, default):
    for path in (("metadata", "playlistMetadataRenderer", "title"),):
        try:
            x = d
            for p in path:
                x = x[p]
            if isinstance(x, str) and x:
                return x
        except Exception:
            pass
    for h in _walk(d, "playlistHeaderRenderer"):
        t = _text(h.get("title"))
        if t:
            return t
    return default


def _innertube(ctx, body, endpoint="browse"):
    return http_post_json(
        f"https://www.youtube.com/youtubei/v1/{endpoint}?key={ctx['key']}&prettyPrint=false",
        {"context": {"client": ctx["client"]}, **body})


def _get_playlist_native(pl_id: str):
    """ترجع (اسم البلاي لست, [(video_id, title), ...], [أخطاء], العدد المتوقع)."""
    errors, title, videos, seen = [], pl_id, [], set()
    expected = None
    base_client = {"clientName": "WEB", "hl": "en", "gl": "US"}
    ctxs = []          # سياقات الطلب (الحديث من الصفحة أولاً ثم الاحتياطي)
    token = None

    # --- 1) صفحة البلاي لست: أول دفعة + مفتاح وإصدار حديثين
    try:
        html = http_get(f"https://www.youtube.com/playlist?list={pl_id}&hl=en"
                        ).decode("utf-8", "ignore")
        kk = re.search(r'"INNERTUBE_API_KEY":"([^"]+)"', html)
        vv = re.search(r'"INNERTUBE_CLIENT_VERSION":"([^"]+)"', html)
        vd = re.search(r'"visitorData":"([^"]+)"', html)
        if kk and vv:
            client = dict(base_client, clientVersion=vv.group(1))
            if vd:
                client["visitorData"] = vd.group(1)
            ctxs.append({"key": kk.group(1), "client": client})
        m = re.search(r"ytInitialData\s*=\s*", html)
        if not m:
            raise ValueError("ytInitialData not found")
        data, _ = json.JSONDecoder().raw_decode(html[m.end():])
        title = _playlist_title(data, title)
        expected = _expected_count(data)
        token = _collect(data, videos, seen)
    except Exception as e:
        errors.append(f"html: {e}")
    ctxs.append({"key": _PUBLIC_KEY,
                 "client": dict(base_client, clientVersion=_PUBLIC_VER)})

    def call(body, endpoint="browse"):
        """يجرّب كل السياقات، ومع كل واحد 3 محاولات."""
        last = None
        for ctx in ctxs:
            for attempt in range(3):
                try:
                    return _innertube(ctx, body, endpoint)
                except Exception as e:
                    last = e
                    time.sleep(1 + attempt)
        raise last

    # --- 2) لو الصفحة فشلت: الدفعة الأولى من الواجهة الداخلية
    if not videos:
        try:
            data = call({"browseId": "VL" + pl_id})
            title = _playlist_title(data, title)
            token = _collect(data, videos, seen)
        except Exception as e:
            errors.append(f"innertube: {e}")

    # --- 3) باقي الدفعات (بدون حد عملي: كل البلاي لست)
    pages = 0
    while token and pages < 2000:
        pages += 1
        try:
            data = call({"continuation": token})
        except Exception as e:
            errors.append(f"continuation (after {len(videos)} videos): {e}")
            break
        before = len(videos)
        token = _collect(data, videos, seen)
        if len(videos) == before and not token:
            break

    # --- 3b) لو لسه ناقص: واجهة "next" (لوحة التشغيل) بالفهرس بدل التوكن
    if videos and expected and len(videos) < expected:
        try:
            vid, idx = videos[-1][0], len(videos) - 1
            for _ in range(200):
                data = call({"playlistId": pl_id, "videoId": vid, "playlistIndex": idx},
                            "next")
                new_n, last_vid, last_idx = 0, None, None
                for r in _walk(data, "playlistPanelVideoRenderer"):
                    v = r.get("videoId")
                    if not v:
                        continue
                    ix = (r.get("navigationEndpoint", {}).get("watchEndpoint", {})
                           .get("index"))
                    if v not in seen:
                        seen.add(v)
                        videos.append((v, _text(r.get("title")) or v))
                        new_n += 1
                    if ix is not None and (last_idx is None or ix >= last_idx):
                        last_vid, last_idx = v, ix
                if not new_n or (expected and len(videos) >= expected):
                    break
                vid = last_vid or videos[-1][0]
                idx = last_idx if last_idx is not None else len(videos) - 1
        except Exception as e:
            errors.append(f"next: {e}")

    if videos and expected and len(videos) < expected and not errors:
        errors.append(f"stopped at {len(videos)}: no continuation token / empty page")

    # --- 4) RSS (آخر 15 فقط) لو ما جبنا ولا فيديو
    if not videos:
        try:
            xml = http_get("https://www.youtube.com/feeds/videos.xml?playlist_id="
                           + pl_id).decode("utf-8", "ignore")
            for vid, vt in re.findall(
                    r"<yt:videoId>([^<]+)</yt:videoId>\s*<yt:channelId>[^<]*</yt:channelId>\s*<title>([^<]*)</title>", xml):
                if vid not in seen:
                    seen.add(vid)
                    videos.append((vid, vt.replace("&amp;", "&").replace("&quot;", '"')
                                   .replace("&#39;", "'").replace("&lt;", "<").replace("&gt;", ">")))
            m = re.search(r"<title>([^<]+)</title>", xml)
            if m and title == pl_id:
                title = m.group(1)
        except Exception as e:
            errors.append(f"rss: {e}")

    return title, videos, errors, expected


def _get_playlist_ytdlp(pl_id: str):
    """الطريقة الأقوى: مكتبة yt-dlp (لو منصّبة). ترجع (اسم, فيديوهات, عدد متوقع)."""
    import yt_dlp

    class _Quiet:
        def debug(self, m): pass
        def info(self, m): pass
        def warning(self, m): pass
        def error(self, m): pass

    opts = {"extract_flat": "in_playlist", "quiet": True, "no_warnings": True,
            "skip_download": True, "ignoreerrors": True, "logger": _Quiet(),
            "socket_timeout": 20}
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(f"https://www.youtube.com/playlist?list={pl_id}",
                                download=False)
    if not info:
        return pl_id, [], None
    videos, seen = [], set()
    for e in info.get("entries") or []:
        vid = (e or {}).get("id")
        if vid and len(vid) == 11 and vid not in seen:
            seen.add(vid)
            videos.append((vid, e.get("title") or vid))
    return info.get("title") or pl_id, videos, info.get("playlist_count")


def get_playlist(pl_id: str):
    """ترجع (اسم, [(id, title)...], [أخطاء], العدد المتوقع). yt-dlp أولاً ثم الطريقة المدمجة."""
    errors, best = [], None

    try:
        t, v, exp = _get_playlist_ytdlp(pl_id)
        best = (t, v, exp)
        if v and (not exp or len(v) >= exp):
            return t, v, [], exp
        if not v:
            errors.append("yt-dlp: no videos returned")
    except ImportError:
        errors.append("yt-dlp not installed (pip install yt-dlp) - using built-in method")
    except Exception as e:
        errors.append(f"yt-dlp: {e}")

    t2, v2, e2, exp2 = _get_playlist_native(pl_id)
    errors += e2
    if best and len(best[1]) >= len(v2):
        return best[0], best[1], errors, best[2] or exp2
    return t2, v2, errors, exp2


def get_title(video_id: str) -> str:
    try:
        u = ("https://www.youtube.com/oembed?format=json&url="
             + urllib.parse.quote(f"https://www.youtube.com/watch?v={video_id}", safe=""))
        return json.loads(http_get(u).decode("utf-8"))["title"]
    except Exception:
        return video_id


def download_thumbnail(video_id: str) -> Image.Image:
    for name in ("maxresdefault", "sddefault", "hqdefault"):
        try:
            data = http_get(f"https://img.youtube.com/vi/{video_id}/{name}.jpg")
            img = Image.open(io.BytesIO(data)).convert("RGB")
            if img.width >= 480:
                return img
        except Exception:
            continue
    raise ThumbnailError()


# ---------------------------------------------------------------- الصور
def crop_16x9(img: Image.Image) -> Image.Image:
    w, h = img.size
    target_h = round(w * 9 / 16)
    if h > target_h:
        top = (h - target_h) // 2
        img = img.crop((0, top, w, top + target_h))
    return img


def crop_9x16(img: Image.Image) -> Image.Image:
    """للشورتس: يشيل الحواف السوداء الجانبية ثم يضبط الصورة على 9:16 (طولية)."""
    w, h = img.size
    # 1) قص الحواف السوداء يمين/شمال (يوتيوب بيحط الشورت في نص صورة 16:9)
    mask = img.convert("L").point(lambda p: 255 if p > 24 else 0)
    box = mask.getbbox()
    if box:
        left, _, right, _ = box
        if (right - left) >= w * 0.2:      # حماية: لو الصورة كلها غامقة نتجاهل القص
            img = img.crop((left, 0, right, h))
    # 2) لو لسه أعرض من 9:16 نقص من النص
    w, h = img.size
    target_w = round(h * 9 / 16)
    if w > target_w:
        left = (w - target_w) // 2
        img = img.crop((left, 0, left + target_w, h))
    return img


def make_icon(img: Image.Image, path: str):
    size = 256
    w, h = img.size
    # نصغّر الصورة لتناسب المربع (يشتغل للعرضي 16:9 والطولي 9:16)
    scale = min(size / w, size / h)
    new_w, new_h = max(1, round(w * scale)), max(1, round(h * scale))
    img = img.resize((new_w, new_h), Image.LANCZOS).convert("RGBA")
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(img, ((size - new_w) // 2, (size - new_h) // 2))
    canvas.save(path, format="ICO",
                sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])


# ---------------------------------------------------------------- الملفات
def safe_name(name: str) -> str:
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "", name).strip(" .")
    return name[:100] or "video"


def _attrib(flags, path):
    subprocess.run(["attrib", *flags, path], creationflags=0x08000000, check=False)


def apply_folder_icon(folder: str, icon_filename: str):
    ini_path = os.path.join(folder, "desktop.ini")
    if os.path.exists(ini_path):
        _attrib(["-s", "-h"], ini_path)
    content = f"[.ShellClassInfo]\r\nIconResource={icon_filename},0\r\n"
    with open(ini_path, "w", encoding="utf-16") as f:
        f.write(content)
    _attrib(["+s", "+h"], ini_path)
    _attrib(["+s", "+h"], os.path.join(folder, icon_filename))
    _attrib(["+s"], folder)
    try:
        ctypes.windll.shell32.SHChangeNotify(0x08000000, 0, None, None)
    except Exception:
        pass


# ---------------------------------------------------------------- الدالة الرئيسية
def _default_log(key, level="info", **kw):
    print(key, level, kw)


def make_video_folder(vid: str, title: str, base_dir: str, shorts: bool = False) -> str:
    thumb = download_thumbnail(vid)
    img = crop_9x16(thumb) if shorts else crop_16x9(thumb)
    folder = os.path.join(base_dir, safe_name(title))
    os.makedirs(folder, exist_ok=True)

    icon_name = f"{vid}.ico"
    icon_path = os.path.join(folder, icon_name)
    if os.path.exists(icon_path):
        _attrib(["-s", "-h"], icon_path)
    make_icon(img, icon_path)
    apply_folder_icon(folder, icon_name)
    return folder


def process_playlist(pl_id: str, base_dir: str, log=_default_log, progress=None,
                     shorts: bool = False):
    title, videos, errors, expected = get_playlist(pl_id)
    if not videos:
        log("playlist_empty", "error")
        for e in errors:
            log("err_generic", "error", error=e)
        return None
    if errors or (expected and len(videos) < expected):
        log("err_generic", "error",
            error=f"got {len(videos)} of {expected or '?'} videos")
        for e in errors:
            log("err_generic", "error", error=e)

    log("playlist_found", "info", title=title, count=len(videos))
    parent = os.path.join(base_dir, safe_name(title))
    os.makedirs(parent, exist_ok=True)

    for i, (vid, vt) in enumerate(videos, 1):
        try:
            log("processing", "info", title=vt)
            make_video_folder(vid, vt, parent, shorts)
        except ThumbnailError:
            log("err_thumb", "error")
        except Exception as e:
            log("err_generic", "error", error=e)
        if progress:
            progress(i, len(videos))

    log("done_folder", "success", folder=parent)
    return parent


def process_url(url: str, base_dir: str, log=_default_log,
                playlist: bool = False, progress=None,
                shorts: bool = False) -> str | None:
    """
    playlist=True  <- لو الرابط فيه list= يحمّل البلاي لست كاملة.
    playlist=False <- يتجاهل البلاي لست ويعالج فيديو واحد فقط.
    """
    if playlist:
        pl_id = extract_playlist_id(url)
        if pl_id:
            return process_playlist(pl_id, base_dir, log, progress, shorts)

    vid = extract_video_id(url)
    if not vid:
        log("invalid_url", "error", url=url)
        return None

    title = get_title(vid)
    log("processing", "info", title=title)
    folder = make_video_folder(vid, title, base_dir, shorts)
    log("done_folder", "success", folder=folder)
    return folder
