using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenAI;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "ScreenAI_single_instance", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        // never show the WinForms crash dialog: log the error and keep running
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenAI");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:s}] {e.Exception}\n\n");
            }
            catch { }
        };
        Application.Run(new OverlayForm());
    }
}

// ---------------- Overlay ----------------
class OverlayForm : Form
{
    readonly Label status = new();
    readonly Label answer = new();
    Settings settings = Settings.Load();
    SettingsForm settingsForm;
    CancellationTokenSource cts;
    int generation;
    bool hiddenByUser;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(22, 22, 25);
        Opacity = 0.9;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 9, 14, 11);

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty
        };
        status.AutoSize = true;
        status.ForeColor = Color.FromArgb(140, 140, 150);
        status.Font = new Font("Segoe UI", 8.25f);
        status.MaximumSize = new Size(480, 0);
        status.Margin = new Padding(0, 0, 0, 5);
        answer.AutoSize = true;
        answer.ForeColor = Color.White;
        answer.Font = new Font("Segoe UI", 11.5f);
        answer.Margin = Padding.Empty;
        answer.MaximumSize = new Size(480, 0);
        panel.Controls.Add(status);
        panel.Controls.Add(answer);
        Controls.Add(panel);

        status.Text = "ScreenAI    O знімок    H сховати    Alt+K налаштування";
        answer.Text = !settings.HasAnyProvider ? "Додайте ключ API: Alt+K" : "Натисніть O, щоб отримати відповідь";

        SizeChanged += (_, _) =>
        {
            PlaceTopRight();
            var old = Region;
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 14, 14));
            old?.Dispose();
        };
        KeyHook.Install(OnKey);
        FormClosed += (_, _) => KeyHook.Uninstall();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // layered | transparent (click-through) | toolwindow | noactivate | topmost
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Do not appear in our own (or any) screen capture.
        SetWindowDisplayAffinity(Handle, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
        PlaceTopRight();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!settings.HasAnyProvider || Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
    }

    void PlaceTopRight()
    {
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 12, wa.Top + 12);
    }

    // returns true to swallow the key
    bool OnKey(Keys key, bool alt, bool ctrl, bool shift)
    {
        if (alt && key == Keys.Q) { BeginInvoke(() => { cts?.Cancel(); Application.Exit(); }); return true; }
        if (alt && key == Keys.K) { BeginInvoke(OpenSettings); return true; }
        if (alt || ctrl) return false;
        if (settingsForm != null && settingsForm.Visible && settingsForm.ContainsFocus) return false;
        if (key == Keys.O) { BeginInvoke(StartAnalysis); return false; }
        if (key == Keys.H)
        {
            BeginInvoke(() => { hiddenByUser = !hiddenByUser; if (hiddenByUser) Hide(); else Show(); });
            return false;
        }
        return false;
    }

    void OpenSettings()
    {
        if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
        settingsForm = new SettingsForm(settings);
        settingsForm.FormClosed += (_, _) =>
        {
            settings = Settings.Load();
            if (settings.HasAnyProvider && answer.Text.StartsWith("Додайте ключ"))
                SetAnswer("Натисніть O, щоб отримати відповідь", Color.White);
        };
        settingsForm.Show();
        settingsForm.Activate();
    }

    void SetAnswer(string text, Color color)
    {
        answer.Text = text;
        answer.ForeColor = color;
    }

    async void StartAnalysis()
    {
        if (!settings.HasAnyProvider) { SetAnswer("Додайте ключ API: Alt+K", ErrColor); return; }

        cts?.Cancel();
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        int gen = ++generation;

        string img;
        try { img = CaptureScreen(); }
        catch (Exception ex) { SetAnswer("Не вдалося зробити знімок: " + ex.Message, ErrColor); return; }

        var sw = Stopwatch.StartNew();
        bool fastDone = false, smartDone = false;
        string fastErr = null, smartErr = null;
        double fastSec = 0, smartSec = 0;
        string fastName = ShortName(settings.FastModel), smartName = ShortName(settings.SmartModel);
        string Part(string name, bool done, string err, double sec) =>
            $"{name}  " + (!done ? $"{sw.Elapsed.TotalSeconds:0.0} с…" : err != null ? "помилка" : $"{sec:0.0} с");
        void UpdateStatus()
        {
            if (gen != generation) return;
            status.Text = Part(fastName, fastDone, fastErr, fastSec) + "     " + Part(smartName, smartDone, smartErr, smartSec);
        }
        SetAnswer("Аналізую…", Color.FromArgb(150, 150, 160));
        UpdateStatus();

        var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) => { if (gen != generation || (fastDone && smartDone)) timer.Stop(); else UpdateStatus(); };
        timer.Start();

        var s = settings;
        var fastTask = RunModel(s, s.FastModel, s.Prompt + "\nBe very short.", img, ct);
        var smartTask = RunModel(s, s.SmartModel, s.Prompt, img, ct);

        _ = fastTask.ContinueWith(t =>
        {
            if (gen != generation) return;
            fastDone = true;
            fastSec = sw.Elapsed.TotalSeconds;
            if (t.Result.err != null) fastErr = t.Result.err;
            else if (!smartDone || smartErr != null) SetAnswer(t.Result.text, QuickColor);
            if (smartDone && smartErr != null && fastErr != null) SetAnswer("Помилка:\n" + smartErr, ErrColor);
            UpdateStatus();
        }, TaskScheduler.FromCurrentSynchronizationContext());

        var r = await smartTask;
        if (gen != generation) return;
        smartDone = true;
        smartSec = sw.Elapsed.TotalSeconds;
        if (r.err == null) SetAnswer(r.text, Color.White);
        else
        {
            smartErr = r.err;
            // both failed -> show error; if fast is still running, its answer will be shown when it arrives
            if (fastDone && fastErr != null) SetAnswer("Помилка:\n" + smartErr, ErrColor);
        }
        UpdateStatus();
        if (r.err != null) status.Text += Environment.NewLine + Trim(smartErr, 90);
    }

    static readonly Color QuickColor = Color.FromArgb(240, 196, 64);
    static readonly Color ErrColor = Color.FromArgb(240, 120, 100);

    static string ShortName(string modelRef)
    {
        var (_, m) = Settings.SplitRef(modelRef);
        m ??= modelRef ?? "";
        var n = m[(m.LastIndexOf('/') + 1)..];
        return Trim(n, 26);
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    static async Task<(string text, string err)> RunModel(Settings st, string modelRef, string prompt, string img, CancellationToken ct)
    {
        var (prov, model) = Settings.SplitRef(modelRef);
        var entry = prov == null ? null : st.EntryFor(prov);
        if (entry == null || string.IsNullOrWhiteSpace(model))
            return (null, $"немає ключа для «{modelRef}» — Alt+K");
        try { return (await Providers.Ask(entry, model, prompt, img, ct), null); }
        catch (OperationCanceledException) { return (null, "скасовано"); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    static string CaptureScreen()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var b = screen.Bounds;
        using var bmp = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);

        Bitmap toSave = bmp;
        const int maxW = 1920;
        if (bmp.Width > maxW)
        {
            int h = bmp.Height * maxW / bmp.Width;
            toSave = new Bitmap(maxW, h);
            using var g2 = Graphics.FromImage(toSave);
            g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g2.DrawImage(bmp, 0, 0, maxW, h);
        }
        using var ms = new MemoryStream();
        var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var ep = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L) } };
        toSave.Save(ms, enc, ep);
        if (!ReferenceEquals(toSave, bmp)) toSave.Dispose();
        return Convert.ToBase64String(ms.ToArray());
    }

    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
}

// ---------------- Global low-level keyboard hook ----------------
static class KeyHook
{
    delegate IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam);
    static Proc proc;
    static IntPtr hook;
    static Func<Keys, bool, bool, bool, bool> handler;

    public static void Install(Func<Keys, bool, bool, bool, bool> h)
    {
        handler = h;
        proc = Callback;
        hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }

    static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var vk = (Keys)Marshal.ReadInt32(lParam);
            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (!win)
            {
                try { if (handler(vk, alt, ctrl, shift)) return 1; } catch { }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, Proc lpfn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
}
