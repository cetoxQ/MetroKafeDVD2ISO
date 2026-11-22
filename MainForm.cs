using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MetroKafeDVD2ISO;

public sealed class MainForm : Form
{
    const string AppName = "MetroKafe DVD2ISO";
    const int WM_DEVICECHANGE = 0x0219, WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
    const double DvdSpeed1x = 1_385_000, CdSpeed1x = 153_600;
    const double StallSeconds = 4;

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    enum NamePreset { None, Label, LabelDate, LabelDateTime, DateTime }

    enum DriveState { Probing, Ready, NoDisc, NotResponding }

    sealed record DriveItem(char Letter, DriveState State, WorkerMessage? Info)
    {
        public string Label => Info?.Label ?? "";

        public override string ToString() => State switch
        {
            DriveState.Ready => $"{Letter}:   {(string.IsNullOrEmpty(Label) ? "(etiketsiz disk)" : Label)}",
            DriveState.Probing => $"{Letter}:   (disk okunuyor…)",
            DriveState.NotResponding => $"{Letter}:   (sürücü yanıt vermiyor)",
            _ => $"{Letter}:   (disk yok)",
        };
    }

    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    // Başlık çubuğu
    readonly FlatButton tabCopy = new() { Text = "Kopyala", Style = FlatButton.Kind.Tab, Selected = true, Dock = DockStyle.Fill };
    readonly FlatButton tabSettings = new() { Text = "Ayarlar", Style = FlatButton.Kind.Tab, Dock = DockStyle.Fill };
    readonly FlatButton tabUpdates = new() { Text = "Güncellemeler", Style = FlatButton.Kind.Tab, Dock = DockStyle.Fill };
    readonly FlatButton updateChip = new() { Text = "● Yeni sürüm", Style = FlatButton.Kind.Chip, Selected = true, Padding = new Padding(10, 3, 10, 3), Anchor = AnchorStyles.Left, Visible = false };

