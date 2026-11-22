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
    /// <returns>Bayt cinsinden boyut; dosya sistemi tanınmazsa 0.</returns>
    public static long ReadFileSystemSize(Func<long, byte[], bool> readSector)
    {
        var buf = new byte[Sector];
        long iso = 0, udf = 0;

        // ISO9660: sektör 16'dan itibaren birim tanımlayıcıları; birincil tanımlayıcı (tip 1) hacim boyutunu verir
        for (long s = 16; s < 32 && readSector(s, buf); s++)
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
        if (IsAnchor(256))
        {
            uint vdsLength = BitConverter.ToUInt32(buf, 16), vdsLocation = BitConverter.ToUInt32(buf, 20);
            uint reserveLength = BitConverter.ToUInt32(buf, 24), reserveLocation = BitConverter.ToUInt32(buf, 28);
            long end = reserveLocation + reserveLength / Sector; // yedek tanımlayıcı dizisi genelde bölümden sonra gelir

            long count = Math.Min(64, vdsLength / Sector);
            for (long i = 0; i < count && readSector(vdsLocation + i, buf); i++)
            {
                ushort tag = BitConverter.ToUInt16(buf, 0);
                if (tag == 5) end = Math.Max(end, (long)BitConverter.ToUInt32(buf, 188) + BitConverter.ToUInt32(buf, 192));
                if (tag == 8) break; // sonlandırıcı
            }

            // UDF, son sektör (N) ve N-256'ya da çapa koyar; bunlar ISO9660 boyutunun dışında kalabilir.
            // Bilinen yapıların hemen ardında N-256 çapasını ara; 256 sektör ilerideki son çapa diskin sonudur.
            // (Bazı yazıcılar art arda birkaç çapa koyar, bu yüzden küçük bir aralık taranır.)
            end = Math.Max(end, iso / Sector);
            for (long s = end; s < end + 64; s++)
            {
                if (!readSector(s, buf)) break;
                if (!IsAnchorAt(s)) continue;
                end = s + 1;
                for (long t = s + 256; t < s + 256 + 16; t++)
                {
                    if (!readSector(t, buf)) break;
                    if (IsAnchorAt(t)) end = t + 1;
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
}
