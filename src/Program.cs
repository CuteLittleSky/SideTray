// SPDX-License-Identifier: LGPL-3.0-or-later
// 副屏任务栏音频控制 —— 把音频输出设备切换做成副屏任务栏里的伪托盘
// Copyright (C) 2026 CuteLittleSky
//
// 本程序是自由软件：你可以依据 GNU 宽通用公共许可证第 3 版（或更高版本）
// 的条款重新发布和/或修改它。本程序不提供任何担保。详见 LICENSE 与 LICENSE.GPL-3.0。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AudioSw;
using Microsoft.Win32;

namespace SideTray
{
    static class Program
    {
        public static bool Exiting;
        public static Mutex AppMutex;

        [STAThread]
        static void Main()
        {
            bool created;
            AppMutex = new Mutex(true, "SideTray_SingleInstance_v1", out created);
            if (!created)
            {
                // 可能是刚刚被看门狗重启的，前一个进程还没退干净，最多等 4 秒
                try { created = AppMutex.WaitOne(4000); } catch { created = false; }
                if (!created) return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext());
        }

        // 任务栏重建导致我们的窗口消失时用：放掉单实例锁，拉起新进程，自己退出
        public static void RestartProcess()
        {
            try
            {
                try { AppMutex.ReleaseMutex(); } catch { }
                try { AppMutex.Dispose(); } catch { }
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.W("RESTART FAILED " + ex.Message); }
            Exiting = true;
            Environment.Exit(0);
        }
    }

