using System.Diagnostics;

namespace MetroKafeDVD2ISO;

/// <summary>Dönüştürme sırasındaki bir ISO.</summary>
sealed class Mp4Item(string path)
{
    public string Path { get; } = path;
    public List<HbTitle> Titles { get; set; } = [];
    /// <summary>Video CD ISO'sunda Titles ile aynı sırada çıkarılacak video dosyaları; DVD'de null.</summary>
    public List<VcdStream>? VcdStreams { get; set; }
    public string Status { get; set; } = "Bekliyor";
    public bool Scanned, Done, Failed;
    public TimeSpan Duration => Titles.Aggregate(TimeSpan.Zero, (a, t) => a + t.Duration);
    public bool Pending => !Done && !Failed;
}

/// <summary>
/// "MP4'e Dönüştür" sekmesi: ISO'ları sırayla HandBrakeCLI ile MP4'e çevirir. Çözünürlük ve kare hızı
/// kaynakla aynı kalır; yalnız ortalama bit hızı (ve buna göre tahmini boyut) seçilir.
/// </summary>
sealed class Mp4Panel : Panel
{
    static readonly Dictionary<string, (string Name, string Hint)> Encoders = new()
    {
        ["x264"] = ("İşlemci (x264) — en iyi kalite",
            "Aynı bit hızında en iyi görüntü kalitesi. Süre işlemciye bağlıdır; genelde videonun süresi kadar ya da daha kısa."),
        ["nvenc_h264"] = ("NVIDIA ekran kartı (NVENC) — çok hızlı",
            "3–10 kat hızlı. Aynı bit hızında kalite biraz düşer; gerekirse bit hızını biraz artırın."),
        ["qsv_h264"] = ("Intel ekran kartı (Quick Sync) — çok hızlı",
            "3–10 kat hızlı. Aynı bit hızında kalite biraz düşer; gerekirse bit hızını biraz artırın."),
        ["vce_h264"] = ("AMD ekran kartı (VCE) — çok hızlı",
            "3–10 kat hızlı. Aynı bit hızında kalite biraz düşer; gerekirse bit hızını biraz artırın."),
    };

    readonly AppSettings settings;
    readonly List<Mp4Item> items = [];
    readonly List<Control> setupControls = [];
    List<string> encoderIds = ["x264"];
    CancellationTokenSource? cts;
    bool loading;

