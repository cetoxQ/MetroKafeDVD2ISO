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

    sealed record DriveItem(char Letter, string Label, bool Ready)
    {
        public override string ToString() => Ready
            ? $"{Letter}:   {(string.IsNullOrEmpty(Label) ? "(etiketsiz disk)" : Label)}"
            : $"{Letter}:   (disk yok)";
    }

    // Başlık çubuğu
    readonly FlatButton tabCopy = new() { Text = "Kopyala", Style = FlatButton.Kind.Tab, Selected = true, Dock = DockStyle.Fill };
    readonly FlatButton tabUpdates = new() { Text = "Güncellemeler", Style = FlatButton.Kind.Tab, Dock = DockStyle.Fill };

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
    readonly CheckBox chkSkipBad = DarkCheck("Okunamayan sektörleri atla (sıfırla doldur) ve devam et", true);
    readonly CheckBox chkOpenFolder = DarkCheck("Bitince klasörü aç", true);
    readonly Panel banner = new() { Dock = DockStyle.Top, BackColor = Theme.WarnBg, Padding = new Padding(16, 10, 12, 10), Margin = new Padding(0, 4, 0, 10), Visible = false };
    readonly Label lblBannerTitle = new() { ForeColor = Theme.WarnText };
    readonly Label lblBannerBody = new() { ForeColor = Theme.WarnTextDim };
    readonly Label lblPercent = new() { AutoSize = true, ForeColor = Theme.Text, Text = "%0", Margin = new Padding(0) };
    readonly Label lblPhase = new() { AutoSize = true, ForeColor = Theme.TextDim, Text = "Hazır.", Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(10, 0, 0, 6) };
    readonly ModernProgress progress = new() { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 12) };
    readonly StatCard cardSpeed = new("Kopyalama hızı");
    readonly StatCard cardDone = new("Kopyalanan");
    readonly StatCard cardEta = new("Kalan süre");
    readonly StatCard cardBad = new("Hatalı sektör");
    readonly FlatButton btnCreate = new() { Text = "ISO Oluştur", Style = FlatButton.Kind.Primary, Padding = new Padding(26, 9, 26, 9) };
    readonly FlatButton btnAbort = new() { Text = "İptal", Padding = new Padding(22, 9, 22, 9), Enabled = false, Margin = new Padding(0, 0, 8, 0) };
    readonly ListBox lstLog = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.TextDim };

    // Güncellemeler sayfası
    readonly Panel pageUpdates = new() { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 18), Visible = false };
    readonly Label lblUpdateStatus = new() { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 10, 0, 0) };
    readonly FlatButton btnCheckUpdate = new() { Text = "Güncellemeleri denetle", Style = FlatButton.Kind.Primary, Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnDownload = new() { Text = "Yeni sürümü indir", Visible = false, Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnGithub = new() { Text = "GitHub sayfası" };
    readonly RichTextBox rtbNotes = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, DetectUrls = false };

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
    bool stallLogged, hadTrouble;

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
        ClientSize = new Size(780, 840);
        Padding = new Padding(1);
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        BuildCopyPage();
        BuildUpdatesPage();
        content.Controls.Add(pageCopy);
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
        tabCopy.Click += (_, _) => ShowPage(copy: true);
        tabUpdates.Click += (_, _) => ShowPage(copy: false);
        btnCheckUpdate.Click += async (_, _) => await CheckUpdatesAsync();
        btnDownload.Click += (_, _) => OpenUrl(latestRelease?.PageUrl ?? UpdateChecker.ReleasesUrl);
        btnGithub.Click += (_, _) => OpenUrl(UpdateChecker.ReleasesUrl);
        FormClosing += OnFormClosing;
        Shown += (_, _) => { RefreshDrives(); ShowNotes(null); };

        // Açılışta konum her zaman Masaüstü
        txtFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        SelectPreset(NamePreset.Label);
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
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = h, BackColor = Theme.TitleBar, ColumnCount = 7, RowCount = 1, Margin = new Padding(0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        tabCopy.Padding = tabUpdates.Padding = new Padding(14, 0, 14, 0);
        min.Click += (_, _) => WindowState = FormWindowState.Minimized;
        close.Click += (_, _) => Close();

        bar.Controls.Add(logo, 0, 0);
        bar.Controls.Add(title, 1, 0);
        bar.Controls.Add(tabCopy, 2, 0);
        bar.Controls.Add(tabUpdates, 3, 0);
        bar.Controls.Add(filler, 4, 0);
        bar.Controls.Add(min, 5, 0);
        bar.Controls.Add(close, 6, 0);
        foreach (Control c in new Control[] { bar, logo, title, filler }) c.MouseDown += DragWindow;
        return bar;
    }

    void ShowPage(bool copy)
    {
        pageCopy.Visible = copy;
        pageUpdates.Visible = !copy;
        tabCopy.Selected = copy;
        tabUpdates.Selected = !copy;
    }

    // ───────────────────────── Sayfa düzenleri ─────────────────────────

    static CheckBox DarkCheck(string text, bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
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

        Add(Section("KAYNAK"));
        Add(Row(cmbDrive, btnRefresh));
        Add(lblInfo);

        Add(Section("HEDEF"));
        Add(FieldLabel("Kayıt konumu"));
        Add(Row(new FieldBox(txtFolder), btnBrowse));
        Add(FieldLabel("Dosya adı"));
        var nameExt = new Label { Text = ".iso", AutoSize = true, ForeColor = Theme.TextDim, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 4, 0) };
        Add(Row(new FieldBox(txtName), nameExt));

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
        Add(chips);
        Add(lblPath);

        var opts = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 12, 0, 6) };
        opts.Controls.Add(chkSkipBad);
        opts.Controls.Add(chkOpenFolder);
        Add(opts);

        // Çizik disk uyarı bandı
        lblBannerTitle.Font = new Font("Segoe UI Semibold", 10f);
        banner.Controls.Add(lblBannerTitle);
        banner.Controls.Add(lblBannerBody);
        banner.Paint += (_, e) => { using var b = new SolidBrush(Theme.WarnText); e.Graphics.FillRectangle(b, 0, 0, 4, banner.Height); };
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
        Add(buttons);

        var logBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 6, 8), Margin = new Padding(0), MinimumSize = new Size(0, 60) };
        logBox.Controls.Add(lstLog);
        root.Controls.Add(logBox);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        pageCopy.Controls.Add(root);
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

    async Task CheckUpdatesAsync()
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
            lblUpdateStatus.ForeColor = Theme.Error;
            lblUpdateStatus.Text = "Güncelleme denetlenemedi: " + ex.Message;
        }
        finally
        {
            btnCheckUpdate.Enabled = true;
        }
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

    void RefreshDrives()
    {
        char? selected = (cmbDrive.SelectedItem as DriveItem)?.Letter;
        var items = new List<DriveItem>();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.CDRom))
        {
            bool ready = d.IsReady;
            string label = "";
            if (ready) try { label = d.VolumeLabel; } catch { }
            items.Add(new DriveItem(d.Name[0], label, ready));
        }
        cmbDrive.SetItems(items);

        if (items.Count == 0)
        {
            lblInfo.Text = "Bilgisayarda DVD/CD sürücüsü bulunamadı.";
            btnCreate.Enabled = false;
            return;
        }
        int idx = items.FindIndex(i => i.Letter == selected);
        if (idx < 0) idx = items.FindIndex(i => i.Ready);
        cmbDrive.SelectedIndex = Math.Max(0, idx);
    }

    void UpdateDriveInfo()
    {
        if (cmbDrive.SelectedItem is not DriveItem item) return;
        var d = new DriveInfo(item.Letter.ToString());
        if (!d.IsReady)
        {
            lblInfo.Text = "Sürücü hazır değil — lütfen bir disk takın.";
            btnCreate.Enabled = false;
        }
        else
        {
            long size = DiscReader.GetDiscSize(item.Letter);
            string fs = "";
            try { fs = d.DriveFormat; } catch { }
            string kind = size > 900L * 1024 * 1024 ? "DVD" : "CD";
            lblInfo.Text = $"Etiket: {(item.Label.Length > 0 ? item.Label : "-")}     Tür: {kind}     Dosya sistemi: {fs}     Boyut: {FormatSize(size)}";
            btnCreate.Enabled = cts == null;
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
        if (cmbDrive.SelectedItem is not DriveItem item) return;
        if (!new DriveInfo(item.Letter.ToString()).IsReady && !DiscReader.IsMediaPresent(item.Letter))
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

        long size = DiscReader.GetDiscSize(item.Letter);
        if (size <= 0) { Warn("Disk boyutu okunamadı."); return; }

        // Boş alan ve FAT32 4 GB sınırı kontrolü
        try
        {
            var target = new DriveInfo(Path.GetPathRoot(output)!);
            long existing = File.Exists(output) ? new FileInfo(output).Length : 0;
            if (target.AvailableFreeSpace + existing < size)
            {
                Warn($"Hedef sürücüde yeterli boş alan yok.\nGereken: {FormatSize(size)}\nBoş: {FormatSize(target.AvailableFreeSpace)}");
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
        SetBusy(true);
        lstLog.Items.Clear();
        Log($"{item.Letter}: → {output}  ({FormatSize(size)})");
        BeginStats(size);

        var prog = new Progress<CopyProgress>(OnProgress);
        bool skip = chkSkipBad.Checked;
        var token = cts.Token;
        try
        {
            var result = await Task.Run(() => DiscReader.Copy(item.Letter, output, skip, prog, token), token);
            copyDone = result.BytesWritten;
            EndStats();
            progress.Value = progress.Maximum;
            lblPercent.Text = "%100";
            double avg = result.BytesWritten / Math.Max(0.001, result.Elapsed.TotalSeconds);
            string summary = $"Tamamlandı — {FormatSize(result.BytesWritten)}, süre {result.Elapsed:hh\\:mm\\:ss}, ortalama {FormatSize((long)avg)}/s" +
                             (result.BadSectors > 0 ? $", okunamayan sektör: {result.BadSectors}" : "");
            lblPhase.Text = "Tamamlandı.";
            cardSpeed.ValueText = $"ort. {FormatSize((long)avg)}/s";
            cardEta.ValueText = "Bitti";
            Log(summary);
            if (hadTrouble && result.BadSectors == 0)
                Log("Disk bazı bölgelerde zor okundu ancak tüm veriler eksiksiz kurtarıldı.");
            if (!Focused) Activate();
            if (result.BadSectors > 0)
                MessageBox.Show(this, $"ISO oluşturuldu ancak {result.BadSectors} sektör okunamadı ve sıfırla dolduruldu.\n" +
                                      "Disk çizik/hasarlı olabilir; bazı dosyalar bozuk olabilir.\n\n" +
                                      "İpucu: Diski yumuşak bir bezle merkezden dışa doğru silip tekrar deneyebilirsiniz.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (chkOpenFolder.Checked)
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
        stallLogged = hadTrouble = false;
        samples.Clear();
        copyWatch.Restart();
        lastProgressChange = 0;
        progress.Value = 0;
        progress.BarColor = Theme.Accent;
        lblPercent.Text = "%0";
        lblPhase.Text = "Okunuyor…";
        ResetStats();
        HideBanner();
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
        if (p.Message != null) Log(p.Message);
        if (p.Detail != null) { damageDetail = p.Detail; damageDetailTime = now; hadTrouble = true; }
        if (p.BytesDone != copyDone) { copyDone = p.BytesDone; lastProgressChange = now; }
        copyBad = p.BadSectors;
        if (p.TotalBytes > 0)
        {
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
        if (stall >= StallSeconds)
        {
            ShowBanner($"⚠  Disk okunmakta zorlanıyor — {stall:0} sn'dir ilerleme yok ({position})",
                (damageDetail != null && now - damageDetailTime < 30 ? $"Hasarlı bölge tekrar deneniyor: {damageDetail}.\n" : "") +
                "Disk çizik, kirli veya hasarlı olabilir. Sürücü bu bölgeyi tekrar tekrar okumaya çalışıyor; " +
                "bu birkaç dakika sürebilir. Bekleyebilir ya da İptal'e basabilirsiniz.\n" +
                "İpucu: Diski yumuşak bir bezle merkezden dışa doğru silip yeniden deneyin.");
            lblPhase.Text = "Okuma takıldı, bekleniyor…";
            if (!stallLogged) { Log($"Okuma takıldı ({position}). Disk çizik olabilir."); stallLogged = true; }
            hadTrouble = true;
        }
        else if (damageDetail != null && now - damageDetailTime < 5)
        {
            ShowBanner("⚠  Hasarlı bölge okunuyor — kopyalama yavaşlayabilir",
                $"Okunamayan bölge sektör sektör tekrar deneniyor ({damageDetail}). " +
                (chkSkipBad.Checked ? "Okunamayan sektörler atlanıp sıfırla doldurulacak." : "Bir sektör okunamazsa işlem duracak."));
            lblPhase.Text = "Hasarlı bölge okunuyor…";
        }
        else if (now > 10 && peakSpeed > 0 && speed < peakSpeed * 0.3)
        {
            ShowBanner($"⚠  Okuma hızı belirgin şekilde düştü ({FormatSize((long)speed)}/s, en yüksek {FormatSize((long)peakSpeed)}/s)",
                "Diskin bu bölgesinde çizik veya leke olabilir; sürücü yavaşlayarak okumaya çalışıyor. İşlem devam ediyor.");
            lblPhase.Text = "Yavaş okunuyor…";
            hadTrouble = true;
        }
        else
        {
            HideBanner();
            stallLogged = false;
            lblPhase.Text = "Okunuyor…";
        }
    }

    void ShowBanner(string title, string body)
    {
        if (lblBannerTitle.Text != title) lblBannerTitle.Text = title;
        if (lblBannerBody.Text != body) lblBannerBody.Text = body;
        banner.Visible = true;
        LayoutBanner();
        progress.BarColor = Color.FromArgb(220, 120, 20);
    }

    // Bant yüksekliği metnin satır sayısına göre ayarlanır
    void LayoutBanner()
    {
        var pad = banner.Padding;
        int w = Math.Max(100, banner.ClientSize.Width - pad.Horizontal);
        var flags = TextFormatFlags.WordBreak;
        int h1 = TextRenderer.MeasureText(lblBannerTitle.Text, lblBannerTitle.Font, new Size(w, int.MaxValue), flags).Height;
        int h2 = TextRenderer.MeasureText(lblBannerBody.Text, lblBannerBody.Font, new Size(w, int.MaxValue), flags).Height;
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
        cmbDrive.Enabled = btnRefresh.Enabled = txtFolder.Enabled = txtName.Enabled = btnBrowse.Enabled = chkSkipBad.Enabled = !busy;
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