    // Kopyala sayfası
    readonly Panel pageCopy = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 18) };
    readonly DropDown cmbDrive = new() { Placeholder = "DVD/CD sürücüsü bulunamadı" };
    readonly FlatButton btnRefresh = new() { Text = "Yenile", Anchor = AnchorStyles.Left };
    readonly Label lblInfo = new() { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(2, 8, 0, 0) };
    readonly TextBox txtFolder = FieldBox.NewTextBox();
    readonly FlatButton btnBrowse = new() { Text = "Gözat…", Anchor = AnchorStyles.Left };
    readonly TextBox txtName = FieldBox.NewTextBox();
    readonly Dictionary<NamePreset, FlatButton> presetButtons = [];
    readonly Label lblPath = new() { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(2, 8, 0, 0) };
    readonly Label lblOptions = new() { AutoSize = true, ForeColor = Theme.TextDim, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(2, 0, 8, 0) };
    readonly FlatButton btnOptions = new() { Text = "Ayarları değiştir", Padding = new Padding(10, 3, 10, 3), Anchor = AnchorStyles.Left };
    readonly Panel banner = new() { Dock = DockStyle.Top, BackColor = Theme.WarnBg, Padding = new Padding(16, 10, 12, 10), Margin = new Padding(0, 4, 0, 10), Visible = false };
    readonly Label lblBannerTitle = new() { ForeColor = Theme.WarnText, AutoEllipsis = true };
    readonly Label lblBannerBody = new() { ForeColor = Theme.WarnTextDim, AutoEllipsis = true };
    readonly List<Control> setupControls = [];
    readonly Label lblJob = new() { AutoSize = false, AutoEllipsis = true, Height = 26, ForeColor = Theme.Text, Visible = false, Margin = new Padding(0, 2, 0, 10), TextAlign = ContentAlignment.MiddleLeft };
    readonly Label lblPercent = new() { AutoSize = true, ForeColor = Theme.Text, Text = "%0", Margin = new Padding(0) };
    readonly Label lblPhase = new() { AutoSize = true, ForeColor = Theme.TextDim, Text = "Hazır.", Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(10, 0, 0, 6) };
    readonly ModernProgress progress = new() { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 12) };
    readonly StatCard cardSpeed = new("Kopyalama hızı");
    readonly StatCard cardDone = new("Kopyalanan");
    readonly StatCard cardEta = new("Kalan süre");
    readonly StatCard cardBad = new("Hatalı sektör");
    readonly FlatButton btnCreate = new() { Text = "ISO Oluştur", Style = FlatButton.Kind.Primary, Padding = new Padding(26, 9, 26, 9) };
    readonly FlatButton btnAbort = new() { Text = "İptal", Padding = new Padding(22, 9, 22, 9), Enabled = false, Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnCopyLog = new() { Text = "Günlüğü kopyala", Padding = new Padding(16, 9, 16, 9), Margin = new Padding(0, 0, 8, 0) };
    readonly ListBox lstLog = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.TextDim };

    // Ayarlar sayfası
    readonly Panel pageSettings = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 18), Visible = false };
    readonly CheckBox chkSkipBad = DarkCheck("Okunamayan sektörleri atla (sıfırla doldur) ve devam et");
    readonly CheckBox chkTimeout = DarkCheck("Takılan bölgeyi şu süreden sonra atla:");
    readonly DropDown cmbTimeout = new() { PreferredWidth = 90, Anchor = AnchorStyles.Left };
    readonly CheckBox chkTwoPass = DarkCheck("Önce sağlam kısımları kopyala, hasarlı bölgelere en sonda dön");
    readonly CheckBox chkSlowDown = DarkCheck("Hasarlı bölgede sürücüyü yavaşlat");
    readonly CheckBox chkVerify = DarkCheck("ISO'yu diskle karşılaştırarak doğrula ve SHA-256 hesapla");
    readonly CheckBox chkEject = DarkCheck("Diski çıkar");
    readonly CheckBox chkOpenFolder = DarkCheck("ISO'nun bulunduğu klasörü aç");
    static readonly int[] TimeoutChoices = [5, 10, 15, 30, 60];
    readonly DropDown cmbMethod = new() { PreferredWidth = 260, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 0, 4) };
    static readonly (CopyMethod Method, string Text)[] MethodChoices =
    [
        (CopyMethod.Auto, "Otomatik (önerilen)"),
        (CopyMethod.Sectors, "Sektör sektör (birebir kopya)"),
        (CopyMethod.Files, "Dosya tabanlı (diskteki dosyalardan)"),
    ];

    // Güncellemeler sayfası
    readonly Panel pageUpdates = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 18), Visible = false };
    readonly Label lblUpdateStatus = new() { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 10, 0, 0) };
    readonly FlatButton btnCheckUpdate = new() { Text = "Güncellemeleri denetle", Style = FlatButton.Kind.Primary, Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnDownload = new() { Text = "Şimdi güncelle", Visible = false, Margin = new Padding(0, 0, 8, 0) };
    readonly CheckBox chkAutoUpdate = DarkCheck("Açılışta güncellemeleri otomatik denetle");
    readonly FlatButton btnGithub = new() { Text = "GitHub sayfası" };
    readonly RichTextBox rtbNotes = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, DetectUrls = false };

    readonly AppSettings settings = AppSettings.Load();
    readonly Dictionary<char, DriveItem> drives = [];
    readonly HashSet<char> probing = [];
    readonly Dictionary<char, System.Diagnostics.Process> stuckProbes = [];
    bool loadingSettings;
    NamePreset currentPreset = NamePreset.Label;
    bool suppressNameEvent;
    CancellationTokenSource? cts;
    System.Windows.Forms.Timer? deviceTimer;
    readonly System.Windows.Forms.Timer statsTimer = new() { Interval = 500 };
    ReleaseInfo? latestRelease;

    // Kopyalama sırasında hız / takılma takibi
    readonly Stopwatch copyWatch = new();
    readonly Queue<(double t, long bytes)> samples = new();
    long copyTotal, copyDone;
    int copyBad;
    double lastProgressChange, peakSpeed;
    string? damageDetail;
    double damageDetailTime;
    bool hadTrouble;
    enum Health { Normal, Slow, Damage, Stall }   // artan önem sırasıyla
    Health health;
    double healthSince, slowSince = -1;
    const double MinWarningSeconds = 5, SlowConfirmSeconds = 5;
    Color bannerStripe = Theme.Success;
    CopyPhase currentPhase;

    public MainForm()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppName;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 820);
        Padding = new Padding(1);
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        BuildCopyPage();
        BuildSettingsPage();
        BuildUpdatesPage();
        content.Controls.Add(pageCopy);
        content.Controls.Add(pageSettings);
        content.Controls.Add(pageUpdates);
        Controls.Add(content);
        Controls.Add(BuildTitleBar());
        content.BringToFront();

        btnRefresh.Click += (_, _) => RefreshDrives();
        cmbDrive.SelectedIndexChanged += (_, _) => UpdateDriveInfo();
        btnBrowse.Click += (_, _) => BrowseFolder();
        txtFolder.TextChanged += (_, _) => UpdatePathPreview();
        txtName.TextChanged += (_, _) =>
        {
            if (!suppressNameEvent) SelectPreset(NamePreset.None);
            UpdatePathPreview();
        };
        btnCreate.Click += async (_, _) => await StartCopyAsync();
        btnAbort.Click += (_, _) =>
        {
            cts?.Cancel();
            btnAbort.Enabled = false;
            lblPhase.Text = "İptal ediliyor…";
        };
        statsTimer.Tick += (_, _) => UpdateStats();
        tabCopy.Click += (_, _) => ShowPage(pageCopy);
        tabSettings.Click += (_, _) => ShowPage(pageSettings);
        tabUpdates.Click += (_, _) => ShowPage(pageUpdates);
        updateChip.Click += (_, _) => ShowPage(pageUpdates);
        btnOptions.Click += (_, _) => ShowPage(pageSettings);
        btnCheckUpdate.Click += async (_, _) => await CheckUpdatesAsync(silent: false);
        btnDownload.Click += async (_, _) => await InstallUpdateAsync();
        btnGithub.Click += (_, _) => OpenUrl(UpdateChecker.ReleasesUrl);
        lstLog.DoubleClick += (_, _) => CopyLogLine();
        btnCopyLog.Click += (_, _) => CopyLog();
        FormClosing += OnFormClosing;
        Shown += async (_, _) =>
        {
            RefreshDrives();
            ShowNotes(null);
            if (settings.AutoCheckUpdates) await CheckUpdatesAsync(silent: true);
        };

        // Açılışta konum her zaman Masaüstü
        txtFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        LoadSettings();
        SelectPreset(Enum.TryParse(settings.NamePreset, out NamePreset np) && np != NamePreset.None ? np : NamePreset.Label);
        ResetStats();
    }

    // ───────────────────────── Pencere çerçevesi ─────────────────────────

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;           // CS_DROPSHADOW
            cp.Style |= 0x20000 | 0x80000;      // WS_MINIMIZEBOX | WS_SYSMENU (görev çubuğundan küçültme)
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2; // Windows 11: yuvarlak köşeler
        try { DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }

    Control BuildTitleBar()
    {
        int h = 44;
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = h, BackColor = Theme.TitleBar, ColumnCount = 9, RowCount = 1, Margin = new Padding(0) };
        for (int i = 0; i < 6; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int logoSize = (int)Math.Round(24 * DeviceDpi / 96.0);
        var logo = new PictureBox { Image = Logo.Render(logoSize), SizeMode = PictureBoxSizeMode.AutoSize, Anchor = AnchorStyles.None, Margin = new Padding(14, 0, 8, 0) };
        var title = new Label { Text = AppName, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 0, 22, 0) };
        var filler = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var min = new TitleButton(TitleButton.Glyph.Minimize);
        var close = new TitleButton(TitleButton.Glyph.Close);
        tabCopy.Padding = tabSettings.Padding = tabUpdates.Padding = new Padding(14, 0, 14, 0);
        updateChip.Margin = new Padding(10, 0, 0, 0);
        min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        close.Click += (_, _) => Close();

        bar.Controls.Add(logo, 0, 0);
        bar.Controls.Add(title, 1, 0);
        bar.Controls.Add(tabCopy, 2, 0);
        bar.Controls.Add(tabSettings, 3, 0);
        bar.Controls.Add(tabUpdates, 4, 0);
        bar.Controls.Add(updateChip, 5, 0);
        bar.Controls.Add(filler, 6, 0);
        bar.Controls.Add(min, 7, 0);
        bar.Controls.Add(close, 8, 0);
        foreach (Control c in new Control[] { bar, logo, title, filler }) c.MouseDown += DragWindow;
        return bar;
    }

    void ShowPage(Panel page)
    {
        foreach (var (p, tab) in new[] { (pageCopy, tabCopy), (pageSettings, tabSettings), (pageUpdates, tabUpdates) })
        {
            p.Visible = p == page;
            tab.Selected = p == page;
        }
    }

    // ───────────────────────── Sayfa düzenleri ─────────────────────────

    static CheckBox DarkCheck(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 2, 0, 2),
        FlatAppearance = { BorderColor = Theme.Border, CheckedBackColor = Theme.Accent, MouseOverBackColor = Theme.SurfaceHover },
        Cursor = Cursors.Hand,
    };

    static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Accent,
        Font = new Font("Segoe UI Semibold", 8.5f),
        Margin = new Padding(0, 10, 0, 6),
    };

    static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Margin = new Padding(2, 8, 0, 4),
    };

    static TableLayoutPanel Row(Control field, Control? button)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(field, 0, 0);
        if (button != null) row.Controls.Add(button, 1, 0);
        return row;
    }

    void BuildCopyPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c) { c.Dock = c.Dock == DockStyle.None ? DockStyle.Top : c.Dock; root.Controls.Add(c); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
        void AddSetup(Control c) { Add(c); setupControls.Add(c); }

        AddSetup(Section("KAYNAK"));
        AddSetup(Row(cmbDrive, btnRefresh));
        AddSetup(lblInfo);

        AddSetup(Section("HEDEF"));
        AddSetup(FieldLabel("Kayıt konumu"));
        AddSetup(Row(new FieldBox(txtFolder), btnBrowse));
        AddSetup(FieldLabel("Dosya adı"));
        var nameExt = new Label { Text = ".iso", AutoSize = true, ForeColor = Theme.TextDim, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0) };
        AddSetup(Row(new FieldBox(txtName), nameExt));

        var chips = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0), WrapContents = true };
        chips.Controls.Add(new Label { Text = "Hazır adlar:", AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(2, 6, 8, 0) });
        foreach (var (preset, text) in new[]
                 {
                     (NamePreset.Label, "Disk adı"),
                     (NamePreset.LabelDate, "Disk adı + tarih"),
                     (NamePreset.LabelDateTime, "Disk adı + tarih + saat"),
                     (NamePreset.DateTime, "Tarih + saat"),
                 })
        {
            var b = new FlatButton { Text = text, Style = FlatButton.Kind.Chip, Padding = new Padding(12, 5, 12, 5), Margin = new Padding(0, 0, 6, 4) };
            b.Click += (_, _) => SelectPreset(preset);
            presetButtons[preset] = b;
            chips.Controls.Add(b);
        }
        AddSetup(chips);
        AddSetup(lblPath);

        var opts = Row(lblOptions, btnOptions);
        opts.Margin = new Padding(0, 12, 0, 8);
        AddSetup(opts);

        // Kopyalama sırasında yukarıdaki kurulum alanı gizlenir, yerine tek satırlık özet gelir (günlüğe yer açılır)
        Add(lblJob);

        // Çizik disk uyarı bandı
        lblBannerTitle.Font = new Font("Segoe UI Semibold", 10f);
        banner.Controls.Add(lblBannerTitle);
        banner.Controls.Add(lblBannerBody);
        banner.Paint += (_, e) => { using var b = new SolidBrush(bannerStripe); e.Graphics.FillRectangle(b, 0, 0, 4, banner.Height); };
        banner.Resize += (_, _) => LayoutBanner();
        Add(banner);

        Add(Section("İLERLEME"));
        lblPercent.Font = new Font("Segoe UI Semibold", 20f);
        var percentRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0) };
        percentRow.Controls.Add(lblPercent);
        percentRow.Controls.Add(lblPhase);
        Add(percentRow);
        Add(progress);

        var cards = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4, Margin = new Padding(0, 0, 0, 12) };
        for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cardBad.Margin = new Padding(0);
        cards.Controls.Add(cardSpeed, 0, 0);
        cards.Controls.Add(cardDone, 1, 0);
        cards.Controls.Add(cardEta, 2, 0);
        cards.Controls.Add(cardBad, 3, 0);
        Add(cards);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 0, 0, 12) };
        buttons.Controls.Add(btnCreate);
        buttons.Controls.Add(btnAbort);
        buttons.Controls.Add(btnCopyLog);
        Add(buttons);

        var logBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 6, 8), Margin = new Padding(0), MinimumSize = new Size(0, 60) };
        logBox.Controls.Add(lstLog);
        root.Controls.Add(logBox);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        pageCopy.Controls.Add(root);
    }

    static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        Margin = new Padding(22, 0, 0, 8),
    };

    void BuildSettingsPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c) { root.Controls.Add(c); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
        Control Indent(Control c) { c.Margin = new Padding(c.Margin.Left + 22, c.Margin.Top, 0, c.Margin.Bottom); return c; }

        Add(Section("KOPYALAMA YÖNTEMİ"));
        Add(cmbMethod);
        Add(Hint("Sektör sektör: diskin birebir kopyası; doğrulama ve hasarlı disk kurtarma bu yöntemde çalışır.\n" +
                 "Dosya tabanlı: ISO diskteki dosyalardan kurulur (eski DVD2ISO gibi). Video CD'ler yalnız bu yöntemle kopyalanabilir.\n" +
                 "Otomatik: Video CD / Super Video CD'de dosya tabanlı, diğer disklerde sektör sektör."));

        Add(Section("ÇİZİK / HASARLI DİSKLER"));
        Add(chkSkipBad);
        Add(Hint("Kapalıysa okunamayan ilk sektörde işlem durur."));

        var timeoutRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(22, 0, 0, 0) };
        chkTimeout.Anchor = AnchorStyles.Left;
        chkTimeout.Margin = new Padding(0, 0, 8, 0);
        timeoutRow.Controls.Add(chkTimeout);
        timeoutRow.Controls.Add(cmbTimeout);
        Add(timeoutRow);
        Add(Indent(Hint("Sürücü bir bölgede bu kadar takılırsa o bölge atlanır; kilitlenip kalmayı önler.")));
        Add(Indent(chkTwoPass));
        Add(Indent(Hint("Hasarlı bölge hızla atlanır, diskin geri kalanı kopyalanır; zor kısımlar en sonda denenir.")));
        Add(chkSlowDown);
        Add(Hint("Yavaş dönen disk çizikli bölgede genelde daha iyi okunur. Her sürücü desteklemez."));

        Add(Section("KOPYALAMA BİTİNCE"));
        Add(chkVerify);
        Add(Hint("Disk ikinci kez okunur; süre yaklaşık iki katına çıkar. Özet, ISO'nun yanına .sha256 dosyası olarak kaydedilir."));
        Add(chkEject);
        Add(chkOpenFolder);

        root.Controls.Add(new Panel { Dock = DockStyle.Fill });
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pageSettings.Controls.Add(root);

        cmbTimeout.SetItems(TimeoutChoices.Select(t => (object)$"{t} sn"));
        foreach (var c in new[] { chkSkipBad, chkTimeout, chkTwoPass, chkSlowDown, chkVerify, chkEject, chkOpenFolder, chkAutoUpdate })
            c.CheckedChanged += (_, _) => SaveSettings();
        cmbTimeout.SelectedIndexChanged += (_, _) => SaveSettings();
        cmbMethod.SetItems(MethodChoices.Select(m => (object)m.Text));
        cmbMethod.SelectedIndexChanged += (_, _) => SaveSettings();
    }

    void LoadSettings()
    {
        loadingSettings = true;
        chkSkipBad.Checked = settings.SkipBadSectors;
        chkTimeout.Checked = settings.StallTimeoutEnabled;
        int idx = Array.IndexOf(TimeoutChoices, settings.StallTimeoutSeconds);
        cmbTimeout.SelectedIndex = idx >= 0 ? idx : Array.IndexOf(TimeoutChoices, 15);
        chkTwoPass.Checked = settings.TwoPass;
        chkSlowDown.Checked = settings.SlowDownOnErrors;
        chkVerify.Checked = settings.Verify;
        chkEject.Checked = settings.EjectWhenDone;
        chkOpenFolder.Checked = settings.OpenFolderWhenDone;
        chkAutoUpdate.Checked = settings.AutoCheckUpdates;
        cmbMethod.SelectedIndex = Math.Max(0, Array.FindIndex(MethodChoices, m => m.Method == settings.CopyMethod));
        loadingSettings = false;
        ApplySettingsUi();
    }

    void SaveSettings()
    {
        if (loadingSettings) return;
        settings.SkipBadSectors = chkSkipBad.Checked;
        settings.StallTimeoutEnabled = chkTimeout.Checked;
        if (cmbTimeout.SelectedIndex >= 0) settings.StallTimeoutSeconds = TimeoutChoices[cmbTimeout.SelectedIndex];
        settings.TwoPass = chkTwoPass.Checked;
        settings.SlowDownOnErrors = chkSlowDown.Checked;
        settings.Verify = chkVerify.Checked;
        settings.EjectWhenDone = chkEject.Checked;
        settings.OpenFolderWhenDone = chkOpenFolder.Checked;
        settings.AutoCheckUpdates = chkAutoUpdate.Checked;
        if (cmbMethod.SelectedIndex >= 0) settings.CopyMethod = MethodChoices[cmbMethod.SelectedIndex].Method;
        settings.Save();
        ApplySettingsUi();
    }

    // Bağımlı seçenekleri aç/kapat ve kopyala sayfasındaki özeti güncelle
    void ApplySettingsUi()
    {
        chkTimeout.Enabled = chkTwoPass.Enabled = chkSkipBad.Checked;
        cmbTimeout.Enabled = chkSkipBad.Checked && chkTimeout.Checked;

        var parts = new List<string>();
        if (settings.SkipBadSectors)
        {
            parts.Add("hatalı sektör atlanır");
            if (settings.StallTimeoutEnabled) parts.Add($"{settings.StallTimeoutSeconds} sn takılmada atla");
            if (settings.TwoPass) parts.Add("önce sağlam kısımlar");
        }
        else parts.Add("hatalı sektörde dur");
        if (settings.SlowDownOnErrors) parts.Add("yavaşlatma");
        if (settings.Verify) parts.Add("doğrulama");
        if (settings.EjectWhenDone) parts.Add("diski çıkar");
        if (settings.CopyMethod == CopyMethod.Sectors) parts.Insert(0, "yalnız sektör sektör");
        if (settings.CopyMethod == CopyMethod.Files) parts.Insert(0, "dosya tabanlı");
        if (cmbDrive.SelectedItem is DriveItem { State: DriveState.Ready }) UpdateDriveInfo();
        lblOptions.Text = "Seçenekler:  " + string.Join("  ·  ", parts);
    }

    void BuildUpdatesPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c) { if (c.Dock == DockStyle.None) c.Dock = DockStyle.Top; root.Controls.Add(c); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }

        Add(Section("GÜNCELLEMELER"));
        Add(new Label
        {
            Text = $"Yüklü sürüm: v{UpdateChecker.Format(UpdateChecker.Current)}",
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 14f),
            Margin = new Padding(0, 0, 0, 12),
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0) };
        buttons.Controls.Add(btnCheckUpdate);
        buttons.Controls.Add(btnDownload);
        buttons.Controls.Add(btnGithub);
        Add(buttons);
        lblUpdateStatus.Text = "Yeni sürüm olup olmadığını görmek için \"Güncellemeleri denetle\"ye basın.";
        Add(lblUpdateStatus);
        chkAutoUpdate.Margin = new Padding(0, 12, 0, 0);
        Add(chkAutoUpdate);

        Add(Section("SÜRÜM NOTLARI"));
        var notesBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(14, 10, 8, 10), Margin = new Padding(0) };
        notesBox.Controls.Add(rtbNotes);
        root.Controls.Add(notesBox);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pageUpdates.Controls.Add(root);
    }

    void ShowNotes(ReleaseInfo? newer)
    {
        rtbNotes.Clear();
        var bold = new Font("Segoe UI Semibold", 11f);
        var normal = new Font("Segoe UI", 9.5f);

        void Write(string text, Font font, Color color)
        {
            rtbNotes.Select(rtbNotes.TextLength, 0);
            rtbNotes.SelectionFont = font;
            rtbNotes.SelectionColor = color;
            rtbNotes.SelectedText = text;
        }

        void WriteMarkdown(string md)
        {
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("# ")) continue;
                if (line.StartsWith("## ")) Write("\n" + line[3..] + "\n", bold, Theme.Accent);
                else if (line.StartsWith("- ") || line.StartsWith("* ")) Write("  •  " + line[2..] + "\n", normal, Theme.Text);
                else if (line.Length > 0) Write(line + "\n", normal, Theme.TextDim);
            }
        }

        if (newer != null)
        {
            Write($"Yeni: {newer.Tag}\n", bold, Theme.Success);
            WriteMarkdown(newer.Notes);
            Write("\n", normal, Theme.Text);
        }

        using var s = typeof(MainForm).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (s != null) WriteMarkdown(new StreamReader(s).ReadToEnd());
        rtbNotes.SelectionStart = 0;
    }

    async Task CheckUpdatesAsync(bool silent)
    {
        btnCheckUpdate.Enabled = false;
        btnDownload.Visible = false;
        lblUpdateStatus.ForeColor = Theme.TextDim;
        lblUpdateStatus.Text = "Denetleniyor…";
        try
        {
            latestRelease = await UpdateChecker.GetLatestAsync();
            var current = UpdateChecker.Current;
            if (latestRelease == null)
            {
                lblUpdateStatus.Text = "Yayınlanmış bir sürüm bulunamadı (depo özel olabilir veya henüz sürüm yayınlanmamış).";
            }
            else if (latestRelease.Version > current)
            {
                lblUpdateStatus.ForeColor = Theme.Success;
                lblUpdateStatus.Text = $"Yeni sürüm mevcut: {latestRelease.Tag}  (yüklü: v{UpdateChecker.Format(current)})";
                btnDownload.Visible = true;
                updateChip.Text = $"● Yeni sürüm: {latestRelease.Tag}";
                updateChip.Visible = true;
                ShowNotes(latestRelease);
            }
            else
            {
                lblUpdateStatus.ForeColor = Theme.Success;
                lblUpdateStatus.Text = $"En güncel sürümü kullanıyorsunuz (v{UpdateChecker.Format(current)}).";
            }
        }
        catch (Exception ex)
        {
            lblUpdateStatus.ForeColor = silent ? Theme.TextDim : Theme.Error;
            lblUpdateStatus.Text = "Güncelleme denetlenemedi: " + ex.Message;
        }
        finally
        {
            btnCheckUpdate.Enabled = true;
        }
    }

    async Task InstallUpdateAsync()
    {
        if (latestRelease == null) return;
        if (latestRelease.DownloadUrl == null)
        {
            OpenUrl(latestRelease.PageUrl);
            return;
        }
        if (cts != null)
        {
            Warn("ISO oluşturma devam ediyorken güncelleme yapılamaz.");
            return;
        }
        btnDownload.Enabled = btnCheckUpdate.Enabled = false;
        lblUpdateStatus.ForeColor = Theme.TextDim;
        try
        {
            var prog = new Progress<double>(pct => lblUpdateStatus.Text = $"İndiriliyor… %{pct:0}");
            await UpdateChecker.DownloadAndInstallAsync(latestRelease, prog);
            lblUpdateStatus.Text = "Kuruluyor; uygulama yeniden başlatılacak…";
            await Task.Delay(300);
            Application.Exit();
        }
        catch (Exception ex)
        {
            lblUpdateStatus.ForeColor = Theme.Error;
            lblUpdateStatus.Text = "Güncelleme yapılamadı: " + ex.Message;
            btnDownload.Enabled = btnCheckUpdate.Enabled = true;
        }
    }

    void CopyLogLine()
    {
        if (lstLog.SelectedItem is not string line) return;
        int sha = line.IndexOf("SHA-256: ", StringComparison.Ordinal);
        try { Clipboard.SetText(sha >= 0 ? line[(sha + 9)..].Trim() : line); } catch { return; }
        Log("Panoya kopyalandı.");
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ───────────────────────── Sürücüler ─────────────────────────

    // Disk takılıp çıkarıldığında listeyi otomatik yenile
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_DEVICECHANGE && cts == null)
        {
            deviceTimer ??= CreateDeviceTimer();
            deviceTimer.Stop();
            deviceTimer.Start();
        }
    }

    System.Windows.Forms.Timer CreateDeviceTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (_, _) => { t.Stop(); if (cts == null) RefreshDrives(); };
        return t;
    }

    // Sürücü listesi diske dokunmadan çıkarılır; her sürücü ayrı işlemde, süre sınırıyla tanınır
    void RefreshDrives()
    {
        char? selected = (cmbDrive.SelectedItem as DriveItem)?.Letter;
        var letters = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom).Select(d => d.Name[0]).ToList();
        foreach (var gone in drives.Keys.Except(letters).ToList()) drives.Remove(gone);
        foreach (var l in letters)
            if (!probing.Contains(l) || !drives.ContainsKey(l)) drives[l] = new DriveItem(l, DriveState.Probing, null);

        var items = letters.Select(l => drives[l]).ToList();
        cmbDrive.SetItems(items);
        if (items.Count == 0)
        {
            lblInfo.Text = "Bilgisayarda DVD/CD sürücüsü bulunamadı.";
            btnCreate.Enabled = false;
            return;
        }
        int idx = items.FindIndex(i => i.Letter == selected);
        cmbDrive.SelectedIndex = Math.Max(0, idx);
        UpdateDriveInfo();
        foreach (var l in letters) _ = ProbeDriveAsync(l);
    }

    async Task ProbeDriveAsync(char letter)
    {
        if (!probing.Add(letter)) return;
        try
        {
            // Önceki tanıma hâlâ sürücüde takılıysa yenisini başlatma
            if (stuckProbes.TryGetValue(letter, out var stuck))
            {
                if (!stuck.HasExited) { SetDrive(new DriveItem(letter, DriveState.NotResponding, null)); return; }
                stuckProbes.Remove(letter);
            }

            var outcome = await WorkerHost.ProbeAsync(letter, ProbeTimeout);
            if (outcome.TimedOut)
            {
                if (outcome.Process != null) stuckProbes[letter] = outcome.Process;
                SetDrive(new DriveItem(letter, DriveState.NotResponding, null));
            }
            else
            {
                var info = outcome.Info;
                SetDrive(new DriveItem(letter, info is { Type: "probe", Ready: true } ? DriveState.Ready : DriveState.NoDisc, info));
            }
        }
        catch (Exception ex)
        {
            SetDrive(new DriveItem(letter, DriveState.NoDisc, new WorkerMessage { Type = "error", Message = ex.Message }));
        }
        finally
        {
            probing.Remove(letter);
        }
    }

    void SetDrive(DriveItem item)
    {
        if (!drives.ContainsKey(item.Letter)) return; // bu arada sürücü çıkarıldı
        drives[item.Letter] = item;
        int idx = cmbDrive.Items.ToList().FindIndex(o => o is DriveItem d && d.Letter == item.Letter);
        if (idx < 0) return;
        cmbDrive.ReplaceItem(idx, item);
        if (idx == cmbDrive.SelectedIndex) UpdateDriveInfo();
    }

    bool UsesFileMethod(WorkerMessage info) =>
        settings.CopyMethod == CopyMethod.Files || (settings.CopyMethod == CopyMethod.Auto && info.MediaKind != null);

    void UpdateDriveInfo()
    {
        if (cmbDrive.SelectedItem is not DriveItem item) return;
        btnCreate.Enabled = false;
        switch (item.State)
        {
            case DriveState.Probing:
                lblInfo.Text = "Disk okunuyor…";
                break;
            case DriveState.NotResponding:
                lblInfo.Text = $"Sürücü {ProbeTimeout.TotalSeconds:0} sn içinde yanıt vermedi. Disk dönmeye başlıyor olabilir ya da sürücü takılmış olabilir.\n" +
                               "Birkaç saniye sonra Yenile'ye basın. Düzelmezse USB kablosunu çıkarıp takın (mümkünse bilgisayarın arka portuna).";
                break;
            case DriveState.NoDisc:
                lblInfo.Text = item.Info?.Type == "error"
                    ? "Disk okunamadı: " + item.Info.Message
                    : "Sürücü hazır değil — lütfen bir disk takın.";
                break;
            default:
                var info = item.Info!;
                string kind = info.MediaKind ?? (info.Size > 900L * 1024 * 1024 ? "DVD" : "CD");
                lblInfo.Text = $"Etiket: {(item.Label.Length > 0 ? item.Label : "-")}     Tür: {kind}     Dosya sistemi: {info.FileSystem}     Boyut: {FormatSize(info.Size)}";
                if (UsesFileMethod(info))
                    lblInfo.Text += info.MediaKind != null
                        ? $"\n{info.MediaKind}: ISO diskteki dosyalardan oluşturulacak (görüntü sektörleri ISO'ya sektör sektör sığmaz)."
                        : "\nDosya tabanlı yöntem: ISO diskteki dosyalardan oluşturulacak.";
                btnCreate.Enabled = cts == null;
                break;
        }
        if (currentPreset != NamePreset.None) SelectPreset(currentPreset);
    }

    // ───────────────────────── Hedef: konum + ad ─────────────────────────

    void SelectPreset(NamePreset preset)
    {
        currentPreset = preset;
        foreach (var (p, b) in presetButtons) b.Selected = p == preset;
        if (preset == NamePreset.None) return;
        suppressNameEvent = true;
        txtName.Text = BuildName(preset);
        suppressNameEvent = false;
    }

    string BuildName(NamePreset preset)
    {
        string label = cmbDrive.SelectedItem is DriveItem { Label.Length: > 0 } item ? SanitizeFileName(item.Label) : "disk";
        var now = DateTime.Now;
        string date = now.ToString("yyyy-MM-dd"), time = now.ToString("HH-mm");
        return preset switch
        {
            NamePreset.LabelDate => $"{label}_{date}",
            NamePreset.LabelDateTime => $"{label}_{date}_{time}",
            NamePreset.DateTime => $"{date}_{time}",
            _ => label,
        };
    }

    string? BuildOutputPath()
    {
        string folder = txtFolder.Text.Trim(), name = txtName.Text.Trim();
        if (folder.Length == 0 || name.Length == 0) return null;
        if (name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        try { return Path.GetFullPath(Path.Combine(folder, name + ".iso")); } catch { return null; }
    }

    void UpdatePathPreview()
    {
        var path = BuildOutputPath();
        lblPath.Text = path == null ? "Geçerli bir konum ve dosya adı girin." : "Kaydedilecek:  " + path;
        lblPath.ForeColor = path == null ? Theme.Error : Theme.TextDim;
    }

    void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "ISO dosyasının kaydedileceği klasörü seçin",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (Directory.Exists(txtFolder.Text)) dlg.InitialDirectory = txtFolder.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dlg.SelectedPath;
    }

    // ───────────────────────── Kopyalama ─────────────────────────

    async Task StartCopyAsync()
    {
        if (cmbDrive.SelectedItem is not DriveItem { State: DriveState.Ready, Info: { } info } item)
        {
            Warn("Sürücüde disk yok veya disk henüz hazır değil.");
            return;
        }

        if (currentPreset != NamePreset.None) SelectPreset(currentPreset); // tarih/saati tazele
        string? output = BuildOutputPath();
        if (output == null) { Warn("Geçerli bir kayıt konumu ve dosya adı girin."); return; }
        if (char.ToUpperInvariant(output[0]) == char.ToUpperInvariant(item.Letter))
        {
            Warn("ISO dosyası okunan diskin kendisine yazılamaz.");
            return;
        }
        if (!Directory.Exists(Path.GetDirectoryName(output)))
        {
            Warn("Kayıt konumu bulunamadı.");
            return;
        }

        long size = info.Size;
        if (size <= 0) { Warn("Disk boyutu okunamadı."); return; }
        // Dosya tabanlı ISO'da Video CD görüntü dosyaları tam sektörlerle (2352 bayt) okunur; biraz daha büyük çıkar
        bool fileMethod = UsesFileMethod(info);
        if (fileMethod) size = size / 2048 * 2352 + 4 * 1024 * 1024;
        // Dosya tabanlı yöntemde dosyalar önce geçici klasöre kopyalanır: ISO ile birlikte iki katı yer gerekir
        long needed = fileMethod ? size * 2 : size;

        // Boş alan ve FAT32 4 GB sınırı kontrolü
        try
        {
            var target = new DriveInfo(Path.GetPathRoot(output)!);
            long existing = File.Exists(output) ? new FileInfo(output).Length : 0;
            if (target.AvailableFreeSpace + existing < needed)
            {
                Warn($"Hedef sürücüde yeterli boş alan yok.\nGereken: {FormatSize(needed)}" +
                     (fileMethod ? " (dosyalar önce geçici olarak kopyalandığı için ISO boyutunun iki katı)" : "") +
                     $"\nBoş: {FormatSize(target.AvailableFreeSpace)}");
                return;
            }
            if (size >= 4L * 1024 * 1024 * 1024 && target.DriveFormat.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
            {
                Warn($"Hedef sürücü {target.DriveFormat} biçimli; 4 GB'tan büyük dosya yazamaz. NTFS veya exFAT bir sürücü seçin.");
                return;
            }
        }
        catch { /* ağ yolu vb. — kontrol edilemiyorsa devam et */ }

        if (File.Exists(output) &&
            MessageBox.Show(this, $"{output}\nzaten var. Üzerine yazılsın mı?", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        cts = new CancellationTokenSource();
        lblJob.Text = $"{item.Letter}:  {(item.Label.Length > 0 ? item.Label : "disk")}   →   {output}";
        SetBusy(true);
        lstLog.Items.Clear();
        Log($"{item.Letter}: → {output}  ({FormatSize(size)})");
        Log($"Disk: etiket '{item.Label}', dosya sistemi {info.FileSystem}, sürücü {info.DeviceLength / 2048:N0} sektör bildiriyor.");
        BeginStats(size);

        var prog = new Progress<WorkerMessage>(m =>
        {
            if (m.Type == "progress") OnProgress(new CopyProgress(m.Phase, m.Done, m.Total, m.Bad, m.Message, m.Detail));
            else if (m.Message != null) Log(m.Message);
        });
        var options = settings.ToCopyOptions();
        var token = cts.Token;
        try
        {
            var result = await WorkerHost.CopyAsync(item.Letter, output, options, prog, token);
            copyDone = copyTotal;
            EndStats();
            progress.Value = progress.Maximum;
            lblPercent.Text = "%100";
            double avg = result.BytesWritten / Math.Max(0.001, result.Elapsed.TotalSeconds);
            string summary = $"Tamamlandı — {FormatSize(result.BytesWritten)}, süre {result.Elapsed:hh\\:mm\\:ss}, ortalama {FormatSize((long)avg)}/s" +
                             (result.BadSectors > 0 ? $", okunamayan sektör: {result.BadSectors}" : "");
            lblPhase.Text = "Tamamlandı.";
            cardDone.CaptionText = "Kopyalanan";
            cardDone.ValueText = FormatSize(result.BytesWritten);
            cardSpeed.ValueText = $"ort. {FormatSize((long)avg)}/s";
            cardEta.ValueText = "Bitti";
            Log(summary);
            if (hadTrouble && result.BadSectors == 0)
                Log("Disk bazı bölgelerde zor okundu ancak tüm veriler eksiksiz kurtarıldı.");

            string? verifyProblem = null;
            if (result.Sha256 != null)
            {
                Log($"SHA-256: {result.Sha256}");
                Log("(Özeti panoya kopyalamak için satıra çift tıklayın.)");
                if (result.VerifyMismatches > 0 || result.VerifyUnreadable > 0)
                {
                    verifyProblem = $"Doğrulama: {result.VerifyMismatches} sektör diskle uyuşmuyor, {result.VerifyUnreadable} sektör ikinci okumada okunamadı.";
                    Log(verifyProblem);
                }
                else
                {
                    Log("Doğrulama başarılı: ISO diskle birebir aynı" + (result.BadSectors > 0 ? " (atlanan sektörler hariç)." : "."));
                }
            }

            if (settings.EjectWhenDone)
                Log(await WorkerHost.EjectAsync(item.Letter, ProbeTimeout) ? "Disk çıkarıldı." : "Disk çıkarılamadı.");
            if (!Focused) Activate();
            if (result.BadSectors > 0 || verifyProblem != null)
                MessageBox.Show(this, (result.BadSectors > 0
                                          ? $"ISO oluşturuldu ancak {result.BadSectors} sektör okunamadı ve sıfırla dolduruldu.\n" +
                                            "Disk çizik/hasarlı olabilir; bazı dosyalar bozuk olabilir.\n\n"
                                          : "") +
                                      (verifyProblem != null ? verifyProblem + "\n\n" : "") +
                                      "İpucu: Diski yumuşak bir bezle merkezden dışa doğru silip tekrar deneyebilirsiniz.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (settings.OpenFolderWhenDone)
                Process.Start("explorer.exe", $"/select,\"{output}\"");
        }
        catch (Exception ex) when (token.IsCancellationRequested)
        {
            _ = ex;
            EndStats();
            lblPhase.Text = "İptal edildi.";
            Log("İşlem kullanıcı tarafından iptal edildi; yarım kalan dosya silindi.");
            TryDelete(output);
            progress.Value = 0;
        }
        catch (Exception ex)
        {
            EndStats();
            lblPhase.Text = "Hata oluştu.";
            Log("HATA: " + ex.Message);
            TryDelete(output);
            MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SaveLog(item.Label.Length > 0 ? item.Label : "disk");
            cts.Dispose();
            cts = null;
            SetBusy(false);
        }
    }

    void ResetStats()
    {
        cardSpeed.ValueText = "—";
        cardDone.ValueText = "—";
        cardEta.ValueText = "—";
        cardBad.ValueText = "0";
        cardBad.ValueColor = Theme.Text;
    }

    void BeginStats(long total)
    {
        copyTotal = total;
        copyDone = 0;
        copyBad = 0;
        peakSpeed = 0;
        damageDetail = null;
        hadTrouble = false;
        currentPhase = CopyPhase.Reading;
        cardDone.CaptionText = "Kopyalanan";
        samples.Clear();
        copyWatch.Restart();
        lastProgressChange = 0;
        progress.Value = 0;
        progress.BarColor = Theme.Accent;
        lblPercent.Text = "%0";
        lblPhase.Text = "Okunuyor…";
        ResetStats();
        health = Health.Normal;
        healthSince = 0;
        slowSince = -1;
        ShowBanner(false, "✓  Okuma normal", "Okuma takılır, yavaşlar ya da hasarlı bölgeye gelirse burada gösterilir ve günlüğe yazılır.");
        statsTimer.Start();
    }

    void EndStats()
    {
        statsTimer.Stop();
        copyWatch.Stop();
        HideBanner();
        progress.BarColor = Theme.Accent;
        double avg = copyDone / Math.Max(0.001, copyWatch.Elapsed.TotalSeconds);
        cardSpeed.ValueText = $"ort. {FormatSize((long)avg)}/s";
        cardDone.ValueText = $"{FormatSize(copyDone)} / {FormatSize(copyTotal)}";
        cardBad.ValueText = copyBad.ToString("N0");
        cardBad.ValueColor = copyBad > 0 ? Theme.Error : Theme.Text;
        Text = AppName;
    }

    void OnProgress(CopyProgress p)
    {
        double now = copyWatch.Elapsed.TotalSeconds;
        if (p.Phase != currentPhase)
        {
            // Yeni aşama: hız ölçümünü sıfırla (doğrulamada ilerleme baştan başlar)
            currentPhase = p.Phase;
            samples.Clear();
            peakSpeed = 0;
            copyDone = p.BytesDone;
            lastProgressChange = now;
            damageDetail = null;
            cardDone.CaptionText = p.Phase switch
            {
                CopyPhase.Verifying => "Doğrulanan",
                CopyPhase.Building => "ISO'ya yazılan",
                _ => "Kopyalanan",
            };
        }
        if (p.Message != null) Log(p.Message);
        if (p.Detail != null) { damageDetail = p.Detail; damageDetailTime = now; hadTrouble = true; }
        if (p.BytesDone != copyDone) { copyDone = p.BytesDone; lastProgressChange = now; }
        copyBad = p.BadSectors;
        if (p.TotalBytes > 0)
        {
            copyTotal = p.TotalBytes;
            progress.Value = (int)(p.BytesDone * progress.Maximum / p.TotalBytes);
            double pct = p.BytesDone * 100.0 / p.TotalBytes;
            lblPercent.Text = $"%{pct:0.0}";
            Text = $"%{pct:0} - {AppName}";
        }
    }

    // Anlık hız son 3 saniyedeki ilerlemeden hesaplanır; okuma takılırsa hız 0'a iner
    void UpdateStats()
    {
        double now = copyWatch.Elapsed.TotalSeconds;
        samples.Enqueue((now, copyDone));
        while (samples.Count > 2 && now - samples.Peek().t > 3) samples.Dequeue();
        var first = samples.Peek();
        double speed = now - first.t > 0.4 ? (copyDone - first.bytes) / (now - first.t) : 0;
        if (now > 4) peakSpeed = Math.Max(peakSpeed, speed);
        double avg = copyDone / Math.Max(0.001, now);

        double x = speed / (copyTotal > 900L * 1024 * 1024 ? DvdSpeed1x : CdSpeed1x);
        cardSpeed.ValueText = $"{FormatSize((long)speed)}/s  ·  {x:0.0}x";
        cardDone.ValueText = $"{FormatSize(copyDone)} / {FormatSize(copyTotal)}";
        cardBad.ValueText = copyBad.ToString("N0");
        cardBad.ValueColor = copyBad > 0 ? Theme.Error : Theme.Text;

        double stall = now - lastProgressChange;
        double etaSpeed = speed > 0 ? speed : avg;
        cardEta.ValueText = stall >= StallSeconds ? "Bekleniyor…"
            : etaSpeed > 0 ? TimeSpan.FromSeconds((copyTotal - copyDone) / etaSpeed).ToString(@"hh\:mm\:ss") : "—";

        string position = $"{FormatSize(copyDone)} konumunda";
        bool slowNow = currentPhase == CopyPhase.Reading && now > 10 && peakSpeed > 0 && speed < peakSpeed * 0.3;
        if (!slowNow) slowSince = -1;
        else if (slowSince < 0) slowSince = now;

        var desired = stall >= StallSeconds ? Health.Stall
            : damageDetail != null && now - damageDetailTime < 5 ? Health.Damage
            : slowSince >= 0 && now - slowSince >= SlowConfirmSeconds ? Health.Slow
            : Health.Normal;

        // Uyarı en az birkaç saniye ekranda kalır (gidip gelmesin); daha ciddi bir durum hemen gösterilir
        if (desired < health && now - healthSince < MinWarningSeconds) return;
        if (desired != health)
        {
            if (desired != Health.Normal) hadTrouble = true;
            Log(desired switch
            {
                Health.Stall => $"Okuma takıldı ({position}). Disk çizik olabilir.",
                Health.Damage => $"Hasarlı bölge okunuyor ({damageDetail}).",
                Health.Slow => $"Okuma hızı düştü: {FormatSize((long)speed)}/s (en yüksek {FormatSize((long)peakSpeed)}/s, {position}).",
                _ => "Okuma normale döndü.",
            });
            health = desired;
            healthSince = now;
        }

        switch (health)
        {
            case Health.Stall:
                ShowBanner(true, $"⚠  Disk okunmakta zorlanıyor — {stall:0} sn'dir ilerleme yok ({position})",
                    (damageDetail != null && now - damageDetailTime < 30 ? $"Hasarlı bölge tekrar deneniyor: {damageDetail}. " : "") +
                    "Disk çizik, kirli veya hasarlı olabilir; sürücü bu bölgeyi tekrar tekrar okumaya çalışıyor, birkaç dakika sürebilir. " +
                    "Bekleyebilir ya da İptal'e basabilirsiniz. İpucu: Diski yumuşak bir bezle merkezden dışa doğru silip yeniden deneyin.");
                lblPhase.Text = "Okuma takıldı, bekleniyor…";
                break;
            case Health.Damage:
                ShowBanner(true, "⚠  Hasarlı bölge okunuyor — kopyalama yavaşlayabilir",
                    $"Okunamayan bölge sektör sektör tekrar deneniyor ({damageDetail}). " +
                    (settings.SkipBadSectors ? "Okunamayan sektörler atlanıp sıfırla doldurulacak." : "Bir sektör okunamazsa işlem duracak."));
                lblPhase.Text = "Hasarlı bölge okunuyor…";
                break;
            case Health.Slow:
                ShowBanner(true, $"⚠  Okuma hızı belirgin şekilde düştü ({FormatSize((long)speed)}/s, en yüksek {FormatSize((long)peakSpeed)}/s)",
                    "Diskin bu bölgesinde çizik veya leke olabilir; sürücü yavaşlayarak okumaya çalışıyor. İşlem devam ediyor.");
                lblPhase.Text = "Yavaş okunuyor…";
                break;
            default:
                ShowBanner(false, "✓  Okuma normal", "Okuma takılır, yavaşlar ya da hasarlı bölgeye gelirse burada gösterilir ve günlüğe yazılır.");
                lblPhase.Text = currentPhase switch
                {
                    CopyPhase.Recovering => "Hasarlı bölgeler tekrar deneniyor…",
                    CopyPhase.Verifying => "Doğrulanıyor…",
                    CopyPhase.Building => "ISO oluşturuluyor…",
                    _ => "Okunuyor…",
                };
                break;
        }
    }

    // Durum alanı kopyalama boyunca sabit yerde ve sabit yükseklikte durur; yalnız rengi ve metni değişir
    void ShowBanner(bool warning, string title, string body)
    {
        if (lblBannerTitle.Text != title) lblBannerTitle.Text = title;
        if (lblBannerBody.Text != body) lblBannerBody.Text = body;
        var back = warning ? Theme.WarnBg : Theme.Surface;
        if (banner.BackColor != back)
        {
            banner.BackColor = lblBannerTitle.BackColor = lblBannerBody.BackColor = back;
            lblBannerTitle.ForeColor = warning ? Theme.WarnText : Theme.Success;
            lblBannerBody.ForeColor = warning ? Theme.WarnTextDim : Theme.TextDim;
            bannerStripe = warning ? Theme.WarnText : Theme.Success;
            banner.Invalidate();
        }
        if (!banner.Visible)
        {
            banner.Visible = true;
            LayoutBanner();
        }
        progress.BarColor = warning ? Color.FromArgb(220, 120, 20) : Theme.Accent;
    }

    // Sabit yükseklik: başlık 1 satır + açıklama en çok 3 satır (taşan kısım … ile kısaltılır)
    void LayoutBanner()
    {
        var pad = banner.Padding;
        int w = Math.Max(100, banner.ClientSize.Width - pad.Horizontal);
        int h1 = TextRenderer.MeasureText("Ag", lblBannerTitle.Font).Height;
        int h2 = TextRenderer.MeasureText("Ag", lblBannerBody.Font).Height * 3;
        lblBannerTitle.SetBounds(pad.Left, pad.Top, w, h1);
        lblBannerBody.SetBounds(pad.Left, pad.Top + h1 + 4, w, h2);
        int total = pad.Vertical + h1 + 4 + h2;
        if (banner.Height != total)
        {
            banner.Height = total;
            banner.Parent?.PerformLayout();
        }
    }

    void HideBanner()
    {
        banner.Visible = false;
        progress.BarColor = Theme.Accent;
    }

    void SetBusy(bool busy)
    {
        // Kopyalarken kurulum alanını gizle, yerine tek satırlık özet göster: günlüğe yer açılır
        foreach (var c in setupControls) c.Visible = !busy;
        lblJob.Visible = busy;
        cmbDrive.Enabled = btnRefresh.Enabled = txtFolder.Enabled = txtName.Enabled = btnBrowse.Enabled = btnOptions.Enabled = !busy;
        pageSettings.Enabled = !busy;
        foreach (var b in presetButtons.Values) b.Enabled = !busy;
        btnCreate.Enabled = !busy;
        btnAbort.Enabled = busy;
        if (!busy) UpdateDriveInfo();
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (cts == null) return;
        if (MessageBox.Show(this, "ISO oluşturma devam ediyor. İptal edip çıkılsın mı?", AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        cts.Cancel();
    }

    void Log(string msg)
    {
        lstLog.Items.Add($"[{DateTime.Now:HH:mm:ss}]  {msg}");
        lstLog.TopIndex = Math.Max(0, lstLog.Items.Count - 1);
    }

    string LogText() => string.Join(Environment.NewLine, lstLog.Items.Cast<string>());

    void CopyLog()
    {
        if (lstLog.Items.Count == 0) return;
        try { Clipboard.SetText($"{AppName} v{UpdateChecker.Format(UpdateChecker.Current)}{Environment.NewLine}{LogText()}"); }
        catch { return; }
        btnCopyLog.Text = "Kopyalandı ✓";
        var t = new System.Windows.Forms.Timer { Interval = 1500 };
        t.Tick += (_, _) => { t.Dispose(); btnCopyLog.Text = "Günlüğü kopyala"; };
        t.Start();
    }

    /// <summary>Her kopyalamanın günlüğü programın klasöründeki logs altına kaydedilir (bkz. AppPaths).</summary>
    void SaveLog(string label)
    {
        try
        {
            string dir = AppPaths.LogsDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{SanitizeFileName(label)}.txt");
            File.WriteAllText(path, $"{AppName} v{UpdateChecker.Format(UpdateChecker.Current)}{Environment.NewLine}{LogText()}{Environment.NewLine}");
            Log("Günlük kaydedildi: " + path);
        }
        catch { }
    }

    void Warn(string msg) => MessageBox.Show(this, msg, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    static string SanitizeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().Length == 0 ? "disk" : s.Trim();
    }

    static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
