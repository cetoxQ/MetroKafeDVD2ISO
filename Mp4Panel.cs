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
    /// <summary>ISO dosyasının boyutu (bayt).</summary>
    public long IsoBytes { get; } = File.Exists(path) ? new FileInfo(path).Length : 0;
    /// <summary>Bu ISO için video bit hızı (satırda düzenlenebilir).</summary>
    public int Kbps { get; set; }
    /// <summary>true: bit hızı ISO boyutuna göre otomatik hesaplandı (elle değiştirilmedi).</summary>
    public bool AutoKbps { get; set; } = true;
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

    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.Border,
        RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        AllowUserToOrderColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, EnableHeadersVisualStyles = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        ScrollBars = ScrollBars.Vertical, AllowDrop = true, ShowCellToolTips = true,
    };
    DataGridViewTextBoxColumn colName = null!, colSize = null!, colInfo = null!, colKbps = null!, colEstimate = null!, colStatus = null!;
    readonly FlatButton btnAddFiles = new() { Text = "ISO ekle…", Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnAddFolder = new() { Text = "Klasör ekle…", Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnRemove = new() { Text = "Seçileni kaldır", Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton btnClear = new() { Text = "Listeyi temizle" };
    readonly FlowLayoutPanel presetRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
    readonly Label lblEstimate = new() { AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 8) };
    readonly DropDown cmbEncoder = new() { PreferredWidth = 330, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
    readonly FlatButton btnDeint = new() { Style = FlatButton.Kind.Tab, Padding = new Padding(0, 4, 8, 4), Margin = new Padding(0, 0, 0, 2) };
    readonly FlowLayoutPanel deintBody = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false, Margin = new Padding(0) };
    TableLayoutPanel folderRow = null!;
    readonly ToolTip tips = new() { InitialDelay = 300, AutoPopDelay = 15000 };
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
        BuildGrid();
        var listBox = new Panel { Dock = DockStyle.Top, Height = 132, BackColor = Theme.Surface, Padding = new Padding(6, 4, 4, 4), Margin = new Padding(0, 0, 0, 8) };
        listBox.Controls.Add(grid);
        Add(listBox);
        var listButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        listButtons.Controls.AddRange([btnAddFiles, btnAddFolder, btnRemove, btnClear]);
        Add(listButtons);

        AddSetup(MainForm.Section("DÖNÜŞTÜRME AYARLARI"));
        var settingsGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        Label Inline(string text, int right = 6) => new() { Text = text, AutoSize = true, ForeColor = Theme.TextDim, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, right, 0) };

        // Sol: bit hızı düğmeleri (seçili satırlara), toplam tahmini boyut, kodlayıcı (açıklaması ipucunda)
        var left = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        left.Controls.Add(Inline("Bit hızı tabloda her ISO için ayrı girilir. Seçili satırlara uygula:", 0));
        BuildPresetChips();
        left.Controls.Add(presetRow);
        lblEstimate.Font = new Font("Segoe UI Semibold", 9.5f);
        left.Controls.Add(lblEstimate);
        var encRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        encRow.Controls.Add(Inline("Kodlayıcı:", 8));
        cmbEncoder.PreferredWidth = 280;
        encRow.Controls.Add(cmbEncoder);
        left.Controls.Add(encRow);

        // Sağ: tıklayınca açılan taramalı görüntü düzeltme, altında ses ve çözünürlük notu
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        right.Controls.Add(btnDeint);
        deintBody.Controls.Add(rbDeintOn);
        deintBody.Controls.Add(SmallHint("Düğün kameralarının çoğu taramalı (interlaced) kayıt yapar. Yalnız taramalı kareler düzeltilir; " +
                                         "bilgisayarda ve telefonda hareketli sahnelerde tarak gibi çizgiler görünmez. Kare hızı kaynakla aynı kalır."));
        deintBody.Controls.Add(rbDeintOff);
        deintBody.Controls.Add(SmallHint("Görüntüye hiçbir filtre uygulanmaz. Kaynak taramalıysa bilgisayarda hareketli sahnelerde yatay çizgiler " +
                                         "görünebilir; televizyonda genelde sorun olmaz."));
        right.Controls.Add(deintBody);
        right.Controls.Add(SmallHint("Ses: AAC 160 kbps stereo (her cihazda oynar).", 0));
        right.Controls.Add(SmallHint("Çözünürlük ve kare hızı kaynakla aynı kalır (kırpma/ölçekleme yok). Çok geçişli kodlama ve turbo analiz kapalı.", 0));

        settingsGrid.Controls.Add(left, 0, 0);
        settingsGrid.Controls.Add(right, 1, 0);
        AddSetup(settingsGrid);

        // "ISO ile aynı klasöre" işaretliyken konum satırı gizli; işaret kaldırılınca aşağı doğru açılır
        AddSetup(chkNextToIso);
        folderRow = MainForm.Row(new FieldBox(txtFolder), btnBrowse);
        folderRow.Margin = new Padding(22, 4, 0, 4);
        Add(folderRow);

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
        cmbEncoder.SelectedIndexChanged += (_, _) => { UpdateEncoderHint(); SaveSettings(); };
        rbDeintOn.CheckedChanged += (_, _) => { UpdateDeintHeader(); SaveSettings(); };
        btnDeint.Click += (_, _) => { deintBody.Visible = !deintBody.Visible; UpdateDeintHeader(); };
        chkNextToIso.CheckedChanged += (_, _) => { UpdateFolderRow(); SaveSettings(); };
        txtFolder.TextChanged += (_, _) => SaveSettings();
        statsTimer.Tick += async (_, _) => await UpdateSystemAsync();
        VisibleChanged += (_, _) => { if (Visible || IsBusy) statsTimer.Start(); else statsTimer.Stop(); };
        grid.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        grid.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
            foreach (var p in paths)
                if (Directory.Exists(p)) foreach (var f in Directory.GetFiles(p, "*.iso")) Enqueue(f, false);
                else if (p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) Enqueue(p, false);
        };
    }

    void BuildGrid()
    {
        var cell = grid.DefaultCellStyle;
        cell.BackColor = Theme.Surface; cell.ForeColor = Theme.Text;
        cell.SelectionBackColor = Theme.SurfaceHover; cell.SelectionForeColor = Theme.Text;
        cell.Padding = new Padding(4, 0, 4, 0);
        var head = grid.ColumnHeadersDefaultCellStyle;
        head.BackColor = Theme.Surface; head.ForeColor = Theme.TextDim; head.SelectionBackColor = Theme.Surface;
        head.Font = new Font("Segoe UI", 8.5f); head.Padding = new Padding(4, 0, 4, 0);
        grid.ColumnHeadersHeight = 24;
        grid.RowTemplate.Height = 26;

        DataGridViewTextBoxColumn Col(string header, float weight, bool right = false)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, MinimumWidth = 40, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
            var align = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft;
            c.DefaultCellStyle.Alignment = align;
            c.HeaderCell.Style.Alignment = align;
            return c;
        }
        colName = Col("ISO dosyası", 30);
        colSize = Col("Boyut", 11, true);
        colInfo = Col("Başlık · süre", 19);
        colKbps = Col("Bit hızı (kbps)", 15, true);
        colKbps.ReadOnly = false;
        colKbps.DefaultCellStyle.ForeColor = Theme.AccentHover;
        colKbps.DefaultCellStyle.BackColor = Color.FromArgb(42, 44, 52);
        colKbps.ToolTipText = "Tıklayıp bu ISO için bit hızını yazın";
        colEstimate = Col("Tahmini MP4", 13, true);
        colStatus = Col("Durum", 13, true);
        grid.Columns.AddRange(colName, colSize, colInfo, colKbps, colEstimate, colStatus);

        // Bit hızı hücresine tıklayınca düzenleme başlar (odak geri gelince kendiliğinden başlamasın)
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == colKbps.Index) grid.BeginEdit(true);
        };
        grid.CellBeginEdit += (_, e) =>
        {
            // Biten, hata alan, taranmamış ya da şu an dönüştürülen ISO'nun bit hızı değiştirilemez
            if (grid.Rows[e.RowIndex].Tag is not Mp4Item item || !item.Pending || IsCurrent(item) || !item.Scanned) { e.Cancel = true; return; }
            grid.Rows[e.RowIndex].Cells[colKbps.Index].Value = item.Kbps.ToString(); // "(oto)" ekini düzenlerken gösterme
        };
        grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox tb)
            {
                tb.BackColor = Theme.SurfaceHover;
                tb.ForeColor = Theme.Text;
                tb.KeyPress -= DigitsOnly;
                tb.KeyPress += DigitsOnly;
            }
        };
        grid.CellEndEdit += (_, e) =>
        {
            if (grid.Rows[e.RowIndex].Tag is not Mp4Item item) return;
            if (int.TryParse(grid.Rows[e.RowIndex].Cells[colKbps.Index].Value?.ToString(), out int k) && k > 0)
            {
                int v = Math.Clamp(k, 300, 50000);
                if (v != item.Kbps)
                {
                    item.Kbps = v;
                    item.AutoKbps = false;
                    Log($"{System.IO.Path.GetFileName(item.Path)}: bit hızı {v} kbps (MP4 ≈ {MainForm.FormatSize(HandBrake.EstimateBytes(item.Duration, v))}).");
                }
            }
            UpdateRow(item);
            RefreshEstimate();
        };
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != colStatus.Index || grid.Rows[e.RowIndex].Tag is not Mp4Item item || e.CellStyle == null) return;
            e.CellStyle.ForeColor = item.Failed ? Theme.Error : item.Done ? Theme.Success : IsCurrent(item) ? Theme.Accent : Theme.TextDim;
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
        };
    }

    static void DigitsOnly(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
    }

    void UpdateRow(Mp4Item item)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!ReferenceEquals(row.Tag, item)) continue;
            bool hasVideo = item.Scanned && item.Titles.Count > 0;
            row.Cells[colName.Index].Value = System.IO.Path.GetFileName(item.Path);
            row.Cells[colName.Index].ToolTipText = item.Path;
            row.Cells[colSize.Index].Value = item.IsoBytes > 0 ? $"{item.IsoBytes / (1024.0 * 1024):N0} MB" : "—";
            row.Cells[colInfo.Index].Value = !item.Scanned ? "" : hasVideo ? $"{item.Titles.Count} başlık · {item.Duration:h\\:mm\\:ss}" : "video bulunamadı";
            if (!row.Cells[colKbps.Index].IsInEditMode)
                row.Cells[colKbps.Index].Value = hasVideo ? item.Kbps + (item.AutoKbps ? " (oto)" : "") : "";
            row.Cells[colEstimate.Index].Value = hasVideo ? "≈ " + MainForm.FormatSize(HandBrake.EstimateBytes(item.Duration, item.Kbps)) : "";
            row.Cells[colStatus.Index].Value = item.Status;
            return;
        }
    }

    /// <summary>MP4 yaklaşık ISO boyutunda çıkacak şekilde bit hızı (100'ün katı); süre yoksa Ayarlar'daki varsayılan.</summary>
    void SetAutoKbps(Mp4Item item)
    {
        item.Kbps = HandBrake.AutoVideoKbps(item.IsoBytes, item.Duration) ?? settings.Mp4VideoKbps;
        item.AutoKbps = true;
    }

    // ───────────────────────── Ayarlar ─────────────────────────

    /// <summary>Hazır bit hızı düğmeleri (Ayarlar'dan değiştirilebilir).</summary>
    void BuildPresetChips()
    {
        presetRow.SuspendLayout();
        foreach (Control c in presetRow.Controls.Cast<Control>().ToList()) c.Dispose();
        presetRow.Controls.Clear();
        FlatButton Chip(string text, Action<Mp4Item> apply)
        {
            var chip = new FlatButton { Text = text, Style = FlatButton.Kind.Chip, Padding = new Padding(9, 3, 9, 3), Margin = new Padding(0, 0, 4, 0), Anchor = AnchorStyles.Left };
            chip.Click += (_, _) => ApplyToSelected(apply);
            return chip;
        }
        var auto = Chip("Otomatik", SetAutoKbps);
        tips.SetToolTip(auto, "MP4 yaklaşık ISO boyutunda çıkacak şekilde (100'ün katı)");
        presetRow.Controls.Add(auto);
        foreach (int k in settings.Mp4Presets.Where(k => k > 0).Take(4))
            presetRow.Controls.Add(Chip(k.ToString(), i => { i.Kbps = k; i.AutoKbps = false; }));
        presetRow.ResumeLayout();
    }

    /// <summary>Seçili satırlara (seçim yoksa bekleyen tüm satırlara) bit hızı uygular.</summary>
    void ApplyToSelected(Action<Mp4Item> apply)
    {
        grid.EndEdit();
        var selected = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Mp4Item>().ToList();
        var targets = (selected.Count > 0 ? selected : items).Where(i => i.Pending && i.Scanned && i.Titles.Count > 0 && !IsCurrent(i)).ToList();
        foreach (var i in targets) apply(i);
        RefreshEstimate();
    }

    /// <summary>Ayarlar'da hazır bit hızları değişince çağrılır.</summary>
    public void ApplyBitrateSettings() => BuildPresetChips();

    void LoadSettings()
    {
        loading = true;
        rbDeintOn.Checked = settings.Mp4Deinterlace;
        rbDeintOff.Checked = !settings.Mp4Deinterlace;
        chkNextToIso.Checked = settings.Mp4NextToIso;
        txtFolder.Text = settings.Mp4Folder.Length > 0 ? settings.Mp4Folder : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        SetEncoders(["x264"]);
        UpdateDeintHeader();
        UpdateFolderRow();
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
        tips.SetToolTip(cmbEncoder, (Encoders.TryGetValue(id, out var e) ? e.Hint : "") +
                                    (HandBrake.IsInstalled ? "" : " Ekran kartı seçenekleri HandBrake indirildikten sonra görünür."));
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
            lblEstimate.Text = "Toplam MP4 ≈ —";
        }
        else
        {
            var total = scanned.Aggregate(TimeSpan.Zero, (a, i) => a + i.Duration);
            long bytes = scanned.Sum(i => HandBrake.EstimateBytes(i.Duration, i.Kbps));
            long iso = scanned.Sum(i => i.IsoBytes);
            lblEstimate.Text = $"Toplam MP4 ≈ {MainForm.FormatSize(bytes)}  (ISO'lar {MainForm.FormatSize(iso)})";
        }
        foreach (var i in items) UpdateRow(i);
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
        foreach (var item in grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Mp4Item>().Where(i => !IsCurrent(i)).ToList())
            items.Remove(item);
        RefreshList();
    }

    void RefreshList()
    {
        grid.EndEdit();
        grid.Rows.Clear();
        foreach (var i in items) grid.Rows[grid.Rows.Add()].Tag = i;
        grid.ClearSelection();
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
        UpdateRow(item);
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
                if (item.AutoKbps) SetAutoKbps(item);
                Log($"{System.IO.Path.GetFileName(item.Path)}: {reason}" +
                    (item.VcdStreams == null ? " " + string.Join("; ", titles.Select(t => t.Describe())) : ""));
                Log($"{System.IO.Path.GetFileName(item.Path)}: ISO {MainForm.FormatSize(item.IsoBytes)} → otomatik bit hızı {item.Kbps} kbps " +
                    $"(MP4 ≈ {MainForm.FormatSize(HandBrake.EstimateBytes(item.Duration, item.Kbps))}). Değiştirmek için tablodaki bit hızına tıklayın.");
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
        grid.EndEdit();
        var settingsNow = new EncodeSettings(0, encoderIds[Math.Max(0, cmbEncoder.SelectedIndex)], rbDeintOn.Checked);
        if (settingsNow.Encoder != "x264")
            Log("Not: Ekran kartı kodlamayı yapar; taramalı görüntü düzeltme (Decomb) ve ses işlemcide yapıldığı için işlemci de kullanılır. " +
                "Görev Yöneticisi'nde ekran kartı yükü \"3D\" değil video grafiklerinde (Video Encode / Video Decode / Video Codec) görünür.");
        Log($"Dönüştürme başladı: {Encoders.GetValueOrDefault(settingsNow.Encoder).Name ?? settingsNow.Encoder}, " +
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
                    await ConvertItemAsync(item, settingsNow with { VideoKbps = item.Kbps }, cts.Token);
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
            Log($"{name}: {what} → {output} ({s.VideoKbps} kbps)");
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
                    UpdateRow(item);
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
                UpdateRow(item);
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
            Log($"{name}: {what} tamam — {MainForm.FormatSize(new FileInfo(output).Length)}, süre {tsw.Elapsed:hh\\:mm\\:ss}.");
        }
        item.Status = "Bitti";
    }

    // Açılır başlık: seçili değeri ve açık/kapalı okunu gösterir
    void UpdateDeintHeader() =>
        btnDeint.Text = $"Taramalı görüntü düzeltme: {(rbDeintOn.Checked ? "Açık" : "Kapalı")}  {(deintBody.Visible ? "▴" : "▾")}";

    void UpdateFolderRow() => folderRow.Visible = !chkNextToIso.Checked && !IsBusy;

    void SetBusy(bool busy)
    {
        foreach (var c in setupControls) c.Visible = !busy;
        UpdateFolderRow();
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
