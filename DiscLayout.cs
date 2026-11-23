namespace MetroKafeDVD2ISO;

/// <summary>
/// Diskin dosya sisteminden (ISO9660 / UDF) gerçek veri boyutunu okur.
/// Yazılabilir CD/DVD'lerde sürücünün bildirdiği boyut, verinin bittiği yerden sonraki
/// okunamayan kuyruk sektörlerini de içerebilir; kopyalama dosya sistemi boyutuyla sınırlanır.
/// </summary>
public static class DiscLayout
{
    const int Sector = DiscCopier.SectorSize;

    /// <param name="readSector">Verilen sektörü 2048 baytlık tampona okur; başarısızsa false.</param>
    /// <param name="baseSector">Oturumun başladığı sektör (çok oturumlu disklerde sonraki oturumlar için).</param>
    /// <returns>Diskin başından itibaren bayt cinsinden bitiş; dosya sistemi tanınmazsa 0.</returns>
    public static long ReadFileSystemSize(Func<long, byte[], bool> readSector, long baseSector = 0)
    {
        var buf = new byte[Sector];
        long iso = 0, udf = 0;

        // ISO9660: oturum başı + 16'dan itibaren birim tanımlayıcıları; birincil tanımlayıcı (tip 1) hacim boyutunu verir.
        // Çok oturumlu disklerde bu boyut diskin başından itibaren (mutlak) sayılır.
        for (long s = baseSector + 16; s < baseSector + 32 && readSector(s, buf); s++)
        {
            if (buf[1] != 'C' || buf[2] != 'D' || buf[3] != '0' || buf[4] != '0' || buf[5] != '1') break;
            if (buf[0] == 1)
            {
                long blocks = BitConverter.ToUInt32(buf, 80);
                int blockSize = BitConverter.ToUInt16(buf, 128);
                if (blockSize is 512 or 1024 or 2048) iso = blocks * blockSize;
                break;
            }
            if (buf[0] == 255) break;
        }

        // UDF: sektör 256'daki çapa (AVDP) → birim tanımlayıcı dizisi → bölüm tanımlayıcısı (etiket 5)
        if (IsAnchor(baseSector + 256))
        {
            uint vdsLength = BitConverter.ToUInt32(buf, 16), vdsLocation = BitConverter.ToUInt32(buf, 20);
            uint reserveLength = BitConverter.ToUInt32(buf, 24), reserveLocation = BitConverter.ToUInt32(buf, 28);
            long reserveEnd = reserveLocation + reserveLength / Sector; // yedek tanımlayıcı dizisi
            long partitionEnd = 0;

            long count = Math.Min(64, vdsLength / Sector);
            for (long i = 0; i < count && readSector(vdsLocation + i, buf); i++)
            {
                ushort tag = BitConverter.ToUInt16(buf, 0);
                if (tag == 5) partitionEnd = Math.Max(partitionEnd, (long)BitConverter.ToUInt32(buf, 188) + BitConverter.ToUInt32(buf, 192));
                if (tag == 8) break; // sonlandırıcı
            }

            // UDF, son sektör (N) ve N-256'ya da çapa koyar; bunlar ISO9660 boyutunun dışında kalabilir.
            // Veri/bölüm sonunun hemen ardında N-256 çapasını ara (yedek tanımlayıcı alanıyla çakışabilir);
            // 256 sektör ilerideki son çapa diskin sonudur. Bazı yazıcılar art arda birkaç çapa koyar.
            long scanFrom = Math.Max(iso / Sector, partitionEnd);
            long end = Math.Max(scanFrom, reserveEnd);
            for (long s = scanFrom; s < scanFrom + 64; s++)
            {
                if (!readSector(s, buf)) break;
                if (!IsAnchorAt(s)) continue;
                end = Math.Max(end, s + 1);
                // Bazı yazıcılar (ör. Windows IMAPI) sonu kesintisiz bir çapa dizisiyle doldurur: dizinin sonuna kadar izle
                for (long t = s + 1; t < s + MaxTail / Sector && readSector(t, buf) && IsAnchorAt(t); t++)
                    end = t + 1;
                // Standart yerleşim: N-256'daki çapanın 256 sektör ilerisinde son çapa (N)
                for (long t = Math.Max(end, s + 256); t < s + 256 + 16; t++)
                {
                    if (!readSector(t, buf)) break;
                    if (IsAnchorAt(t)) end = Math.Max(end, t + 1);
                }
                break;
            }
            udf = end * Sector;
        }

        return Math.Max(iso, udf);

        bool IsAnchor(long sector) => readSector(sector, buf) && IsAnchorAt(sector);
        // Etiket 2 = AVDP; etiketteki konum alanı sektörün kendisini göstermeli
        bool IsAnchorAt(long sector) => BitConverter.ToUInt16(buf, 0) == 2 && BitConverter.ToUInt32(buf, 12) == sector;
    }

    /// <summary>Bu kadardan büyük farkta kuyruk değil gerçek veri olabilir (ör. çok oturumlu disk); kırpılmaz.</summary>
    public const long MaxTail = 4096L * Sector;

    /// <summary>
    /// Kopyalanacak ana boyutu seçer. Sürücünün bildirdiği fazlalık küçükse dosya sistemi boyutu kullanılır;
    /// fazlalık kopyalamanın sonunda ayrıca denenir (bkz. <see cref="DiscCopier"/>).
    /// </summary>
    public static long ChooseSize(long deviceLength, long fileSystemSize)
    {
        if (fileSystemSize <= 0) return deviceLength;
        if (deviceLength <= 0) return fileSystemSize;
        // Dosya sistemi sürücünün bildirdiğinden büyükse disk eksik yazılmış olabilir; okunabilecek kadarını al
        if (fileSystemSize >= deviceLength) return deviceLength;
        return deviceLength - fileSystemSize > MaxTail ? deviceLength : fileSystemSize;
    }

