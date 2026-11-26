namespace CursorPress;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(17, 20, 27);
    private static readonly Color TextColor = Color.FromArgb(235, 240, 248);
    private static readonly Color Muted = Color.FromArgb(153, 166, 186);
    private static readonly Color Accent = Color.FromArgb(143, 222, 184);
    private readonly CursorEngine engine = new();
    private readonly Settings settings = Settings.Load();
    private readonly CheckBox enabled = new();
    private readonly Label status = new();
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem pauseItem;
    private readonly Icon appIcon;
    private readonly List<Action> translations = new();
    private readonly Button ruButton, enButton;
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 250 };
    private bool ready, quitting, resourcesDisposed;
    private readonly bool smoke;
    private System.Windows.Forms.Timer? smokeTimer;

    private static string T(string ru, string en) => UiText.T(ru, en);

    public MainForm(bool smoke = false, bool autoClose = true, bool startup = false, string? language = null)
    {
        this.smoke = smoke;
        UiText.Language = language ?? settings.Language;
        settings.Language = UiText.Language;
        Text = "CursorPress";
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(700, 470);
        MinimumSize = new Size(640, 509);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("CursorPress.AppIcon")!)
            appIcon = new Icon(iconStream);
        Icon = appIcon;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(30, 24, 30, 20) };
        Controls.Add(scroll);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(layout);
        void Add(Control control, int height)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.Controls.Add(control, 0, layout.RowCount++);
            control.Dock = DockStyle.Fill;
        }

        var header = new TableLayoutPanel { ColumnCount = 3, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.Controls.Add(new Label { Text = "CursorPress", Font = new Font("Segoe UI", 29, FontStyle.Bold), Dock = DockStyle.Fill, AutoSize = true });
        ruButton = LanguageButton("RU");
        enButton = LanguageButton("EN");
        ruButton.Click += (_, _) => SelectLanguage("ru");
        enButton.Click += (_, _) => SelectLanguage("en");
        header.Controls.Add(ruButton);
        header.Controls.Add(enButton);
        Add(header, 64);
        var enableRow = new Panel { Padding = new Padding(0, 15, 0, 0) };
        Bind(enabled, "Анимация курсора", "Cursor animation");
        enabled.AutoSize = true;
        enabled.Location = new Point(0, 17);
        enabled.Font = new Font(Font, FontStyle.Bold);
        enabled.Checked = false;
        enabled.CheckedChanged += (_, _) => { if (ready) ApplyEnabled(); };
        enableRow.Controls.Add(enabled);
        status.ForeColor = Accent;
        status.Dock = DockStyle.Right;
        status.Width = 170;
        status.TextAlign = ContentAlignment.MiddleRight;
        enableRow.Controls.Add(status);
        Add(enableRow, 58);

        AddSlider(layout, "Сила нажатия", "Compression", 5, 40, settings.Compression, true, value => { settings.Compression = value; QueueSettings(); });
        AddSlider(layout, "Сжатие", "Press duration", 50, 250, settings.PressMs, false, value => { settings.PressMs = value; QueueSettings(); });
        AddSlider(layout, "Возвращение", "Release duration", 70, 350, settings.ReleaseMs, false, value => { settings.ReleaseMs = value; QueueSettings(); });

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var buttonLabel = new Label { AutoSize = true, Padding = new Padding(0, 4, 12, 0), ForeColor = Muted };
        Bind(buttonLabel, "Кнопки:", "Buttons:");
        buttons.Controls.Add(buttonLabel);
        var left = new Label { AutoSize = true, Padding = new Padding(0, 4, 15, 0) };
        Bind(left, "Левая  ✓", "Left  ✓");
        buttons.Controls.Add(left);
        var right = new CheckBox { AutoSize = true, Checked = settings.RightButton };
        var middle = new CheckBox { AutoSize = true, Checked = settings.MiddleButton };
        Bind(right, "Правая", "Right");
        Bind(middle, "Средняя", "Middle");
        right.CheckedChanged += (_, _) => { settings.RightButton = right.Checked; QueueSettings(); };
        middle.CheckedChanged += (_, _) => { settings.MiddleButton = middle.Checked; QueueSettings(); };
        buttons.Controls.Add(right);
        buttons.Controls.Add(middle);
        Add(buttons, 42);

        var autostart = new CheckBox { AutoSize = true, Anchor = AnchorStyles.Left };
        Bind(autostart, "Запускать при входе в Windows", "Start with Windows");
        bool updatingAutostart = false;
        if (!smoke)
        {
            try { autostart.Checked = Startup.IsEnabled(); }
            catch { autostart.Enabled = false; }
        }
        autostart.CheckedChanged += (_, _) =>
        {
            if (smoke || updatingAutostart) return;
            try { Startup.SetEnabled(autostart.Checked); }
            catch (Exception ex)
            {
                updatingAutostart = true;
                autostart.Checked = !autostart.Checked;
                updatingAutostart = false;
                MessageBox.Show(this, T("Не удалось изменить автозагрузку: ", "Could not update startup settings: ") + ex.Message, "CursorPress");
            }
        };
        Add(autostart, 36);

        var footer = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var hide = MakeButton(Accent, Background);
        Bind(hide, "Свернуть в трей", "Minimize to tray");
        hide.Click += (_, _) => HideToTray();
        var restore = MakeButton(Color.FromArgb(186, 49, 61), Color.White);
        Bind(restore, "Сброс настроек курсора", "Reset cursor settings");
        restore.FlatAppearance.MouseOverBackColor = Color.FromArgb(211, 60, 74);
        restore.FlatAppearance.MouseDownBackColor = Color.FromArgb(151, 36, 47);
        restore.Click += (_, _) =>
        {
            enabled.Checked = false;
            if (!Native.ResetScheme()) MessageBox.Show(this, T("Windows не смогла загрузить схему курсоров.", "Windows could not reload your cursor scheme."), "CursorPress");
        };
        footer.Controls.Add(hide);
        footer.Controls.Add(restore);
        Add(footer, 48);
        var shortcut = new Label { ForeColor = Muted, Font = new Font("Segoe UI", 9) };
        Bind(shortcut, "Ctrl + Alt + P — пауза", "Ctrl + Alt + P — pause");
        Add(shortcut, 24);

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem();
        Bind(openItem, "Открыть настройки", "Open settings");
        openItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(openItem);
        pauseItem = new ToolStripMenuItem();
        pauseItem.Click += (_, _) => enabled.Checked = !enabled.Checked;
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem();
        Bind(exitItem, "Выход", "Exit");
        exitItem.Click += (_, _) => Quit();
        menu.Items.Add(exitItem);
        tray = new NotifyIcon { Icon = Icon, Text = "CursorPress", ContextMenuStrip = menu, Visible = !smoke };
        tray.DoubleClick += (_, _) => OpenSettings();
        engine.Failed += ex =>
        {
            enabled.Checked = false;
            MessageBox.Show(this, T("Анимация остановлена: ", "Animation stopped: ") + ex.Message, "CursorPress");
        };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            try { ApplySettings(); SaveSettings(); }
            catch (Exception ex) { enabled.Checked = false; MessageBox.Show(this, ex.Message, "CursorPress"); }
        };
        RefreshLanguage();
        Shown += (_, _) =>
        {
            ready = true;
            if (smoke)
            {
                enabled.Checked = false;
                if (autoClose)
                {
                    smokeTimer = new System.Windows.Forms.Timer { Interval = 1800 };
                    smokeTimer.Tick += (_, _) => Quit();
                    smokeTimer.Start();
                }
            }
            else
            {
                if (startup) enabled.Checked = settings.AnimationEnabled;
                ApplyEnabled();
                if (!Native.RegisterHotKey(Handle, 1, 0x4003, 0x50))
                    status.Text = T("Горячая клавиша занята", "Hotkey unavailable");
                if (startup) HideToTray();
            }
        };
    }

    private static Button MakeButton(Color background, Color foreground) => new()
    {
        AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat,
        BackColor = background, ForeColor = foreground, Padding = new Padding(12, 3, 12, 3), Margin = new Padding(0, 0, 12, 0),
        FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand
    };

    private void AddSlider(TableLayoutPanel layout, string ru, string en, int min, int max, int value, bool percent, Action<int> changed)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        var title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        Bind(title, ru, en);
        row.Controls.Add(title);
        var slider = new TrackBar { Minimum = min, Maximum = max, Value = value, TickStyle = TickStyle.None, Dock = DockStyle.Fill, SmallChange = 1, LargeChange = 5 };
        var label = new Label { ForeColor = Accent, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
        Action updateValue = () => label.Text = slider.Value + (percent ? "%" : T(" мс", " ms"));
        translations.Add(updateValue);
        updateValue();
        slider.ValueChanged += (_, _) => { updateValue(); changed(slider.Value); };
        row.Controls.Add(slider);
        row.Controls.Add(label);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.Controls.Add(row, 0, layout.RowCount++);
    }
    private void QueueSettings()
    {
        if (!ready || smoke) return;
        saveTimer.Stop();
        saveTimer.Start();
    }
    private void ApplySettings()
    {
        engine.PressMs = settings.PressMs;
        engine.ReleaseMs = settings.ReleaseMs;
        engine.RightButton = settings.RightButton;
        engine.MiddleButton = settings.MiddleButton;
        if (Math.Abs(engine.Compression - settings.Compression / 100.0) > .001)
            engine.ConfigureCompression(settings.Compression / 100.0);
    }
    private void ApplyEnabled()
    {
        if (smoke) { enabled.Checked = false; return; }
        try
        {
            ApplySettings();
            engine.SetEnabled(enabled.Checked);
            settings.AnimationEnabled = enabled.Checked;
            RefreshStateText();
            SaveSettings();
        }
        catch (Exception ex)
        {
            enabled.Checked = false;
            MessageBox.Show(this, T("Не удалось включить анимацию: ", "Could not enable animation: ") + ex.Message, "CursorPress");
        }
    }
    private void SaveSettings()
    {
        if (smoke) return;
        try { settings.Save(); }
        catch { status.Text = T("Настройки не сохранены", "Settings not saved"); }
    }
    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    private void OpenSettings() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam == 1) enabled.Checked = !enabled.Checked;
        base.WndProc(ref m);
    }

    private void Quit() { quitting = true; Close(); }
    private void Bind(Control control, string ru, string en)
    {
        Action translate = () => control.Text = T(ru, en);
        translations.Add(translate);
        translate();
    }
    private void Bind(ToolStripItem item, string ru, string en)
    {
        Action translate = () => item.Text = T(ru, en);
        translations.Add(translate);
        translate();
    }
    private static Button LanguageButton(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = new Padding(3, 10, 0, 20),
        FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Cursor = Cursors.Hand
    };
    private void SelectLanguage(string language)
    {
        settings.Language = UiText.Language = language;
        RefreshLanguage();
        SaveSettings();
    }
    private void RefreshLanguage()
    {
        SuspendLayout();
        foreach (var translate in translations) translate();
        foreach (var (button, language) in new[] { (ruButton, "ru"), (enButton, "en") })
        {
            button.BackColor = UiText.Language == language ? Accent : Color.FromArgb(35, 41, 53);
            button.ForeColor = UiText.Language == language ? Background : TextColor;
        }
        RefreshStateText();
        ResumeLayout(true);
    }
    private void RefreshStateText()
    {
        status.Text = enabled.Checked ? T("● Работает", "● Active") : T("○ На паузе", "○ Paused");
        pauseItem.Text = enabled.Checked ? T("Приостановить", "Pause") : T("Включить", "Enable");
        tray.Text = enabled.Checked ? T("CursorPress — работает", "CursorPress — active") : T("CursorPress — пауза", "CursorPress — paused");
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (ready && !smoke) Native.RegisterHotKey(Handle, 1, 0x4003, 0x50);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        Native.UnregisterHotKey(Handle, 1);
        base.OnHandleDestroyed(e);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
        }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            if (IsHandleCreated) Native.UnregisterHotKey(Handle, 1);
            saveTimer.Dispose();
            smokeTimer?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            engine.Dispose();
            appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

}
