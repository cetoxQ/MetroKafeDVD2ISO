using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace MetroKafeDVD2ISO;

public enum CopyPhase { Reading, Recovering, Verifying }

/// <param name="Detail">Hasarlı bölgede tekrar deneme yapılırken durum bilgisi.</param>
public sealed record CopyProgress(CopyPhase Phase, long BytesDone, long TotalBytes, int BadSectors, string? Message = null, string? Detail = null);

public sealed record CopyResult(long BytesWritten, int BadSectors, TimeSpan Elapsed,
    string? Sha256 = null, int VerifyMismatches = 0, int VerifyUnreadable = 0);

public sealed class CopyOptions
{
    public bool SkipBadSectors { get; init; } = true;
    /// <summary>Bir okuma bu süreden uzun sürerse kesilip atlanır (0 = kapalı).</summary>
    public int StallTimeoutSeconds { get; init; } = 15;
    /// <summary>Önce sağlam bölümleri oku, hasarlı bölgelere en sonda dön.</summary>
    public bool TwoPass { get; init; } = true;
    /// <summary>Hasarlı bölgede sürücü hızını düşür.</summary>
    public bool SlowDownOnErrors { get; init; } = true;
    public bool Verify { get; init; }
}

public enum ReadStatus { Ok, Error, TimedOut }

/// <summary>Sektör okunabilen kaynak (gerçek sürücü veya test için taklit).</summary>
public interface ISectorSource : IDisposable
{
    long Size { get; }
    /// <summary>Size'dan sonra sürücünün bildirdiği, veri içermesi beklenmeyen kuyruk; en sonda denenir.</summary>
    long TailLength => 0;
    /// <summary>Okunmadan sıfırla doldurulacak aralıklar (bayt): oturum araları, ses izleri.</summary>
    IReadOnlyList<(long Start, long End)> Gaps => [];
    /// <summary>Çok oturumlu diskte son oturumun başı (sektör); tek oturumda 0.</summary>
    long LastSessionStart => 0;
    ReadStatus Read(long offset, byte[] buffer, int bufferOffset, int count);
    /// <summary>Sürücüyü yavaş (true) veya en yüksek (false) hıza ayarlar; destek yoksa false döner.</summary>
    bool SetSlowSpeed(bool slow);
}

/// <summary>
/// Diski ISO dosyasına kopyalar. İki geçişli modda hasarlı bölgeler önce atlanır
/// (atlama aralığı art arda hatalarda büyür), sağlam kısımlar bittikten sonra sektör sektör kurtarılır.
/// </summary>
public sealed class DiscCopier
{
    public const int SectorSize = 2048;
    const int ChunkSize = SectorSize * 32;            // 64 KB okuma blokları
    const int RetriesPerSector = 3;
    const long MaxSkip = 16L * 1024 * 1024;
    const long RestoreSpeedAfter = 64L * 1024 * 1024; // tek geçişte bu kadar sorunsuz okumadan sonra hızı geri aç

    readonly ISectorSource src;
    readonly CopyOptions opt;
    readonly IProgress<CopyProgress> progress;
    readonly CancellationToken ct;
    readonly byte[] buffer = new byte[ChunkSize];
    readonly List<long> badSectors = [];
    readonly HashSet<long> mergedSectors = [];   // diskten farklı olması beklenen sektörler (doğrulamada atlanır)
    long total;
    long processed;
    CopyPhase phase;
    bool slowed, speedWarned;
    long lastReportTicks;

    public DiscCopier(ISectorSource src, CopyOptions opt, IProgress<CopyProgress> progress, CancellationToken ct)
    {
        this.src = src;
        this.opt = opt;
        this.progress = progress;
        this.ct = ct;
        total = src.Size / SectorSize * SectorSize;
    }

    bool TwoPass => opt.TwoPass && opt.SkipBadSectors;