    /// <summary>
    /// Diskin içindekiler tablosundaki (TOC) bir iz. Start sektör cinsindendir; Number 0xAA kapanış (lead-out) kaydıdır.
    /// </summary>
    public sealed record TocTrack(int Number, bool IsData, long Start);

    /// <param name="Size">Kopyalanacak ana boyut (bayt).</param>
    /// <param name="TailLength">Size'dan sonra en sonda denenecek kuyruk (bayt).</param>
    /// <param name="Gaps">Okunmadan sıfırla doldurulacak aralıklar (bayt; oturum araları, ses izleri).</param>
    /// <param name="LastSessionStart">Son veri oturumunun başı (sektör); tek oturumda 0.</param>
    public sealed record DiscPlan(long Size, long TailLength, IReadOnlyList<(long Start, long End)> Gaps,
        long LastSessionStart, string Description);

    public const int LeadOut = 0xAA;

    /// <summary>
    /// Okuma planı. TOC varsa yalnız veri izleri okunur: ses izleri ve çok oturumlu disklerde oturumlar
    /// arasındaki hiç okunamayan boşluklar (lead-out/lead-in) okunmadan sıfırla doldurulur.
    /// Her veri izinin sonu kendi dosya sisteminden bulunur.
    /// </summary>
    public static DiscPlan Plan(IReadOnlyList<TocTrack>? toc, long deviceLength, Func<long, byte[], bool> readSector)
    {
        var tracks = toc?.Where(t => t.Number != LeadOut).OrderBy(t => t.Start).ToList() ?? [];
        if (!tracks.Any(t => t.IsData))
        {
            long fs = ReadFileSystemSize(readSector);
            long size = ChooseSize(deviceLength, fs);
            string why = toc == null ? L.T("iz bilgisi alınamadı") : L.T("veri izi bulunamadı");
            return new DiscPlan(size, Math.Max(0, deviceLength - size), [], 0,
                L.F("Disk düzeni: {0}; boyut {1}.", why, Describe(fs, deviceLength)));
        }

        long deviceSectors = deviceLength > 0 ? deviceLength / Sector : long.MaxValue;
        long leadOut = Math.Min(toc!.FirstOrDefault(t => t.Number == LeadOut)?.Start ?? deviceSectors, deviceSectors);

        var ranges = new List<(long Start, long End, long TrackEnd)>();
        long lastSession = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (!tracks[i].IsData) continue;
            long start = tracks[i].Start;
            long trackEnd = Math.Min(i + 1 < tracks.Count ? tracks[i + 1].Start : leadOut, leadOut);
            if (trackEnd <= start) continue;
            long fsEnd = ReadFileSystemSize(readSector, start) / Sector;
            bool ownFileSystem = fsEnd > start && fsEnd <= trackEnd;
            // Kendi dosya sistemi olan sonraki iz yeni bir oturumdur; yoksa aynı oturumun bir izidir (ör. Video CD görüntü izi)
            if (ownFileSystem && start > 0) lastSession = start;
            ranges.Add((start, ownFileSystem ? fsEnd : trackEnd, trackEnd));
        }
        if (ranges.Count == 0)
            return new DiscPlan(deviceLength, 0, [], 0, L.T("Disk düzeni: veri izleri okunamadı; sürücünün bildirdiği boyut kullanılıyor."));

        // Son veri izinde dosya sistemi izin sonundan çok gerideyse kesmek yerine izin tamamını oku (ChooseSize ile aynı ilke)
        var last = ranges[^1];
        long tail = last.TrackEnd - last.End;
        if (tail * Sector > MaxTail)
        {
            ranges[^1] = (last.Start, last.TrackEnd, last.TrackEnd);
            tail = 0;
        }

        var gaps = new List<(long, long)>();
        long cursor = 0;
        foreach (var r in ranges)
        {
            if (r.Start > cursor) gaps.Add((cursor * Sector, r.Start * Sector));
            cursor = Math.Max(cursor, r.End);
        }

        int audio = tracks.Count(t => !t.IsData);
        var sb = new System.Text.StringBuilder();
        sb.Append(L.F("Disk düzeni: {0} iz (", tracks.Count));
        sb.Append(string.Join(", ", tracks.Select(t => $"{t.Number}: {(t.IsData ? L.T("veri") : L.T("ses"))} @{t.Start:N0}")));
        sb.Append(L.F("), kapanış @{0:N0}. Okunacak: ", leadOut));
        sb.Append(string.Join(", ", ranges.Select(r => $"{r.Start:N0}–{r.End - 1:N0}")));
        if (gaps.Count > 0) sb.Append(L.F("; izler/oturumlar arasındaki {0:N0} sektörlük okunamaz boşluk sıfırla doldurulacak", gaps.Sum(g => g.Item2 - g.Item1) / Sector));
        if (audio > 0) sb.Append(L.F("; {0} ses izi ISO'ya alınmaz", audio));
        if (lastSession > 0) sb.Append(L.T("; çok oturumlu disk, ISO son oturumun dosyalarını gösterecek"));
        sb.Append('.');

        return new DiscPlan(ranges[^1].End * Sector, tail * Sector, gaps, lastSession, sb.ToString());
    }

    static string Describe(long fs, long device) =>
        fs > 0 ? L.F("dosya sisteminden {0:N0} sektör (sürücü {1:N0} bildiriyor)", fs / Sector, device / Sector) : L.F("sürücüden {0:N0} sektör", device / Sector);
}
