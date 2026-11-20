"""
yt_folder_icon_ui.py  —  ملف الواجهة (ttkbootstrap + تعدد اللغات)

الملفات الثلاثة يجب أن تكون في نفس المجلد:
    yt_folder_icon_ui.py   (الواجهة)
    yt_core.py             (برمجة التطبيق)
    yt_lang.py             (اللغات)

التشغيل:
    python -m pip install -r requirements.txt
    python yt_folder_icon_ui.py

لبناء نسخة Windows متعددة الملفات (مجلد يحتوي على EXE والملفات المطلوبة):
    build_exe.bat
ثم شغّل dist\\YTFolderIcon\\YTFolderIcon.exe مع إبقاء مجلد YTFolderIcon كاملاً.
"""
import os
import threading
import tkinter as tk
from tkinter import filedialog, messagebox

import ttkbootstrap as ttk
from ttkbootstrap.constants import *

import yt_core
import yt_lang


class App(ttk.Window):
    def __init__(self):
        self.cfg = yt_core.load_config()
        self.lang = self.cfg.get("lang", yt_lang.DEFAULT_LANG)
        if self.lang not in yt_lang.LANGUAGES:
            self.lang = yt_lang.DEFAULT_LANG
        try:
            super().__init__(themename=self.cfg["theme"], size=(780, 660))
        except tk.TclError:  # ثيم محفوظ غير موجود
            super().__init__(themename="darkly", size=(780, 660))
        self.minsize(660, 580)

        self.dest = tk.StringVar(value=self.cfg["dest"])
        self.playlist_var = tk.BooleanVar(value=bool(self.cfg.get("playlist", False)))
        self.shorts_var = tk.BooleanVar(value=bool(self.cfg.get("shorts", False)))
        self.theme_var = tk.StringVar(value=self.style.theme.name)
        self.lang_var = tk.StringVar(value=yt_lang.LANGUAGES[self.lang])
        self.last_folder = None
        self._texts = []  # (widget, key, option)  لإعادة الترجمة الفورية

        self._build_header()
        self._build_body()
        self._build_log()
        self._apply_text_colors()
        self._retranslate()
        self.protocol("WM_DELETE_WINDOW", self._on_close)

    # ------------------------------------------------------------ الترجمة
    def t(self, key, **kw):
        return yt_lang.t(self.lang, key, **kw)

    def _tr(self, widget, key, option="text"):
        """سجّل عنصراً ليُترجم تلقائياً عند تغيير اللغة."""
        self._texts.append((widget, key, option))
        return widget

    def _retranslate(self):
        self.title(self.t("app_title"))
        for widget, key, option in self._texts:
            widget.configure(**{option: self.t(key)})

    def _change_language(self, _=None):
        self.lang = yt_lang.lang_from_name(self.lang_var.get())
        self._retranslate()

    # ------------------------------------------------------------ الواجهة
    def _build_header(self):
        bar = ttk.Frame(self, padding=(15, 12))
        bar.pack(fill=X)

        self._tr(ttk.Label(bar, font=("Segoe UI", 16, "bold"), bootstyle=PRIMARY),
                 "header").pack(side=LEFT)

        # اللغة
        lang_box = ttk.Frame(bar)
        lang_box.pack(side=RIGHT, padx=(12, 0))
        self._tr(ttk.Label(lang_box), "language").pack(side=LEFT, padx=(0, 6))
        lang_combo = ttk.Combobox(lang_box, textvariable=self.lang_var,
                                  values=list(yt_lang.LANGUAGES.values()),
                                  state="readonly", width=11)
        lang_combo.pack(side=LEFT)
        lang_combo.bind("<<ComboboxSelected>>", self._change_language)

        # الثيم
        theme_box = ttk.Frame(bar)
        theme_box.pack(side=RIGHT)
        self._tr(ttk.Label(theme_box), "theme").pack(side=LEFT, padx=(0, 6))
        theme_combo = ttk.Combobox(theme_box, textvariable=self.theme_var,
                                   values=sorted(self.style.theme_names()),
                                   state="readonly", width=14)
        theme_combo.pack(side=LEFT)
        theme_combo.bind("<<ComboboxSelected>>", self._change_theme)

        ttk.Separator(self).pack(fill=X)

    def _build_body(self):
        body = ttk.Frame(self, padding=15)
        body.pack(fill=X)

        self._tr(ttk.Label(body), "urls_label").pack(anchor=W)

        box = ttk.Frame(body)
        box.pack(fill=X, pady=(4, 10))
        self.urls = tk.Text(box, height=7, wrap="none", relief="flat", padx=8, pady=6)
        sb = ttk.Scrollbar(box, command=self.urls.yview, bootstyle="round")
        self.urls.configure(yscrollcommand=sb.set)
        sb.pack(side=RIGHT, fill=Y)
        self.urls.pack(side=LEFT, fill=X, expand=True)

        row = ttk.Frame(body)
        row.pack(fill=X, pady=(0, 10))
        self._tr(ttk.Label(row), "save_to").pack(side=LEFT, padx=(0, 8))
        ttk.Entry(row, textvariable=self.dest).pack(side=LEFT, fill=X, expand=True)
        self._tr(ttk.Button(row, command=self._browse, bootstyle="secondary-outline"),
                 "browse").pack(side=LEFT, padx=(8, 0))

        pl_row = ttk.Frame(body)
        pl_row.pack(fill=X, pady=(0, 10))
        self._tr(ttk.Checkbutton(pl_row, variable=self.playlist_var,
                                 bootstyle="success-round-toggle"),
                 "playlist").pack(side=LEFT)
        # خيار الشورتس (ثمنيل طولي 9:16)
        self._tr(ttk.Checkbutton(pl_row, variable=self.shorts_var,
                                 bootstyle="info-round-toggle"),
                 "shorts").pack(side=LEFT, padx=(20, 0))

        btns = ttk.Frame(body)
        btns.pack(fill=X)
        self.run_btn = self._tr(ttk.Button(btns, command=self._run, bootstyle=SUCCESS),
                                "create")
        self.run_btn.pack(side=LEFT, fill=X, expand=True)
        self._tr(ttk.Button(btns, command=self._paste, bootstyle="info-outline"),
                 "paste").pack(side=LEFT, padx=(8, 0))
        self.open_btn = self._tr(
            ttk.Button(btns, command=self._open_last, bootstyle="warning-outline",
                       state=DISABLED), "open_folder")
        self.open_btn.pack(side=LEFT, padx=(8, 0))

        self.progress = ttk.Progressbar(body, bootstyle="success-striped", maximum=100)
        self.progress.pack(fill=X, pady=(12, 0))

    def _build_log(self):
        frame = self._tr(ttk.Labelframe(self, padding=8), "log")
        frame.pack(fill=BOTH, expand=True, padx=15, pady=(0, 15))
        self.logbox = tk.Text(frame, height=8, state="disabled", relief="flat",
                              padx=8, pady=6, wrap="word")
        sb = ttk.Scrollbar(frame, command=self.logbox.yview, bootstyle="round")
        self.logbox.configure(yscrollcommand=sb.set)
        sb.pack(side=RIGHT, fill=Y)
        self.logbox.pack(side=LEFT, fill=BOTH, expand=True)

    # ------------------------------------------------------------ الثيمات
    def _change_theme(self, _=None):
        self.style.theme_use(self.theme_var.get())
        self._apply_text_colors()

    def _apply_text_colors(self):
        c = self.style.colors
        for w in (self.urls, self.logbox):
            w.configure(bg=c.inputbg, fg=c.inputfg, insertbackground=c.inputfg,
                        selectbackground=c.selectbg, selectforeground=c.selectfg,
                        highlightthickness=1, highlightbackground=c.border,
                        highlightcolor=c.primary)
        self.logbox.tag_config("info", foreground=c.inputfg)
        self.logbox.tag_config("success", foreground=c.success)
        self.logbox.tag_config("error", foreground=c.danger)

    # ------------------------------------------------------------ الأحداث
    def _browse(self):
        d = filedialog.askdirectory(initialdir=self.dest.get())
        if d:
            self.dest.set(d)

    def _paste(self):
        try:
            self.urls.insert("end", self.clipboard_get().strip() + "\n")
        except tk.TclError:
            pass

    def _open_last(self):
        path = self.last_folder or self.dest.get()
        if path and os.path.isdir(path):
            os.startfile(path)

    def log(self, key, level="info", **kw):
        """تكتب رسالة في السجل بلغة الواجهة الحالية."""
        def _w():
            self.logbox.configure(state="normal")
            self.logbox.insert("end", self.t(key, **kw) + "\n", level)
            self.logbox.see("end")
            self.logbox.configure(state="disabled")
        self.after(0, _w)

    def _run(self):
        urls = [u for u in self.urls.get("1.0", "end").splitlines() if u.strip()]
        if not urls:
            messagebox.showwarning(self.t("warn_title"), self.t("no_urls"))
            return
        base = self.dest.get().strip()
        try:
            os.makedirs(base, exist_ok=True)
        except OSError as e:
            messagebox.showerror(self.t("error_title"), self.t("bad_dest", error=e))
            return

        self.run_btn.configure(state=DISABLED)
        self.progress.configure(value=0)

        use_playlist = self.playlist_var.get()
        use_shorts = self.shorts_var.get()

        def work():
            total = len(urls)
            for i, u in enumerate(urls, 1):
                base_pct = (i - 1) / total * 100
                span = 100 / total

                def prog(done, n, b=base_pct, s=span):
                    self.after(0, lambda v=b + s * done / n: self.progress.configure(value=v))

                try:
                    folder = yt_core.process_url(u, base, self.log,
                                                 playlist=use_playlist, progress=prog,
                                                 shorts=use_shorts)
                    if folder:
                        self.last_folder = folder
                except yt_core.ThumbnailError:
                    self.log("err_thumb", "error")
                except Exception as e:
                    self.log("err_generic", "error", error=e)
                self.after(0, lambda v=i / total * 100: self.progress.configure(value=v))
            self.log("finished", "success")
            self.after(0, self._done)

        threading.Thread(target=work, daemon=True).start()

    def _done(self):
        self.run_btn.configure(state=NORMAL)
        self.open_btn.configure(state=NORMAL)

    def _on_close(self):
        yt_core.save_config({"theme": self.theme_var.get(),
                             "dest": self.dest.get(),
                             "lang": self.lang,
                             "playlist": self.playlist_var.get(),
                             "shorts": self.shorts_var.get()})
        self.destroy()


if __name__ == "__main__":
    App().mainloop()
