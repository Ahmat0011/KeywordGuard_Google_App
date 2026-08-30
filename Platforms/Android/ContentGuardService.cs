using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using LanguageData.Models;
using LanguageData.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LanguageData.Platforms.Android;

[Service(
    Label = "Language Data Service",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE",
    Exported = true)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/content_guard_service")]
public class ContentGuardService : AccessibilityService
{
    private const string OWN_PACKAGE = "com.android.languagedata.service";
    private const string LOG_TAG = "LDService";
    private const string PREFS_NAME = "langdata_prefs";
    private const string PREF_TIMER_END = "timer_end_ms";
    private const string PREF_KEYWORDS_JSON = "keywords_json";
    private const string PREF_TARGETAPPS_JSON = "targetapps_json";

    public static bool IsServiceConnected { get; private set; }
    public static int TotalEventsReceived { get; private set; }
    public static int KeywordBlockCount { get; private set; }
    public static string LastEventPackage { get; private set; } = "";
    public static string LastBlockInfo { get; private set; } = "";
    public static string DiagnosticLog { get; private set; } = "";

    private static void AddDiag(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        DiagnosticLog = line + "\n" + (DiagnosticLog.Length > 2000 ? DiagnosticLog[..2000] : DiagnosticLog);
        global::Android.Util.Log.Debug(LOG_TAG, msg);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] OwnAppIdentifiers =
    {
        "Language Data",
        "com.android.languagedata.service",
        "languagedata",
        "LanguageData"
    };

    private static readonly string[] ProtectedClassNames =
    {
        "InstalledAppDetails",
        "AppInfoDashboardFragment",
        "InstalledAppDetailsTop",
        "AppInfoActivity",
        "ApplicationDetailsActivity",
        "AppButtonsPreferenceController",
        "AppStorageSettings",
        "DeviceAdminAdd",
        "ToggleAccessibilityServicePreference",
        "UninstallAppProgress",
        "UninstallerActivity",
        "PackageInstallerActivity",
        "UninstallUninstalling",
        "UninstallAlertDialogFragment",
        "ForceStopDialogFragment"
    };

    private static readonly string[] DangerousActionTexts =
    {
        "deinstallieren", "uninstall",
        "deaktivieren", "disable",
        "beenden erzwingen", "force stop", "force-stop", "stoppen erzwingen",
        "updates deinstallieren", "uninstall updates",
        "app deinstallieren", "app entfernen",
        "daten loeschen", "clear data", "speicher loeschen",
        "cache loeschen", "clear cache"
    };

    // Spezifische Signalwörter zur Blockierung des Pixel-Reparaturmodus
    private static readonly string[] RepairModeKeywords =
    {
        "reparaturmodus",
        "repair mode",
        "in den reparaturmodus wechseln",
        "reparaturmodus beenden"
    };

    // Spezifische Signalwörter zur Blockierung von Profil-/Nutzerwechsel & Gastkonten
    private static readonly string[] UserManagementKeywords =
    {
        "nutzerwechsel zulassen",
        "nutzer hinzufügen",
        "gast hinzufügen",
        "aktivitäten von gästen löschen",
        "nutzer vom sperrbildschirm aus hinzufügen",
        "multiple users",
        "allow multiple users",
        "add user",
        "add guest"
    };

    private static readonly string[] KeyboardPackages =
    {
        "inputmethod",
        "com.google.android.inputmethod",
        "com.samsung.android.honeyboard",
        "com.swiftkey.inputmethod",
        "com.microsoft.swiftkey",
        "com.touchtype.swiftkey",
        "com.sec.android.inputmethod",
        "com.anysoftkeyboard",
        "com.menny.android",
        "keyboard"
    };

    private readonly Dictionary<string, string> _lastTextBySource = new();
    private long _lastContentCheckMs = 0;
    private string _lastContentCheckPkg = "";
    private const long CONTENT_CHECK_INTERVAL_MS = 2000;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        IsServiceConnected = true;

        CancelRecoveryAlarm();

        try
        {
            var prefs = global::Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
            long timerEndMs = prefs?.GetLong(PREF_TIMER_END, 0) ?? 0;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (timerEndMs > 0 && nowMs < timerEndMs)
            {
                ScheduleRecoveryAlarm();
            }
        }
        catch { }

        var info = ServiceInfo;
        if (info != null)
        {
            string pkgs = info.PackageNames != null ? string.Join(",", info.PackageNames) : "null (=ALL)";
            AddDiag($"Service CONNECTED. Events={info.EventTypes}, Flags={info.Flags}, Pkgs={pkgs}");
        }

        try
        {
            var handler = new Handler(Looper.MainLooper!);
            System.Action checkLoop = null!;
            checkLoop = () =>
            {
                if (!ConfigService.IsTimerActive())
                {
                    try
                    {
                        MainActivity.SetAppIconVisible(true);
                        AddDiag("Wächter: Timer abgelaufen, Icon wieder sichtbar gemacht.");
                    }
                    catch { }
                    CancelRecoveryAlarm();
                }
                else
                {
                    handler.PostDelayed(checkLoop, 30000);
                }
            };
            handler.PostDelayed(checkLoop, 30000);
        }
        catch { }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        IsServiceConnected = false;
        AddDiag("Service DESTROYED");
        try
        {
            var prefs = global::Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
            long timerEndMs = prefs?.GetLong(PREF_TIMER_END, 0) ?? 0;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (timerEndMs > 0 && nowMs < timerEndMs)
            {
                ScheduleRecoveryAlarm();
            }
        }
        catch { }
    }

#pragma warning disable CA1416
    public static void ScheduleRecoveryAlarm()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var am = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (am == null) return;

            var intent = new Intent(context, typeof(ServiceRecoveryReceiver));

            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;

            var pi = PendingIntent.GetBroadcast(context, 9999, intent, flags);
            if (pi == null) return;

            long triggerMs = Java.Lang.JavaSystem.CurrentTimeMillis() + 5000;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                am.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, pi);
            else
                am.Set(AlarmType.RtcWakeup, triggerMs, pi);

            AddDiag("Recovery alarm scheduled.");
        }
        catch (Exception ex)
        {
            AddDiag($"Recovery alarm error: {ex.Message}");
        }
    }

    public static void CancelRecoveryAlarm()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var am = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (am == null) return;

            var intent = new Intent(context, typeof(ServiceRecoveryReceiver));

            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                flags |= PendingIntentFlags.Immutable;

            var pi = PendingIntent.GetBroadcast(context, 9999, intent, flags);
            if (pi != null)
            {
                am.Cancel(pi);
                AddDiag("Recovery alarm cancelled.");
            }
        }
        catch { }
    }
