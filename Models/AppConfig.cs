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
    public bool IsIconHidden { get; set; }
}
