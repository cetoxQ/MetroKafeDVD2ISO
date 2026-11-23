using System.Globalization;

namespace MetroKafeDVD2ISO;

/// <summary>
/// Arayüz dili. Anahtar Türkçe metnin kendisidir; İngilizce karşılıklar Strings.En.*.cs dosyalarındaki
/// sözlüklerde durur. Karşılığı olmayan metin Türkçe gösterilir (DEBUG'da günlüğe yazılır).
/// Biçimli metinlerde yer tutucular string.Format sözdizimiyle yazılır: L.F("{0} dosya", n).
/// </summary>
static partial class L
{
    public const string Turkish = "tr", English = "en";

    /// <summary>Alt işlemler (Worker) dili bu ortam değişkeninden alır.</summary>
    public const string EnvVar = "MK_LANG";

    public static string Lang { get; private set; } = Turkish;
    public static bool En => Lang == English;

    /// <summary>Kayıtlı seçim yoksa Windows dili: Türkçe ise Türkçe, değilse İngilizce.</summary>
    public static string Detect() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == Turkish ||
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Turkish ? Turkish : English;

    public static void Init(string? lang)
    {
        Lang = lang == English || lang == Turkish ? lang : Detect();
        // Sayı ve tarih biçimleri de dile uysun (1,8 GB / 1.8 GB)
        var culture = CultureInfo.GetCultureInfo(En ? "en-US" : "tr-TR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        Environment.SetEnvironmentVariable(EnvVar, Lang);
    }

    /// <summary>Türkçe metnin geçerli dildeki karşılığı.</summary>
    public static string T(string tr)
    {
        if (!En) return tr;
        if (en.Value.TryGetValue(tr, out var s)) return s;
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[L] çevirisi yok: {tr}");
#endif
        return tr;
    }

    /// <summary>Biçimli metin: L.F("{0} sektör okunamadı", n).</summary>
    public static string F(string tr, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(tr), args);

    static readonly Lazy<Dictionary<string, string>> en = new(() =>
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in new[] { EnCore, EnMain, EnMp4, EnEngine })
            foreach (var (tr, enText) in part) d[tr] = enText;
        return d;
    });

    /// <summary>Denetim için: tüm İngilizce sözlük girdileri.</summary>
    internal static IReadOnlyDictionary<string, string> EnglishTable => en.Value;

    // Dil seçicideki adlar her dilde kendi adıyla gösterilir (Türkçe / English); ortak girdiler buraya
    static readonly (string Tr, string En)[] EnCore = [];
}
