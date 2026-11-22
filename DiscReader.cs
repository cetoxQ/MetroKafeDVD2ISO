using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MetroKafeDVD2ISO;

/// <param name="Detail">Hasarlı bölgede tekrar deneme yapılırken durum bilgisi.</param>
public sealed record CopyProgress(long BytesDone, long TotalBytes, int BadSectors, string? Message, string? Detail = null);

public sealed record CopyResult(long BytesWritten, int BadSectors, TimeSpan Elapsed);

/// <summary>
/// Optik sürücüyü ham olarak (\\.\X:) sektör sektör okuyup ISO dosyasına yazar.
/// </summary>
public static class DiscReader
{
    public const int SectorSize = 2048;
    const int SectorsPerChunk = 32;           // 64 KB okuma blokları
    const int RetriesPerSector = 3;

    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2;
    const uint OPEN_EXISTING = 3;
    const uint FSCTL_ALLOW_EXTENDED_DASD_IO = 0x00090083;
    const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    const uint IOCTL_STORAGE_CHECK_VERIFY = 0x002D4800;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, out long outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPos, uint method);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeWaitHandle OpenThread(uint access, bool inherit, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelSynchronousIo(SafeWaitHandle thread);

    const uint THREAD_TERMINATE = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern unsafe bool ReadFile(SafeFileHandle h, byte* buffer, int toRead, out int read, IntPtr overlapped);

    static SafeFileHandle OpenDevice(char drive)
    {
        var h = CreateFile($@"\\.\{drive}:", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{drive}: sürücüsü açılamadı");
        // Dosya sisteminin bildirdiği sınırın ötesini de okuyabilmek için
        DeviceIoControl(h, FSCTL_ALLOW_EXTENDED_DASD_IO, IntPtr.Zero, 0, out _, 0, out _, IntPtr.Zero);
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

    /// <summary>Diskin toplam veri boyutu (bayt, sektör katı).</summary>
    public static long GetDiscSize(char drive)
    {
        long size = 0;
        try
        {
            using var h = OpenDevice(drive);
            if (DeviceIoControl(h, IOCTL_DISK_GET_LENGTH_INFO, IntPtr.Zero, 0, out long len, sizeof(long), out _, IntPtr.Zero))
                size = len;
        }
        catch { }
        if (size <= 0)
        {
            try { size = new DriveInfo(drive.ToString()).TotalSize; } catch { }
        }
        return size / SectorSize * SectorSize;
    }

    public static unsafe CopyResult Copy(char drive, string outputPath, bool skipBadSectors,
        IProgress<CopyProgress> progress, CancellationToken ct)
    {
        long total = GetDiscSize(drive);
        if (total <= 0) throw new IOException("Disk boyutu okunamadı. Sürücüde okunabilir bir disk olduğundan emin olun.");

        using var dev = OpenDevice(drive);
        var sw = Stopwatch.StartNew();
        var buffer = GC.AllocateUninitializedArray<byte>(SectorSize * SectorsPerChunk, pinned: true);
        long done = 0;
        int bad = 0;
        long lastReportMs = -1000;

        // Çizik diskte ReadFile dakikalarca dönmeyebilir; iptal edilince bekleyen okumayı kes
        using var thread = OpenThread(THREAD_TERMINATE, false, GetCurrentThreadId());
        using var cancelReg = ct.Register(() => { if (!thread.IsInvalid) CancelSynchronousIo(thread); });

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
        output.SetLength(total);
        output.Position = 0;

        fixed (byte* p = buffer)
        {
            while (done < total)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buffer.Length, total - done);

                if (!TryRead(dev, done, p, want))
                {
                    // Blok okunamadı: sektör sektör tekrar dene
                    for (int off = 0; off < want; off += SectorSize)
                    {
                        ct.ThrowIfCancellationRequested();
                        bool ok = false;
                        long sector = (done + off) / SectorSize;
                        for (int attempt = 1; attempt <= RetriesPerSector && !ok; attempt++)
                        {
                            ct.ThrowIfCancellationRequested();
                            progress.Report(new CopyProgress(done + off, total, bad, null,
                                $"sektör {sector:N0}, deneme {attempt}/{RetriesPerSector}"));
                            ok = TryRead(dev, done + off, p + off, SectorSize);
                        }
                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            if (!skipBadSectors)
                                throw new IOException($"Sektör {sector:N0} okunamadı: {new Win32Exception(err).Message}");
                            new Span<byte>(p + off, SectorSize).Clear();
                            bad++;
                            progress.Report(new CopyProgress(done + off, total, bad,
                                $"Okunamayan sektör {sector:N0} atlandı (sıfırla dolduruldu)"));
                        }
                    }
                }

                output.Write(buffer, 0, want);
                done += want;

                long ms = sw.ElapsedMilliseconds;
                if (ms - lastReportMs >= 150 || done == total)
                {
                    lastReportMs = ms;
                    progress.Report(new CopyProgress(done, total, bad, null));
                }
            }
        }

        output.Flush(true);
        return new CopyResult(done, bad, sw.Elapsed);
    }

    static unsafe bool TryRead(SafeFileHandle h, long offset, byte* dest, int count)
    {
        if (!SetFilePointerEx(h, offset, out _, 0)) return false;
        int pos = 0;
        while (pos < count)
        {
            if (!ReadFile(h, dest + pos, count - pos, out int read, IntPtr.Zero) || read == 0)
                return false;
            pos += read;
        }
        return true;
    }
}
