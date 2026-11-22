using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MetroKafeDVD2ISO;

/// <summary>Ana program ile yardımcı işlem arasındaki tek satırlık JSON mesajı.</summary>
public sealed class WorkerMessage
{
    public string Type { get; set; } = "";          // probe, info, progress, result, error, eject

    // probe
    public bool Ready { get; set; }
    public string? Label { get; set; }
    public string? FileSystem { get; set; }
    public long DeviceLength { get; set; }
    public string? MediaKind { get; set; }        // "Video CD", "Super Video CD" veya null
    public long Size { get; set; }

    // progress / result
    public CopyPhase Phase { get; set; }
    public long Done { get; set; }
    public long Total { get; set; }
    public int Bad { get; set; }
    public string? Message { get; set; }
    public string? Detail { get; set; }
    public long ElapsedMs { get; set; }
    public string? Sha256 { get; set; }
    public int Mismatches { get; set; }
    public int Unreadable { get; set; }
    public bool Cancelled { get; set; }
    public bool Ok { get; set; }
}

/// <summary>
/// Diske dokunan her iş ayrı bir işlemde (bu exe'nin --probe/--copy/--eject ile başlatılmış kopyası) yapılır.
/// Sürücü bir okumada takılırsa Windows o işlemi okuma bitene kadar sonlandıramaz; pencere ise etkilenmez.
/// </summary>
public static class Worker
{
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };

    public static bool IsCommand(string[] args) => args.Length > 0 && args[0] is "--probe" or "--copy" or "--eject";

    public static int Run(string[] args)
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        void Send(WorkerMessage m)
        {
            // Ana program kapandıysa boru kopmuştur; yazma hatası önemsiz
            try { lock (stdout) stdout.WriteLine(JsonSerializer.Serialize(m, Json)); } catch { }
        }

#if DEBUG
        // Test: takılan sürücüyü taklit et (MK_TEST_HANG=--probe veya --copy)
        if (Environment.GetEnvironmentVariable("MK_TEST_HANG") == args[0]) Thread.Sleep(Timeout.Infinite);
#endif
        try
        {
            char drive = args[1][0];
            switch (args[0])
            {
                case "--probe":
                    Send(Probe(drive));
                    return 0;
                case "--eject":
                    Send(new WorkerMessage { Type = "eject", Ok = DeviceSource.Eject(drive) });
                    return 0;
                default:
                    return Copy(drive, args[2], JsonSerializer.Deserialize<CopyOptions>(args[3]) ?? new CopyOptions(), Send);
            }
        }
        catch (Exception ex)
        {
            Send(new WorkerMessage { Type = "error", Message = ex.Message });
            return 1;
        }
    }

    static WorkerMessage Probe(char drive)
    {
        var di = new DriveInfo(drive.ToString());
        if (!di.IsReady) return new WorkerMessage { Type = "probe", Ready = false };
        string label = "", fs = "";
        try { label = di.VolumeLabel; fs = di.DriveFormat; } catch { }
        using var dev = new DeviceSource(drive, 10, CancellationToken.None);
        return new WorkerMessage
        {
            Type = "probe",
            Ready = true,
            MediaKind = FileIsoBuilder.DetectVideoCd($"{drive}:\\"),
            Label = label,
            FileSystem = fs,
            DeviceLength = dev.DeviceLength,
            Message = dev.Description,
            Size = dev.Size,
        };
    }

    static int Copy(char drive, string output, CopyOptions opt, Action<WorkerMessage> send)
    {
        using var cts = new CancellationTokenSource();
        // "cancel" satırı gelirse ya da ana program kapanıp boru koparsa iptal et
        new Thread(() =>
        {
            try
            {
                using var r = new StreamReader(Console.OpenStandardInput());
                string? line;
                while ((line = r.ReadLine()) != null && line != "cancel") { }
            }
            catch { }
            cts.Cancel();
        }) { IsBackground = true }.Start();

        var progress = new SyncProgress(p => send(new WorkerMessage
        {
            Type = "progress",
            Phase = p.Phase,
            Done = p.BytesDone,
            Total = p.TotalBytes,
            Bad = p.BadSectors,
            Message = p.Message,
            Detail = p.Detail,
        }));

        try
        {
            string root = $"{drive}:\\";
            string? videoCd = FileIsoBuilder.DetectVideoCd(root);
            if (opt.Method == CopyMethod.Files || (opt.Method == CopyMethod.Auto && videoCd != null))
            {
                send(new WorkerMessage
                {
                    Type = "info",
                    Message = videoCd != null
                        ? $"{videoCd} algılandı. Görüntü, ISO sektörlerine sığmayan özel sektörlerde (Mode 2 Form 2) durduğu için " +
                          "sektör sektör kopyalanamaz; ISO diskteki dosyalardan oluşturuluyor."
                        : "Dosya tabanlı yöntem: ISO diskteki dosyalardan oluşturuluyor.",
                });
                if (opt.Verify)
                    send(new WorkerMessage { Type = "info", Message = "Dosya tabanlı yöntemde diskle sektör karşılaştırması yapılamaz; yalnız SHA-256 hesaplanacak." });
                string label = "";
                try { label = new DriveInfo(drive.ToString()).VolumeLabel; } catch { }
                var fr = FileIsoBuilder.Build(root, label, output, opt.Verify, progress, cts.Token);
                send(new WorkerMessage { Type = "result", Done = fr.BytesWritten, ElapsedMs = (long)fr.Elapsed.TotalMilliseconds, Sha256 = fr.Sha256 });
                return 0;
            }

            int timeout = opt.SkipBadSectors ? opt.StallTimeoutSeconds : 0;
            using var dev = new DeviceSource(drive, timeout, cts.Token);
            send(new WorkerMessage { Type = "info", Message = dev.Description });
            if (dev.TailLength > 0)
                send(new WorkerMessage
                {
                    Type = "info",
                    Message = $"Dosya sistemi {dev.Size / DiscCopier.SectorSize:N0}. sektörde bitiyor; ardındaki " +
                              $"{dev.TailLength / DiscCopier.SectorSize:N0} sektör en sonda denenecek, okunamazsa hata sayılmayacak.",
                });

            var r = new DiscCopier(dev, opt, progress, cts.Token).Run(output);
            send(new WorkerMessage
            {
                Type = "result",
                Done = r.BytesWritten,
                Bad = r.BadSectors,
                ElapsedMs = (long)r.Elapsed.TotalMilliseconds,
                Sha256 = r.Sha256,
                Mismatches = r.VerifyMismatches,
                Unreadable = r.VerifyUnreadable,
            });
            return 0;
        }
        catch (Exception ex)
        {
            try { File.Delete(output); } catch { }
            send(new WorkerMessage { Type = "error", Message = ex.Message, Cancelled = cts.IsCancellationRequested });
            return cts.IsCancellationRequested ? 2 : 1;
        }
    }

    sealed class SyncProgress(Action<CopyProgress> report) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => report(value);
    }

    internal static WorkerMessage? Parse(string line)
    {
        try { return JsonSerializer.Deserialize<WorkerMessage>(line, Json); } catch { return null; }
    }
}