    class Config
    {
        public int X = int.MinValue;
        public int Y = int.MinValue;
        public bool TopMost = true;
        public string Mode = "taskbar";   // taskbar | float
        public bool ShrinkTaskList = false;
        public bool SwitchInput = true;                 // 切输出时一起切麦克风
        public string BgColor = "";                      // 上次采到的任务栏颜色 "r,g,b"，冷启动兜底
        public List<string[]> Pairs = new List<string[]>();  // 手动配对：{输出名/ID, 输入名/ID}

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideTray"); }
        }
        public static string FilePath { get { return Path.Combine(Dir, "config.txt"); } }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int hash = v.IndexOf('#');
                    if (hash >= 0) v = v.Substring(0, hash).Trim();
                    switch (k)
                    {
                        case "x": int ix; if (int.TryParse(v, out ix)) c.X = ix; break;
                        case "y": int iy; if (int.TryParse(v, out iy)) c.Y = iy; break;
                        case "topmost": c.TopMost = v != "0"; break;
                        case "mode": if (v == "float" || v == "taskbar") c.Mode = v; break;
                        case "shrink": c.ShrinkTaskList = v != "0"; break;
                        case "switchinput": c.SwitchInput = v != "0"; break;
                        case "bgcolor": c.BgColor = v; break;
                        case "pair":
                            {
                                var parts = v.Split('|');
                                if (parts.Length == 2) c.Pairs.Add(new[] { parts[0].Trim(), parts[1].Trim() });
                            }
                            break;
                    }
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# SideTray 配置，改完重启程序生效");
                sb.AppendLine("# mode: taskbar = 嵌进副屏任务栏, float = 浮动面板");
                sb.AppendLine("mode=" + Mode);
                sb.AppendLine("topmost=" + (TopMost ? "1" : "0"));
                sb.AppendLine("# shrink: 1 = 嵌入时缩窄任务按钮区给它让位（会跟 TrafficMonitor 抢布局，默认 0）");
                sb.AppendLine("shrink=" + (ShrinkTaskList ? "1" : "0"));
                sb.AppendLine("# switchinput: 1 = 切换输出设备时把麦克风也切到配对的设备");
                sb.AppendLine("switchinput=" + (SwitchInput ? "1" : "0"));
                if (!string.IsNullOrEmpty(BgColor)) sb.AppendLine("bgcolor=" + BgColor);
                foreach (var pr in Pairs) sb.AppendLine("pair=" + pr[0] + "|" + pr[1]);
                sb.AppendLine("x=" + X);
                sb.AppendLine("y=" + Y);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    static class Log
    {
        static readonly object pad = new object();
        public static readonly string File_ = Path.Combine(Config.Dir, "log.txt");
        public static void W(string s)
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                lock (pad) { if (File.Exists(File_) && new FileInfo(File_).Length > 300000) File.Delete(File_); }
                File.AppendAllText(File_, DateTime.Now.ToString("MM-dd HH:mm:ss.f") + "  " + s + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }

    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(26, 26, 30);
        public static readonly Color Border = Color.FromArgb(64, 64, 74);
        public static readonly Color Text = Color.FromArgb(233, 233, 238);
        public static readonly Color Dim = Color.FromArgb(138, 138, 150);
        public static readonly Color Accent = Color.FromArgb(88, 168, 255);
        public static readonly Color Track = Color.FromArgb(58, 58, 68);
        public static readonly Font FontMain = MakeFont(9.0f);
        public static readonly Font FontSmall = MakeFont(8.0f);
        public static readonly Font FontCompact = MakeFont(8.5f);
        public static readonly Font FontTiny = MakeFont(7.5f);

        static Font MakeFont(float size)
        {
            try { return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", size); }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Speaker(Graphics g, RectangleF box, bool muted, Color c)
        {
            float s = box.Height / 24f;
            float cx = box.X + box.Width * 0.40f;
            float cy = box.Y + box.Height / 2f;
            using (var b = new SolidBrush(c))
            {
                g.FillRectangle(b, cx - 7f * s, cy - 2.5f * s, 4f * s, 5f * s);
                g.FillPolygon(b, new[]
                {
                    new PointF(cx - 3f * s, cy - 2.5f * s),
                    new PointF(cx + 2f * s, cy - 7.5f * s),
                    new PointF(cx + 2f * s, cy + 7.5f * s),
                    new PointF(cx - 3f * s, cy + 2.5f * s)
                });
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (muted)
            {
                using (var pen = new Pen(c, 1.6f * s))
                {
                    g.DrawLine(pen, cx + 5f * s, cy - 4.5f * s, cx + 11f * s, cy + 4.5f * s);
                    g.DrawLine(pen, cx + 11f * s, cy - 4.5f * s, cx + 5f * s, cy + 4.5f * s);
                }
            }
            else
            {
                using (var pen = new Pen(c, 1.4f * s))
                {
                    float r1 = 4.5f * s, r2 = 8f * s;
                    g.DrawArc(pen, cx + 2f * s - r1, cy - r1, r1 * 2, r1 * 2, -50f, 100f);
                    g.DrawArc(pen, cx + 2f * s - r2, cy - r2, r2 * 2, r2 * 2, -50f, 100f);
                }
            }
        }
    }

    class DockForm : Form
    {
        const int WM_HOTKEY = 0x0312;
        const int HOTKEY_TOGGLE = 1;
        const int HOTKEY_CYCLE = 2;
        const int HOTKEY_MODE = 3;
        const int HOTKEY_SNAP = 4;

        const int FW = 424, FH = 50;    // 浮动模式尺寸
        const int EW = 248, EH = 40;    // 嵌入任务栏尺寸

        Config cfg;
        readonly List<AudioDeviceInfo> devices = new List<AudioDeviceInfo>();
        string defaultId;
        string inputId;
        int volume = -1;
        bool muted;

        bool draggingWindow, draggingSlider;
        Point dragOrigin, winOrigin;
        int hoverZone;

        // 嵌入状态
        bool embedded;
        IntPtr tbTaskbar, tbHost, tbTaskList;
        long savedStyle;
        int taskListOrigWidth;
        bool taskListShrunk;
        bool userHidden;
        Point lastPlaced = new Point(int.MinValue, int.MinValue);
        int tickCount;
        bool exiting;
        Rectangle floatBounds;
        Color barBg = Ui.Bg, barText = Ui.Text, barDim = Ui.Dim, barAccent = Ui.Accent;
        Bitmap embedBg;
        bool layeredOk = true;
        int lastLayerLogTick;
        int lastPaintLogTick;   // 分层透明是否可用（不可用就退回截底图）
        bool needBg;
        int bgMiss;
        int embedFailures;

        ToolTip tip = new ToolTip();
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        System.Windows.Forms.Timer poll;

        const int ZoneIcon = 0, ZoneName = 1, ZoneSlider = 2;
        Rectangle RIcon, RName, RSlider, RPct;

        public DockForm()
        {
            cfg = Config.Load();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "副屏任务栏音频控制";
            BackColor = Ui.Bg;
            DoubleBuffered = true;
            tip.InitialDelay = 400;

            floatBounds = ComputeFloatBounds();
            Bounds = floatBounds;
            ClientSize = new Size(FW, FH);
            ComputeLayout();

            ApplySavedColors();   // 冷启动先用上次记住的任务栏颜色，避免第一帧闪一下黑
            RefreshAudio(true);
            BuildTray();

            Load += (s, e) =>
            {
                if (cfg.Mode == "taskbar") EmbedNow(); else ApplyFloatMode();
            };

            poll = new System.Windows.Forms.Timer { Interval = 1500 };
            poll.Tick += (s, e) => PollTick();
            poll.Start();

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
            MouseDoubleClick += (s, e) => OpenSoundSettings();
            FormClosing += (s, e) =>
            {
                RestoreTaskList();
                if (poll != null) poll.Stop();
                cfg.Save();
                if (tray != null) tray.Visible = false;
                Program.Exiting = exiting;
            };
        }

        // ---------------- 位置 / 布局 ----------------
        Rectangle ComputeFloatBounds()
        {
            Screen sc = null;
            foreach (var s in Screen.AllScreens) if (!s.Primary) { sc = s; break; }
            if (sc == null) sc = Screen.PrimaryScreen;
            var wa = sc.WorkingArea;
            if (cfg.X != int.MinValue && cfg.Y != int.MinValue)
            {
                var r = new Rectangle(cfg.X, cfg.Y, FW, FH);
                if (Screen.FromPoint(new Point(cfg.X + 10, cfg.Y + 10)).WorkingArea.IntersectsWith(r)) return r;
            }
            return new Rectangle(wa.Right - FW - 24, wa.Bottom - FH - 8, FW, FH);
        }

        void ComputeLayout()
        {
            int cx = ClientSize.Width, cy = ClientSize.Height;
            if (embedded)
            {
                RIcon = new Rectangle(8, cy / 2 - 12, 24, 24);
                RName = new Rectangle(36, 2, 96, cy - 4);
                RSlider = new Rectangle(136, cy / 2 - 6, 62, 12);
                RPct = new Rectangle(200, 2, 42, cy - 4);
            }
            else
            {
                RIcon = new Rectangle(10, cy / 2 - 12, 28, 24);
                RName = new Rectangle(44, 6, 216, cy - 12);
                RSlider = new Rectangle(266, cy / 2 - 10, 96, 20);
                RPct = new Rectangle(366, 6, 50, cy - 12);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ComputeLayout();
            Region = null;   // 圆角靠分层透明的逐像素 alpha 画，不用 Region 剪裁
        }

        // ---------------- 音频 ----------------
        void PollTick()
        {
            if (!IsHandleCreated) return;
            RefreshAudio(false);
            if (embedded)
            {
                EnsureEmbedded();
                if (++tickCount % 4 == 0) RepositionIfNeeded();
                if (layeredOk) { /* 分层透明模式下不需要底图 */ }
                else if (needBg) { if (bgMiss < 40) ResampleBg(); }          // 截屏失败：每 1.5 秒重试，最多 1 分钟
                else if (tickCount % 40 == 0) ValidateBg();             // 每 60 秒核对一次任务栏颜色
            }
            else if (cfg.Mode == "taskbar" && embedFailures < 12 && Native.IsWindowVisible(Handle)) EmbedNow();
        }

        void RefreshAudio(bool full)
        {
            try
            {
                string id = AudioCore.GetDefaultId(ERole.eMultimedia) ?? AudioCore.GetDefaultId(ERole.eConsole);
                int pct; bool m;
                AudioCore.GetVolume(out pct, out m);
                inputId = AudioCore.GetDefaultId(EDataFlow.eCapture, ERole.eMultimedia)
                          ?? AudioCore.GetDefaultId(EDataFlow.eCapture, ERole.eConsole);
                bool changed = full || id != defaultId || pct != volume || m != muted;
                defaultId = id;
                volume = pct;
                muted = m;
                if (full || devices.Count == 0) LoadDeviceList();
                else if (changed && !IsCurrentInList()) LoadDeviceList();
                if (changed)
                {
                    UpdateTip();
                    if (tray != null) tray.Text = TrayText();
                    UpdateNow(); Invalidate();
                }
            }
            catch { }
        }

        bool IsCurrentInList()
        {
            foreach (var d in devices) if (d.Id == defaultId) return true;
            return false;
        }

        void LoadDeviceList()
        {
            devices.Clear();
            devices.AddRange(AudioCore.ListRenderDevices(false));
            UpdateTip();
        }

        string CurrentName()
        {
            foreach (var d in devices) if (d.Id == defaultId) return d.Name;
            if (devices.Count > 0) return devices[0].Name;
            return "无输出设备";
        }

        string ShortName()
        {
            string n = CurrentName();
            int p = n.IndexOf(" (");
            if (p > 0) return n.Substring(0, p);
            return n;
        }

        string TrayText()
        {
            string n = CurrentName();
            if (n.Length > 40) n = n.Substring(0, 40);
            return "副屏任务栏音频控制\n" + n + (muted ? "（静音）" : "  " + volume + "%");
        }

        void UpdateTip()
        {
            try
            {
                string mic = MicShortName();
                string txt = "副屏任务栏音频控制 · " + CurrentName() + (muted ? "（静音）" : "  " + volume + "%");
                if (!string.IsNullOrEmpty(mic)) txt += "  ·  麦克风：" + mic;
                tip.SetToolTip(this, txt);
            }
            catch { }
        }

        void CycleDevice()
        {
            if (devices.Count < 2) return;
            int idx = devices.FindIndex(d => d.Id == defaultId);
            idx = (idx + 1) % devices.Count;
            SwitchTo(devices[idx]);   // 走同一条路径，才会联动麦克风
        }

        void SwitchTo(AudioDeviceInfo d)
        {
            AudioCore.SetDefault(d.Id, true);
            Thread.Sleep(120);
            if (cfg.SwitchInput)
            {
                AudioDeviceInfo mic;
                string micName = ResolveMic(d.Id, d.Name, out mic);
                if (mic != null)
                {
                    AudioCore.SetDefault(mic.Id, true);
                    Log.W("SWITCH out=" + d.Name + " -> mic=" + mic.Name);
                }
                else Log.W("SWITCH out=" + d.Name + " -> 没找到配对麦克风，输入保持不变");
            }
            RefreshAudio(true);
        }

        void SwitchInput(AudioDeviceInfo mic)
        {
            AudioCore.SetDefault(mic.Id, true);
            Thread.Sleep(120);
            RefreshAudio(true);
        }

        // "耳机 (HyperX Cloud III)" -> "HyperX Cloud III"（先把 (R)/(TM) 去掉再取括号里）
        static string Paren(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string s = name.Replace("(R)", "").Replace("(r)", "").Replace("(TM)", "").Replace("(tm)", "");
            int a = s.IndexOf('('), b = s.IndexOf(')');
            if (a >= 0 && b > a + 1) return s.Substring(a + 1, b - a - 1);
            return s;
        }

        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in (s ?? "").ToLowerInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        static readonly HashSet<string> MicStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio", "device", "interface", "usb", "usb3", "digital", "high", "definition",
            "realtek", "nvidia", "amd", "intel", "series", "microphone", "mic", "stereo",
            "音频", "接口", "设备", "数字", "高清晰度"
        };

        static HashSet<string> Tokens(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in (s ?? "").Split(new[] { ' ', '-', '_', '/', '\\', ',', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
                if (w.Length >= 2) set.Add(w);
            return set;
        }

        // 找出跟这台输出设备"同一台硬件"的麦克风：
        //  1) 配置文件里 pair= 手动指定
        //  2) 括号里的型号完全一致（Realtek(R) Audio ↔ Realtek(R) Audio）
        //  3) 型号里有 ≥2 个共同的实词（排除 audio/usb/digital 这类通用词）
        string ResolveMic(string outId, string outName, out AudioDeviceInfo mic)
        {
            mic = null;
            var caps = AudioCore.ListCaptureDevices(false);
            if (caps.Count == 0) return null;

            foreach (var rule in cfg.Pairs)
            {
                if (EqOrContains(outId, rule[0]) || EqOrContains(outName, rule[0]))
                    foreach (var c in caps)
                        if (EqOrContains(c.Id, rule[1]) || EqOrContains(c.Name, rule[1])) { mic = c; return c.Name; }
            }

            string op = Norm(Paren(outName));
            if (op.Length >= 3)
                foreach (var c in caps)
                    if (Norm(Paren(c.Name)) == op) { mic = c; return c.Name; }

            var ot = Tokens(Paren(outName));
            int best = 0;
            foreach (var c in caps)
            {
                int score = 0;
                foreach (var tk in Tokens(Paren(c.Name)))
                    if (!MicStopWords.Contains(tk) && ot.Contains(tk)) score++;
                if (score > best) { best = score; mic = c; }
            }
            return best >= 2 ? mic.Name : null;
        }

        static bool EqOrContains(string value, string pattern)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern)) return false;
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase)
                || value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string MicShortName()
        {
            foreach (var c in AudioCore.ListCaptureDevices(false))
                if (c.Id == inputId) return c.Name;
            return "";
        }

        void SetVolumeFromX(int x)
        {
            int trackL = RSlider.Left + 6;
            int trackR = RSlider.Right - 6;
            double f = (double)(x - trackL) / Math.Max(1, trackR - trackL);
            f = Math.Max(0, Math.Min(1, f));
            int pct = (int)Math.Round(f * 100);
            if (pct == volume) return;
            volume = pct;
            AudioCore.SetVolume(pct);
            muted = false;
            Invalidate();
        }

        // ---------------- 绘制 ----------------
        // 背景完全由 OnPaint 自己铺（嵌入时是任务栏底图、浮动时是渐变卡片），
        // 这里禁掉默认背景擦除，否则悬停重绘时会先糊一层 BackColor（近黑）——就是用户看到的“黑一下”。
        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        // 分层窗口（Win8+ 开始支持给子窗口用），才能做到「底色完全透出任务栏」
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            
            if (layeredOk)
            {
                try { PaintLayered(); return; }
                catch (Exception ex)
                {
                    layeredOk = false;
                    Log.W("LAYERED 失败，退回底图方案: " + ex.Message);
                }
            }
            PaintOpaque(e.Graphics);
        }

        // 只画图标/文字/滑条，背景逐像素透明——和 TrafficMonitor 一样的做法
        void PaintLayered()
        {
            using (var bmp = RenderBitmap()) PushLayered(bmp);
        }

        // 状态变了就主动推一帧（子窗口在某些情况下收不到 WM_PAINT，只靠 Invalidate 会一直不显示）
        void UpdateNow()
        {
            if (!IsHandleCreated) return;
            if (!layeredOk) { Invalidate(); return; }
            try { PaintLayered(); }
            catch (Exception ex) { layeredOk = false; Log.W("LAYERED 失败，退回底图方案: " + ex.Message); Invalidate(); }
        }

        Bitmap RenderBitmap()
        {
            var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                // 关键：分层窗口里 alpha=0 的区域会被「点穿」到任务栏，
                // 所以整块垫一层 alpha=1 的底色（肉眼看不见），保证整条都能点
                using (var hit = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                    g.FillRectangle(hit, 0, 0, bmp.Width, bmp.Height);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                if (!embedded) DrawFloatPanel(g);
                PaintContent(g, embedded);
            }
            return bmp;
        }

        // 调试用：把实际渲染的画面存成 PNG（带 alpha），方便不看屏幕也能确认长什么样
        public void SaveSnapshot()
        {
            try
            {
                string path = Path.Combine(Config.Dir, "snapshot.png");
                using (var bmp = RenderBitmap())
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);   // 带 alpha，便于检查哪些是透明的
                Log.W("SNAPSHOT " + path);
            }
            catch (Exception ex) { Log.W("SNAPSHOT 失败 " + ex.Message); }
        }

        // 自己建 DIB 并拷「预乘 alpha」的像素。
        // 不能用 bmp.GetHbitmap()——它会把 alpha 通道摊平，结果就是「窗口看不见但能点」。
        void PushLayered(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero, bits = IntPtr.Zero;
            try
            {
                var bmi = new Native.BITMAPINFO();
                bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h;      // 负数 = 自上而下
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;  // BI_RGB

                hBmp = Native.CreateDIBSection(screenDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (hBmp == IntPtr.Zero || bits == IntPtr.Zero) throw new Exception("CreateDIBSection 失败");

                int maxAOut = 0, solidOut = 0;
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    var row = new byte[Math.Abs(stride)];
                    var outRow = new byte[w * 4];
                    
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * stride), row, 0, row.Length);
                        for (int x = 0; x < w; x++)
                        {
                            byte b = row[x * 4], g2 = row[x * 4 + 1], r = row[x * 4 + 2], a = row[x * 4 + 3];
                            // 预乘：颜色 * alpha / 255（UpdateLayeredWindow 要的就是这个）
                            outRow[x * 4] = (byte)(b * a / 255);
                            outRow[x * 4 + 1] = (byte)(g2 * a / 255);
                            outRow[x * 4 + 2] = (byte)(r * a / 255);
                            outRow[x * 4 + 3] = a;
                            if (a > maxAOut) maxAOut = a;
                            if (a >= 200) solidOut++;
                        }
                        Marshal.Copy(outRow, 0, new IntPtr(bits.ToInt64() + (long)y * w * 4), outRow.Length);
                    }
                }
                finally { bmp.UnlockBits(data); }

