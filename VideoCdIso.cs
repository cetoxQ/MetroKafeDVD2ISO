using System.Text;

namespace MetroKafeDVD2ISO;

/// <summary>ISO içindeki bir Video CD / Super Video CD video dosyası (ör. MPEGAV\AVSEQ01.DAT).</summary>
/// <param name="Lba">Dosyanın ISO'daki ilk sektörü.</param>
/// <param name="DataOffset">RIFF/CDXA başlığından sonra sektör verisinin başladığı bayt (düz MPEG'de 0).</param>
/// <param name="DataLength">Sektör verisinin bayt uzunluğu.</param>
/// <param name="Cdxa">true: veri 2352 baytlık ham XA sektörleri (Windows'un Form 2 dosyaları için sunduğu biçim).</param>
public sealed record VcdStream(string Name, long Lba, long DataOffset, long DataLength, bool Cdxa, TimeSpan Duration);

/// <summary>
/// Video CD ISO'larından film çıkarma. HandBrake ISO içine yalnız DVD (VIDEO_TS) olarak bakar; Video CD'deki
/// MPEGAV\*.DAT (SVCD'de MPEG2\*.MPG) dosyalarını tanımaz. Bu sınıf ISO9660 dizinini okuyup bu dosyaları bulur ve
/// RIFF/CDXA kabındaki ham sektörlerden düz MPEG akışı çıkarır; HandBrake bu akışı okuyabilir.
/// </summary>
public static class VideoCdIso
{
    const int Sector = 2048, RawSector = 2352, XaHeader = 24, Form2Data = 2324, Form1Data = 2048;

    /// <summary>ISO'da Video CD/SVCD videosu varsa türünü ve dosyalarını döndürür; yoksa null.</summary>
    public static (string Kind, List<VcdStream> Streams)? Find(string isoPath)
    {
        using var fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var pvd = ReadAt(fs, 16L * Sector, Sector);
        if (pvd.Length < Sector || pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001") return null;
        long rootLba = BitConverter.ToUInt32(pvd, 156 + 2), rootSize = BitConverter.ToUInt32(pvd, 156 + 10);
        var root = ReadDir(fs, rootLba, rootSize);

        foreach (var (dirName, kind, ext) in new[] { ("MPEGAV", "Video CD", ".DAT"), ("MPEG2", "Super Video CD", ".MPG") })
        {
            var dir = root.FirstOrDefault(e => e.IsDir && e.Name.Equals(dirName, StringComparison.OrdinalIgnoreCase));
            if (dir == default) continue;
            var streams = new List<VcdStream>();
            foreach (var f in ReadDir(fs, dir.Lba, dir.Size).Where(e => !e.IsDir && e.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Name))
            {
                var s = Describe(fs, $"{dirName}\\{f.Name}", f.Lba, f.Size, kind == "Video CD" ? 75 : 150);
                if (s.Duration >= TimeSpan.FromSeconds(5)) streams.Add(s);
            }
            if (streams.Count > 0) return (kind, streams);
        }
        return null;
    }

    /// <summary>Süre, sektör sayısından tahmin edilir (VCD 1x: saniyede 75 sektör; SVCD 2x: 150).</summary>
    static VcdStream Describe(FileStream fs, string name, long lba, long size, int sectorsPerSecond)
    {
        var head = ReadAt(fs, lba * Sector, (int)Math.Min(4096, size));
        if (head.Length >= 12 && Encoding.ASCII.GetString(head, 0, 4) == "RIFF" && Encoding.ASCII.GetString(head, 8, 4) == "CDXA")
        {
            // Parçaları dolaş: "fmt " ... "data"
            for (int off = 12; off + 8 <= head.Length;)
            {
                string id = Encoding.ASCII.GetString(head, off, 4);
                long len = BitConverter.ToUInt32(head, off + 4);
                if (id == "data")
                {
                    long dataLen = Math.Min(len, size - (off + 8));
                    long sectors = dataLen / RawSector;
                    return new VcdStream(name, lba, off + 8, sectors * RawSector, true, TimeSpan.FromSeconds((double)sectors / sectorsPerSecond));
                }
                off += 8 + (int)len + (int)(len & 1);
            }
        }
        // Düz MPEG (ör. başka bir araçla hazırlanmış kalıp)
        return new VcdStream(name, lba, 0, size, false, TimeSpan.FromSeconds((double)size / (sectorsPerSecond * Form2Data)));
    }

    /// <summary>Videoyu düz MPEG dosyası olarak çıkarır.</summary>
    public static void Extract(string isoPath, VcdStream s, string outPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var input = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var output = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        input.Position = s.Lba * Sector + s.DataOffset;
        long remaining = s.DataLength, done = 0;
        if (!s.Cdxa)
        {
            var buf = new byte[1 << 20];
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                int n = input.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
                if (n <= 0) break;
                output.Write(buf, 0, n);
                remaining -= n;
                done += n;
                progress?.Report((double)done / s.DataLength);
            }
            return;
        }

        var sector = new byte[RawSector];
        long total = s.DataLength / RawSector;
        for (long i = 0; i < total; i++)
        {
            if ((i & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report((double)i / total);
            }
            input.ReadExactly(sector);
            // XA alt başlığı (bayt 16–23): bayt 18 alt mod. 0x20 = Form 2 (2324 bayt), aksi Form 1 (2048 bayt).
            // Görüntü/ses/veri bitleri (0x02/0x04/0x08) olmayan boş sektörler atlanır.
            byte submode = sector[18];
            if ((submode & 0x0E) == 0) continue;
            output.Write(sector, XaHeader, (submode & 0x20) != 0 ? Form2Data : Form1Data);
        }
        progress?.Report(1);
    }

    static byte[] ReadAt(FileStream fs, long offset, int count)
    {
        if (offset >= fs.Length) return [];
        fs.Position = offset;
        var buf = new byte[Math.Min(count, fs.Length - offset)];
        fs.ReadExactly(buf);
        return buf;
    }

    static List<(string Name, bool IsDir, long Lba, long Size)> ReadDir(FileStream fs, long lba, long size)
    {
        var list = new List<(string, bool, long, long)>();
        var data = ReadAt(fs, lba * Sector, (int)Math.Min(size, 1 << 20));
        for (int pos = 0; pos < data.Length;)
        {
            int len = data[pos];
            if (len == 0)
            {
                pos = (pos / Sector + 1) * Sector; // kayıt sektör sınırını aşmaz; sonraki sektöre geç
                continue;
            }
            if (pos + 33 > data.Length || pos + len > data.Length) break;
            long extent = BitConverter.ToUInt32(data, pos + 2), dataLen = BitConverter.ToUInt32(data, pos + 10);
            bool isDir = (data[pos + 25] & 0x02) != 0;
            int nameLen = data[pos + 32];
            if (!(nameLen == 1 && data[pos + 33] <= 1)) // "." ve ".." kayıtları
            {
                string name = Encoding.ASCII.GetString(data, pos + 33, nameLen);
                int semi = name.IndexOf(';');
                if (semi >= 0) name = name[..semi];
                list.Add((name.TrimEnd('.'), isDir, extent, dataLen));
            }
            pos += len;
        }
        return list;
    }
}