/// <summary>Ana programın yardımcı işlemleri başlatıp süre sınırıyla izlediği taraf.</summary>
public static class WorkerHost
{
    public sealed record ProbeOutcome(WorkerMessage? Info, bool TimedOut, Process? Process);

    static Process Start(params string[] args)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("Yardımcı işlem başlatılamadı.");
    }

    static void TryKill(Process p)
    {
        // Okumada takılı işlem hemen ölmeyebilir; okuma bitince Windows sonlandırır
        try { p.Kill(); } catch { }
    }

    /// <summary>Tek satırlık yanıt bekleyen kısa işler (tanıma, çıkarma).</summary>
    static async Task<ProbeOutcome> RunShortAsync(TimeSpan timeout, params string[] args)
    {
        var p = Start(args);
        var line = p.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(line, Task.Delay(timeout)) != line)
        {
            TryKill(p);
            return new ProbeOutcome(null, true, p);
        }
        var text = await line;
        return new ProbeOutcome(text == null ? null : Worker.Parse(text), false, p);
    }

    public static Task<ProbeOutcome> ProbeAsync(char drive, TimeSpan timeout) => RunShortAsync(timeout, "--probe", drive.ToString());

    public static async Task<bool> EjectAsync(char drive, TimeSpan timeout) =>
        (await RunShortAsync(timeout, "--eject", drive.ToString())).Info?.Ok == true;

    /// <summary>
    /// Kopyalamayı yardımcı işlemde yürütür. İptalde işleme haber verir; 5 sn içinde bitmezse
    /// beklemeyi bırakır (takılı işlem okuma bitince kendiliğinden kapanır ve yarım dosyayı siler).
    /// </summary>
    public static async Task<CopyResult> CopyAsync(char drive, string output, CopyOptions options,
        IProgress<WorkerMessage> progress, CancellationToken ct)
    {
        var p = Start("--copy", drive.ToString(), output, JsonSerializer.Serialize(options));
        using var reg = ct.Register(() =>
        {
            try { p.StandardInput.WriteLine("cancel"); p.StandardInput.Flush(); } catch { }
        });

        var reader = Task.Run(async () =>
        {
            WorkerMessage? final = null;
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                var m = Worker.Parse(line);
                if (m == null) continue;
                if (m.Type is "result" or "error") final = m;
                else progress.Report(m);
            }
            return final;
        });

        try
        {
            await reader.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (await Task.WhenAny(reader, Task.Delay(5000)) != reader)
            {
                TryKill(p);
                throw;
            }
        }

        var result = await reader;
        if (result?.Type == "result")
            return new CopyResult(result.Done, result.Bad, TimeSpan.FromMilliseconds(result.ElapsedMs),
                result.Sha256, result.Mismatches, result.Unreadable);
        if (result?.Cancelled == true || ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        throw new IOException(result?.Message ?? "Okuma işlemi beklenmedik şekilde sonlandı.");
    }
}
