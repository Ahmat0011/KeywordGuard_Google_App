using System.Text.Json;
using LanguageData.Models;

namespace LanguageData.Services;

/// <summary>
/// Zentrale Konfigurationsverwaltung.
/// Triple-Storage: AES-256 (UI) + JSON-Sync + SharedPreferences (Service).
/// </summary>
public static class ConfigService
{
    private const string JSON_FILE = "langdata_config.json";
    private const string LOG_TAG = "LDService";
    private const string PREFS_NAME = "langdata_prefs";
    private const string PREF_TIMER_END = "timer_end_ms";
    private const string PREF_KEYWORDS_JSON = "keywords_json";
    private const string PREF_TARGETAPPS_JSON = "targetapps_json";
    private const string PREF_ICON_HIDDEN = "icon_hidden";

    private static bool _openedViaDeepLink;

    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions _jsonWriteOptions = new()
    {
        WriteIndented = false
    };

    // ═══════════════════════════════════════════════════════════
    // Deep Link Flag
    // ═══════════════════════════════════════════════════════════

    public static void SetDeepLinkOpened(bool value) => _openedViaDeepLink = value;
    public static bool WasOpenedViaDeepLink() => _openedViaDeepLink;

    // ═══════════════════════════════════════════════════════════
    // Config Pfad
    // ═══════════════════════════════════════════════════════════

    public static string GetJsonPath()
    {
#if ANDROID
        try
        {
            var filesDir = Android.App.Application.Context.FilesDir;
            if (filesDir != null)
                return Path.Combine(filesDir.AbsolutePath, JSON_FILE);
        }
        catch { }
#endif
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(appData, JSON_FILE);
    }

    // ═══════════════════════════════════════════════════════════
    // Speichern (Verschluesselt + JSON + SharedPreferences)
    // ═══════════════════════════════════════════════════════════

    public static void SaveConfig(AppConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, _jsonWriteOptions);

            // 1. Verschluesselt speichern
            CryptoHelper.SaveEncrypted(json);

            // 2. JSON-Sync
            string jsonPath = GetJsonPath();
            File.WriteAllText(jsonPath, json);

            // 3. SharedPreferences (fuer AccessibilityService - zuverlaessigster Weg)
#if ANDROID
            try
            {
                var prefs = Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, Android.Content.FileCreationMode.Private);
                var editor = prefs?.Edit();
                if (editor != null)
                {
                    // Timer als Unix-Millisekunden speichern (kein DateTime-Serialisierungsproblem!)
                    if (config.EndTime.HasValue)
                    {
                        long endMs = new DateTimeOffset(config.EndTime.Value).ToUnixTimeMilliseconds();
                        editor.PutLong(PREF_TIMER_END, endMs);
                    }
                    else
                    {
                        editor.PutLong(PREF_TIMER_END, 0);
                    }

                    // Keywords als JSON-Array (nur die Liste, nicht die ganze Config)
                    string kwJson = JsonSerializer.Serialize(config.Keywords ?? new List<BlockedKeyword>(), _jsonWriteOptions);
                    editor.PutString(PREF_KEYWORDS_JSON, kwJson);

                    // TargetApps als JSON-Array
                    string taJson = JsonSerializer.Serialize(config.TargetApps ?? new List<string>(), _jsonWriteOptions);
                    editor.PutString(PREF_TARGETAPPS_JSON, taJson);

                    // Icon-Hidden Status persistent speichern
                    editor.PutBoolean(PREF_ICON_HIDDEN, config.IsIconHidden);

                    editor.Apply(); // Async, sofort verfuegbar im gleichen Prozess
                }

                Android.Util.Log.Debug(LOG_TAG,
                    $"Config saved: path={jsonPath}, KW={config.Keywords?.Count ?? 0}, End={config.EndTime}, prefs=OK");
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error(LOG_TAG, $"SharedPrefs save error: {ex.Message}");
            }
#endif
        }
        catch (Exception ex)
        {
#if ANDROID
            Android.Util.Log.Error(LOG_TAG, $"Config save error: {ex.Message}");
#endif
        }
    }

    // ═══════════════════════════════════════════════════════════
    // Laden (Verschluesselt, Fallback auf JSON)
    // ═══════════════════════════════════════════════════════════

    public static AppConfig LoadConfig()
    {
        try
        {
            string? encrypted = CryptoHelper.LoadEncrypted();
            if (encrypted != null)
            {
                var config = JsonSerializer.Deserialize<AppConfig>(encrypted, _jsonReadOptions);
                if (config != null) return config;
            }

            string jsonPath = GetJsonPath();
            if (File.Exists(jsonPath))
            {
                string json = File.ReadAllText(jsonPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonReadOptions);
                if (config != null) return config;
            }
        }
        catch { }

        return new AppConfig();
    }

    // ═══════════════════════════════════════════════════════════
    // Timer-Check (nutzt SharedPreferences fuer Zuverlaessigkeit)
    // ═══════════════════════════════════════════════════════════

    public static bool IsTimerActive()
    {
#if ANDROID
        try
        {
            var prefs = Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, Android.Content.FileCreationMode.Private);
            if (prefs != null)
            {
                long endMs = prefs.GetLong(PREF_TIMER_END, 0);
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (endMs > 0 && nowMs < endMs) return true;
            }
        }
        catch { }
#endif
        // Fallback auf JSON/Encrypted
        try
        {
            var config = LoadConfig();
            return config.EndTime != null && DateTime.Now < config.EndTime.Value;
        }
        catch { }

        return false;
    }

    // ═══════════════════════════════════════════════════════════
    // Icon-Hidden Status (persistent ueber SharedPreferences)
    // ═══════════════════════════════════════════════════════════

    public static bool IsIconHidden()
    {
#if ANDROID
        try
        {
            var prefs = Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, Android.Content.FileCreationMode.Private);
            return prefs?.GetBoolean(PREF_ICON_HIDDEN, false) ?? false;
        }
        catch { }
#endif
        return false;
    }

    public static void SetIconHidden(bool hidden)
    {
#if ANDROID
        try
        {
            var prefs = Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, Android.Content.FileCreationMode.Private);
            var editor = prefs?.Edit();
            editor?.PutBoolean(PREF_ICON_HIDDEN, hidden);
            editor?.Apply();
        }
        catch { }
#endif
    }
}
