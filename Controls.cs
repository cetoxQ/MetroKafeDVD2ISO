using System.Drawing.Drawing2D;

namespace MetroKafeDVD2ISO;

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(24, 25, 30);
    public static readonly Color TitleBar = Color.FromArgb(17, 18, 22);
    public static readonly Color Surface = Color.FromArgb(34, 36, 42);
    public static readonly Color SurfaceHover = Color.FromArgb(46, 49, 57);
    public static readonly Color Border = Color.FromArgb(56, 59, 68);
    public static readonly Color Text = Color.FromArgb(236, 236, 240);
    public static readonly Color TextDim = Color.FromArgb(150, 156, 166);
    public static readonly Color Accent = Color.FromArgb(245, 158, 11);
    public static readonly Color AccentHover = Color.FromArgb(251, 191, 36);
    public static readonly Color AccentText = Color.FromArgb(24, 25, 30);
    public static readonly Color Close = Color.FromArgb(232, 17, 35);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color Error = Color.FromArgb(248, 113, 113);
    public static readonly Color WarnBg = Color.FromArgb(56, 42, 12);
    public static readonly Color WarnText = Color.FromArgb(252, 211, 77);
    public static readonly Color WarnTextDim = Color.FromArgb(230, 200, 140);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color ParentBack(Control c) => c.Parent?.BackColor ?? Bg;
}

/// <summary>Yuvarlak köşeli düz düğme.</summary>
sealed class FlatButton : Control
{
    public enum Kind { Primary, Secondary, Chip, Tab }

    Kind kind = Kind.Secondary;
    bool hover, down, selected;

    public Kind Style { get => kind; set { kind = value; Invalidate(); } }
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
        AutoSize = true;
        Padding = new Padding(16, 7, 16, 7);
        Margin = new Padding(0);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(t.Width + Padding.Horizontal, t.Height + Padding.Vertical);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Parent?.PerformLayout(); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

        Color back, fore, border = Color.Empty;
        switch (kind)
        {
            case Kind.Primary:
                back = !Enabled ? Color.FromArgb(70, 60, 40) : down ? Theme.Accent : hover ? Theme.AccentHover : Theme.Accent;
                fore = Enabled ? Theme.AccentText : Theme.TextDim;
                break;
            case Kind.Chip:
                back = selected ? Color.FromArgb(70, 52, 14) : hover ? Theme.SurfaceHover : Theme.Surface;
                border = selected ? Theme.Accent : Theme.Border;
                fore = selected ? Theme.AccentHover : Enabled ? Theme.Text : Theme.TextDim;
                break;
            case Kind.Tab:
                back = hover ? Color.FromArgb(30, 32, 38) : Theme.ParentBack(this);
                fore = selected || hover ? Theme.Text : Theme.TextDim;
                break;
            default:
                back = !Enabled ? Theme.Surface : down ? Theme.Border : hover ? Theme.SurfaceHover : Theme.Surface;
                border = Theme.Border;
                fore = Enabled ? Theme.Text : Color.FromArgb(95, 100, 110);
                break;
        }

        if (kind == Kind.Tab)
        {
            using (var b = new SolidBrush(back)) g.FillRectangle(b, ClientRectangle);
            if (selected)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 10, Height - 3, Width - 20, 3);
        }
        else
        {
            using var path = Theme.Round(r, kind == Kind.Chip ? Height / 2f : 6);
            using (var b = new SolidBrush(back)) g.FillPath(b, path);
            if (border != Color.Empty)
                using (var p = new Pen(border)) g.DrawPath(p, path);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        var font = kind == Kind.Primary ? new Font(Font, FontStyle.Bold) : Font;
        TextRenderer.DrawText(g, Text, font, ClientRectangle, fore, flags);
        if (!ReferenceEquals(font, Font)) font.Dispose();
    }
}

/// <summary>Başlık çubuğundaki küçült / kapat düğmeleri.</summary>
sealed class TitleButton : Control
{
    public enum Glyph { Minimize, Close }
    readonly Glyph glyph;
    bool hover;

    public TitleButton(Glyph glyph)
    {
        this.glyph = glyph;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Margin = new Padding(0);
        Dock = DockStyle.Fill;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(hover ? (glyph == Glyph.Close ? Theme.Close : Color.FromArgb(40, 42, 50)) : Theme.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f * 5f;
        float cx = Width / 2f, cy = Height / 2f;
        using var pen = new Pen(hover ? Color.White : Theme.Text, Math.Max(1f, DeviceDpi / 96f));
        if (glyph == Glyph.Close)
        {
            g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
            g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
        }
        else
        {
            g.SmoothingMode = SmoothingMode.None;
            g.DrawLine(pen, cx - s, cy, cx + s, cy);
        }
    }
}

/// <summary>İnce, yuvarlak ilerleme çubuğu.</summary>
sealed class ModernProgress : Control
{
    int value;
    Color bar = Theme.Accent;

    public int Maximum { get; set; } = 1000;
    public int Value { get => value; set { this.value = Math.Clamp(value, 0, Maximum); Invalidate(); } }
    public Color BarColor { get => bar; set { bar = value; Invalidate(); } }

