using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetroKafeDVD2ISO;

/// <summary>Taramada bulunan bir video başlığı (DVD'de bir "title").</summary>
public sealed record HbTitle(int Index, TimeSpan Duration, int Width, int Height, int ParNum, int ParDen,
    double Fps, int Chapters, bool Interlaced)
{
    public string Describe() =>
        $"başlık {Index}: {Duration:h\\:mm\\:ss}" +
        (Width > 0 ? $", {Width}×{Height}, {Fps.ToString("0.###", CultureInfo.InvariantCulture)} fps" : "") +
        (Chapters > 1 ? $", {Chapters} bölüm" : "") + (Interlaced ? ", taramalı" : "");
}

public sealed record HbScan(IReadOnlyList<HbTitle> Titles, int MainFeature);

/// <param name="Encoder">HandBrake kodlayıcı adı: x264, nvenc_h264, qsv_h264, vce_h264.</param>
public sealed record EncodeSettings(int VideoKbps, string Encoder, bool Deinterlace);

public sealed record HbProgress(double Percent, double Fps, TimeSpan? Eta);

/// <summary>
/// HandBrakeCLI ile MP4 dönüştürme. Araç programa gömülü değildir; ilk kullanımda HandBrake'in resmi
/// GitHub sürümlerinden indirilip programın klasöründeki tools\ altına kaydedilir.
/// </summary>
public static class HandBrake
{
    public const int AudioKbps = 160;
    /// <summary>Bundan kısa başlıklar (menü, logo) dönüştürülmez.</summary>
    public static readonly TimeSpan MinTitle = TimeSpan.FromSeconds(60);

    public static string ExePath => Path.Combine(AppPaths.DataDir, "tools", "HandBrakeCLI.exe");
    public static bool IsInstalled => File.Exists(ExePath);

    /// <summary>Video + ses bit hızından yaklaşık MP4 boyutu (kapsayıcı payı %1).</summary>
    public static long EstimateBytes(TimeSpan duration, int videoKbps) =>
        (long)((videoKbps + AudioKbps) * 1000.0 / 8 * duration.TotalSeconds * 1.01);

    // ───────────────────────── Kurulum ─────────────────────────

    public static async Task<string> InstallAsync(IProgress<string> status, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{UpdateChecker.Repo}/{UpdateChecker.Format(UpdateChecker.Current)}");

        status.Report("HandBrake sürüm bilgisi alınıyor…");
        string json = await http.GetStringAsync("https://api.github.com/repos/HandBrake/HandBrake/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        string version = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        string? url = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (Regex.IsMatch(a.GetProperty("name").GetString() ?? "", @"^HandBrakeCLI-[\d.]+-win-x86_64\.zip$"))
                url = a.GetProperty("browser_download_url").GetString();
        if (url == null) throw new IOException("HandBrake sürümünde Windows komut satırı paketi bulunamadı.");

        string dir = Path.GetDirectoryName(ExePath)!;
        Directory.CreateDirectory(dir);
        string zip = Path.Combine(dir, "HandBrakeCLI.download.zip");
        try
        {
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long? length = resp.Content.Headers.ContentLength;
                await using var input = await resp.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(zip);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buf, ct)) > 0)
                {
                    await output.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (length > 0) status.Report($"HandBrake {version} indiriliyor… %{done * 100 / length.Value}");
                }
            }

            status.Report("HandBrake kuruluyor…");
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("HandBrakeCLI.exe", StringComparison.OrdinalIgnoreCase))
                            ?? throw new IOException("İndirilen pakette HandBrakeCLI.exe yok.");
                string tmp = ExePath + ".tmp";
                entry.ExtractToFile(tmp, true);
                File.Move(tmp, ExePath, true);
            }
            File.WriteAllText(Path.Combine(dir, "HandBrakeCLI.version.txt"), version);
            return version;
        }
        finally
        {
            try { File.Delete(zip); } catch { }
        }
    }

    /// <summary>Bu bilgisayarda kullanılabilen H.264 kodlayıcıları (HandBrake yalnız çalışanları listeler).</summary>
    public static async Task<List<string>> GetH264EncodersAsync()
    {
        var (output, _) = await RunToEndAsync(["--help"], CancellationToken.None);
        return ParseH264Encoders(output);
    }

    /// <summary>
    /// "--help" çıktısındaki kodlayıcı listesinden x264 ve donanım H.264 kodlayıcıları (NVIDIA nvenc, Intel qsv,
    /// AMD vce/vcn — HandBrake sürümüne göre adı değişebilir). HandBrake yalnız çalışanları listeler.
    /// </summary>
    public static List<string> ParseH264Encoders(string help)
    {
        var found = Regex.Matches(help, @"^\s+(\S+)\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToList();
        var list = new List<string>();
        if (found.Contains("x264")) list.Add("x264");
        list.AddRange(found.Where(e => Regex.IsMatch(e, @"^(nvenc|qsv|vce|vcn)_h264$")));
        return list;
    }

    /// <summary>Kodlayıcıya uygun donanım kod çözme (videonun açılması da ekran kartında yapılsın).</summary>
    public static string? HardwareDecoderFor(string encoder) => encoder switch
    {
        "nvenc_h264" => "nvdec",
        "qsv_h264" => "qsv",
        _ => null,
    };

    // ───────────────────────── Tarama ─────────────────────────

    public static async Task<HbScan> ScanAsync(string input, CancellationToken ct)
    {
        var (stdout, stderr) = await RunToEndAsync(["--json", "--scan", "-t", "0", "--min-duration", "10", "-i", input], ct);
        string? json = ExtractJsonAfter(stdout, "JSON Title Set:");
        // Video başlığı olmayan kalıplarda (veri ISO'su, Video CD) HandBrake başlık listesi yazmaz
        if (json == null && stderr.Contains("No title found", StringComparison.OrdinalIgnoreCase))
            return new HbScan([], 0);
        if (json == null)
            throw new IOException("HandBrake ISO'yu tarayamadı. " + LastLines(stderr, 3));
        return ParseScan(json);
    }

    public static HbScan ParseScan(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var titles = new List<HbTitle>();
        foreach (var t in root.GetProperty("TitleList").EnumerateArray())
        {
            var d = t.GetProperty("Duration");
            var g = t.GetProperty("Geometry");
            var par = g.GetProperty("PAR");
            var fr = t.GetProperty("FrameRate");
            double den = fr.GetProperty("Den").GetDouble();
            titles.Add(new HbTitle(
                t.GetProperty("Index").GetInt32(),
                TimeSpan.FromSeconds(d.GetProperty("Ticks").GetInt64() / 90000.0),
                g.GetProperty("Width").GetInt32(),
                g.GetProperty("Height").GetInt32(),
                par.GetProperty("Num").GetInt32(),
                par.GetProperty("Den").GetInt32(),
                den > 0 ? fr.GetProperty("Num").GetDouble() / den : 0,
                t.TryGetProperty("ChapterList", out var ch) ? ch.GetArrayLength() : 0,
                t.TryGetProperty("InterlaceDetected", out var il) && il.GetBoolean()));
        }
        int main = root.TryGetProperty("MainFeature", out var mf) ? mf.GetInt32() : 0;
        return new HbScan(titles, main);
    }

    /// <summary>
    /// Dönüştürülecek başlıklar: ana başlık (DVD'nin bildirdiği ya da en uzun) + 1 dakikadan uzun diğerleri.
    /// Ana başlıkla aynı süredeki kopyalar ve birbirinin kopyası olan başlıklar atlanır. Ana başlık diğerlerinin
    /// toplamına eşitse ("hepsini oynat") yalnız ana başlık alınır; böylece hiçbir video iki kez çıkmaz.
    /// </summary>
    public static (List<HbTitle> Titles, string Reason) PickTitles(HbScan scan)
    {
        if (scan.Titles.Count == 0) return ([], "Dönüştürülecek video başlığı bulunamadı.");
        var candidates = scan.Titles.Where(t => t.Duration >= MinTitle).ToList();
        // Hepsi 1 dakikadan kısaysa (menü videoları ya da çok kısa kayıt) yalnız en uzunu al
        if (candidates.Count == 0)
            return ([scan.Titles.OrderByDescending(t => t.Duration).First()], "Tüm başlıklar 1 dakikadan kısa; yalnız en uzunu dönüştürülecek.");

        var main = candidates.FirstOrDefault(t => t.Index == scan.MainFeature)
                   ?? candidates.OrderByDescending(t => t.Duration).First();
        static bool Same(TimeSpan a, TimeSpan b) => Math.Abs((a - b).TotalSeconds) <= 1;

        var others = new List<HbTitle>();
        foreach (var t in candidates.Where(t => t != main).OrderBy(t => t.Index))
        {
            if (Same(t.Duration, main.Duration)) continue;
            if (others.Any(o => Same(o.Duration, t.Duration) && o.Chapters == t.Chapters)) continue;
            others.Add(t);
        }

        if (others.Count >= 2)
        {
            double sum = others.Sum(o => o.Duration.TotalSeconds);
            double tolerance = Math.Max(5, main.Duration.TotalSeconds * 0.03);
            if (Math.Abs(sum - main.Duration.TotalSeconds) <= tolerance)
                return ([main], $"Ana başlık diğer {others.Count} başlığın tamamını içeriyor (\"hepsini oynat\"); yalnız ana başlık dönüştürülecek.");
        }

        var picked = new List<HbTitle> { main };
        picked.AddRange(others);
        string reason = picked.Count == 1
            ? "Tek video başlığı."
            : $"Ana başlık {main.Index} + {others.Count} ayrı başlık ({string.Join(", ", others.Select(o => o.Index))}).";
        return (picked, reason);
    }

    // ───────────────────────── Dönüştürme ─────────────────────────

    static readonly Regex ProgressRx = new(
        @"Encoding: task \d+ of \d+, ([\d.]+) %(?: \(([\d.]+) fps, avg [\d.]+ fps, ETA (\d+)h(\d+)m(\d+)s\))?",
        RegexOptions.Compiled);

    public static List<string> BuildArgs(string input, int title, string output, EncodeSettings s)
    {
        var args = new List<string>
        {
            "-i", input, "-t", title.ToString(CultureInfo.InvariantCulture), "-o", output,
            "--format", "av_mp4", "--markers",
            "-e", s.Encoder, "-b", s.VideoKbps.ToString(CultureInfo.InvariantCulture),
            "--no-multi-pass",                     // çok geçişli kodlama ve turbo analiz kapalı
            "--cfr",                               // -r verilmediği için kare hızı kaynakla aynı, sabit
            "--crop-mode", "none", "--auto-anamorphic", // kırpma/ölçekleme yok: çözünürlük ve en-boy kaynakla aynı
            "-E", "av_aac", "-B", AudioKbps.ToString(CultureInfo.InvariantCulture), "--mixdown", "stereo",
        };
        if (s.Deinterlace) args.AddRange(["--comb-detect", "--decomb"]);
        if (HardwareDecoderFor(s.Encoder) is { } hw) args.AddRange(["--enable-hw-decoding", hw]);
        return args;
    }

    /// <summary>Bir başlığı MP4'e dönüştürür. İptalde HandBrake kapatılır ve yarım dosya silinir.</summary>
    /// <param name="info">HandBrake günlüğünden önemli satırlar (ör. gerçekte kullanılan kodlayıcı) için.</param>
    public static async Task EncodeAsync(string input, int title, string output, EncodeSettings settings,
        IProgress<HbProgress> progress, CancellationToken ct, Action<string>? info = null)
    {
        var psi = NewStartInfo(BuildArgs(input, title, output, settings));
        using var p = Process.Start(psi) ?? throw new IOException("HandBrake başlatılamadı.");
        var errTail = new Queue<string>();
        var errTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync()) != null)
            {
                lock (errTail) { errTail.Enqueue(line); if (errTail.Count > 40) errTail.Dequeue(); }
                // Ör. "+ encoder: H.264 (libx264)", "+ encoder: H.264 (Intel Media SDK)", "+ decoder: mpeg2video (qsv)"
                var m = Regex.Match(line, @"\+ (encoder|decoder): (.+)$");
                if (m.Success) info?.Invoke($"HandBrake {(m.Groups[1].Value == "encoder" ? "kodlayıcı" : "kod çözücü")}: {m.Groups[2].Value.Trim()}");
            }
        });
        var outTask = Task.Run(async () =>
        {
            // İlerleme satırları \r ile ayrılır
            var sb = new StringBuilder();
            var buf = new char[256];
            int n;
            while ((n = await p.StandardOutput.ReadAsync(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] is '\r' or '\n') { Report(sb.ToString()); sb.Clear(); }
                    else sb.Append(buf[i]);
                }
            Report(sb.ToString());
        });

        void Report(string line)
        {
            var m = ProgressRx.Match(line);
            if (!m.Success) return;
            double pct = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double fps = m.Groups[2].Success ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            TimeSpan? eta = m.Groups[3].Success
                ? new TimeSpan(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value))
                : null;
            progress.Report(new HbProgress(pct, fps, eta));
        }

        try
        {
            await p.WaitForExitAsync(ct);
            await Task.WhenAll(outTask, errTask);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            try { p.WaitForExit(10000); } catch { }
            TryDelete(output);
            throw;
        }

        if (p.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
        {
            TryDelete(output);
            string tail;
            lock (errTail) tail = string.Join(" | ", errTail.Where(l => Regex.IsMatch(l, "error|fail|cannot|unable|not available", RegexOptions.IgnoreCase)).TakeLast(3));
            throw new IOException($"HandBrake dönüştürmeyi tamamlayamadı (çıkış kodu {p.ExitCode})." +
                                  (tail.Length > 0 ? " " + tail : "") +
                                  (settings.Encoder != "x264" ? " Ekran kartı kodlayıcısı yerine \"İşlemci (x264)\" seçip tekrar deneyin." : ""));
        }
    }

    // ───────────────────────── Yardımcılar ─────────────────────────

    static ProcessStartInfo NewStartInfo(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>Çıktı ve hata akışlarını aynı anda okur (biri dolup diğerini kilitlemesin).</summary>
    static async Task<(string Out, string Err)> RunToEndAsync(string[] args, CancellationToken ct)
    {
        using var p = Process.Start(NewStartInfo(args)) ?? throw new IOException("HandBrake başlatılamadı.");
        var o = p.StandardOutput.ReadToEndAsync(ct);
        var e = p.StandardError.ReadToEndAsync(ct);
        try
        {
            await p.WaitForExitAsync(ct);
            return (await o, await e);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw;
        }
    }

    /// <summary>"Marker: { ... }" biçimindeki JSON nesnesini, iç içe parantezleri sayarak çıkarır.</summary>
    public static string? ExtractJsonAfter(string text, string marker)
    {
        int i = text.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        int start = text.IndexOf('{', i);
        if (start < 0) return null;
        int depth = 0;
        bool inString = false, escape = false;
        for (int k = start; k < text.Length; k++)
        {
            char c = text[k];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return text[start..(k + 1)];
        }
        return null;
    }

    static string LastLines(string text, int count) =>
        string.Join(" | ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));

    static void TryDelete(string path) { try { File.Delete(path); } catch { } }
}
