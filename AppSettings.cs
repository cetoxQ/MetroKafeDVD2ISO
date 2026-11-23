using System.Text.Json;

namespace MetroKafeDVD2ISO;

/// <summary>
/// Ayar ve günlüklerin yeri: programın bulunduğu klasör (taşınabilir kullanım).
/// Klasör yazılabilir değilse (ör. Program Files) %AppData%\MetroKafeDVD2ISO kullanılır.
/// </summary>
public static class AppPaths
{
    static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroKafeDVD2ISO");

    static readonly Lazy<string> dataDir = new(() =>
    {
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null && IsWritable(exeDir)) return exeDir;
        Directory.CreateDirectory(AppDataDir);
        return AppDataDir;
    });

    public static string DataDir => dataDir.Value;
    public static string SettingsFile => Path.Combine(DataDir, "MetroKafeDVD2ISO.settings.json");
    public static string LogsDir => Path.Combine(DataDir, "logs");
    /// <summary>Önceki sürümlerin ayar dosyası (bir kez taşınır).</summary>
    public static string LegacySettingsFile => Path.Combine(AppDataDir, "settings.json");

    static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".yazma-denemesi-{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }
}

/// <summary>Programın klasöründeki MetroKafeDVD2ISO.settings.json içinde saklanan kullanıcı ayarları.</summary>
public sealed class AppSettings
{
    public bool SkipBadSectors { get; set; } = true;
    public bool StallTimeoutEnabled { get; set; } = true;
    public int StallTimeoutSeconds { get; set; } = 15;
    public bool TwoPass { get; set; } = true;
    public bool SlowDownOnErrors { get; set; } = true;
    public bool Verify { get; set; }
    public bool EjectWhenDone { get; set; }
    public bool OpenFolderWhenDone { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>"tr" / "en"; boşsa Windows diline göre seçilir.</summary>
    public string Language { get; set; } = "";
    public string NamePreset { get; set; } = "Label";
    public CopyMethod CopyMethod { get; set; } = CopyMethod.Auto;

    // MP4 dönüştürme
    /// <summary>Varsayılan video bit hızı: MP4 sekmesi her açılışta bununla başlar.</summary>
    public int Mp4VideoKbps { get; set; } = 2500;
    /// <summary>MP4 sekmesindeki hazır bit hızı düğmeleri.</summary>
    public int[] Mp4Presets { get; set; } = [1500, 2500, 4000, 6000];
    public string Mp4Encoder { get; set; } = "x264";
    public bool Mp4Deinterlace { get; set; } = true;
    public bool Mp4NextToIso { get; set; } = true;
    public string Mp4Folder { get; set; } = "";
    public bool AutoMp4AfterIso { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();

            // Önceki sürümlerde ayarlar %AppData%'daydı: bir kez program klasörüne taşı
            if (File.Exists(AppPaths.LegacySettingsFile))
            {
                var old = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.LegacySettingsFile)) ?? new AppSettings();
                old.Save();
                return old;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public CopyOptions ToCopyOptions() => new()
    {
        Method = CopyMethod,
        SkipBadSectors = SkipBadSectors,
        StallTimeoutSeconds = StallTimeoutEnabled ? StallTimeoutSeconds : 0,
        TwoPass = TwoPass,
        SlowDownOnErrors = SlowDownOnErrors,
        Verify = Verify,
    };
}