    public ModernProgress()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 10;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        using (var path = Theme.Round(track, Height / 2f))
        using (var b = new SolidBrush(Theme.Surface))
            g.FillPath(b, path);
        float w = (Width - 1) * (float)value / Maximum;
        if (w <= 0) return;
        w = Math.Max(w, Height);
        using (var path = Theme.Round(new RectangleF(0, 0, w, Height - 1), Height / 2f))
        using (var b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, w), Height), Color.FromArgb(253, 200, 70), bar, 0f))
            g.FillPath(b, path);
    }
}

/// <summary>Metin kutusu gibi kontrolleri saran yuvarlak kenarlıklı kutu.</summary>
sealed class FieldBox : Panel
{
    readonly Control inner;
    bool focused;

    public FieldBox(Control inner)
    {
        this.inner = inner;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(10, 7, 10, 7);
        Margin = new Padding(0, 0, 8, 0);
        AutoSize = true;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        inner.Dock = DockStyle.Fill;
        inner.BackColor = Theme.Surface;
        inner.ForeColor = Theme.Text;
        inner.Enter += (_, _) => { focused = true; Invalidate(); };
        inner.Leave += (_, _) => { focused = false; Invalidate(); };
        Controls.Add(inner);
    }

    public int PreferredWidth { get; set; } = 200;

    public override Size GetPreferredSize(Size proposedSize) =>
        new(PreferredWidth, TextRenderer.MeasureText("Ag", Font).Height + Padding.Vertical);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6);
        using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
        using (var p = new Pen(focused ? Theme.Accent : Theme.Border)) g.DrawPath(p, path);
    }

    public static TextBox NewTextBox() => new() { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text };
}

/// <summary>Koyu temalı açılır liste (sürücü seçimi).</summary>
sealed class DropDown : Control
{
    readonly List<object> items = [];
    int selectedIndex = -1;
    bool hover;

    public event EventHandler? SelectedIndexChanged;
    public string Placeholder { get; set; } = "";
    public int PreferredWidth { get; set; } = 200;
    public IReadOnlyList<object> Items => items;
    public object? SelectedItem => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value == selectedIndex) return;
            selectedIndex = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DropDown()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        AutoSize = true;
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(0, 0, 8, 0);
    }

    public void ReplaceItem(int index, object item)
    {
        items[index] = item;
        Invalidate();
    }

    public void SetItems(IEnumerable<object> newItems)
    {
        items.Clear();
        items.AddRange(newItems);
        selectedIndex = -1;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(PreferredWidth, TextRenderer.MeasureText("Ag", Font).Height + 14);

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (items.Count == 0) return;
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Font,
        };
        for (int i = 0; i < items.Count; i++)
        {
            int idx = i;
            var mi = new ToolStripMenuItem(items[i].ToString()) { ForeColor = Theme.Text, AutoSize = false, Width = Width, Height = Height };
            mi.Click += (_, _) => SelectedIndex = idx;
            menu.Items.Add(mi);
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6);
        using (var b = new SolidBrush(hover && Enabled ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(b, path);
        using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);

        string text = SelectedItem?.ToString() ?? Placeholder;
        var color = !Enabled || SelectedItem == null ? Theme.TextDim : Theme.Text;
        var tr = new Rectangle(10, 0, Width - 40, Height);
        TextRenderer.DrawText(g, text, Font, tr, color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        float s = DeviceDpi / 96f * 4f, cx = Width - 18, cy = Height / 2f;
        using var pen = new Pen(Theme.TextDim, Math.Max(1.5f, DeviceDpi / 96f * 1.5f));
        g.DrawLines(pen, [new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2)]);
    }
}

sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Theme.Text;
        base.OnRenderItemText(e);
    }

    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.SurfaceHover;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
    }
}

/// <summary>Başlık + değerden oluşan küçük istatistik kartı.</summary>
sealed class StatCard : Panel
{
    readonly Label caption = new() { AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.TextDim };
    readonly Label value = new() { AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Text, Padding = new Padding(0, 3, 0, 0), Text = "—" };

    public StatCard(string captionText)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(12, 9, 12, 9);
        Margin = new Padding(0, 0, 8, 0);
        Dock = DockStyle.Fill;
        caption.Text = captionText;
        Controls.Add(value);
        Controls.Add(caption);
    }

    public string CaptionText { get => caption.Text; set => caption.Text = value; }
    public string ValueText { get => value.Text; set => this.value.Text = value; }
    public Color ValueColor { get => value.ForeColor; set => this.value.ForeColor = value; }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        caption.Font = new Font(Font.FontFamily, Font.Size * 0.88f);
        value.Font = new Font("Segoe UI Semibold", Font.Size * 1.3f);
        Height = GetPreferredSize(Size.Empty).Height;
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(120, Padding.Vertical + TextRenderer.MeasureText("Ag", caption.Font).Height +
                 TextRenderer.MeasureText("Ag", value.Font).Height + value.Padding.Vertical);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.ParentBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 8);
        using var b = new SolidBrush(Theme.Surface);
        g.FillPath(b, path);
    }
}
