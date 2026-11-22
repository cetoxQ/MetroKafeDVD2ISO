using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MetroKafeDVD2ISO;

/// <summary>Uygulama logosu (disk). Hem pencerede hem app.ico üretiminde kullanılır.</summary>
public static class Logo
{
    public static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        float s = size;
        RectangleF Circle(float diameter) => new((s - diameter) / 2, (s - diameter) / 2, diameter, diameter);

        var disc = Circle(s * 0.94f);
        using (var br = new LinearGradientBrush(disc, Color.FromArgb(253, 200, 70), Color.FromArgb(234, 88, 12), 45f))
            g.FillEllipse(br, disc);

        // Yansıma parıltısı
        using (var sheen = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
        {
            g.FillPie(sheen, disc.X, disc.Y, disc.Width, disc.Height, 200, 45);
            g.FillPie(sheen, disc.X, disc.Y, disc.Width, disc.Height, 20, 45);
        }

        // İz halkası
        if (size >= 32)
        {
            using var ring = new Pen(Color.FromArgb(60, 120, 40, 0), Math.Max(1f, s / 64f));
            var r = Circle(s * 0.72f);
            g.DrawEllipse(ring, r);
        }

        using (var hub = new SolidBrush(Color.FromArgb(30, 31, 36)))
            g.FillEllipse(hub, Circle(s * 0.40f));
        using (var hub2 = new SolidBrush(Color.FromArgb(253, 230, 138)))
            g.FillEllipse(hub2, Circle(s * 0.28f));

        // Ortadaki delik (şeffaf)
        g.CompositingMode = CompositingMode.SourceCopy;
        using (var hole = new SolidBrush(Color.Transparent))
            g.FillEllipse(hole, Circle(s * 0.12f));

        return bmp;
    }

    /// <summary>PNG gömülü çok boyutlu .ico dosyası yazar.</summary>
    public static void SaveIco(string path, params int[] sizes)
    {
        var images = sizes.Select(sz =>
        {
            using var bmp = Render(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return (sz, data: ms.ToArray());
        }).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (sz, data) in images)
        {
            w.Write((byte)(sz >= 256 ? 0 : sz)); w.Write((byte)(sz >= 256 ? 0 : sz));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
    }
}
