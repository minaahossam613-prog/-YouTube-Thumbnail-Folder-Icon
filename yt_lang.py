"""
yt_lang.py  —  ملف اللغات (كل النصوص هنا)

لإضافة لغة جديدة:
  1) أضف اسمها في LANGUAGES  (الرمز: الاسم الذي يظهر في القائمة)
  2) أضف قاموساً بنفس المفاتيح في STRINGS
أي مفتاح ناقص يُعرض بالإنجليزية تلقائياً.
"""

DEFAULT_LANG = "en"

LANGUAGES = {
    "en": "English",
    "ru": "Русский",
    "zh": "中文",
    "es": "Español",
    "ar": "العربية",
}

STRINGS = {
    # ------------------------------------------------------------------ English
    "en": {
        "app_title": "YouTube Thumbnail → Folder Icon",
        "header": "🎬  Thumbnail Folder Icons",
        "theme": "Theme:",
        "language": "Language:",
        "urls_label": "YouTube links (one per line)",
        "save_to": "Save to",
        "browse": "Browse…",
        "create": "Create folders",
        "paste": "Paste from clipboard",
        "open_folder": "Open folder",
        "log": "Log",
        "warn_title": "Warning",
        "no_urls": "Paste at least one link",
        "error_title": "Error",
        "bad_dest": "Invalid save location:\n{error}",
        "invalid_url": "Invalid link: {url}",
        "processing": "• {title}",
        "done_folder": "✓ Done: {folder}",
        "err_thumb": "Could not download the thumbnail",
        "err_generic": "✗ Error: {error}",
        "finished": "Finished.",
        "playlist": "Full playlist",
        "playlist_found": "📃 Playlist: {title} ({count} videos)",
        "playlist_empty": "Could not find any videos in this playlist",
        "shorts": "Shorts (vertical 9:16 thumbnail)",
    },
    # ------------------------------------------------------------------ Русский
    "ru": {
        "app_title": "Обложка YouTube → значок папки",
        "header": "🎬  Значки папок из обложек",
        "theme": "Тема:",
        "language": "Язык:",
        "urls_label": "Ссылки на YouTube (по одной в строке)",
        "save_to": "Сохранить в",
        "browse": "Обзор…",
        "create": "Создать папки",
        "paste": "Вставить из буфера",
        "open_folder": "Открыть папку",
        "log": "Журнал",
        "warn_title": "Внимание",
        "no_urls": "Вставьте хотя бы одну ссылку",
        "error_title": "Ошибка",
        "bad_dest": "Недопустимое место сохранения:\n{error}",
        "invalid_url": "Недопустимая ссылка: {url}",
        "processing": "• {title}",
        "done_folder": "✓ Готово: {folder}",
        "err_thumb": "Не удалось загрузить обложку",
        "err_generic": "✗ Ошибка: {error}",
        "finished": "Завершено.",
        "playlist": "Весь плейлист",
        "playlist_found": "📃 Плейлист: {title} (видео: {count})",
        "playlist_empty": "В этом плейлисте не найдено видео",
        "shorts": "Shorts (вертикальная обложка 9:16)",
    },
    # ------------------------------------------------------------------ 中文
    "zh": {
        "app_title": "YouTube 缩略图 → 文件夹图标",
        "header": "🎬  缩略图文件夹图标",
        "theme": "主题：",
        "language": "语言：",
        "urls_label": "YouTube 链接（每行一个）",
        "save_to": "保存到",
        "browse": "浏览…",
        "create": "创建文件夹",
        "paste": "从剪贴板粘贴",
        "open_folder": "打开文件夹",
        "log": "日志",
        "warn_title": "提示",
        "no_urls": "请至少粘贴一个链接",
        "error_title": "错误",
        "bad_dest": "保存位置无效：\n{error}",
        "invalid_url": "链接无效：{url}",
        "processing": "• {title}",
        "done_folder": "✓ 完成：{folder}",
        "err_thumb": "无法下载缩略图",
        "err_generic": "✗ 错误：{error}",
        "finished": "已完成。",
        "playlist": "整个播放列表",
        "playlist_found": "📃 播放列表：{title}（{count} 个视频）",
        "playlist_empty": "在此播放列表中未找到视频",
        "shorts": "Shorts（竖版 9:16 缩略图）",
    },
    # ------------------------------------------------------------------ Español
    "es": {
        "app_title": "Miniatura de YouTube → Icono de carpeta",
        "header": "🎬  Iconos de carpeta con miniaturas",
        "theme": "Tema:",
        "language": "Idioma:",
        "urls_label": "Enlaces de YouTube (uno por línea)",
        "save_to": "Guardar en",
        "browse": "Examinar…",
        "create": "Crear carpetas",
        "paste": "Pegar del portapapeles",
        "open_folder": "Abrir carpeta",
        "log": "Registro",
        "warn_title": "Aviso",
        "no_urls": "Pega al menos un enlace",
        "error_title": "Error",
        "bad_dest": "Ubicación de guardado no válida:\n{error}",
        "invalid_url": "Enlace no válido: {url}",
        "processing": "• {title}",
        "done_folder": "✓ Listo: {folder}",
        "err_thumb": "No se pudo descargar la miniatura",
        "err_generic": "✗ Error: {error}",
        "finished": "Terminado.",
        "playlist": "Lista completa",
        "playlist_found": "📃 Lista: {title} ({count} vídeos)",
        "playlist_empty": "No se encontraron vídeos en esta lista",
        "shorts": "Shorts (miniatura vertical 9:16)",
    },
    # ------------------------------------------------------------------ العربية
    "ar": {
        "app_title": "ثمنيل يوتيوب → أيقونة فولدر",
        "header": "🎬  أيقونات فولدرات من الثمنيل",
        "theme": "الثيم:",
        "language": "اللغة:",
        "urls_label": "روابط يوتيوب (رابط في كل سطر)",
        "save_to": "مكان الحفظ",
        "browse": "اختيار…",
        "create": "إنشاء الفولدرات",
        "paste": "لصق من الحافظة",
        "open_folder": "فتح الفولدر",
        "log": "السجل",
        "warn_title": "تنبيه",
        "no_urls": "الصق رابطاً واحداً على الأقل",
        "error_title": "خطأ",
        "bad_dest": "مكان الحفظ غير صالح:\n{error}",
        "invalid_url": "رابط غير صالح: {url}",
        "processing": "• {title}",
        "done_folder": "✓ تم: {folder}",
        "err_thumb": "تعذّر تحميل الثمنيل",
        "err_generic": "✗ خطأ: {error}",
        "finished": "انتهى.",
        "playlist": "تحميل البلاي لست كاملة",
        "playlist_found": "📃 بلاي لست: {title} ({count} فيديو)",
        "playlist_empty": "لم يتم العثور على فيديوهات في البلاي لست",
        "shorts": "فيديوهات شورتس (ثمنيل طولي 9:16)",
    },
}


def t(lang: str, key: str, **kwargs) -> str:
    """ترجع النص بلغة lang مع استبدال المتغيرات {name}."""
    text = STRINGS.get(lang, {}).get(key) or STRINGS[DEFAULT_LANG].get(key, key)
    try:
        return text.format(**kwargs)
    except (KeyError, IndexError):
        return text


def lang_from_name(name: str) -> str:
    """من الاسم المعروض في القائمة إلى رمز اللغة."""
    for code, label in LANGUAGES.items():
        if label == name:
            return code
    return DEFAULT_LANG