    public CopyResult Run(string outputPath)
    {
        if (total <= 0) throw new IOException("Disk boyutu okunamadı. Sürücüde okunabilir bir disk olduğundan emin olun.");
        var sw = Stopwatch.StartNew();
        try
        {
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
            {
                output.SetLength(total); // sıfırla dolu; atlanan bölgeler sıfır kalır
                CopyAll(output);
                MergeLastSessionDescriptors(output);
                ReadTail(output);
                output.Flush(true);
            }
        }
        finally
        {
            if (slowed) src.SetSlowSpeed(false);
        }

        var elapsed = sw.Elapsed;
        if (!opt.Verify) return new CopyResult(total, badSectors.Count, elapsed);
        var (sha, mismatches, unreadable) = Verify(outputPath);
        return new CopyResult(total, badSectors.Count, sw.Elapsed, sha, mismatches, unreadable);
    }

    void CopyAll(FileStream output)
    {
        var pending = new List<(long start, long len)>();
        long pos = 0, skip = 0, lastErrorPos = -1;
        phase = CopyPhase.Reading;
        Report(force: true);

        while (pos < total)
        {
            ct.ThrowIfCancellationRequested();
            // Oturum araları / ses izleri: hiç okunmaz, dosyada sıfır kalır
            if (InGap(pos, out long gapEnd))
            {
                long end = Math.Min(gapEnd, total);
                processed += end - pos;
                pos = end;
                Report();
                continue;
            }
            long limit = Math.Min(total, NextGapStart(pos));
            int len = (int)Math.Min(ChunkSize, limit - pos);
            var st = src.Read(pos, buffer, 0, len);
            if (st == ReadStatus.Ok)
            {
                Write(output, pos, len);
                pos += len;
                processed += len;
                skip = 0;
                if (slowed && !TwoPass && pos - lastErrorPos > RestoreSpeedAfter) SetSlow(false);
                Report();
                continue;
            }

            string why = st == ReadStatus.TimedOut ? "okuma zaman aşımına uğradı" : "okunamadı";
            if (TwoPass)
            {
                long skipLen = Math.Min(len + skip, limit - pos);
                pending.Add((pos, skipLen));
                Report(force: true, message: $"Hasarlı bölge ({FormatMB(pos)} konumunda {why}); {FormatMB(skipLen, true)} atlandı, en sonda tekrar denenecek");
                pos += skipLen;
                skip = skip == 0 ? ChunkSize : Math.Min(skip * 2, MaxSkip);
            }
            else
            {
                if (opt.SlowDownOnErrors) SetSlow(true);
                RecoverRange(output, pos, len);
                pos += len;
                lastErrorPos = pos;
            }
        }

        if (pending.Count == 0) return;

        phase = CopyPhase.Recovering;
        long pendingBytes = pending.Sum(p => p.len);
        Report(force: true, message: $"Sağlam bölümler bitti. {pending.Count} hasarlı bölge ({FormatMB(pendingBytes, true)}) tekrar deneniyor");
        if (opt.SlowDownOnErrors) SetSlow(true);
        foreach (var (start, len) in pending) RecoverRange(output, start, len);
    }

    bool InGap(long pos, out long end)
    {
        foreach (var (s, e) in src.Gaps)
            if (pos >= s && pos < e) { end = e; return true; }
        end = 0;
        return false;
    }

    long NextGapStart(long pos)
    {
        long next = long.MaxValue;
        foreach (var (s, _) in src.Gaps)
            if (s > pos) next = Math.Min(next, s);
        return next;
    }

    /// <summary>
    /// Çok oturumlu disklerde ISO9660, son oturumun başı + 16'ya yeni birim tanımlayıcıları yazar ve bunlar
    /// tüm oturumların son halini (mutlak adreslerle) tarif eder. ISO dosyası açılırken yalnız sektör 16'ya
    /// bakıldığı için son oturumun tanımlayıcıları oraya kopyalanır; yoksa ISO'da sadece ilk oturum görünür.
    /// </summary>
    void MergeLastSessionDescriptors(FileStream output)
    {
        long session = src.LastSessionStart;
        if (session <= 0) return;

        List<byte[]> ReadDescriptors(long start)
        {
            var list = new List<byte[]>();
            for (int i = 0; i < 16; i++)
            {
                var b = new byte[SectorSize];
                if (src.Read((start + 16 + i) * SectorSize, b, 0, SectorSize) != ReadStatus.Ok) break;
                if (b[1] != 'C' || b[2] != 'D' || b[3] != '0' || b[4] != '0' || b[5] != '1') break;
                list.Add(b);
                if (b[0] == 255) break; // sonlandırıcı
            }
            return list;
        }

        var last = ReadDescriptors(session);
        var first = ReadDescriptors(0);
        // Yalnız ilk oturumun tanımlayıcılarının kapladığı yere yaz; ardındaki gerçek veriye dokunma
        if (!last.Any(d => d[0] == 1) || last.Count > first.Count) return;

        for (int i = 0; i < last.Count; i++)
        {
            output.Position = (16 + i) * (long)SectorSize;
            output.Write(last[i], 0, SectorSize);
            mergedSectors.Add(16 + i);
        }
        Report(force: true, message: "Çok oturumlu disk: son oturumun dosya sistemi tanımı ISO'nun başına yazıldı; ISO açıldığında son eklenen dosyalar da görünür.");
    }

