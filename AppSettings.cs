using System.Text.Json;

namespace MetroKafeDVD2ISO;

/// <summary>%AppData%\MetroKafeDVD2ISO\settings.json içinde saklanan kullanıcı ayarları.</summary>
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
    public string NamePreset { get; set; } = "Label";
    public CopyMethod CopyMethod { get; set; } = CopyMethod.Auto;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroKafeDVD2ISO", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
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