#pragma warning restore CA1416

    private bool IsTargetApp(string pkg, List<string>? userTargetApps)
    {
        if (string.IsNullOrEmpty(pkg)) return false;

        if (pkg.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains("web", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var defaultApps = new List<string>
        {
            "com.android.chrome",
            "org.mozilla.firefox",
            "com.zhiliaoapp.musically",
            "com.google.android.youtube"
        };

        if (userTargetApps == null || userTargetApps.Count == 0)
        {
            return defaultApps.Contains(pkg, StringComparer.OrdinalIgnoreCase);
        }

        return userTargetApps.Contains(pkg, StringComparer.OrdinalIgnoreCase);
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        try
        {
            if (e == null || e.PackageName == null) return;

            TotalEventsReceived++;
            string pkg = e.PackageName.ToString();
            LastEventPackage = pkg;

            if (pkg == OWN_PACKAGE) return;

            long timerEndMs = 0;
            string? keywordsJson = null;
            string? targetAppsJson = null;
            try
            {
                var prefs = global::Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
                if (prefs != null)
                {
                    timerEndMs = prefs.GetLong(PREF_TIMER_END, 0);
                    keywordsJson = prefs.GetString(PREF_KEYWORDS_JSON, null);
                    targetAppsJson = prefs.GetString(PREF_TARGETAPPS_JSON, null);
                }
            }
            catch { }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool timerActive = timerEndMs > 0 && nowMs < timerEndMs;
            if (!timerActive) return;

            // ═══════════════════════════════════════════════════════════
            // A) SYSTEM- UND EINSTELLUNGSSCHUTZ
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.WindowStateChanged)
            {
                string cls = e.ClassName?.ToString() ?? "";
                foreach (var name in ProtectedClassNames)
                {
                    if (cls.Contains(name))
                    {
                        var r = RootInActiveWindow;
                        if (r != null && ContainsOwnAppReference(r))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz: {name} in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }
                        break;
                    }
                }

                if (pkg.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase))
                {
                    var root = RootInActiveWindow;
                    if (root != null)
                    {
                        // 1. App-Schutz (Deinstallation / Speicher leeren)
                        if (ContainsOwnAppReference(root) && ContainsDangerousAction(root))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz: Aktion in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }

                        // 2. Pixel-Reparaturmodus-Schutz
                        if (ContainsRepairMode(root))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz: Reparaturmodus in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }

                        // 3. Mehrbenutzer- & Gastmodus-Schutz
                        if (ContainsUserManagement(root))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz: Nutzerverwaltung in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }
                    }
                }
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // B) KEYWORD-BLOCKIERUNG BEIM TIPPEN
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.ViewTextChanged)
            {
                List<string>? targetApps = null;
                if (!string.IsNullOrEmpty(targetAppsJson))
                {
                    try { targetApps = JsonSerializer.Deserialize<List<string>>(targetAppsJson, _jsonOptions); } catch { }
                }

                if (!IsTargetApp(pkg, targetApps)) return;

                bool isKeyboard = false;
                foreach (var kb in KeyboardPackages)
                {
                    if (pkg.Contains(kb, StringComparison.OrdinalIgnoreCase)) { isKeyboard = true; break; }
                }
                if (isKeyboard) return;

                string? eventText = null;
                if (e.Text != null && e.Text.Count > 0)
                {
                    var parts = new System.Text.StringBuilder();
                    foreach (var t in e.Text) if (t != null) parts.Append(t.ToString());
                    eventText = parts.ToString();
                }

                List<BlockedKeyword>? keywords = null;
                if (!string.IsNullOrEmpty(keywordsJson))
                {
                    try { keywords = JsonSerializer.Deserialize<List<BlockedKeyword>>(keywordsJson, _jsonOptions); } catch { }
                }
                if (keywords == null || keywords.Count == 0) return;

                string sourceKey = pkg + ":" + (e.Source?.ViewIdResourceName ?? "unknown");

                if (string.IsNullOrEmpty(eventText) || eventText.Length <= 1)
                {
                    if (_lastTextBySource.TryGetValue(sourceKey, out string? prevText) && !string.IsNullOrEmpty(prevText) && prevText.Length > 1)
                    {
                        if (CheckTextAgainstKeywords(prevText, keywords))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Keyword (gesendet) in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                        }
                    }
                    _lastTextBySource[sourceKey] = "";
                    return;
                }

                _lastTextBySource[sourceKey] = eventText;

                if (eventText.Length > 0)
                {
                    char lastChar = eventText[eventText.Length - 1];
                    bool isComplete = lastChar == ' ' || lastChar == '\n' || lastChar == '\r' ||
                                      lastChar == '.' || lastChar == ',' || lastChar == '!' ||
                                      lastChar == '?' || lastChar == ';' || lastChar == ':' || lastChar == '\t';
                    if (!isComplete) return;
                }

                if (CheckTextAgainstKeywords(eventText, keywords))
                {
                    KeywordBlockCount++;
                    LastBlockInfo = $"Keyword in {pkg}";
                    PerformGlobalAction(GlobalAction.Back);
                }
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // C) KEYWORD-SCAN AUF DEM BILDSCHIRM
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.WindowContentChanged)
            {
                bool isKb = false;
                foreach (var kb in KeyboardPackages)
                {
                    if (pkg.Contains(kb, StringComparison.OrdinalIgnoreCase)) { isKb = true; break; }
                }
                if (isKb) return;

                if (pkg.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase))
                {
                    var root = RootInActiveWindow;
                    if (root != null)
                    {
                        if (ContainsOwnAppReference(root) && ContainsDangerousAction(root))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz (WCC): {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }

                        if (ContainsRepairMode(root))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz (WCC Reparaturmodus): {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }

                        if (ContainsUserManagement(root))
                        {
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz (WCC Nutzerverwaltung): {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }
                    }
                    return;
                }

                List<string>? targetApps2 = null;
                if (!string.IsNullOrEmpty(targetAppsJson))
                {
                    try { targetApps2 = JsonSerializer.Deserialize<List<string>>(targetAppsJson, _jsonOptions); } catch { }
                }

                if (!IsTargetApp(pkg, targetApps2)) return;

                if (pkg == _lastContentCheckPkg && nowMs - _lastContentCheckMs < CONTENT_CHECK_INTERVAL_MS) return;

                List<BlockedKeyword>? keywords2 = null;
                if (!string.IsNullOrEmpty(keywordsJson))
                {
                    try { keywords2 = JsonSerializer.Deserialize<List<BlockedKeyword>>(keywordsJson, _jsonOptions); } catch { }
                }
                if (keywords2 == null || keywords2.Count == 0) return;

                _lastContentCheckMs = nowMs;
                _lastContentCheckPkg = pkg;

                var rootNode = RootInActiveWindow;
                if (rootNode != null && ContainsForbiddenText(rootNode, keywords2))
                {
                    KeywordBlockCount++;
                    LastBlockInfo = $"Keyword (Inhalt) in {pkg}";
                    PerformGlobalAction(GlobalAction.Back);
                }
                return;
            }
        }
        catch { }
    }

    public override void OnInterrupt() { }

    private bool ContainsOwnAppReference(AccessibilityNodeInfo node)
    {
        if (node == null) return false;
        string? text = node.Text?.ToString();
        string? desc = node.ContentDescription?.ToString();

        if (text != null) foreach (var id in OwnAppIdentifiers) if (text.Contains(id, StringComparison.OrdinalIgnoreCase)) return true;
        if (desc != null) foreach (var id in OwnAppIdentifiers) if (desc.Contains(id, StringComparison.OrdinalIgnoreCase)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsOwnAppReference(child)) return true;
        }
        return false;
    }

    private bool ContainsDangerousAction(AccessibilityNodeInfo node)
    {
        if (node == null) return false;
        string? text = node.Text?.ToString()?.ToLowerInvariant();
        string? desc = node.ContentDescription?.ToString()?.ToLowerInvariant();

        if (text != null) foreach (var action in DangerousActionTexts) if (text.Contains(action)) return true;
        if (desc != null) foreach (var action in DangerousActionTexts) if (desc.Contains(action)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsDangerousAction(child)) return true;
        }
        return false;
    }

    private bool ContainsRepairMode(AccessibilityNodeInfo node)
    {
        if (node == null) return false;
        string? text = node.Text?.ToString()?.ToLowerInvariant();
        string? desc = node.ContentDescription?.ToString()?.ToLowerInvariant();

        if (text != null) foreach (var kw in RepairModeKeywords) if (text.Contains(kw)) return true;
        if (desc != null) foreach (var kw in RepairModeKeywords) if (desc.Contains(kw)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsRepairMode(child)) return true;
        }
        return false;
    }

    private bool ContainsUserManagement(AccessibilityNodeInfo node)
    {
        if (node == null) return false;
        string? text = node.Text?.ToString()?.ToLowerInvariant();
        string? desc = node.ContentDescription?.ToString()?.ToLowerInvariant();

        if (text != null) foreach (var kw in UserManagementKeywords) if (text.Contains(kw)) return true;
        if (desc != null) foreach (var kw in UserManagementKeywords) if (desc.Contains(kw)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsUserManagement(child)) return true;
        }
        return false;
    }

    private bool ContainsForbiddenText(AccessibilityNodeInfo node, List<BlockedKeyword> keywords)
    {
        if (node == null || keywords == null || keywords.Count == 0) return false;

        string? text = node.Text?.ToString();
        string? desc = node.ContentDescription?.ToString();

        if (CheckTextAgainstKeywords(text, keywords)) return true;
        if (CheckTextAgainstKeywords(desc, keywords)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsForbiddenText(child, keywords)) return true;
        }
        return false;
    }

    private static bool CheckTextAgainstKeywords(string? text, List<BlockedKeyword> keywords)
    {
        if (text == null) return false;

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw.Value)) continue;

            if (kw.IsAggressive)
            {
                if (text.Contains(kw.Value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else
            {
                try
                {
                    string pattern = @"\b" + Regex.Escape(kw.Value) + @"\b";
                    if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return true;
                }
                catch
                {
                    string normalizedText = text.Trim().ToLowerInvariant();
                    string normalizedKw = kw.Value.Trim().ToLowerInvariant();

                    int idx = normalizedText.IndexOf(normalizedKw);
                    while (idx >= 0)
                    {
                        bool startOk = idx == 0 || !char.IsLetterOrDigit(normalizedText[idx - 1]);
                        int end = idx + normalizedKw.Length;
                        bool endOk = end >= normalizedText.Length || !char.IsLetterOrDigit(normalizedText[end]);
                        if (startOk && endOk) return true;
                        idx = normalizedText.IndexOf(normalizedKw, idx + 1);
                    }
                }
            }
        }
        return false;
    }
}