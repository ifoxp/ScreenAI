using System.Runtime.InteropServices;

namespace ScreenAI;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(27, 27, 30);
    public static readonly Color Surface = Color.FromArgb(38, 38, 43);
    public static readonly Color SurfaceHover = Color.FromArgb(50, 50, 56);
    public static readonly Color Border = Color.FromArgb(62, 62, 70);
    public static readonly Color Text = Color.FromArgb(232, 232, 236);
    public static readonly Color Muted = Color.FromArgb(150, 150, 160);
    public static readonly Color Accent = Color.FromArgb(232, 184, 48);   // same amber as the quick answer
    public static readonly Color AccentHover = Color.FromArgb(245, 200, 80);

    public static Font Body => new("Segoe UI", 9.75f);
    public static Font Small => new("Segoe UI", 8.75f);
    public static Font Title => new("Segoe UI Semibold", 15f);
    public static Font Section => new("Segoe UI Semibold", 10.5f);
    public static Font Mono => new("Consolas", 9f);

    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case Button b when b.Tag as string != "accent":
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Surface;
                    b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = SurfaceHover;
                    b.FlatAppearance.MouseDownBackColor = Border;
                    b.Cursor = Cursors.Hand;
                    b.Padding = new Padding(8, 1, 8, 1);
                    break;
                case TextBox t:
                    t.BackColor = Surface;
                    t.ForeColor = Text;
                    t.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox cb:
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = Surface;
                    cb.ForeColor = Text;
                    break;
                case ListBox lb:
                    lb.BackColor = Surface;
                    lb.ForeColor = Text;
                    lb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case CheckBox ch:
                    ch.ForeColor = Text;
                    ch.FlatStyle = FlatStyle.Flat;
                    break;
            }
            if (c.HasChildren) Apply(c);
        }
    }

    // dark title bar on Windows 10 20H1+ / 11
    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, 4); // older Windows 10 builds
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

class SettingsForm : Form
{
    const int W = 460; // width of one column
    const int Gap = 36; // space between columns

