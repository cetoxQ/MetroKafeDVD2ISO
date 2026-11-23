using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;

namespace MetroKafeDVD2ISO;

/// <summary>
/// Diskteki dosyalardan ISO oluşturur. Video CD gibi Mode 2 Form 2 sektörlü disklerde sektör sektör kopya
/// mümkün değildir: bu sektörler 2048 baytlık ISO sektörüne sığmaz ve sürücü onları normal veri olarak okutmaz.
/// Windows ise bu dosyaları (ör. MPEGAV\AVSEQ01.DAT) okunabilir biçimde sunar.
///
/// İki adımda çalışır: dosyalar önce ISO'nun yanındaki geçici klasöre kopyalanır (ilerleme, hız ve takılma
/// buradan izlenir; okunamayan bölümler atlanabilir), sonra ISO bu yerel kopyadan Windows'un IMAPI2FS
/// bileşeniyle oluşturulur ve geçici klasör silinir.
/// </summary>
public static class FileIsoBuilder
{
    const int FsIso9660 = 1, FsJoliet = 2, FsUdf = 4;
    const int ReadChunk = 64 * 1024;

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

    public static CopyResult Build(string root, string volumeLabel, string outputPath, bool computeHash, bool skipUnreadable,
        IProgress<CopyProgress> progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
            "." + Path.GetFileNameWithoutExtension(outputPath) + ".dosyalar");
        try
        {
            if (Directory.Exists(temp) && !TryDelete(temp)) temp += "-" + Environment.ProcessId;
            var tempDir = Directory.CreateDirectory(temp);
            tempDir.Attributes |= FileAttributes.Hidden;

            int badSectors = CopyFiles(root, temp, skipUnreadable, progress, ct);
            var (total, hash) = WriteIso(temp, volumeLabel, outputPath, computeHash, progress, ct);
            return new CopyResult(total, badSectors, sw.Elapsed, hash);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>IMAPI dosya tutamaçlarını COM nesneleri serbest kalınca bırakır; birkaç kez dener.</summary>
    static bool TryDelete(string dir)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                return true;
            }
            catch
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(200);
            }
        }
        return false;
    }

    /// <summary>1. adım: diskteki tüm dosyaları geçici klasöre kopyalar. Okunamayan sektör sayısını döndürür.</summary>
    static int CopyFiles(string root, string temp, bool skipUnreadable, IProgress<CopyProgress> progress, CancellationToken ct)
    {
        progress.Report(new CopyProgress(CopyPhase.Reading, 0, 0, 0, L.T("Diskteki dosyalar listeleniyor…")));
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true };
        var dirs = Directory.EnumerateDirectories(root, "*", options).ToList();
        var files = Directory.EnumerateFiles(root, "*", options).Select(f => new FileInfo(f)).ToList();
        long total = Math.Max(1, files.Sum(f => f.Length));
        progress.Report(new CopyProgress(CopyPhase.Reading, 0, total, 0,
            L.F("{0:N0} dosya ({1:0.#} MB) geçici klasöre kopyalanıyor; ardından ISO oluşturulacak.", files.Count, total / (1024.0 * 1024))));

        foreach (var d in dirs) Directory.CreateDirectory(Path.Combine(temp, Path.GetRelativePath(root, d)));

        var buf = new byte[ReadChunk];
        long done = 0, lastReport = 0;
        int bad = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(root, f.FullName);
            string target = Path.Combine(temp, rel);
            long unreadable = 0;

            using (var input = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                long length = input.Length;
                output.SetLength(length); // okunamayan bölümler sıfır kalır
                for (long pos = 0; pos < length;)
                {
                    ct.ThrowIfCancellationRequested();
                    int want = (int)Math.Min(ReadChunk, length - pos);
                    int got;
                    try
                    {
                        input.Position = pos;
                        got = input.Read(buf, 0, want);
                        if (got <= 0) break;
                    }
                    catch (IOException ex)
                    {
                        if (!skipUnreadable)
                            throw new IOException(L.F("\"{0}\" dosyası okunamadı ({1}). Disk çizik veya hasarlı olabilir.", rel, ex.Message), ex);
                        unreadable += want;
                        pos += want;
                        done += want;
                        continue;
                    }
                    output.Position = pos;
                    output.Write(buf, 0, got);
                    pos += got;
                    done += got;
                    if (done - lastReport >= 1 << 20)
                    {
                        lastReport = done;
                        progress.Report(new CopyProgress(CopyPhase.Reading, done, total, bad));
                    }
                }
            }

            try
            {
                File.SetLastWriteTimeUtc(target, f.LastWriteTimeUtc);
                File.SetCreationTimeUtc(target, f.CreationTimeUtc);
            }
            catch { }

            if (unreadable > 0)
            {
                int sectors = (int)((unreadable + 2047) / 2048);
                bad += sectors;
                progress.Report(new CopyProgress(CopyPhase.Reading, done, total, bad,
                    L.F("\"{0}\": {1:N0} KB okunamadı, sıfırla dolduruldu (dosya bozuk olabilir).", rel, unreadable / 1024)));
            }
        }
        progress.Report(new CopyProgress(CopyPhase.Reading, total, total, bad));
        return bad;
    }

    /// <summary>2. adım: yerel kopyadan ISO'yu oluşturur (IMAPI2FS: ISO9660 + Joliet + UDF).</summary>
    static (long total, string? hash) WriteIso(string folder, string volumeLabel, string outputPath, bool computeHash,
        IProgress<CopyProgress> progress, CancellationToken ct)
    {
        var type = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage")
                   ?? throw new IOException(L.T("Windows'un ISO bileşeni (IMAPI2FS) bulunamadı."));
        dynamic fsi = Activator.CreateInstance(type)!;
        object? root = null, result = null, stream = null;
        try
        {
            fsi.FreeMediaBlocks = 0;                                 // kapasite sınırı yok (varsayılan 650 MB)
            fsi.FileSystemsToCreate = FsIso9660 | FsJoliet | FsUdf;  // UDF: 4 GB'tan büyük dosyalar için
            try { fsi.VolumeName = SanitizeLabel(volumeLabel); } catch { fsi.VolumeName = "DISC"; }

            progress.Report(new CopyProgress(CopyPhase.Building, 0, 0, 0, L.T("ISO oluşturuluyor…")));
            root = fsi.Root;
            ((dynamic)root).AddTree(folder, false);
            ct.ThrowIfCancellationRequested();

            result = fsi.CreateResultImage();
            long total = (long)((dynamic)result).TotalBlocks * (int)((dynamic)result).BlockSize;
            stream = ((dynamic)result).ImageStream;
            var image = (IStream)stream;

            using var sha = computeHash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            long done = 0, lastReport = 0;
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
            {
                var buf = new byte[1 << 20];
                IntPtr pRead = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        image.Read(buf, buf.Length, pRead);
                        int n = Marshal.ReadInt32(pRead);
                        if (n <= 0) break;
                        output.Write(buf, 0, n);
                        sha?.AppendData(buf, 0, n);
                        done += n;
                        if (done - lastReport >= 4 << 20 || done >= total)
                        {
                            lastReport = done;
                            progress.Report(new CopyProgress(CopyPhase.Building, Math.Min(done, total), total, 0));
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pRead);
                }
                output.Flush(true);
            }

            string? hash = null;
            if (sha != null)
            {
                hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                try { File.WriteAllText(outputPath + ".sha256", $"{hash} *{Path.GetFileName(outputPath)}\n"); } catch { }
            }
            return (done, hash);
        }
        catch (COMException ex)
        {
            throw new IOException(L.F("ISO oluşturulamadı: {0}", ex.Message), ex);
        }
        finally
        {
            // IMAPI kaynak dosyaları açık tutar; geçici klasör silinebilsin diye tüm COM nesnelerini hemen bırak
            foreach (var o in new[] { stream, result, root })
                if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
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
