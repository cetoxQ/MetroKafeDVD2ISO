using System.Net;
using System.Text.Json;

namespace MetroKafeDVD2ISO;

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string Notes);

/// <summary>GitHub Releases üzerinden yeni sürüm denetimi.</summary>
public static class UpdateChecker
{
    public const string Owner = "cetoxQ";
    public const string Repo = "MetroKafeDVD2ISO";
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";
    public static string ReleasesUrl => RepoUrl + "/releases";

    public static Version Current => Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>En son yayınlanan sürüm; sürüm yoksa veya depoya erişilemiyorsa null.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Repo}/{Format(Current)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var resp = await http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        Version.TryParse(tag.TrimStart('v', 'V'), out var version);
        return new ReleaseInfo(
            Normalize(version ?? new Version(0, 0, 0)),
            tag,
            root.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesUrl : ReleasesUrl,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }
}