    /// <summary>
    /// Dosya sisteminin bittiği yerden sonra sürücünün bildirdiği kuyruğu dener. Yazılabilir disklerde bu
    /// sektörler genelde okunamaz ve veri içermez; okunabildiği kadarı eklenir, ilk hatada durulur ve
    /// bu hata sayılmaz. Böylece hem sahte "hatalı sektör" çıkmaz hem de gerçek veri kesilmez.
    /// </summary>
    void ReadTail(FileStream output)
    {
        long end = total + src.TailLength / SectorSize * SectorSize, pos = total;
        if (end <= total) return;
        const int TailChunk = SectorSize * 16;

        while (pos < end)
        {
            ct.ThrowIfCancellationRequested();
            int len = (int)Math.Min(TailChunk, end - pos);
            if (src.Read(pos, buffer, 0, len) == ReadStatus.Ok)
            {
                Write(output, pos, len);
                pos += len;
                continue;
            }
            // Blok okunamadı: okunabilen sektörleri (ör. son UDF çapası) tek tek al, ilk hatada bitir
            int s = 0;
            while (s < len && src.Read(pos + s, buffer, s, SectorSize) == ReadStatus.Ok)
            {
                Write(output, pos + s, SectorSize, s);
                s += SectorSize;
            }
            pos += s;
            break;
        }

        long added = (pos - total) / SectorSize, skipped = (end - pos) / SectorSize;
        if (added > 0)
            Report(force: true, message: $"Dosya sisteminden sonra {added:N0} sektör daha okunabildi ve ISO'ya eklendi.");
        if (skipped > 0)
        {
            Report(force: true, message: $"Disk sonundaki {skipped:N0} sektör okunamadı. Bunlar veri içermeyen kuyruk sektörleri " +
                                         "(yazılabilir disklerde normaldir); hata sayılmadı.");
            output.SetLength(pos);
        }
        total = pos;
        processed = total;
    }

    /// <summary>Bir aralığı önce blok blok, okunamayan bloklarda sektör sektör kurtarır.</summary>
    void RecoverRange(FileStream output, long start, long length)
    {
        for (long off = start; off < start + length; off += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = (int)Math.Min(ChunkSize, start + length - off);
            var st = src.Read(off, buffer, 0, len);
            if (st == ReadStatus.Ok)
            {
                Write(output, off, len);
                processed += len;
                Report();
                continue;
            }

            for (int s = 0; s < len; s += SectorSize)
            {
                long sector = (off + s) / SectorSize;
                st = ReadStatus.Error;
                for (int attempt = 1; attempt <= RetriesPerSector; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    Report(force: true, detail: $"sektör {sector:N0}, deneme {attempt}/{RetriesPerSector}");
                    st = src.Read(off + s, buffer, s, SectorSize);
                    // Zaman aşımında tekrar denemek yine dakikalar sürer; doğrudan atla
                    if (st != ReadStatus.Error) break;
                }

                if (st == ReadStatus.Ok)
                {
                    Write(output, off + s, SectorSize, s);
                }
                else
                {
                    string why = st == ReadStatus.TimedOut ? "zaman aşımı" : "okunamadı";
                    if (!opt.SkipBadSectors)
                        throw new IOException($"Sektör {sector:N0} okunamadı ({why}). Disk çizik veya hasarlı olabilir.");
                    badSectors.Add(sector);
                    Report(force: true, message: $"Sektör {sector:N0} atlandı ({why}), sıfırla dolduruldu");
                }
                processed += SectorSize;
            }
        }
    }

