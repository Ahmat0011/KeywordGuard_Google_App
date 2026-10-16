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
/// Hinweis: Alle installierten Webbrowser werden nun automatisch überwacht.
/// TargetApps wird für zusätzliche überwachte Apps genutzt (z. B. YouTube, TikTok).
/// </summary>
public class AppConfig
{
    public DateTime? EndTime { get; set; }
    public List<BlockedKeyword> Keywords { get; set; } = new();
    public List<string> TargetApps { get; set; } = new() 
    { 
        "com.google.android.youtube",
        "com.zhiliaoapp.musically"
    };
    public bool IsIconHidden { get; set; }
}
