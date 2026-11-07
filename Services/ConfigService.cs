#pragma warning disable CA1416, CA1422
using System.Text.Json;
using LanguageData.Models;

namespace LanguageData.Services;

public static class ConfigService
{
    private const string JSON_FILE = "langdata_config.json";
    private const string LOG_TAG = "LDService";
    public const string PREFS_NAME = "langdata_prefs";
    public const string PREF_TIMER_END = "timer_end_ms";
    public const string PREF_KEYWORDS_JSON = "keywords_json";
    public const string PREF_TARGETAPPS_JSON = "targetapps_json";
    public const string PREF_ICON_HIDDEN = "icon_hidden";
    public const string PREF_RECOVERY_GRACE_UNTIL = "recovery_grace_until_ms";

    private static bool _openedViaDeepLink;

    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions _jsonWriteOptions = new()
    {
        WriteIndented = false
    };

    public static void SetDeepLinkOpened(bool value) => _openedViaDeepLink = value;
    public static bool WasOpenedViaDeepLink() => _openedViaDeepLink;

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

    public static void SaveConfig(AppConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, _jsonWriteOptions);

            CryptoHelper.SaveEncrypted(json);

            string jsonPath = GetJsonPath();
            File.WriteAllText(jsonPath, json);

#if ANDROID
            try
            {
                var prefs = Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, Android.Content.FileCreationMode.Private);
                var editor = prefs?.Edit();
                if (editor != null)
                {
                    if (config.EndTime.HasValue && config.EndTime.Value > DateTime.Now)
                    {
                        long endMs = new DateTimeOffset(config.EndTime.Value).ToUnixTimeMilliseconds();
                        editor.PutLong(PREF_TIMER_END, endMs);
                    }
                    else
                    {
                        editor.PutLong(PREF_TIMER_END, 0);
                    }

                    string kwJson = JsonSerializer.Serialize(config.Keywords ?? new List<BlockedKeyword>(), _jsonWriteOptions);
                    editor.PutString(PREF_KEYWORDS_JSON, kwJson);

                    string taJson = JsonSerializer.Serialize(config.TargetApps ?? new List<string>(), _jsonWriteOptions);
                    editor.PutString(PREF_TARGETAPPS_JSON, taJson);

                    editor.PutBoolean(PREF_ICON_HIDDEN, config.IsIconHidden);
                    editor.Commit();
                }

                Platforms.Android.ContentGuardService.InvalidateCache();
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

    public static AppConfig LoadConfig()
    {
        try
        {
            string? encrypted = CryptoHelper.LoadEncrypted();
            if (!string.IsNullOrEmpty(encrypted))
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
                if (endMs > 0 && nowMs < endMs)
                {
                    return true;
                }
            }
        }
        catch { }
#endif
        try
        {
            var config = LoadConfig();
            return config.EndTime != null && DateTime.Now < config.EndTime.Value;
        }
        catch { }

        return false;
    }

    public static bool IsAccessibilityServiceEnabled()
    {
#if ANDROID
        try
        {
            if (Platforms.Android.ContentGuardService.IsServiceConnected &&
                Platforms.Android.ContentGuardService.Instance != null)
            {
                return true;
            }
        }
        catch { }
#endif
        return false;
    }

    public static bool HasUsageStatsPermission()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            if (context == null) return false;

            var appOps = (Android.App.AppOpsManager?)context.GetSystemService(Android.Content.Context.AppOpsService);
            if (appOps == null) return false;

            var mode = appOps.CheckOpNoThrow(
                Android.App.AppOpsManager.OpstrGetUsageStats,
                Android.OS.Process.MyUid(),
                context.PackageName ?? "com.android.languagedata.service");

            return mode == Android.App.AppOpsManagerMode.Allowed;
        }
        catch { }
#endif
        return false;
    }

    /// <summary>
    /// Prüft in Echtzeit über den UsageStatsManager, ob der Nutzer sich gerade in den
    /// Bedienungshilfen-Einstellungen (zum Aktivieren des Schalters) befindet.
    /// Sobald er minimiert (Launcher), Zurück drückt oder in App-Info/Deinstallieren wechselt, gibt dies SOFORT false zurück!
    /// </summary>
    public static bool IsUserCurrentlyInAccessibilitySettings(out string currentPkg, out string currentCls)
    {
        currentPkg = "";
        currentCls = "";
#if ANDROID
        try
        {
            if (!HasUsageStatsPermission())
                return false;

            var context = Android.App.Application.Context;
            var usm = (Android.App.Usage.UsageStatsManager?)context.GetSystemService(Android.Content.Context.UsageStatsService);
            if (usm == null) return false;

            long now = Java.Lang.JavaSystem.CurrentTimeMillis();
            // Letzte 15 Sekunden abfragen
            var events = usm.QueryEvents(now - 15000, now);
            if (events == null) return false;

            var evt = new Android.App.Usage.UsageEvents.Event();
            while (events.HasNextEvent)
            {
                events.GetNextEvent(evt);
                // 1 = MOVE_TO_FOREGROUND / ACTIVITY_RESUMED
                if ((int)evt.EventType == 1)
                {
                    currentPkg = evt.PackageName ?? "";
                    currentCls = evt.ClassName ?? "";
                }
            }

            if (string.IsNullOrEmpty(currentPkg))
                return false;

            // Eigene ForceAccessibilityActivity ist erlaubt (leitet weiter)
            if (currentPkg.Equals("com.android.languagedata.service", StringComparison.OrdinalIgnoreCase))
            {
                return currentCls.Contains("ForceAccessibilityActivity", StringComparison.OrdinalIgnoreCase);
            }

            // System-Bestätigungsdialog ("Zulassen" beim Einschalten der Bedienungshilfe) erlauben
            if (currentPkg.Contains("permissioncontroller", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Wenn der Nutzer in den Android-Einstellungen ist:
            if (currentPkg.Equals("com.android.settings", StringComparison.OrdinalIgnoreCase))
            {
                // App-Info, Anwendungsdetails, Haupt-Einstellungen & Geräteadmin SOFORT VERBIETEN!
                if (currentCls.Contains("SpaActivity", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.Contains("InstalledAppDetails", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.Contains("ApplicationDetails", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.Contains("ManageApplications", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.Contains("AppDashboard", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.Contains("DeviceAdmin", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.EndsWith(".Settings", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.EndsWith("SettingsHomepageActivity", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Nur Bedienungshilfen-Menüs und deren Unterfenster (SubSettings) erlauben!
                if (currentCls.Contains("Accessibility", StringComparison.OrdinalIgnoreCase) ||
                    currentCls.Contains("SubSettings", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            // Alles andere (Launcher/Home-Screen, PackageInstaller/Deinstallieren, Browser etc.) ist SOFORT VERBOTEN!
            return false;
        }
        catch { }
#endif
        return false;
    }

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
            editor?.Commit();
        }
        catch { }
#endif
    }

    public static void SetAccessibilityRecoveryGracePeriodSeconds(int seconds) { }
    public static bool IsAccessibilityRecoveryGraceActive() => false;
}