namespace LanguageData.Models;

/// <summary>
/// Einzelnes Keyword mit Aggressiv-Flag.
/// Wird von UI, Service und Speicher gemeinsam genutzt (kein Konvertieren nötig).
/// </summary>
public class BlockedKeyword
{
    public string Value { get; set; } = "";
    public bool IsAggressive { get; set; }
}

/// <summary>
/// Zentrale App-Konfiguration.
/// Ein einziges Modell für UI, verschlüsselte Speicherung UND AccessibilityService.
/// </summary>
public class AppConfig
{
    public DateTime? EndTime { get; set; }
    public List<BlockedKeyword> Keywords { get; set; } = new();
    public List<string> TargetApps { get; set; } = new() 
    { 
        "com.android.chrome", 
        "org.mozilla.firefox", 
        "com.zhiliaoapp.musically", 
        "com.google.android.youtube",
        "com.microsoft.emmx",
        "com.opera.browser",
        "com.brave.browser",
        "com.duckduckgo.mobile.android",
        "com.kiwibrowser.browser",
        "com.yandex.browser"
    };
    public bool IsIconHidden { get; set; }
}