    (string sha, int mismatches, int unreadable) Verify(string outputPath)
    {
        phase = CopyPhase.Verifying;
        processed = 0;
        Report(force: true, message: "Doğrulama başladı: disk tekrar okunup ISO ile karşılaştırılıyor");

        // Atlanan ve bilerek değiştirilen sektörler karşılaştırılmaz
        var bad = badSectors.Concat(mergedSectors).ToHashSet();
        var fileBuf = new byte[ChunkSize];
        int mismatches = 0, unreadable = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var file = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);

        for (long pos = 0; pos < total; pos += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            int len = (int)Math.Min(ChunkSize, total - pos);
            file.ReadExactly(fileBuf, 0, len);
            sha.AppendData(fileBuf, 0, len);

            // Boşluk içeren blok diskten bütün olarak okunmaz; sektör sektör, boşluklar atlanarak karşılaştırılır
            bool touchesGap = InGap(pos, out _) || NextGapStart(pos) < pos + len;
            bool chunkOk = !touchesGap && src.Read(pos, buffer, 0, len) == ReadStatus.Ok;
            if (!chunkOk || !buffer.AsSpan(0, len).SequenceEqual(fileBuf.AsSpan(0, len)))
            {
                for (int s = 0; s < len; s += SectorSize)
                {
                    long sector = (pos + s) / SectorSize;
                    if (bad.Contains(sector) || InGap(pos + s, out _)) continue;
                    if (!chunkOk && src.Read(pos + s, buffer, s, SectorSize) != ReadStatus.Ok)
                    {
                        unreadable++;
                        continue;
                    }
                    if (!buffer.AsSpan(s, SectorSize).SequenceEqual(fileBuf.AsSpan(s, SectorSize)))
                    {
                        mismatches++;
                        Report(force: true, message: $"Doğrulama: sektör {sector:N0} diskle uyuşmuyor");
                    }
                }
            }
            processed = pos + len;
            Report();
        }

        string hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        try { File.WriteAllText(outputPath + ".sha256", $"{hash} *{Path.GetFileName(outputPath)}\n"); } catch { }
        return (hash, mismatches, unreadable);
    }

    void SetSlow(bool slow)
    {
        if (slowed == slow) return;
        if (src.SetSlowSpeed(slow))
        {
            slowed = slow;
            Report(force: true, message: slow ? "Sürücü hızı düşürüldü (hasarlı bölge daha yavaş okunuyor)" : "Sürücü hızı normale döndü");
        }
        else if (!speedWarned)
        {
            speedWarned = true;
            Report(force: true, message: "Sürücü hız ayarını desteklemiyor; normal hızda devam ediliyor");
        }
    }

    void Write(FileStream output, long offset, int count, int bufferOffset = 0)
    {
        output.Position = offset;
        output.Write(buffer, bufferOffset, count);
    }

    void Report(bool force = false, string? message = null, string? detail = null)
    {
        long now = Stopwatch.GetTimestamp();
        if (!force && Stopwatch.GetElapsedTime(lastReportTicks, now).TotalMilliseconds < 150 && processed < total) return;
        lastReportTicks = now;
        progress.Report(new CopyProgress(phase, processed, total, badSectors.Count, message, detail));
    }

    static string FormatMB(long bytes, bool size = false) =>
        size && bytes < 1024 * 1024 ? $"{bytes / 1024} KB" : $"{bytes / (1024.0 * 1024):0.#} MB";
}

/// <summary>Optik sürücüyü ham olarak (\\.\X:) okur. Zaman aşımı ve iptal için bekleyen okumayı keser.</summary>
public sealed class DeviceSource : ISectorSource
{
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
    const uint OPEN_EXISTING = 3;
    const uint FSCTL_ALLOW_EXTENDED_DASD_IO = 0x00090083;
    const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    const uint IOCTL_STORAGE_CHECK_VERIFY = 0x002D4800;
    const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;
    const uint IOCTL_CDROM_SET_SPEED = 0x00024060;
    const uint IOCTL_CDROM_READ_TOC_EX = 0x00024054;
    const uint THREAD_TERMINATE = 0x0001;
    const int ERROR_OPERATION_ABORTED = 995;