                // 诊断：alpha 全 0 的话窗口就会「看不见但能点到」
                if (Environment.TickCount - lastLayerLogTick > 60000)
                {
                    Log.W("LAYERED " + w + "x" + h + " maxAlpha=" + maxAOut + " solidPx=" + solidOut);
                    lastLayerLogTick = Environment.TickCount;
                }

                oldBmp = Native.SelectObject(memDc, hBmp);
                var size = new Native.SIZE { cx = w, cy = h };
                var src = new Point(0, 0);
                var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (!Native.UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, 2))
                    throw new Exception("UpdateLayeredWindow 返回 false, err=" + Marshal.GetLastWin32Error());
            }
            finally
            {
                if (oldBmp != IntPtr.Zero) Native.SelectObject(memDc, oldBmp);
                if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        void DrawFloatPanel(Graphics g)
        {
            var full = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(full, 13))
            {
                using (var bg = new LinearGradientBrush(full, Color.FromArgb(34, 34, 40), Ui.Bg, LinearGradientMode.Vertical))
                    g.FillPath(bg, path);
                using (var pen = new Pen(Ui.Border, 1f)) g.DrawPath(pen, path);
            }
        }

        // 兜底：分层不可用时回到「截任务栏底图」那套
        void PaintOpaque(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var full = new Rectangle(0, 0, Width - 1, Height - 1);
            if (embedded)
            {
                if (embedBg != null)
                {
                    var oldInterp = g.InterpolationMode;
                    if (embedBg.Height != Height) g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(embedBg, new Rectangle(0, 0, Width, Height));
                    g.InterpolationMode = oldInterp;
                }
                else using (var b = new SolidBrush(barBg)) g.FillRectangle(b, full);
            }
            else DrawFloatPanel(g);

            PaintContent(g, false);
        }

        void PaintContent(Graphics g, bool transparent)
        {
            // 嵌入任务栏时用「白字 + 深色描边」：不管任务栏是浅色、深色还是壁纸透出来的，
            // 都能看清；也因此完全不依赖截屏取色（截屏在某些远程/多屏环境下拿到的是别的画面）
            var textC = embedded ? Color.White : Ui.Text;
            var dimC = embedded ? Color.FromArgb(232, 232, 240) : Ui.Dim;
            var accentC = embedded ? Color.FromArgb(126, 200, 255) : Ui.Accent;
            var shadowC = Color.FromArgb(175, 0, 0, 0);
            var fontMain = embedded ? Ui.FontCompact : Ui.FontMain;
            var fontSmall = embedded ? Ui.FontTiny : Ui.FontSmall;

            // 悬停不画任何高亮（按之前那次修复的结论：高亮方块看着脏，只改光标形状）


            Ui.Speaker(g, RIcon, muted, muted ? Color.FromArgb(255, 138, 138) : textC);

            // 设备名
            float nameX = RName.X + 4;
            float availW = RName.Width - 8;
            using (var sfTrim = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var sfPlain = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                if (embedded || transparent)
                {
                    var rectName = new RectangleF(nameX, RName.Y, availW, RName.Height);
                    string shortName = ShortName();
                    if (transparent) DrawShadowedText(g, shortName, fontMain, textC, shadowC, rectName, sfTrim);
                    else using (var b = new SolidBrush(textC)) g.DrawString(shortName, fontMain, b, rectName, sfTrim);
                }
                else
                {
                    string fullName = CurrentName();
                    string mainPart = fullName, parenPart = "";
                    int pIdx = fullName.IndexOf(" (");
                    if (pIdx > 0) { mainPart = fullName.Substring(0, pIdx); parenPart = fullName.Substring(pIdx + 1); }
                    using (var bMain = new SolidBrush(Ui.Text))
                    using (var dBrand = new SolidBrush(Ui.Dim))
                    {
                        float mainW = g.MeasureString(mainPart, Ui.FontMain).Width;
                        var rectMain = new RectangleF(nameX, RName.Y, availW, RName.Height);
                        if (mainW > availW - 4)
                        {
                            g.DrawString(mainPart, Ui.FontMain, bMain, rectMain, sfTrim);
                        }
                        else
                        {
                            g.DrawString(mainPart, Ui.FontMain, bMain, rectMain, sfPlain);
                            if (parenPart.Length > 0)
                            {
                                float px = nameX + mainW + 2;
                                float pw = RName.Right - 4 - px;
                                if (pw > 12)
                                    g.DrawString(parenPart, Ui.FontSmall, dBrand, new RectangleF(px, RName.Y, pw, RName.Height), sfTrim);
                            }
                        }
                    }
                }
            }

            // 音量条
            int cy = RSlider.Y + RSlider.Height / 2;
            int tl = RSlider.Left + 5, tr = RSlider.Right - 5;
            float thick = embedded ? 3f : 4f;
            float knobR = embedded ? 4.5f : 5f;
            int pctShown = Math.Max(0, Math.Min(100, volume));
            int kx = tl + (int)Math.Round((tr - tl) * (pctShown / 100.0));

            if (transparent)
            {
                // 深浅两层，保证在任何壁纸/任务栏颜色上都看得见
                using (var under = new Pen(Color.FromArgb(80, 0, 0, 0), thick + 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(under, tl, cy, tr, cy);
                using (var baseTrack = new Pen(Color.FromArgb(225, dimC), thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(baseTrack, tl, cy, tr, cy);
            }
            else
            {
                using (var tb = new Pen(embedded ? Color.FromArgb(110, dimC) : Ui.Track, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(tb, tl, cy, tr, cy);
            }

            if (!muted && pctShown > 0)
                using (var ab = new Pen(accentC, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(ab, tl, cy, kx, cy);
            using (var kb = new SolidBrush(muted ? Color.FromArgb(160, 160, 170) : accentC))
                g.FillEllipse(kb, kx - knobR, cy - knobR, knobR * 2, knobR * 2);
            if (!embedded)
                using (var kw = new SolidBrush(Ui.Bg)) g.FillEllipse(kw, kx - 2, cy - 2, 4, 4);

            // 百分比
            string pctText = volume < 0 ? "--" : pctShown + "%";
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                var rectPct = new RectangleF(RPct.X, RPct.Y, RPct.Width, RPct.Height);
                if (transparent) DrawShadowedText(g, pctText, fontSmall, dimC, shadowC, rectPct, sf);
                else using (var b = new SolidBrush(dimC)) g.DrawString(pctText, fontSmall, b, rectPct, sf);
            }
        }

        // 8 方向描边：白字在浅色任务栏上也清清楚楚（像视频字幕那种效果）
        void DrawShadowedText(Graphics g, string text, Font font, Color color, Color shadow, RectangleF rect, StringFormat sf)
        {
            using (var sh = new SolidBrush(shadow))
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var r = rect; r.Offset(dx, dy);
                        g.DrawString(text, font, sh, r, sf);
                    }
            }
            using (var b = new SolidBrush(color)) g.DrawString(text, font, b, rect, sf);
        }

        // ---------------- 鼠标 ----------------
        int HitTest(Point p)
        {
            if (RIcon.Contains(p)) return ZoneIcon;
            if (RSlider.Contains(p)) return ZoneSlider;
            if (RName.Contains(p)) return ZoneName;
            return -1;
        }

        void OnDown(object s, MouseEventArgs e)
        {
            int z = HitTest(e.Location);
            Log.W("MOUSEDOWN btn=" + e.Button + " client=" + e.Location + " zone=" + z + " embedded=" + embedded);
            Native.SetFocus(Handle);
            if (e.Button == MouseButtons.Right)
            {
                ShowMainMenu(Cursor.Position);
                return;
            }
            if (z == ZoneIcon) { ToggleMute(); return; }
            if (z == ZoneName) { ShowDeviceMenu(Cursor.Position); return; }
            if (z == ZoneSlider) { draggingSlider = true; SetVolumeFromX(e.X); return; }
            if (embedded) return;   // 嵌在任务栏里就不给拖了
            draggingWindow = true;
            dragOrigin = Cursor.Position;
            winOrigin = Location;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            int z = HitTest(e.Location);
            hoverZone = z;   // 仅记录光标所在区域；悬停不再触发重绘，彻底避免悬停时的任何闪动/高亮
            if (embedded) Cursor = (z == ZoneSlider) ? Cursors.SizeWE : (z >= 0 ? Cursors.Hand : Cursors.Default);
            else Cursor = (z == ZoneName || z == ZoneIcon) ? Cursors.Hand : (z == ZoneSlider ? Cursors.SizeWE : Cursors.SizeAll);

            if (draggingSlider) { SetVolumeFromX(e.X); return; }
            if (draggingWindow)
            {
                var d = new Size(Cursor.Position.X - dragOrigin.X, Cursor.Position.Y - dragOrigin.Y);
                Location = new Point(winOrigin.X + d.Width, winOrigin.Y + d.Height);
            }
        }

        void OnUp(object s, MouseEventArgs e)
        {
            if (draggingSlider) { draggingSlider = false; if (!embedded) { cfg.X = Left; cfg.Y = Top; cfg.Save(); } }
            if (draggingWindow) { draggingWindow = false; cfg.X = Left; cfg.Y = Top; cfg.Save(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (volume < 0) return;
            int next = Math.Max(0, Math.Min(100, volume + (e.Delta > 0 ? 2 : -2)));
            if (next == volume) return;
            volume = next;
            AudioCore.SetVolume(next);
            Invalidate();
        }

        void ToggleMute()
        {
            AudioCore.SetMute(!muted);
            Thread.Sleep(60);
            RefreshAudio(true);
        }

        void OpenSoundSettings()
        {
            try { Process.Start("ms-settings:sound"); } catch { }
        }

        // ---------------- 嵌入任务栏 ----------------
        void EmbedNow()
        {
            if (!IsHandleCreated) return;
            IntPtr tb = Native.FindWindow("Shell_SecondaryTrayWnd", null);
            if (tb == IntPtr.Zero) tb = Native.FindWindow("Shell_TrayWnd", null);
            if (tb == IntPtr.Zero) { Log.W("EMBED 找不到任务栏"); FailFloat(); return; }

            IntPtr host = Native.FindWindowEx(tb, IntPtr.Zero, "WorkerW", null);
            if (host == IntPtr.Zero) host = tb;
            IntPtr clock = Native.FindWindowEx(tb, IntPtr.Zero, "ClockButton", null);
            if (clock == IntPtr.Zero) clock = Native.FindWindowEx(tb, IntPtr.Zero, "TrayClockWClass", null);
            if (clock == IntPtr.Zero) { Log.W("EMBED 找不到时钟"); FailFloat(); return; }

            Native.RECT tbRect, hostRect, clockRect;
            Native.GetWindowRect(tb, out tbRect);
            Native.GetWindowRect(host, out hostRect);
            Native.GetWindowRect(clock, out clockRect);

            // 找出任务栏里已有的"外挂"窗口（比如 TrafficMonitor），排在它左边
            int limitLeft = ComputeLimit(tb, host);

            // 先按目标位置截一张任务栏底图，嵌进去后拿它当背景，和任务栏无缝
            int xScreen = limitLeft - EW - 8;
            int yScreen = tbRect.Top + (tbRect.Bottom - tbRect.Top - EH) / 2;
            CaptureTaskbarBg(tbRect, xScreen, yScreen, !embedded);

            // 藏掉浮动模式的圆角/尺寸，切成子窗口
            embedded = true;
            TopMost = false;
            Region = null;
            ClientSize = new Size(EW, EH);
            ComputeLayout();

            bool wasVisible = Native.IsWindowVisible(Handle);
            tbTaskbar = tb;
            tbHost = host;
            savedStyle = Native.GetWindowLongPtr(Handle, -16).ToInt64();
            long childStyle = (savedStyle & ~0x80000000L) | 0x40000000L;
            Native.SetWindowLongPtr(Handle, -16, new IntPtr(childStyle));
            Native.SetParent(Handle, host);

            var pt = new Point(xScreen, yScreen);
            Native.ScreenToClient(host, ref pt);
            Native.SetWindowPos(Handle, IntPtr.Zero, pt.X, pt.Y, EW, EH, (uint)(0x0004 | 0x0010 | 0x0200 | (wasVisible ? 0x0040 : 0))); // NOZORDER|NOACTIVATE|NOOWNERZORDER|SHOWWINDOW
            lastPlaced = pt;

            if (!userHidden) Native.ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
            if (cfg.ShrinkTaskList) ShrinkTaskList(host, pt.X);
            embedFailures = 0;
            Log.W("EMBED ok host=" + Native.ClassOf(host) + " at " + pt.X + "," + pt.Y + " limitLeft=" + limitLeft + " wasVisible=" + wasVisible);
            UpdateNow(); Invalidate();
        }

        // 任务栏右侧已有小工具（TrafficMonitor 等）或时钟的最左边缘，屏幕坐标
        int ComputeLimit(IntPtr tb, IntPtr host)
        {
            Native.RECT tbRect, clockRect;
            Native.GetWindowRect(tb, out tbRect);
            int limit = tbRect.Right;
            IntPtr clock = Native.FindWindowEx(tb, IntPtr.Zero, "ClockButton", null);
            if (clock == IntPtr.Zero) clock = Native.FindWindowEx(tb, IntPtr.Zero, "TrayClockWClass", null);
            if (clock != IntPtr.Zero && Native.GetWindowRect(clock, out clockRect)) limit = clockRect.Left;

            IntPtr child = IntPtr.Zero;
            while ((child = Native.FindWindowEx(host, child, null, null)) != IntPtr.Zero)
            {
                if (child == Handle) continue;
                string cls = Native.ClassOf(child);
                if (cls == "MSTaskListWClass" || cls == "MSTaskSwWClass" || cls == "WorkerW") continue;
                if (!Native.IsWindowVisible(child)) continue;
                Native.RECT r;
                Native.GetWindowRect(child, out r);
                uint cpid;
                Native.GetWindowThreadProcessId(child, out cpid);
                if (cpid == (uint)Process.GetCurrentProcess().Id) continue;
                if (r.Right - r.Left >= EW) limit = Math.Min(limit, r.Left);
            }
            return limit;
        }

        // 已嵌入时定期校位：TrafficMonitor 可能在我们之后才回到任务栏
        void RepositionIfNeeded()
        {
            if (!embedded || !IsHandleCreated) return;
            if (tbHost == IntPtr.Zero || !Native.IsWindow(tbHost) || tbTaskbar == IntPtr.Zero) return;
            Native.RECT tbRect;
            Native.GetWindowRect(tbTaskbar, out tbRect);
            int limit = ComputeLimit(tbTaskbar, tbHost);
            int xScreen = limit - EW - 8;
            int yScreen = tbRect.Top + (tbRect.Bottom - tbRect.Top - EH) / 2;
            var pt = new Point(xScreen, yScreen);
            Native.ScreenToClient(tbHost, ref pt);
            if (Math.Abs(pt.X - lastPlaced.X) < 4 && Math.Abs(pt.Y - lastPlaced.Y) < 4) return;

            CaptureTaskbarBg(tbRect, xScreen, yScreen, !embedded);
            Native.SetWindowPos(Handle, IntPtr.Zero, pt.X, pt.Y, EW, EH, 0x0004 | 0x0010 | 0x0200);
            lastPlaced = pt;
            Log.W("EMBED reposition -> " + pt.X + "," + pt.Y + " limitLeft=" + limit);
            UpdateNow(); Invalidate();
        }

        void ShrinkTaskList(IntPtr host, int ourX)
        {
            IntPtr tl = Native.FindWindowEx(host, IntPtr.Zero, "MSTaskListWClass", null);
            if (tl == IntPtr.Zero) return;
            Native.RECT r;
            Native.GetWindowRect(tl, out r);
            int w = r.Right - r.Left;
            if (w < EW + 200) return;
            taskListOrigWidth = w;
            taskListShrunk = true;
            tbTaskList = tl;
            var pt = new Point(r.Left, r.Top);
            Native.ScreenToClient(host, ref pt);
            Native.SetWindowPos(tl, IntPtr.Zero, pt.X, pt.Y, w - (EW + 8), r.Bottom - r.Top, 0x0004 | 0x0010);
            Log.W("EMBED task list " + w + " -> " + (w - (EW + 8)));
        }

        void RestoreTaskList()
        {
            if (!taskListShrunk) return;
            taskListShrunk = false;
            try
            {
                if (tbTaskList != IntPtr.Zero && Native.IsWindow(tbTaskList) && tbHost != IntPtr.Zero)
                {
                    Native.RECT r;
                    Native.GetWindowRect(tbTaskList, out r);
                    var pt = new Point(r.Left, r.Top);
                    Native.ScreenToClient(tbHost, ref pt);
                    Native.SetWindowPos(tbTaskList, IntPtr.Zero, pt.X, pt.Y, taskListOrigWidth, r.Bottom - r.Top, 0x0004 | 0x0010);
                }
            }
            catch { }
        }

        void EnsureEmbedded()
        {
            if (!embedded) return;
            if (!IsHandleCreated) { embedded = false; return; }
            if (tbHost != IntPtr.Zero && Native.IsWindow(tbHost) && Native.GetParent(Handle) == tbHost) return;
            Log.W("EMBED host lost, re-embed");
            embedded = false;
            taskListShrunk = false;
            EmbedNow();
        }

        void FailFloat()
        {
            embedFailures++;
            Log.W("EMBED fail " + embedFailures);
            if (embedFailures >= 12)
            {
                cfg.ShrinkTaskList = false;
                ApplyFloatMode();
            }
        }

        void UnembedNow()
        {
            if (!embedded) return;
            embedded = false;
            RestoreTaskList();
            Native.SetParent(Handle, IntPtr.Zero);
            Native.SetWindowLongPtr(Handle, -16, new IntPtr(savedStyle));
            ClientSize = new Size(FW, FH);
            ComputeLayout();
            Bounds = floatBounds;
            TopMost = cfg.TopMost;
            using (var p = Ui.Round(new Rectangle(0, 0, Width, Height), 13)) Region = new Region(p);
            if (!userHidden) Native.ShowWindow(Handle, 4);
            Log.W("UNEMBED");
            Invalidate();
        }

        void ApplyFloatMode()
        {
            if (embedded) UnembedNow();
            floatBounds = ComputeFloatBounds();
            ClientSize = new Size(FW, FH);
            ComputeLayout();
            Bounds = floatBounds;
            TopMost = cfg.TopMost;
            Region = null;
            Invalidate();
        }

        void SetMode(string mode)
        {
            cfg.Mode = mode;
            cfg.Save();
            if (mode == "taskbar")
            {
                embedFailures = 0;
                if (!embedded) EmbedNow();
            }
            else
            {
                UnembedNow();
            }
        }

        /// <summary>采样任务栏外观当底图；截屏失败（锁屏 / DWM 未就绪时会返回全黑）就退回上次记住的颜色</summary>
        void CaptureTaskbarBg(Native.RECT tbRect, int x, int y, bool allowFull)
        {
            try
            {
                Bitmap shot = null;
                if (allowFull)
                {
                    var b1 = new Bitmap(EW, EH);
                    using (var g1 = Graphics.FromImage(b1))
                        g1.CopyFromScreen(x, y, 0, 0, new Size(EW, EH));
                    if (IsBlack(b1)) b1.Dispose(); else shot = b1;
                }
                if (shot == null)
                {
                    // 只取窗口正上方那条纯任务栏像素，绝不会把我们自己拍进去
                    var b2 = new Bitmap(EW, 1);
                    using (var g2 = Graphics.FromImage(b2))
                        g2.CopyFromScreen(x, tbRect.Top + 5, 0, 0, new Size(EW, 1));
                    if (IsBlack(b2)) b2.Dispose(); else shot = b2;
                }
                if (shot == null)
                {
                    needBg = true;
                    bgMiss++;
                    Log.W("BG 采样是全黑（第 " + bgMiss + " 次），先用上次颜色");
                    ApplySavedColors();
                    return;
                }

                if (embedBg != null) embedBg.Dispose();
                embedBg = shot;
                needBg = false;
                bgMiss = 0;

                long r = 0, gg = 0, b = 0;
                int n = 0;
                int stepY = Math.Max(1, shot.Height / 6);
                for (int py = 0; py < shot.Height; py += stepY)
                    for (int px = 0; px < shot.Width; px += 8)
                    {
                        var c = shot.GetPixel(px, py);
                        r += c.R; gg += c.G; b += c.B; n++;
                    }
                var avg = Color.FromArgb((int)(r / n), (int)(gg / n), (int)(b / n));
                SetLookFromColor(avg);

                string rgb = avg.R + "," + avg.G + "," + avg.B;
                if (cfg.BgColor != rgb) { cfg.BgColor = rgb; cfg.Save(); }   // 记住，冷启动兜底
                Log.W("EMBED bg avg=" + rgb + " light=" + (avg.GetBrightness() > 0.6f));
            }
            catch (Exception ex)
            {
                embedBg = null;
                needBg = true;
                bgMiss++;
                ApplySavedColors();
                Log.W("BG 采样异常: " + ex.Message);
            }
        }

        static bool IsBlack(Bitmap b)
        {
            int stepY = Math.Max(1, b.Height / 6), stepX = Math.Max(1, b.Width / 8);
            for (int yy = 0; yy < b.Height; yy += stepY)
                for (int xx = 0; xx < b.Width; xx += stepX)
                {
                    var c = b.GetPixel(xx, yy);
                    if (c.R > 4 || c.G > 4 || c.B > 4) return false;
                }
            return true;
        }

        void SetLookFromColor(Color avg)
        {
            barBg = avg;
            bool light = avg.GetBrightness() > 0.6f;
            barText = light ? Color.FromArgb(28, 28, 32) : Color.FromArgb(236, 236, 242);
            barDim = light ? Color.FromArgb(96, 96, 104) : Color.FromArgb(152, 152, 164);
            barAccent = light ? Color.FromArgb(0, 96, 190) : Color.FromArgb(96, 176, 255);
        }

        void ApplySavedColors()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cfg.BgColor)) { barBg = Ui.Bg; barText = Ui.Text; barDim = Ui.Dim; barAccent = Ui.Accent; return; }
                var parts = cfg.BgColor.Split(',');
                SetLookFromColor(Color.FromArgb(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])));
            }
            catch { barBg = Ui.Bg; barText = Ui.Text; barDim = Ui.Dim; barAccent = Ui.Accent; }
        }

        // 已嵌入时重采一次（只取窗口上方那条安全像素）
        void ResampleBg()
        {
            if (!embedded || tbTaskbar == IntPtr.Zero || tbHost == IntPtr.Zero || !IsHandleCreated) return;
            Native.RECT tbRect;
            Native.GetWindowRect(tbTaskbar, out tbRect);
            var p0 = new Point(lastPlaced.X, lastPlaced.Y);
            Native.ClientToScreen(tbHost, ref p0);
            CaptureTaskbarBg(tbRect, p0.X, p0.Y, false);
            Invalidate();
        }

        // 每 60 秒核对任务栏实际颜色，偏差大就重采（换主题 / 换壁纸后自愈）
        void ValidateBg()
        {
            if (!embedded || tbTaskbar == IntPtr.Zero || tbHost == IntPtr.Zero) return;
            try
            {
                Native.RECT tbRect;
                Native.GetWindowRect(tbTaskbar, out tbRect);
                var p0 = new Point(lastPlaced.X, lastPlaced.Y);
                Native.ClientToScreen(tbHost, ref p0);
                using (var bmp = new Bitmap(1, 1))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(p0.X + EW / 2, tbRect.Top + 5, 0, 0, new Size(1, 1));
                    var c = bmp.GetPixel(0, 0);
                    if (Math.Abs(c.R - barBg.R) > 40 || Math.Abs(c.G - barBg.G) > 40 || Math.Abs(c.B - barBg.B) > 40)
                    {
                        Log.W("BG 偏差过大: " + barBg.R + "," + barBg.G + "," + barBg.B + " -> " + c.R + "," + c.G + "," + c.B);
                        SetLookFromColor(c);
                        if (!layeredOk) ResampleBg(); else Invalidate();
                    }
                }
            }
            catch { }
        }

        // ---------------- 菜单 ----------------
        // 自己算弹出位置：任务栏在最底部，菜单要往上弹，且不能被屏幕切掉
        void ShowMenuAt(ContextMenuStrip menu, Point screen)
        {
            var sc = Screen.FromPoint(screen);
            menu.PerformLayout();
            var size = menu.GetPreferredSize(Size.Empty);
            int x = screen.X - 12;
            int y = screen.Y - size.Height - 10;
            if (y < sc.WorkingArea.Top + 4) y = screen.Y + 18;
            if (x + size.Width > sc.WorkingArea.Right - 4) x = sc.WorkingArea.Right - size.Width - 4;
            if (x < sc.WorkingArea.Left + 4) x = sc.WorkingArea.Left + 4;
            menu.Show(new Point(x, y));
        }

        void ShowDeviceMenu(Point at)
        {
            var menu = new ContextMenuStrip { Font = Ui.FontMain };
            LoadDeviceList();
            if (devices.Count == 0) menu.Items.Add(new ToolStripMenuItem("（没有可用输出设备）") { Enabled = false });
            foreach (var d in devices)
            {
                var info = d;
                var item = new ToolStripMenuItem(info.Name) { Checked = info.Id == defaultId, CheckOnClick = false };
                item.Click += (s, e) => { try { SwitchTo(info); } catch { } };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var more = new ToolStripMenuItem("Windows 声音设置...");
            more.Click += (s, e) => OpenSoundSettings();
            menu.Items.Add(more);
            menu.Closed += (s, e) => BeginInvoke(new Action(() => menu.Dispose()));
            ShowMenuAt(menu, at);
        }

        void ShowMainMenu(Point at)
        {
            var menu = new ContextMenuStrip { Font = Ui.FontMain };

            var dev = new ToolStripMenuItem("切换输出设备");
            LoadDeviceList();
            foreach (var d in devices)
            {
                var info = d;
                var it = new ToolStripMenuItem(Short(info.Name)) { Checked = info.Id == defaultId };
                it.Click += (s, e) => { try { SwitchTo(info); } catch { } };
                dev.DropDownItems.Add(it);
            }
            var ddm = dev.DropDown as ToolStripDropDownMenu;
            if (ddm != null) ddm.ShowImageMargin = true;
            menu.Items.Add(dev);

            // 输入设备（麦克风）
            var micMenu = new ToolStripMenuItem("切换输入设备");
            var caps = AudioCore.ListCaptureDevices(false);
            foreach (var c in caps)
            {
                var info = c;
                var it = new ToolStripMenuItem(Short(info.Name)) { Checked = info.Id == inputId };
                it.Click += (s, e) => { try { SwitchInput(info); } catch { } };
                micMenu.DropDownItems.Add(it);
            }
            if (caps.Count == 0)
                micMenu.DropDownItems.Add(new ToolStripMenuItem("（没有可用输入设备）") { Enabled = false });
            else
            {
                AudioDeviceInfo autoMic = null;
                ResolveMic(defaultId, CurrentName(), out autoMic);
                micMenu.DropDownItems.Add(new ToolStripSeparator());
                micMenu.DropDownItems.Add(new ToolStripMenuItem(
                    autoMic != null ? "与当前输出配对：" + Short(autoMic.Name) : "与当前输出无配对") { Enabled = false });
            }
            var micDdm = micMenu.DropDown as ToolStripDropDownMenu;
            if (micDdm != null) micDdm.ShowImageMargin = true;
            menu.Items.Add(micMenu);

            var linkMic = new ToolStripMenuItem("切输出时同时切麦克风") { Checked = cfg.SwitchInput, CheckOnClick = true };
            linkMic.CheckedChanged += (s, e) => { cfg.SwitchInput = linkMic.Checked; cfg.Save(); };
            menu.Items.Add(linkMic);

            var mute = new ToolStripMenuItem(muted ? "取消静音" : "静音");
            mute.Click += (s, e) => ToggleMute();
            menu.Items.Add(mute);

            var place = new ToolStripMenuItem("显示方式") { ShortcutKeyDisplayString = "Ctrl+Alt+B" };
            var inBar = new ToolStripMenuItem("嵌进副屏任务栏") { Checked = embedded, CheckOnClick = false };
            inBar.Click += (s, e) => SetMode("taskbar");
            var flt = new ToolStripMenuItem("浮动面板") { Checked = !embedded, CheckOnClick = false };
            flt.Click += (s, e) => SetMode("float");
            place.DropDownItems.Add(inBar);
            place.DropDownItems.Add(flt);
            menu.Items.Add(place);

            var resample = new ToolStripMenuItem("重新采样背景（换主题/壁纸后用）");
            resample.Click += (s, e) => { if (embedded) ResampleBg(); else { /* 浮动模式没有底图 */ } };
            resample.Enabled = embedded && !layeredOk;
            resample.Visible = !layeredOk;
            menu.Items.Add(resample);

            var toggle = new ToolStripMenuItem("隐藏面板") { ShortcutKeyDisplayString = "Ctrl+Alt+A" };
            toggle.Click += (s, e) => { Hide(); };
            menu.Items.Add(toggle);

            menu.Items.Add(new ToolStripSeparator());

            var top = new ToolStripMenuItem("总在最前") { Checked = TopMost, CheckOnClick = true, Enabled = !embedded };
            top.CheckedChanged += (s, e) => { TopMost = top.Checked; cfg.TopMost = top.Checked; cfg.Save(); };
            menu.Items.Add(top);

            var autostart = new ToolStripMenuItem("开机自启") { Checked = IsAutoStart(), CheckOnClick = true };
            autostart.CheckedChanged += (s, e) => SetAutoStart(autostart.Checked);
            menu.Items.Add(autostart);

            if (!embedded)
            {
                var reset = new ToolStripMenuItem("回到副屏右下角");
                reset.Click += (s, e) => { Bounds = ComputeFloatBounds(); cfg.X = Left; cfg.Y = Top; cfg.Save(); };
                menu.Items.Add(reset);
            }

            menu.Items.Add(new ToolStripSeparator());
            var snd = new ToolStripMenuItem("Windows 声音设置...");
            snd.Click += (s, e) => OpenSoundSettings();
            menu.Items.Add(snd);
            var cfgItem = new ToolStripMenuItem("打开配置文件...");
            cfgItem.Click += (s, e) => OpenConfig();
            menu.Items.Add(cfgItem);

            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("退出");
            exit.Click += (s, e) => { exiting = true; cfg.Save(); Close(); Application.Exit(); };
            menu.Items.Add(exit);

            menu.Closed += (s, e) => BeginInvoke(new Action(() => menu.Dispose()));
            ShowMenuAt(menu, at);
        }

        static string Short(string n)
        {
            if (n.Length <= 42) return n;
            return n.Substring(0, 40) + "...";
        }

        // ---------------- 托盘图标 ----------------
        void BuildTray()
        {
            trayMenu = new ContextMenuStrip { Font = Ui.FontMain };
            trayMenu.Items.Add(new ToolStripMenuItem("显示 / 隐藏面板", null, (s, e) => ToggleVisible()) { ShortcutKeyDisplayString = "Ctrl+Alt+A" });
            trayMenu.Items.Add(new ToolStripMenuItem("下一台输出设备", null, (s, e) => CycleDevice()) { ShortcutKeyDisplayString = "Ctrl+Alt+V" });
            trayMenu.Items.Add(new ToolStripMenuItem("声音设置", null, (s, e) => OpenSoundSettings()));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("嵌进副屏任务栏", null, (s, e) => SetMode("taskbar")));
            trayMenu.Items.Add(new ToolStripMenuItem("切换成浮动面板", null, (s, e) => SetMode("float")));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("打开配置文件", null, (s, e) => OpenConfig()));
            trayMenu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => { exiting = true; cfg.Save(); Close(); Application.Exit(); }));

            tray = new NotifyIcon
            {
                Icon = MakeIcon(muted),
                Text = TrayText(),
                Visible = true,
                ContextMenuStrip = trayMenu
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ToggleVisible();
            };
        }

        void ToggleVisible()
        {
            if (Visible) { userHidden = true; Hide(); } else { userHidden = false; Show(); BringToFront(); }
        }

        void OpenConfig()
        {
            try { cfg.Save(); Process.Start("notepad.exe", Config.FilePath); }
            catch { }
        }

        Icon MakeIcon(bool mute)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Ui.Speaker(g, new RectangleF(0, 0, 30, 32), mute, Color.FromArgb(240, 240, 245));
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // ---------------- 热键 / 自启 ----------------
        public IntPtr MyHandle;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MyHandle = Handle;
            Native.RegisterHotKey(Handle, HOTKEY_TOGGLE, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.A);
            Native.RegisterHotKey(Handle, HOTKEY_CYCLE, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.V);
            Native.RegisterHotKey(Handle, HOTKEY_MODE, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.B);
            Native.RegisterHotKey(Handle, HOTKEY_SNAP, Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.S);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Native.UnregisterHotKey(Handle, HOTKEY_TOGGLE);
            Native.UnregisterHotKey(Handle, HOTKEY_CYCLE);
            Native.UnregisterHotKey(Handle, HOTKEY_MODE);
            Native.UnregisterHotKey(Handle, HOTKEY_SNAP);
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x007B) { m.Result = IntPtr.Zero; return; } // WM_CONTEXTMENU：不交给 DefWindowProc，否则会转发给任务栏弹出原生菜单
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_TOGGLE) ToggleVisible();
                else if (id == HOTKEY_CYCLE) CycleDevice();
                else if (id == HOTKEY_MODE) SetMode(embedded ? "float" : "taskbar");
                else if (id == HOTKEY_SNAP) SaveSnapshot();
                return;
            }
            base.WndProc(ref m);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "SideTray";

        static bool IsAutoStart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }

        // 嵌入/取消嵌入时恢复任务栏按钮区
        public void RestoreOnExit()
        {
            RestoreTaskList();
        }
    }

    // 用 ApplicationContext 而不是 Application.Run(form)：
    // 这样资源管理器重启、任务栏窗口被销毁时，进程不会跟着退出，看门狗会重建窗口并重新嵌入
    class TrayContext : ApplicationContext
    {
        DockForm form;
        System.Windows.Forms.Timer watchdog;

        public TrayContext()
        {
            Log.W("START");
            form = new DockForm();
            form.Show();
            watchdog = new System.Windows.Forms.Timer { Interval = 2000 };
            watchdog.Tick += (s, e) =>
            {
                if (Program.Exiting) return;
                if (form == null || form.IsDisposed || !form.Created || !Native.IsWindow(form.MyHandle))
                {
                    // 资源管理器重启会把嵌在任务栏里的窗口一起带走；
                    // 同一个进程里重建窗体会导致鼠标/热键失效，所以直接重启进程
                    if (TooSoon()) return;
                    Log.W("WATCHDOG restart process");
                    Program.RestartProcess();
                }
            };
            watchdog.Start();
        }

        static bool TooSoon()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                var stamp = Path.Combine(Config.Dir, "restart.stamp");
                if (File.Exists(stamp) && (DateTime.Now - File.GetLastWriteTime(stamp)).TotalSeconds < 10) return true;
                File.WriteAllText(stamp, DateTime.Now.ToString("O"));
            }
            catch { }
            return false;
        }
    }

    static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref Point p);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref Point p);

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            return sb.ToString();
        }
    }
}
