using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace MetroKafeDVD2ISO;

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string Notes, string? DownloadUrl);

/// <summary>GitHub Releases üzerinden yeni sürüm denetimi ve kurulumu.</summary>
public static class UpdateChecker
{
    public const string Owner = "cetoxQ";
    public const string Repo = "MetroKafeDVD2ISO";
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";
    public static string ReleasesUrl => RepoUrl + "/releases";

    public static Version Current => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repo}/{Format(Current)}");
        return http;
    }

    /// <summary>En son yayınlanan sürüm; sürüm yoksa veya depoya erişilemiyorsa null.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var http = CreateClient(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        Version.TryParse(tag.TrimStart('v', 'V'), out var version);

        string? download = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if (a.GetProperty("name").GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                {
                    download = a.GetProperty("browser_download_url").GetString();
                    break;
                }

        return new ReleaseInfo(
            Normalize(version ?? new Version(0, 0, 0)),
            tag,
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesUrl : ReleasesUrl,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            download);
    }

    /// <summary>
    /// Yeni exe'yi mevcut exe'nin yanına indirir ve uygulama kapanınca onu yerine koyup yeniden başlatan
    /// küçük bir betik çalıştırır. Dönüşte uygulamanın kapanması gerekir.
    /// </summary>
    public static async Task DownloadAndInstallAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken ct = default)
    {
        if (release.DownloadUrl == null) throw new InvalidOperationException("Bu sürümde indirilebilir exe yok.");
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        string dir = Path.GetDirectoryName(exe)!;
        string newExe = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + ".new.exe");

        try
        {
            using var http = CreateClient(TimeSpan.FromMinutes(10));
            using var resp = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long? length = resp.Content.Headers.ContentLength;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(newExe, FileMode.Create, FileAccess.Write);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await input.ReadAsync(buf, ct)) > 0)
            {
                await output.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (length > 0) progress.Report(done * 100.0 / length.Value);
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException($"\"{dir}\" klasörüne yazılamıyor. Uygulamayı yazılabilir bir klasöre (ör. Masaüstü) taşıyıp tekrar deneyin.");
        }
        catch
        {
            try { File.Delete(newExe); } catch { }
            throw;
        }

        int pid = Environment.ProcessId;
        string script = Path.Combine(Path.GetTempPath(), $"MetroKafeDVD2ISO_update_{pid}.cmd");
        File.WriteAllText(script, $"""
            @echo off
            chcp 65001 >nul
            :wait
            tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)
            move /y "{newExe}" "{exe}" >nul
            start "" "{exe}"
            del "%~f0"
            """, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