    [StructLayout(LayoutKind.Sequential)]
    struct CDROM_SET_SPEED { public int RequestType; public ushort ReadSpeed; public ushort WriteSpeed; public int RotationControl; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, out long outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref CDROM_SET_SPEED inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPos, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern unsafe bool ReadFile(SafeFileHandle h, byte* buffer, int toRead, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeWaitHandle OpenThread(uint access, bool inherit, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelSynchronousIo(SafeWaitHandle thread);

    readonly SafeFileHandle dev;
    readonly SafeWaitHandle thread;
    readonly CancellationTokenRegistration cancelReg;
    readonly Thread? watchdog;
    readonly long timeoutTicks;
    readonly CancellationToken ct;
    long readStart;            // okuma başlangıcı (Stopwatch zaman damgası), 0 = boşta
    volatile bool timedOut, disposed;

    /// <summary>Kopyalanacak boyut: veri izlerinin dosya sistemi sonu; bilinmiyorsa sürücünün bildirdiği.</summary>
    public long Size { get; }
    /// <summary>Sürücünün bildirdiği boyut (yazılabilir disklerde okunamayan kuyruk sektörlerini içerebilir).</summary>
    public long DeviceLength { get; }
    public long TailLength { get; }
    public IReadOnlyList<(long Start, long End)> Gaps { get; }
    public long LastSessionStart { get; }
    /// <summary>Günlüğe yazılacak disk düzeni özeti (izler, oturumlar, okunacak aralıklar).</summary>
    public string Description { get; }

    /// <summary>Okuma yapacak iş parçacığında oluşturulmalıdır.</summary>
    public DeviceSource(char drive, int timeoutSeconds, CancellationToken ct)
    {
        this.ct = ct;
        dev = Open(drive, GENERIC_READ);
        DeviceIoControl(dev, FSCTL_ALLOW_EXTENDED_DASD_IO, IntPtr.Zero, 0, out _, 0, out _, IntPtr.Zero);
        Drive = drive;

        // Çizik diskte ReadFile dakikalarca dönmeyebilir; iptal veya zaman aşımında bekleyen okumayı kes
        thread = OpenThread(THREAD_TERMINATE, false, GetCurrentThreadId());
        cancelReg = ct.Register(() => { if (!thread.IsInvalid) CancelSynchronousIo(thread); });
        if (timeoutSeconds > 0)
        {
            timeoutTicks = (long)(timeoutSeconds * (double)Stopwatch.Frequency);
            watchdog = new Thread(Watch) { IsBackground = true, Name = "okuma-bekçisi" };
            watchdog.Start();
        }

        DeviceLength = DeviceIoControl(dev, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, out long len, sizeof(long), out _, IntPtr.Zero)
            ? len / DiscCopier.SectorSize * DiscCopier.SectorSize : 0;
        var plan = DiscLayout.Plan(ReadToc(), DeviceLength,
            (sector, buf) => Read(sector * DiscCopier.SectorSize, buf, 0, DiscCopier.SectorSize) == ReadStatus.Ok);
        Size = plan.Size;
        TailLength = plan.TailLength;
        Gaps = plan.Gaps;
        LastSessionStart = plan.LastSessionStart;
        Description = plan.Description;
    }

    /// <summary>İçindekiler tablosu (TOC), adresler LBA olarak; desteklenmiyorsa null.</summary>
    List<DiscLayout.TocTrack>? ReadToc()
    {
        // CDROM_READ_TOC_EX: Format=0 (TOC), Msf=0 (LBA), başlangıç izi 1
        var input = new byte[] { 0, 1, 0, 0 };
        var toc = new byte[4 + 100 * 8 + 16];
        if (!DeviceIoControl(dev, IOCTL_CDROM_READ_TOC_EX, input, input.Length, toc, toc.Length, out int got, IntPtr.Zero) || got < 12)
            return null;

        int length = (toc[0] << 8) | toc[1];               // uzunluk alanı kendisini (2 bayt) saymaz
        int entries = Math.Min((Math.Min(length + 2, got) - 4) / 8, 100);
        var tracks = new List<DiscLayout.TocTrack>();
        for (int i = 0; i < entries; i++)
        {
            int o = 4 + i * 8;
            int control = toc[o + 1] & 0x0F;                 // alt dört bit: kontrol; 0x04 = veri izi
            int number = toc[o + 2];
            long lba = ((long)toc[o + 4] << 24) | ((long)toc[o + 5] << 16) | ((long)toc[o + 6] << 8) | toc[o + 7];
            tracks.Add(new DiscLayout.TocTrack(number, (control & 0x04) != 0, lba));
        }

        // Tutarsız TOC'a güvenme (adresler sıralı olmalı, disk boyutunu aşmamalı); yoksa eski yönteme dön
        long maxSector = DeviceLength > 0 ? DeviceLength / DiscCopier.SectorSize + 1 : long.MaxValue;
        for (int i = 0; i < tracks.Count; i++)
            if (tracks[i].Start < 0 || tracks[i].Start > maxSector || (i > 0 && tracks[i].Start < tracks[i - 1].Start))
                return null;
        return tracks.Count > 0 && tracks[^1].Number == DiscLayout.LeadOut ? tracks : null;
    }

    public char Drive { get; }

    bool IsDvd => Size > 900L * 1024 * 1024;

    void Watch()
    {
        while (!disposed)
        {
            Thread.Sleep(200);
            long s = Interlocked.Read(ref readStart);
            if (s != 0 && !timedOut && Stopwatch.GetTimestamp() - s > timeoutTicks)
            {
                timedOut = true;
                CancelSynchronousIo(thread);
            }
        }
    }

    public unsafe ReadStatus Read(long offset, byte[] buffer, int bufferOffset, int count)
    {
        for (int spurious = 0; ; spurious++)
        {
            timedOut = false;
            long start = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref readStart, start);
            bool ok;
            fixed (byte* p = &buffer[bufferOffset])
                ok = ReadAll(offset, p, count);
            int err = Marshal.GetLastWin32Error();
            Interlocked.Exchange(ref readStart, 0);

            if (ok) return ReadStatus.Ok;
            ct.ThrowIfCancellationRequested();
            if (err == ERROR_OPERATION_ABORTED && timedOut)
            {
                // Bekçi bir önceki okumayı hedeflemiş olabilir; süre gerçekten dolmadıysa tekrar dene
                if (Stopwatch.GetTimestamp() - start >= timeoutTicks * 9 / 10) return ReadStatus.TimedOut;
                if (spurious < 2) continue;
            }
            return ReadStatus.Error;
        }
    }

    unsafe bool ReadAll(long offset, byte* dest, int count)
    {
        if (!SetFilePointerEx(dev, offset, out _, 0)) return false;
        int pos = 0;
        while (pos < count)
        {
            if (!ReadFile(dev, dest + pos, count - pos, out int read, IntPtr.Zero) || read == 0)
                return false;
            pos += read;
        }
        return true;
    }

    public bool SetSlowSpeed(bool slow)
    {
        // KB/s: DVD 2x ≈ 2770, CD 4x ≈ 706; 0xFFFF = en yüksek hız
        var req = new CDROM_SET_SPEED { ReadSpeed = slow ? (ushort)(IsDvd ? 2770 : 706) : (ushort)0xFFFF, WriteSpeed = 0xFFFF };
        return DeviceIoControl(dev, IOCTL_CDROM_SET_SPEED, ref req, Marshal.SizeOf<CDROM_SET_SPEED>(), IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    public void Dispose()
    {
        disposed = true;
        cancelReg.Dispose();
        watchdog?.Join(1000);
        thread.Dispose();
        dev.Dispose();
    }

    static SafeFileHandle Open(char drive, uint access)
    {
        var h = CreateFile($@"\\.\{drive}:", access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{drive}: sürücüsü açılamadı");
        return h;
    }

    /// <summary>Diskte medya var mı (sürücü hazır mı)?</summary>
    public static bool IsMediaPresent(char drive)
    {
        try
        {
            using var h = CreateFile($@"\\.\{drive}:", 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return false;
            return DeviceIoControl(h, IOCTL_STORAGE_CHECK_VERIFY, IntPtr.Zero, 0, out _, 0, out _, IntPtr.Zero);
        }
        catch { return false; }
    }

    /// <summary>Diski sürücüden çıkarır.</summary>
    public static bool Eject(char drive)
    {
        try
        {
            using var h = Open(drive, GENERIC_READ);
            return DeviceIoControl(h, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0, out _, 0, out _, IntPtr.Zero);
        }
        catch { return false; }
    }
}