    readonly Settings s;
    readonly List<KeyEntry> keys;
    readonly ListBox keyList = new() { Width = W, Height = 104, IntegralHeight = false };
    readonly TextBox keyBox = new() { Width = 276, UseSystemPasswordChar = true, PlaceholderText = "ключ або кнопка «З буфера»" };
    readonly TextBox urlBox = new() { Width = 276, PlaceholderText = "свій сервер (необов'язково): https://…/v1" };
    readonly CheckBox ollama = new() { Text = "Локальна Ollama на цьому ПК (без ключа)", AutoSize = true };
    readonly ComboBox fastProv = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox smartProv = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox fastModel = new() { Width = 230 };
    readonly ComboBox smartModel = new() { Width = 230 };
    readonly TextBox promptBox = new() { Width = W, Height = 306, Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly Label info = new() { AutoSize = true, MaximumSize = new Size(2 * W + Gap - 140, 0) };
    readonly Dictionary<string, Task<List<string>>> modelCache = new();

    public SettingsForm(Settings settings)
    {
        s = settings;
        keys = s.Keys.Select(k => new KeyEntry { Provider = k.Provider, Key = k.Key, BaseUrl = k.BaseUrl }).ToList();

        Text = "ScreenAI — налаштування";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, 22, 18);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        FlowLayoutPanel Column() => new()
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
        };
        var left = Column();
        var right = Column();
        var col = left;
        void Add(Control c, int top = 0) { c.Margin = new Padding(0, top, 0, 0); col.Controls.Add(c); }
        Label Text_(string text, Font font, Color color, int width = W) =>
            new() { Text = text, Font = font, ForeColor = color, AutoSize = true, MaximumSize = new Size(width, 0) };
        FlowLayoutPanel Flow(params Control[] cs)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            foreach (var c in cs) { c.Margin = new Padding(0, 0, 8, 0); f.Controls.Add(c); }
            return f;
        }
        void Section(string title, string hint = null)
        {
            bool first = col.Controls.Count == 0; // top of a column: no divider
            if (!first) Add(new Panel { Width = W, Height = 1, BackColor = Theme.Border }, 18);
            Add(Text_(title, first ? Theme.Title : Theme.Section, Theme.Text), first ? 0 : 12);
            if (hint != null) Add(Text_(hint, Theme.Small, Theme.Muted), 2);
        }

        // --- about ---
        Add(Text_("ScreenAI", Theme.Title, Theme.Text));
        Add(Text_("Відповіді на те, що зараз на екрані: тести, задачі, питання. " +
                  "Натискаєте O — програма робить знімок екрана й надсилає його двом моделям одночасно. " +
                  "Спершу у правому верхньому куті з'являється швидка відповідь (жовта), " +
                  "а коли допрацює точніша модель — її відповідь (біла). " +
                  "Сам оверлей на знімки екрана не потрапляє.",
                  Theme.Body, Theme.Muted), 4);

        var keysGrid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        keysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        void HotKey(string k, string what)
        {
            keysGrid.Controls.Add(new Label
            {
                Text = k, Font = Theme.Mono, ForeColor = Theme.Text, BackColor = Theme.Surface,
                AutoSize = true, Padding = new Padding(5, 2, 5, 2), Margin = new Padding(0, 3, 10, 3)
            });
            keysGrid.Controls.Add(new Label
            {
                Text = what, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, 5, 0, 3),
                MaximumSize = new Size(W - 80, 0)
            });
        }
        HotKey("O", "знімок екрана і запит; повторне натискання скасовує попередній запит");
        HotKey("H", "сховати / показати оверлей (запит при цьому не зупиняється)");
        HotKey("Alt+K", "ці налаштування");
        HotKey("Alt+Q", "закрити програму");
        Add(keysGrid, 10);

        // --- keys ---
        Section("Ключі API",
            "Вставте ключ будь-якого провайдера — програма сама визначить, чий він: " +
            "OpenRouter, OpenAI, Claude, Gemini, Grok, Groq, Mistral, Together, Fireworks. " +
            "Через OpenRouter одним ключем доступні майже всі моделі.");
        var fromClip = new Button { Text = "З буфера", AutoSize = true };
        var add = new Button { Text = "Додати", AutoSize = true };
        var remove = new Button { Text = "Видалити вибраний", AutoSize = true };
        fromClip.Click += async (_, _) =>
        {
            if (!Clipboard.ContainsText()) { Info("У буфері немає тексту", true); return; }
            keyBox.Text = Clipboard.GetText().Trim();
            await AddKey();
        };
        add.Click += async (_, _) => await AddKey();
        remove.Click += (_, _) =>
        {
            if (keyList.SelectedIndex < 0) { Info("Спершу виберіть ключ у списку", true); return; }
            var e = keys[keyList.SelectedIndex];
            keys.RemoveAt(keyList.SelectedIndex);
            modelCache.Remove(e.Provider);
            RefreshKeys();
        };
        ollama.Checked = s.UseOllama;
        ollama.CheckedChanged += (_, _) => { modelCache.Remove("ollama"); RefreshProviders(); };

        Add(keyList, 10);
        Add(Flow(keyBox, fromClip, add), 8);
        Add(Flow(urlBox, remove), 6);
        Add(ollama, 8);

        // --- models (right column) ---
        col = right;
        Section("Моделі",
            "Список береться у провайдера, тож нові моделі з'являються самі. " +
            "Показано лише моделі, які розуміють зображення. Щоб знайти модель, почніть друкувати частину назви.");
        foreach (var cb in new[] { fastModel, smartModel })
        {
            cb.DropDownStyle = ComboBoxStyle.DropDown;
            cb.MaxDropDownItems = 20;
            var box = cb;
            box.TextUpdate += (_, _) => FilterModels(box);
            box.DropDown += (_, _) => { if (box.Items.Count == 0) SetItems(box, AllModels(box), box.Text); };
        }
        fastProv.DisplayMember = smartProv.DisplayMember = "Name";
        fastProv.SelectedIndexChanged += async (_, _) => await FillModels(fastProv, fastModel, null);
        smartProv.SelectedIndexChanged += async (_, _) => await FillModels(smartProv, smartModel, null);

        Label Caption(string t) => new() { Text = t, AutoSize = false, Width = 74, Height = 26, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
        Add(Flow(Caption("Швидка"), fastProv, fastModel), 10);
        Add(Flow(Caption("Точна"), smartProv, smartModel), 6);

        // --- prompt ---
        Section("Інструкція для моделей",
            "Як відповідати: формат тестів, задач, формул. Можна змінити під себе.");
        promptBox.Text = ToBox(s.Prompt);
        promptBox.Font = Theme.Small;
        var reset = new Button { Text = "Повернути стандартну", AutoSize = true };
        reset.Click += (_, _) => promptBox.Text = ToBox(Settings.DefaultPrompt);
        Add(promptBox, 10);
        Add(reset, 6);

        // --- footer (full width) ---
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Location = new Point(22, 16)
        };
        var columns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        left.Margin = new Padding(0, 0, Gap, 0);
        columns.Controls.Add(left);
        columns.Controls.Add(right);
        root.Controls.Add(columns);
        col = root;
        Add(new Panel { Width = 2 * W + Gap, Height = 1, BackColor = Theme.Border }, 18);
        var save = new Button
        {
            Text = "Зберегти", AutoSize = false, Width = 120, Height = 32, Tag = "accent",
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.FromArgb(25, 22, 10),
            Font = new Font("Segoe UI Semibold", 9.75f), Cursor = Cursors.Hand
        };
        save.FlatAppearance.BorderSize = 0;
        save.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
        save.Click += (_, _) => SaveAndClose();
        var footer = new TableLayoutPanel { ColumnCount = 2, Width = 2 * W + Gap, Height = 40, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        info.Anchor = AnchorStyles.Left;
        info.Font = Theme.Small;
        info.ForeColor = Theme.Muted;
        save.Anchor = AnchorStyles.Right;
        footer.Controls.Add(info, 0, 0);
        footer.Controls.Add(save, 1, 0);
        Add(footer, 10);

        Controls.Add(root);
        Theme.Apply(this);
        // TopMost above already created the handle, so apply right away (and again on recreate)
        Theme.DarkTitleBar(Handle);
        HandleCreated += (_, _) => Theme.DarkTitleBar(Handle);

        RefreshKeys();
        Load += async (_, _) =>
        {
            var (fp, fm) = Settings.SplitRef(s.FastModel);
            var (sp, sm) = Settings.SplitRef(s.SmartModel);
            SelectProvider(fastProv, fp);
            SelectProvider(smartProv, sp);
            await FillModels(fastProv, fastModel, fm);
            await FillModels(smartProv, smartModel, sm);
            if (!HasProviders()) Info("Щоб почати, додайте хоча б один ключ.");
        };
    }

    void Info(string text, bool error = false)
    {
        error |= text.Contains("не вдалося", StringComparison.OrdinalIgnoreCase)
              || text.Contains("не приймає") || text.Contains("не завантажились") || text.Contains("закороткий") || text.Contains("занадто короткий");
        info.Text = text;
        info.ForeColor = error ? Color.FromArgb(240, 120, 100) : Theme.Muted;
    }

    bool HasProviders() => keys.Count > 0 || ollama.Checked;

    async Task AddKey()
    {
        var key = keyBox.Text.Trim();
        if (key.Length < 8) { Info("Ключ занадто короткий", true); return; }
        var url = urlBox.Text.Trim();
        Info("Визначаю провайдера…");
        string prov;
        if (url.StartsWith("http")) prov = "custom";
        else prov = await Providers.Detect(key);
        if (prov == null) { Info("Не вдалося визначити провайдера. Якщо це свій сервер, вкажіть його адресу в полі нижче."); return; }
        if (!Providers.Get(prov).VisionApi)
        {
            Info($"Це ключ {Providers.Get(prov).Name}, але його API не приймає картинки. Цю модель можна використати через OpenRouter.");
            return;
        }
        keys.RemoveAll(k => k.Provider == prov);
        keys.Add(new KeyEntry { Provider = prov, Key = key, BaseUrl = prov == "custom" ? url : "" });
        modelCache.Remove(prov);
        keyBox.Text = "";
        urlBox.Text = "";
        RefreshKeys();
        Info($"Додано: {Providers.Get(prov).Name}. Завантажую моделі…");
        try
        {
            var models = await Models(prov);
            Info($"{Providers.Get(prov).Name}: {models.Count} моделей, що бачать картинки");
        }
        catch (Exception ex) { Info($"{Providers.Get(prov).Name}: ключ додано, але моделі не завантажились: {ex.Message}"); }
        // first key ever -> point both slots at it
        if (fastProv.SelectedItem == null) SelectProvider(fastProv, prov);
        if (smartProv.SelectedItem == null) SelectProvider(smartProv, prov);
    }

    static string Mask(string k) => k.Length <= 12 ? "••••" : k[..6] + "…" + k[^4..];

    void RefreshKeys()
    {
        keyList.Items.Clear();
        foreach (var k in keys)
            keyList.Items.Add($"{Providers.Get(k.Provider)?.Name ?? k.Provider} — {Mask(k.Key)}" +
                              (k.Provider == "custom" ? $"  ({k.BaseUrl})" : ""));
        RefreshProviders();
    }

    void RefreshProviders()
    {
        foreach (var cb in new[] { fastProv, smartProv })
        {
            var cur = (cb.SelectedItem as ProviderDef)?.Id;
            cb.Items.Clear();
            foreach (var p in Providers.All)
                if (keys.Any(k => k.Provider == p.Id) || (p.Id == "ollama" && ollama.Checked))
                    cb.Items.Add(p);
            SelectProvider(cb, cur);
        }
    }

    static void SelectProvider(ComboBox cb, string id)
    {
        foreach (ProviderDef p in cb.Items)
            if (p.Id == id) { cb.SelectedItem = p; return; }
        if (cb.Items.Count > 0 && cb.SelectedItem == null) cb.SelectedIndex = 0;
    }

    KeyEntry Entry(string prov) =>
        prov == "ollama" ? new KeyEntry { Provider = "ollama", Key = "ollama" } : keys.FirstOrDefault(k => k.Provider == prov);

    Task<List<string>> Models(string prov)
    {
        if (!modelCache.TryGetValue(prov, out var t))
        {
            var e = Entry(prov);
            t = e == null ? Task.FromResult(new List<string>()) : Providers.ListModels(e);
            modelCache[prov] = t;
        }
        return t;
    }

    readonly Dictionary<ComboBox, List<string>> allModels = new();

    static string ToBox(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    List<string> AllModels(ComboBox cb) => allModels.TryGetValue(cb, out var l) ? l : new List<string>();

    static void SetItems(ComboBox cb, List<string> items, string text)
    {
        cb.BeginUpdate();
        cb.Items.Clear();
        cb.Items.AddRange(items.ToArray());
        cb.EndUpdate();
        cb.Text = text;
        cb.SelectionStart = text?.Length ?? 0;
    }

    // search: every word typed must appear somewhere in the model id ("claude opus", "gpt 5", "flash")
    void FilterModels(ComboBox cb)
    {
        var text = cb.Text;
        int caret = cb.SelectionStart;
        var words = text.ToLowerInvariant().Split(new[] { ' ', '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
        var all = AllModels(cb);
        var found = words.Length == 0 ? all : all.Where(m => words.All(w => m.ToLowerInvariant().Contains(w))).ToList();

        try
        {
            // never leave an open dropdown with an empty list — WinForms throws "index 0 must be less than 0"
            if (found.Count == 0 && cb.DroppedDown) cb.DroppedDown = false;
            cb.BeginUpdate();
            cb.Items.Clear();
            cb.Items.AddRange(found.Take(300).ToArray());
            cb.EndUpdate();
            if (found.Count > 0 && cb.Focused && !cb.DroppedDown)
            {
                cb.DroppedDown = true;
                Cursor.Current = Cursors.Default; // dropping down hides the mouse cursor otherwise
            }
        }
        catch { /* list glitch must never break typing */ }
        // Items/DroppedDown changes reset the text — restore what the user typed
        cb.Text = text;
        cb.SelectionStart = Math.Min(caret, text.Length);
        cb.SelectionLength = 0;
        Info(words.Length == 0 ? $"{all.Count} моделей" : $"Знайдено: {found.Count} з {all.Count}");
    }

    async Task FillModels(ComboBox provBox, ComboBox modelBox, string preferred)
    {
        var p = provBox.SelectedItem as ProviderDef;
        var keep = preferred ?? modelBox.Text;
        allModels[modelBox] = new List<string>();
        SetItems(modelBox, new List<string>(), keep);
        if (p == null) return;
        try
        {
            var models = await Models(p.Id);
            if (!ReferenceEquals(provBox.SelectedItem, p)) return; // changed meanwhile
            allModels[modelBox] = models;
            string text = models.Contains(keep) ? keep
                : !string.IsNullOrEmpty(preferred) ? preferred
                : models.FirstOrDefault() ?? "";
            SetItems(modelBox, models, text);
            Info($"{p.Name}: {models.Count} моделей, що бачать картинки");
        }
        catch (Exception ex)
        {
            modelCache.Remove(p.Id);
            Info($"{p.Name}: не вдалося завантажити моделі — {ex.Message}");
        }
    }

    void SaveAndClose()
    {
        s.Keys = keys;
        s.UseOllama = ollama.Checked;
        if (fastProv.SelectedItem is ProviderDef fp && fastModel.Text.Trim().Length > 0)
            s.FastModel = fp.Id + ":" + fastModel.Text.Trim();
        if (smartProv.SelectedItem is ProviderDef sp && smartModel.Text.Trim().Length > 0)
            s.SmartModel = sp.Id + ":" + smartModel.Text.Trim();
        s.Prompt = string.IsNullOrWhiteSpace(promptBox.Text) ? Settings.DefaultPrompt : promptBox.Text.Trim().Replace("\r\n", "\n");
        s.Save();
        Close();
    }
}