    readonly ListBox lstFiles = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false };
    readonly FlatButton btnAddFiles = new() { Text = "ISO ekle…", Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnAddFolder = new() { Text = "Klasör ekle…", Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnRemove = new() { Text = "Seçileni kaldır", Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnClear = new() { Text = "Listeyi temizle" };
    readonly TextBox txtKbps = FieldBox.NewTextBox();
    readonly FlowLayoutPanel presetRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
    readonly Label lblEstimate = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 8) };
    readonly DropDown cmbEncoder = new() { PreferredWidth = 330, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
    readonly Label lblEncoderHint = new() { AutoSize = true, ForeColor = Theme.TextDim, MaximumSize = new Size(350, 0), Margin = new Padding(0, 4, 0, 0) };
    readonly RadioButton rbDeintOn = DarkRadio("Açık — taramalı kareleri düzelt (Decomb)");
    readonly RadioButton rbDeintOff = DarkRadio("Kapalı — görüntüye dokunma");
    readonly CheckBox chkNextToIso = MainForm.DarkCheck("MP4'ü ISO ile aynı klasöre kaydet");
    readonly TextBox txtFolder = FieldBox.NewTextBox();
    readonly FlatButton btnBrowse = new() { Text = "Gözat…", Anchor = AnchorStyles.Left };
    readonly ModernProgress progress = new() { Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 6) };
    readonly Label lblStatus = new() { AutoSize = false, AutoEllipsis = true, Height = 22, ForeColor = Theme.TextDim, Text = "Hazır." };
    readonly Label lblSystem = new() { AutoSize = false, AutoEllipsis = true, Height = 22, ForeColor = Theme.Text, Text = "Sistem bilgisi okunuyor…" };
    readonly System.Windows.Forms.Timer statsTimer = new() { Interval = 1000 };
    SystemStats? stats;
    bool sampling;
    readonly FlatButton btnStart = new() { Text = "Dönüştürmeyi başlat", Style = FlatButton.Kind.Primary, Padding = new Padding(22, 9, 22, 9) };
    readonly FlatButton btnCancel = new() { Text = "İptal", Padding = new Padding(22, 9, 22, 9), Enabled = false, Margin = new Padding(0, 0, 8, 0) };
    readonly ListBox lstLog = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.TextDim };

    public bool IsBusy => cts != null;

    public Mp4Panel(AppSettings settings)
    {
        this.settings = settings;
        Dock = DockStyle.Fill;
        Padding = new Padding(22, 16, 22, 18);
        Visible = false;
        Build();
        LoadSettings();
        RefreshEstimate();
    }

    static RadioButton DarkRadio(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Text,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 2, 0, 0),
        FlatAppearance = { BorderColor = Theme.Border, CheckedBackColor = Theme.Accent, MouseOverBackColor = Theme.SurfaceHover },
    };

    static Label SmallHint(string text, int left = 22) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.TextDim,
        MaximumSize = new Size(330, 0),
        Margin = new Padding(left, 0, 0, 6),
    };

    // ───────────────────────── Düzen ─────────────────────────

    void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c) { if (c.Dock == DockStyle.None) c.Dock = DockStyle.Top; root.Controls.Add(c); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
        void AddSetup(Control c) { Add(c); setupControls.Add(c); }

        Add(MainForm.Section("ISO DOSYALARI"));
        lstFiles.ItemHeight = TextRenderer.MeasureText("Ag", Font).Height + 10;
        lstFiles.DrawItem += DrawFileItem;
        var listBox = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Surface, Padding = new Padding(8, 6, 6, 6), Margin = new Padding(0, 0, 0, 8) };
        listBox.Controls.Add(lstFiles);
        Add(listBox);
        var listButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        listButtons.Controls.AddRange([btnAddFiles, btnAddFolder, btnRemove, btnClear]);
        Add(listButtons);

        AddSetup(MainForm.Section("DÖNÜŞTÜRME AYARLARI"));
        var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // Sol: bit hızı, tahmini boyut, kodlayıcı
        var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        left.Controls.Add(new Label { Text = "Ortalama video bit hızı", AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 0, 0, 4) });
        var kbpsRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        kbpsRow.Controls.Add(new FieldBox(txtKbps) { PreferredWidth = 80, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) });
        kbpsRow.Controls.Add(new Label { Text = "kbps", AutoSize = true, ForeColor = Theme.TextDim, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) });
        left.Controls.Add(kbpsRow);
        BuildPresetChips();
        left.Controls.Add(presetRow);
        lblEstimate.Font = new Font("Segoe UI Semibold", 9.5f);
        left.Controls.Add(lblEstimate);
        left.Controls.Add(new Label { Text = "Kodlayıcı", AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 0, 0, 4) });
        left.Controls.Add(cmbEncoder);
        left.Controls.Add(lblEncoderHint);

        // Sağ: taramalı görüntü düzeltme, ses
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        right.Controls.Add(new Label { Text = "Taramalı görüntü düzeltme (deinterlace)", AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 0, 0, 2) });
        right.Controls.Add(rbDeintOn);
        right.Controls.Add(SmallHint("Düğün kameralarının çoğu taramalı (interlaced) kayıt yapar. Yalnız taramalı kareler düzeltilir; " +
                                     "bilgisayarda ve telefonda hareketli sahnelerde tarak gibi çizgiler görünmez. Kare hızı kaynakla aynı kalır."));
        right.Controls.Add(rbDeintOff);
        right.Controls.Add(SmallHint("Görüntüye hiçbir filtre uygulanmaz. Kaynak taramalıysa bilgisayarda hareketli sahnelerde yatay çizgiler " +
                                     "görünebilir; televizyonda genelde sorun olmaz."));
        right.Controls.Add(SmallHint("Ses: AAC 160 kbps stereo (her cihazda oynar).", 0));

        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(right, 1, 0);
        AddSetup(grid);
        var note = SmallHint("Çözünürlük ve kare hızı kaynakla aynı kalır (kırpma ve ölçekleme yok). Çok geçişli kodlama ve turbo analiz kapalı.", 0);
        note.MaximumSize = Size.Empty;
        note.Margin = new Padding(0, 4, 0, 6);
        AddSetup(note);

        AddSetup(chkNextToIso);
        var folderRow = MainForm.Row(new FieldBox(txtFolder), btnBrowse);
        folderRow.Margin = new Padding(0, 4, 0, 4);
        AddSetup(folderRow);

        Add(MainForm.Section("İLERLEME"));
        Add(progress);
        Add(lblStatus);
        Add(lblSystem);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 4, 0, 10) };
        buttons.Controls.Add(btnStart);
        buttons.Controls.Add(btnCancel);
        Add(buttons);

        var logBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(10, 8, 6, 8), Margin = new Padding(0), MinimumSize = new Size(0, 50) };
        logBox.Controls.Add(lstLog);
        root.Controls.Add(logBox);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        btnAddFiles.Click += (_, _) => AddFilesDialog();
        btnAddFolder.Click += (_, _) => AddFolderDialog();
        btnRemove.Click += (_, _) => RemoveSelected();
        btnClear.Click += (_, _) => { items.RemoveAll(i => !IsCurrent(i)); RefreshList(); };
        btnBrowse.Click += (_, _) => BrowseFolder();
        btnStart.Click += async (_, _) => await RunQueueAsync();
        btnCancel.Click += (_, _) => { cts?.Cancel(); btnCancel.Enabled = false; lblStatus.Text = "İptal ediliyor…"; };
        txtKbps.TextChanged += (_, _) => RefreshEstimate(); // sekmedeki değer o işe özel; varsayılan Ayarlar'da
        txtKbps.KeyPress += (_, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        cmbEncoder.SelectedIndexChanged += (_, _) => { UpdateEncoderHint(); SaveSettings(); };
        rbDeintOn.CheckedChanged += (_, _) => SaveSettings();
        chkNextToIso.CheckedChanged += (_, _) => { txtFolder.Enabled = btnBrowse.Enabled = !chkNextToIso.Checked; SaveSettings(); };
        txtFolder.TextChanged += (_, _) => SaveSettings();
        statsTimer.Tick += async (_, _) => await UpdateSystemAsync();
        VisibleChanged += (_, _) => { if (Visible || IsBusy) statsTimer.Start(); else statsTimer.Stop(); };
        lstFiles.AllowDrop = true;
        lstFiles.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        lstFiles.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (var p in paths)
                if (Directory.Exists(p)) foreach (var f in Directory.GetFiles(p, "*.iso")) Enqueue(f, false);
                else if (p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) Enqueue(p, false);
        };
    }

    void DrawFileItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        var item = items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Theme.SurfaceHover : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);

        string info = !item.Scanned ? ""
            : item.Titles.Count == 0 ? "video bulunamadı"
            : $"{item.Titles.Count} başlık · {item.Duration:h\\:mm\\:ss} · ≈ {MainForm.FormatSize(HandBrake.EstimateBytes(item.Duration, Kbps))}";
        var statusColor = item.Failed ? Theme.Error : item.Done ? Theme.Success : IsCurrent(item) ? Theme.Accent : Theme.TextDim;
        int statusWidth = 210;
        var nameRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - statusWidth - 230, e.Bounds.Height);
        var infoRect = new Rectangle(nameRect.Right + 6, e.Bounds.Y, 220, e.Bounds.Height);
        var statusRect = new Rectangle(e.Bounds.Right - statusWidth, e.Bounds.Y, statusWidth - 4, e.Bounds.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(e.Graphics, System.IO.Path.GetFileName(item.Path), lstFiles.Font, nameRect, Theme.Text, flags);
        TextRenderer.DrawText(e.Graphics, info, lstFiles.Font, infoRect, Theme.TextDim, flags);
        TextRenderer.DrawText(e.Graphics, item.Status, lstFiles.Font, statusRect, statusColor, flags | TextFormatFlags.Right);
    }

    // ───────────────────────── Ayarlar ─────────────────────────

    /// <summary>Hazır bit hızı düğmeleri (Ayarlar'dan değiştirilebilir).</summary>
    void BuildPresetChips()
    {
        presetRow.SuspendLayout();
        foreach (Control c in presetRow.Controls.Cast<Control>().ToList()) c.Dispose();
        presetRow.Controls.Clear();
        presetRow.Controls.Add(new Label { Text = "Hazır:", AutoSize = true, ForeColor = Theme.TextDim, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) });
        foreach (int k in settings.Mp4Presets.Where(k => k > 0).Take(4))
        {
            var chip = new FlatButton { Text = k.ToString(), Style = FlatButton.Kind.Chip, Padding = new Padding(9, 3, 9, 3), Margin = new Padding(0, 0, 4, 0), Anchor = AnchorStyles.Left };
            chip.Click += (_, _) => txtKbps.Text = k.ToString();
            presetRow.Controls.Add(chip);
        }
        presetRow.ResumeLayout();
    }

    /// <summary>Ayarlar'da varsayılan/hazır bit hızları değişince çağrılır.</summary>
    public void ApplyBitrateSettings()
    {
        BuildPresetChips();
        if (IsBusy) return;
        txtKbps.Text = settings.Mp4VideoKbps.ToString();
        RefreshEstimate();
    }

    int Kbps => int.TryParse(txtKbps.Text, out int k) ? Math.Clamp(k, 300, 50000) : 2500;

    void LoadSettings()
    {
        loading = true;
        txtKbps.Text = settings.Mp4VideoKbps.ToString();
        rbDeintOn.Checked = settings.Mp4Deinterlace;
        rbDeintOff.Checked = !settings.Mp4Deinterlace;
        chkNextToIso.Checked = settings.Mp4NextToIso;
        txtFolder.Text = settings.Mp4Folder.Length > 0 ? settings.Mp4Folder : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        txtFolder.Enabled = btnBrowse.Enabled = !chkNextToIso.Checked;
        SetEncoders(["x264"]);
        loading = false;
    }

    void SaveSettings()
    {
        if (loading) return;
        if (cmbEncoder.SelectedIndex >= 0) settings.Mp4Encoder = encoderIds[cmbEncoder.SelectedIndex];
        settings.Mp4Deinterlace = rbDeintOn.Checked;
        settings.Mp4NextToIso = chkNextToIso.Checked;
        settings.Mp4Folder = txtFolder.Text.Trim();
        settings.Save();
    }

    void SetEncoders(List<string> ids)
    {
        bool wasLoading = loading;
        loading = true;
        encoderIds = ids.Count > 0 ? ids : ["x264"];
        cmbEncoder.SetItems(encoderIds.Select(id => (object)(Encoders.TryGetValue(id, out var e) ? e.Name : id)));
        int idx = encoderIds.IndexOf(settings.Mp4Encoder);
        cmbEncoder.SelectedIndex = idx >= 0 ? idx : 0;
        loading = wasLoading;
        UpdateEncoderHint();
    }

    void UpdateEncoderHint()
    {
        string id = cmbEncoder.SelectedIndex >= 0 ? encoderIds[cmbEncoder.SelectedIndex] : "x264";
        lblEncoderHint.Text = (Encoders.TryGetValue(id, out var e) ? e.Hint : "") +
                              (HandBrake.IsInstalled ? "" : " Ekran kartı seçenekleri HandBrake indirildikten sonra görünür.");
    }

    /// <summary>HandBrake kuruluysa bu bilgisayardaki kodlayıcıları yükler (sekme ilk açıldığında).</summary>
    public async Task LoadEncodersAsync()
    {
        var gpus = SystemStats.GpuNames();
        Log("Ekran kartları: " + (gpus.Count > 0 ? string.Join(", ", gpus) : "bulunamadı"));
        if (!HandBrake.IsInstalled) return;
        try
        {
            var list = await HandBrake.GetH264EncodersAsync();
            SetEncoders(list);
            Log("HandBrake'in bu bilgisayarda kullanabildiği kodlayıcılar: " +
                string.Join(", ", list.Select(id => Encoders.TryGetValue(id, out var e) ? e.Name.Split(" — ")[0] : id)));
            // Takılı kart için kodlayıcı görünmüyorsa nedenini söyle
            bool Has(string vendor) => gpus.Any(g => g.Contains(vendor, StringComparison.OrdinalIgnoreCase));
            if (Has("NVIDIA") && !list.Contains("nvenc_h264"))
                Log("NVIDIA kart var ama HandBrake NVENC'i kullanamıyor: ekran kartı sürücüsünü güncelleyin (çok eski kartlarda NVENC yoktur).");
            if ((Has("AMD") || Has("Radeon")) && !list.Any(e => e.StartsWith("vce") || e.StartsWith("vcn")))
                Log("AMD kart var ama HandBrake kullanamıyor: AMD Adrenalin sürücüsünü güncelleyin.");
            if (Has("Intel") && !list.Contains("qsv_h264"))
                Log("Intel ekran kartı var ama Quick Sync kullanılamıyor: Intel grafik sürücüsünü güncelleyin; BIOS'ta dahili grafik kapalı olabilir.");
        }
        catch (Exception ex) { Log("Kodlayıcılar okunamadı: " + ex.Message); }
    }

    async Task UpdateSystemAsync()
    {
        if (sampling) return;
        sampling = true;
        try
        {
            stats ??= new SystemStats();
            var snap = await Task.Run(stats.Sample);
            lblSystem.Text = SystemStats.Format(snap);
        }
        catch { lblSystem.Text = "Sistem bilgisi okunamadı."; }
        finally { sampling = false; }
    }

    void RefreshEstimate()
    {
        var scanned = items.Where(i => i.Scanned && i.Titles.Count > 0 && !i.Done).ToList();
        if (scanned.Count == 0)
        {
            lblEstimate.Text = $"Tahmini boyut: saatlik video ≈ {MainForm.FormatSize(HandBrake.EstimateBytes(TimeSpan.FromHours(1), Kbps))}";
        }
        else
        {
            var total = scanned.Aggregate(TimeSpan.Zero, (a, i) => a + i.Duration);
            lblEstimate.Text = $"Tahmini toplam boyut: ≈ {MainForm.FormatSize(HandBrake.EstimateBytes(total, Kbps))}  " +
                               $"({scanned.Count} ISO, {(int)total.TotalHours}:{total:mm\\:ss})";
        }
        lstFiles.Invalidate();
    }

    void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "MP4 dosyalarının kaydedileceği klasörü seçin", UseDescriptionForTitle = true };
        if (Directory.Exists(txtFolder.Text)) dlg.InitialDirectory = txtFolder.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dlg.SelectedPath;
    }

    // ───────────────────────── Liste ─────────────────────────

    Mp4Item? current;
    bool IsCurrent(Mp4Item i) => ReferenceEquals(i, current);

    void AddFilesDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "ISO kalıbı (*.iso)|*.iso", Multiselect = true, Title = "Dönüştürülecek ISO dosyalarını seçin" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            foreach (var f in dlg.FileNames) Enqueue(f, false);
    }

    void AddFolderDialog()
    {
        using var dlg = new FolderBrowserDialog { Description = "İçindeki tüm ISO dosyaları eklenecek klasörü seçin", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var files = Directory.GetFiles(dlg.SelectedPath, "*.iso").OrderBy(f => f).ToList();
        if (files.Count == 0) Log("Klasörde ISO dosyası bulunamadı: " + dlg.SelectedPath);
        foreach (var f in files) Enqueue(f, false);
    }

    void RemoveSelected()
    {
        int i = lstFiles.SelectedIndex;
        if (i < 0 || i >= items.Count || IsCurrent(items[i])) return;
        items.RemoveAt(i);
        RefreshList();
    }

    void RefreshList()
    {
        int sel = lstFiles.SelectedIndex;
        lstFiles.BeginUpdate();
        lstFiles.Items.Clear();
        foreach (var i in items) lstFiles.Items.Add(i.Path);
        lstFiles.EndUpdate();
        if (sel >= 0 && sel < lstFiles.Items.Count) lstFiles.SelectedIndex = sel;
        RefreshEstimate();
    }

    /// <summary>Listeye ISO ekler; autoStart ise dönüştürme çalışmıyorsa başlatır (ISO sonrası otomatik MP4).</summary>
    public void Enqueue(string iso, bool autoStart)
    {
        var existing = items.FirstOrDefault(i => string.Equals(i.Path, iso, StringComparison.OrdinalIgnoreCase));
        if (existing != null && existing.Pending) return;
        if (existing != null) items.Remove(existing);
        var item = new Mp4Item(iso);
        items.Add(item);
        RefreshList();
        Log("Listeye eklendi: " + System.IO.Path.GetFileName(iso));
        if (HandBrake.IsInstalled) _ = ScanAsync(item);
        else item.Status = "Bekliyor (HandBrake gerekli)";
        if (autoStart && !IsBusy) _ = RunQueueAsync();
    }

    async Task ScanAsync(Mp4Item item)
    {
        if (item.Scanned) return;
        item.Status = "Taranıyor…";
        lstFiles.Invalidate();
        try
        {
            var scan = await HandBrake.ScanAsync(item.Path, CancellationToken.None);
            var (titles, reason) = HandBrake.PickTitles(scan);
            // HandBrake ISO içine yalnız DVD olarak bakar; DVD başlığı yoksa Video CD dosyalarını ara
            if (titles.Count == 0 && await Task.Run(() => VideoCdIso.Find(item.Path)) is { } vcd)
            {
                // Ana video (en uzun) önce, diğerleri ad sırasıyla ayrı MP4 olarak
                var main = vcd.Streams.OrderByDescending(v => v.Duration).First();
                var ordered = new List<VcdStream> { main };
                ordered.AddRange(vcd.Streams.Where(v => v != main));
                item.VcdStreams = ordered;
                titles = ordered.Select((v, i) => new HbTitle(i + 1, v.Duration, 0, 0, 0, 0, 0, 1, false)).ToList();
                reason = $"{vcd.Kind}: {ordered.Count} video dosyası ({string.Join(", ", ordered.Select(v => $"{v.Name} ≈ {v.Duration:h\\:mm\\:ss}"))}).";
                if (vcd.Kind == "Video CD")
                    reason += " Video CD görüntüsü düşük çözünürlüklüdür (352×288); 1000–1500 kbps yeterlidir, daha yüksek bit hızı kaliteyi artırmaz, yalnız dosyayı büyütür.";
            }
            item.Titles = titles;
            item.Scanned = true;
            if (titles.Count == 0)
            {
                item.Failed = true;
                item.Status = "Video yok";
                Log($"{System.IO.Path.GetFileName(item.Path)}: dönüştürülecek video bulunamadı " +
                    "(ISO'da ne DVD VIDEO_TS ne de Video CD MPEGAV/MPEG2 klasörü var).");
            }
            else
            {
                if (!IsCurrent(item)) item.Status = "Bekliyor";
                Log($"{System.IO.Path.GetFileName(item.Path)}: {reason}" +
                    (item.VcdStreams == null ? " " + string.Join("; ", titles.Select(t => t.Describe())) : ""));
            }
        }
        catch (Exception ex)
        {
            item.Scanned = true;
            item.Failed = true;
            item.Status = "Taranamadı";
            Log($"{System.IO.Path.GetFileName(item.Path)}: {ex.Message}");
        }
        RefreshEstimate();
    }

    // ───────────────────────── Dönüştürme ─────────────────────────

    async Task<bool> EnsureHandBrakeAsync()
    {
        if (HandBrake.IsInstalled) return true;
        if (MessageBox.Show(this,
                "MP4 dönüştürme için HandBrakeCLI gerekiyor (ücretsiz, açık kaynak, yaklaşık 25 MB).\n\n" +
                "HandBrake'in resmi GitHub sayfasından indirilip programın klasöründeki \"tools\" altına kaydedilecek. İndirilsin mi?",
                "MetroKafe DVD2ISO", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return false;
        try
        {
            var status = new Progress<string>(s => lblStatus.Text = s);
            string version = await HandBrake.InstallAsync(status, CancellationToken.None);
            Log($"HandBrake {version} indirildi: {HandBrake.ExePath}");
            await LoadEncodersAsync();
            return true;
        }
        catch (Exception ex)
        {
            lblStatus.Text = "HandBrake indirilemedi.";
            Log("HandBrake indirilemedi: " + ex.Message);
            MessageBox.Show(this, "HandBrake indirilemedi: " + ex.Message, "MetroKafe DVD2ISO", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    string OutputFor(Mp4Item item, int order)
    {
        string dir = chkNextToIso.Checked ? System.IO.Path.GetDirectoryName(item.Path)! : txtFolder.Text.Trim();
        string name = System.IO.Path.GetFileNameWithoutExtension(item.Path) + (order == 0 ? "" : $"_{order + 1}");
        string path = System.IO.Path.Combine(dir, name + ".mp4");
        for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(dir, $"{name} ({n}).mp4");
        return path;
    }

    async Task RunQueueAsync()
    {
        if (IsBusy) return;
        if (!items.Any(i => i.Pending)) { Log("Listede dönüştürülecek ISO yok. \"ISO ekle…\" ile ekleyin."); return; }
        if (!await EnsureHandBrakeAsync()) return;
        if (!chkNextToIso.Checked && !Directory.Exists(txtFolder.Text.Trim()))
        {
            MessageBox.Show(this, "Kayıt klasörü bulunamadı.", "MetroKafe DVD2ISO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        cts = new CancellationTokenSource();
        SetBusy(true);
        var settingsNow = new EncodeSettings(Kbps, encoderIds[Math.Max(0, cmbEncoder.SelectedIndex)], rbDeintOn.Checked);
        if (settingsNow.Encoder != "x264")
            Log("Not: Ekran kartı kodlamayı yapar; taramalı görüntü düzeltme (Decomb) ve ses işlemcide yapıldığı için işlemci de kullanılır. " +
                "Görev Yöneticisi'nde ekran kartı yükü \"3D\" değil \"Video Encode\" grafiğinde görünür.");
        Log($"Dönüştürme başladı: {settingsNow.VideoKbps} kbps, {Encoders.GetValueOrDefault(settingsNow.Encoder).Name ?? settingsNow.Encoder}, " +
            $"deinterlace {(settingsNow.Deinterlace ? "açık" : "kapalı")}, ses AAC {HandBrake.AudioKbps} kbps.");
        var sw = Stopwatch.StartNew();
        int converted = 0;
        var failedBefore = items.Where(i => i.Failed).ToHashSet();
        try
        {
            // Sıra çalışırken eklenen ISO'lar da (ISO sonrası otomatik MP4) aynı döngüde işlenir
            while (items.FirstOrDefault(i => i.Pending) is { } item)
            {
                cts.Token.ThrowIfCancellationRequested();
                current = item;
                await ScanAsync(item);
                if (item.Failed) continue;
                try
                {
                    await ConvertItemAsync(item, settingsNow, cts.Token);
                    item.Done = true;
                    converted++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    item.Failed = true;
                    item.Status = "Hata";
                    Log($"{System.IO.Path.GetFileName(item.Path)}: {ex.Message}");
                }
                finally
                {
                    current = null;
                    RefreshEstimate();
                }
            }
            int failedNow = items.Count(i => i.Failed && !failedBefore.Contains(i));
            progress.Value = converted > 0 ? progress.Maximum : 0;
            lblStatus.Text = $"Bitti — {converted} ISO dönüştürüldü" + (failedNow > 0 ? $", {failedNow} ISO'da hata (ayrıntı günlükte)" : "") +
                             $", süre {sw.Elapsed:hh\\:mm\\:ss}.";
            Log(lblStatus.Text);
        }
        catch (OperationCanceledException)
        {
            foreach (var i in items.Where(i => i.Pending)) i.Status = "Bekliyor";
            lblStatus.Text = "İptal edildi.";
            Log("Dönüştürme iptal edildi; yarım kalan MP4 silindi.");
            progress.Value = 0;
        }
        finally
        {
            current = null;
            cts.Dispose();
            cts = null;
            SetBusy(false);
            RefreshEstimate();
        }
    }

    async Task ConvertItemAsync(Mp4Item item, EncodeSettings s, CancellationToken ct)
    {
        string name = System.IO.Path.GetFileName(item.Path);
        var totalSeconds = Math.Max(1, items.Where(i => i.Scanned && !i.Failed).Sum(i => i.Duration.TotalSeconds));
        double doneBefore = items.Where(i => i.Done).Sum(i => i.Duration.TotalSeconds);
        int index = items.Where(i => !i.Failed).ToList().IndexOf(item) + 1, count = items.Count(i => !i.Failed);

        for (int k = 0; k < item.Titles.Count; k++)
        {
            var title = item.Titles[k];
            string output = OutputFor(item, k);
            var vcdStream = item.VcdStreams?[k];
            string what = vcdStream?.Name ?? $"başlık {title.Index}";
            Log($"{name}: {what} → {output}");
            var tsw = Stopwatch.StartNew();

            // Video CD: videoyu önce MP4'ün klasörüne gizli geçici .mpg olarak çıkar, HandBrake'e onu ver
            string input = item.Path;
            int hbTitle = title.Index;
            string? temp = null;
            if (vcdStream != null)
            {
                temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!,
                    "." + System.IO.Path.GetFileNameWithoutExtension(output) + ".vcd.mpg");
                var extractProg = new Progress<double>(f =>
                {
                    item.Status = $"Çıkarılıyor %{f * 100:0}";
                    lblStatus.Text = $"ISO {index}/{count} · {name} · {vcdStream.Name} çıkarılıyor… %{f * 100:0}";
                    lstFiles.Invalidate();
                });
                await Task.Run(() => VideoCdIso.Extract(item.Path, vcdStream, temp, extractProg, ct), ct);
                try { File.SetAttributes(temp, File.GetAttributes(temp) | FileAttributes.Hidden); } catch { }
                input = temp;
                hbTitle = 1;
            }
            var prog = new Progress<HbProgress>(p =>
            {
                item.Status = $"%{p.Percent:0}";
                double overall = (doneBefore + title.Duration.TotalSeconds * p.Percent / 100) / totalSeconds;
                progress.Value = (int)(Math.Clamp(overall, 0, 1) * progress.Maximum);
                lblStatus.Text = $"ISO {index}/{count} · {name}" + (item.Titles.Count > 1 ? $" · başlık {k + 1}/{item.Titles.Count}" : "") +
                                 $" · %{p.Percent:0.0}" + (p.Fps > 0 ? $" · {p.Fps:0} fps" : "") +
                                 (p.Eta is { } eta ? $" · kalan {eta:hh\\:mm\\:ss}" : "");
                lstFiles.Invalidate();
            });
            var info = new Progress<string>(Log);
            bool encoderLogged = false;
            try
            {
                await HandBrake.EncodeAsync(input, hbTitle, output, s, prog, ct, line =>
                {
                    if (encoderLogged && line.Contains("kodlayıcı")) return;
                    encoderLogged |= line.Contains("kodlayıcı");
                    ((IProgress<string>)info).Report(line);
                });
            }
            finally
            {
                if (temp != null) try { File.Delete(temp); } catch { }
            }
            doneBefore += title.Duration.TotalSeconds;
            Log($"{name}: {what} tamam —{MainForm.FormatSize(new FileInfo(output).Length)}, süre {tsw.Elapsed:hh\\:mm\\:ss}.");
        }
        item.Status = "Bitti";
    }

    void SetBusy(bool busy)
    {
        foreach (var c in setupControls) c.Visible = !busy;
        btnStart.Enabled = !busy;
        btnCancel.Enabled = busy;
        btnAddFiles.Enabled = btnAddFolder.Enabled = true; // sıra çalışırken de ISO eklenebilir
        btnClear.Enabled = btnRemove.Enabled = !busy;
    }

    public void CancelAll() => cts?.Cancel();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { statsTimer.Dispose(); stats?.Dispose(); }
        base.Dispose(disposing);
    }

    void Log(string msg)
    {
        lstLog.Items.Add($"[{DateTime.Now:HH:mm:ss}]  {msg}");
        lstLog.TopIndex = Math.Max(0, lstLog.Items.Count - 1);
    }
}
