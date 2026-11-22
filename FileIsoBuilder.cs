using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;

namespace MetroKafeDVD2ISO;

/// <summary>
/// Diskteki dosyalardan (Windows'un IMAPI2FS bileşeniyle) ISO oluşturur.
/// Video CD gibi Mode 2 Form 2 sektörlü disklerde sektör sektör kopya mümkün değildir: bu sektörler
/// 2048 baytlık ISO sektörüne sığmaz ve sürücü onları normal veri olarak okutmaz. Windows ise bu
/// dosyaları (ör. MPEGAV\AVSEQ01.DAT) okunabilir biçimde sunar; ISO bu dosyalardan kurulur.
/// </summary>
public static class FileIsoBuilder
{
    const int FsIso9660 = 1, FsJoliet = 2, FsUdf = 4;

    /// <summary>Video CD / Super Video CD mi? (dosya sistemi üzerinden bakılır)</summary>
    public static string? DetectVideoCd(string root)
    {
        try
        {
            if (File.Exists(Path.Combine(root, "VCD", "INFO.VCD")) || Directory.Exists(Path.Combine(root, "MPEGAV"))) return "Video CD";
            if (File.Exists(Path.Combine(root, "SVCD", "INFO.SVD")) || Directory.Exists(Path.Combine(root, "MPEG2"))) return "Super Video CD";
        }
        catch { }
        return null;
    }

    public static CopyResult Build(string root, string volumeLabel, string outputPath, bool computeHash,
        IProgress<CopyProgress> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var type = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage")
                   ?? throw new IOException("Windows'un ISO bileşeni (IMAPI2FS) bulunamadı.");
        dynamic fsi = Activator.CreateInstance(type)!;
        try
        {
            fsi.FreeMediaBlocks = 0;                                 // kapasite sınırı yok (varsayılan 650 MB)
            fsi.FileSystemsToCreate = FsIso9660 | FsJoliet | FsUdf;  // UDF: 4 GB'tan büyük dosyalar için
            try { fsi.VolumeName = SanitizeLabel(volumeLabel); } catch { fsi.VolumeName = "DISC"; }

            progress.Report(new CopyProgress(CopyPhase.Reading, 0, 0, 0, "Diskteki dosyalar listeleniyor…"));
            ct.ThrowIfCancellationRequested();
            fsi.Root.AddTree(root, false);
            ct.ThrowIfCancellationRequested();

            dynamic result = fsi.CreateResultImage();
            long total = (long)result.TotalBlocks * (int)result.BlockSize;
            var stream = (IStream)result.ImageStream;

            using var sha = computeHash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
            {
                var buf = new byte[1 << 20];
                long done = 0, lastReport = 0;
                IntPtr pRead = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        stream.Read(buf, buf.Length, pRead);
                        int n = Marshal.ReadInt32(pRead);
                        if (n <= 0) break;
                        output.Write(buf, 0, n);
                        sha?.AppendData(buf, 0, n);
                        done += n;
                        if (done - lastReport >= 4 << 20 || done >= total)
                        {
                            lastReport = done;
                            progress.Report(new CopyProgress(CopyPhase.Reading, Math.Min(done, total), total, 0));
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pRead);
                }
                output.Flush(true);
                total = done;
            }

            string? hash = null;
            if (sha != null)
            {
                hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                try { File.WriteAllText(outputPath + ".sha256", $"{hash} *{Path.GetFileName(outputPath)}\n"); } catch { }
            }
            return new CopyResult(total, 0, sw.Elapsed, hash);
        }
        catch (COMException ex)
        {
            throw new IOException("Diskteki dosyalar okunurken hata oluştu: " + ex.Message +
                                  " (Disk çizik olabilir; okunamayan dosya varsa dosya tabanlı yöntem duramaz.)", ex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(fsi);
        }
    }

    // ISO9660 birim adı: büyük harf, rakam ve alt çizgi; en çok 32 karakter
    static string SanitizeLabel(string label)
    {
        var chars = label.ToUpperInvariant().Select(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "DISC" : s[..Math.Min(32, s.Length)];
    }
}
