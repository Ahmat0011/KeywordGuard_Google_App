#pragma warning disable CA1416, CA1422
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views.Accessibility;
using LanguageData.Models;
using LanguageData.Services;
using System.Text;
using System.Text.Json;

namespace LanguageData.Platforms.Android;

[Service(
    Label = "Language Data Service",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE",
    Exported = true)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/content_guard_service")]
public class ContentGuardService : AccessibilityService
{
    public const string OWN_PACKAGE = "com.android.languagedata.service";
    private const string LOG_TAG = "LDService";
    private const string PREFS_NAME = "langdata_prefs";
    private const string PREF_TIMER_END = "timer_end_ms";
    private const string PREF_KEYWORDS_JSON = "keywords_json";
    private const string PREF_TARGETAPPS_JSON = "targetapps_json";

    public static ContentGuardService? Instance { get; private set; }
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

    // ═══════════════════════════════════════════════════════════
    // Hochperformantes Caching für serialisierte Konfigurationsdaten
    // ═══════════════════════════════════════════════════════════
    private static string? _lastKeywordsRaw;
    private static List<BlockedKeyword> _cachedKeywords = new();
    private static string? _lastTargetAppsRaw;
    private static List<string> _cachedTargetApps = new();

    private static List<BlockedKeyword> GetKeywords(string? rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return new List<BlockedKeyword>();
        if (rawJson == _lastKeywordsRaw) return _cachedKeywords;

        try
        {
            _cachedKeywords = JsonSerializer.Deserialize<List<BlockedKeyword>>(rawJson, _jsonOptions) ?? new List<BlockedKeyword>();
            _lastKeywordsRaw = rawJson;
        }
        catch
        {
            _cachedKeywords = new List<BlockedKeyword>();
        }
        return _cachedKeywords;
    }

    private static List<string> GetTargetApps(string? rawJson)
    {
        if (string.IsNullOrEmpty(rawJson)) return new List<string>();
        if (rawJson == _lastTargetAppsRaw) return _cachedTargetApps;

        try
        {
            _cachedTargetApps = JsonSerializer.Deserialize<List<string>>(rawJson, _jsonOptions) ?? new List<string>();
            _lastTargetAppsRaw = rawJson;
        }
        catch
        {
            _cachedTargetApps = new List<string>();
        }
        return _cachedTargetApps;
    }

    // Eindeutige Erkennungsmerkmale unserer eigenen App
    private static readonly string[] OwnAppIdentifiers =
    {
        "language data",
        "com.android.languagedata.service",
        "languagedata"
    };

    // Geschützte System-Klassen (App-Info, Deinstallation, Stoppen, Admin, Bedienungshilfe)
    private static readonly string[] AlwaysBlockedForOwnAppClasses =
    {
        "InstalledAppDetails",
        "AppInfoDashboardFragment",
        "InstalledAppDetailsTop",
        "AppInfoActivity",
        "ApplicationDetailsActivity",
        "AppButtonsPreferenceController",
        "AppStorageSettings",
        "ToggleAccessibilityServicePreference",
        "AccessibilityDetailsSettings",
        "AccessibilityServiceWarning",
        "UninstallAppProgress",
        "UninstallerActivity",
        "UninstallUninstalling",
        "UninstallAlertDialogFragment",
        "ForceStopDialogFragment"
    };

    // Begriffe, die zeigen, dass auf dem Bildschirm unsere App gerade deinstalliert,
    // gestoppt, geleert oder in den Rechten/Admin/Bedienungshilfen deaktiviert werden soll
    private static readonly string[] DangerousActionTexts =
    {
        // Deinstallieren & Entfernen
        "deinstallieren", "uninstall",
        "updates deinstallieren", "uninstall updates",
        "app deinstallieren", "app entfernen",
        "möchten sie diese app deinstallieren",
        "do you want to uninstall",

        // Stoppen / Beenden erzwingen
        "beenden erzwingen", "stopp erzwingen", "stoppen erzwingen",
        "force stop", "force-stop", "ausführung beenden",

        // Deaktivieren & Geräteadmin
        "deaktivieren", "disable", "deactivate",
        "geräte-admin-app deaktivieren", "geräteadministrator deaktivieren",
        "deaktivieren und deinstallieren", "deactivate and uninstall",

        // Bedienungshilfe ausschalten
        "language data service verwenden", "use language data service",
        "language data service beenden", "stop language data service",
        "dienst verwenden", "bedienungshilfe verwenden",

        // Speicher / Daten löschen (würde Regeln/Timer zurücksetzen)
        "daten loeschen", "daten löschen", "clear data",
        "speicher loeschen", "speicher löschen", "speicherinhalt löschen",
        "clear storage", "speicher und cache", "storage & cache",
        "cache loeschen", "cache löschen", "clear cache", "cache leeren"
    };

    // View-IDs in den Android-Einstellungen & im PackageInstaller für gefährliche Aktionen
    private static readonly string[] DangerousViewIds =
    {
        "uninstall_button",
        "force_stop_button",
        "clear_data_button",
        "clear_cache_button",
        "action_buttons",
        "admin_warning",
        "restricted_action"
    };

    // Begriffe für APK-Updates (Visual Studio / manuelles Update), die ERLAUBT bleiben müssen
    private static readonly string[] AllowedUpdateTexts =
    {
        "aktualisieren",
        "update",
        "möchten sie diese app aktualisieren",
        "do you want to update this app"
    };

    // Spezifische Signalwörter zur Blockierung des Reparaturmodus
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
    private long _lastSelfDefenseBlockMs = 0;
    private const long CONTENT_CHECK_INTERVAL_MS = 250;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        Instance = this;
        IsServiceConnected = true;

        try
        {
            var filesDir = global::Android.App.Application.Context.FilesDir;
            if (filesDir != null)
            {
                CryptoHelper.Initialize(filesDir.AbsolutePath);
            }
        }
        catch { }

        CancelRecoveryAlarm();
        DeviceAdminReceiver.EnforceUninstallProtection(this);

        // 1. Alle installierten Browser asynchron im BrowserDetector vorladen
        try
        {
            BrowserDetector.PreloadInstalledBrowsers(this);
        }
        catch { }

        // 2. Recovery Alarm bei aktivem Timer wieder einrichten
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
            var mainLooper = Looper.MainLooper;
            if (mainLooper != null)
            {
                var handler = new Handler(mainLooper);
                System.Action checkLoop = null!;
                checkLoop = () =>
                {
                    if (!ConfigService.IsTimerActive())
                    {
                        try
                        {
                            MainActivity.SetAppIconVisible(true);
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
        }
        catch { }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
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

    public void TriggerImmediateLockdown(string reason)
    {
        ExecuteSelfDefenseBlock(RootInActiveWindow, reason);
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        try
        {
            if (e == null) return;

            string pkg = e.PackageName?.ToString() ?? "";
            if (string.IsNullOrEmpty(pkg)) return;

            TotalEventsReceived++;
            string cls = e.ClassName?.ToString() ?? "";
            LastEventPackage = pkg;

            // Eigene App-Oberfläche niemals selbst blockieren
            if (string.Equals(pkg, OWN_PACKAGE, StringComparison.OrdinalIgnoreCase))
                return;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // ═══════════════════════════════════════════════════════════
            // 1. SYSTEM- & SELBSTSCHUTZ (IMMER AKTIV - 24/7, VOR IsExcluded!)
            //    Verhindert Deinstallieren, Stoppen, Admin-Deaktivierung &
            //    Bedienungshilfen-Abschaltung NUR für diese App.
            //    Andere Apps können weiterhin frei deinstalliert werden!
            // ═══════════════════════════════════════════════════════════
            if (BrowserDetector.IsSystemOrInstallerPackage(pkg) || IsProtectedClass(cls))
            {
                var sourceNode = e.Source;
                var root = RootInActiveWindow ?? sourceNode;
                try
                {
                    if (CheckAndBlockSelfTampering(root, e, pkg, cls))
                    {
                        return;
                    }
                }
                finally
                {
                    if (root != null && !ReferenceEquals(root, sourceNode))
                    {
                        SafeRecycle(root);
                    }
                    if (sourceNode != null)
                    {
                        SafeRecycle(sourceNode);
                    }
                }
            }

            // Explizit ausgeschlossene Apps (Gemini, Notizen, Office, Messenger etc.) NIEMALS blockieren!
            if (BrowserDetector.IsExcluded(pkg)) return;

            // Tastatur-Pakete für den Rest ignorieren
            foreach (var kb in KeyboardPackages)
            {
                if (pkg.Contains(kb, StringComparison.OrdinalIgnoreCase)) return;
            }

            // Konfigurationsdaten (Keywords & Ziel-Apps) aus SharedPreferences lesen
            string? keywordsJson = null;
            string? targetAppsJson = null;
            try
            {
                var prefs = global::Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
                if (prefs != null)
                {
                    keywordsJson = prefs.GetString(PREF_KEYWORDS_JSON, null);
                    targetAppsJson = prefs.GetString(PREF_TARGETAPPS_JSON, null);
                }
            }
            catch { }

            var targetApps = GetTargetApps(targetAppsJson);
            var keywords = GetKeywords(keywordsJson);

            // Sobald Keywords eingetragen sind, werden diese IMMER blockiert (auch nach Visual-Studio-Update)
            if (keywords.Count == 0) return;

            // Prüfen, ob diese App überwacht werden soll (jeder Browser ODER explizite Zusatz-App)
            if (!BrowserDetector.IsMonitoredApp(pkg, targetApps)) return;

            // ═══════════════════════════════════════════════════════════
            // 2. KEYWORD- & URL-BLOCKIERUNG BEIM TIPPEN (ViewTextChanged)
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.ViewTextChanged)
            {
                string? eventText = null;
                if (e.Text != null && e.Text.Count > 0)
                {
                    var parts = new StringBuilder();
                    foreach (var t in e.Text)
                    {
                        if (t != null) parts.Append(t.ToString());
                    }
                    eventText = parts.ToString();
                }

                var srcNode = e.Source;
                string sourceKey = pkg + ":" + (srcNode?.ViewIdResourceName ?? "unknown");

                if (_lastTextBySource.Count > 100)
                {
                    _lastTextBySource.Clear();
                }

                // Text gelöscht/abgesendet: vorherigen Text prüfen
                if (string.IsNullOrEmpty(eventText) || eventText.Length <= 1)
                {
                    if (_lastTextBySource.TryGetValue(sourceKey, out string? prevText) &&
                        !string.IsNullOrEmpty(prevText) && prevText.Length > 1)
                    {
                        if (CheckTextAgainstKeywords(prevText, keywords))
                        {
                            BlockBrowserOrApp(srcNode, $"Keyword (gesendet) in {pkg}");
                        }
                    }
                    _lastTextBySource[sourceKey] = "";
                    SafeRecycle(srcNode);
                    return;
                }

                _lastTextBySource[sourceKey] = eventText;

                // A) Aggressive Keywords & URL-/Domain-Keywords: SOFORT beim Tippen prüfen
                foreach (var kw in keywords)
                {
                    if (string.IsNullOrWhiteSpace(kw.Value)) continue;

                    if (kw.IsAggressive || IsUrlOrDomainKeyword(kw.Value))
                    {
                        if (eventText.Contains(kw.Value.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            BlockBrowserOrApp(srcNode, $"Sofort-Keyword/URL in {pkg}");
                            SafeRecycle(srcNode);
                            return;
                        }
                    }
                }

                // B) Reguläre Keywords: Prüfung bei Worttrennern oder URL-Bestandteilen
                char lastChar = eventText[eventText.Length - 1];
                bool isDelimiter = char.IsWhiteSpace(lastChar) || char.IsPunctuation(lastChar) ||
                                   lastChar == '/' || lastChar == '\\' || lastChar == '&' ||
                                   lastChar == '?' || lastChar == '=' || lastChar == '-' || lastChar == ':';

                if (isDelimiter || eventText.Contains(' ') || eventText.Contains('.') || eventText.Contains('/'))
                {
                    if (CheckTextAgainstKeywords(eventText, keywords))
                    {
                        BlockBrowserOrApp(srcNode, $"Keyword in {pkg}");
                    }
                }

                SafeRecycle(srcNode);
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // 3. KEYWORD- & URL-SCAN AUF DEM BILDSCHIRM
            //    (WindowContentChanged, WindowStateChanged, ViewClicked)
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.WindowContentChanged ||
                e.EventType == EventTypes.WindowStateChanged ||
                e.EventType == EventTypes.ViewClicked)
            {
                bool forceImmediate = e.EventType == EventTypes.WindowStateChanged || pkg != _lastContentCheckPkg;
                if (!forceImmediate && (nowMs - _lastContentCheckMs < CONTENT_CHECK_INTERVAL_MS))
                    return;

                _lastContentCheckMs = nowMs;
                _lastContentCheckPkg = pkg;

                var rootNode = RootInActiveWindow;
                if (rootNode != null)
                {
                    try
                    {
                        if (ContainsForbiddenText(rootNode, keywords, 0))
                        {
                            BlockBrowserOrApp(null, $"Keyword/URL (Inhalt) in {pkg}");
                        }
                    }
                    finally
                    {
                        SafeRecycle(rootNode);
                    }
                }
            }
        }
        catch { }
    }

    public override void OnInterrupt() { }

    private static void SafeRecycle(AccessibilityNodeInfo? node)
    {
        if (node == null) return;
        try
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            {
                node.Recycle();
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════
    // SELBSTSCHUTZ-LOGIK (PRÄZISE 2-STUFEN-ERKENNUNG)
    // ═══════════════════════════════════════════════════════════
    private static bool IsProtectedClass(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return false;
        if (cls.Contains("DeviceAdminAdd", StringComparison.OrdinalIgnoreCase) ||
            cls.Contains("PackageInstallerActivity", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var name in AlwaysBlockedForOwnAppClasses)
        {
            if (cls.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool CheckAndBlockSelfTampering(AccessibilityNodeInfo? root, AccessibilityEvent e, string pkg, string cls)
    {
        var screenTexts = new List<string>(64);
        var viewIds = new List<string>(32);

        // Texte aus dem aktuellen Event selbst sammeln
        if (e.Text != null)
        {
            foreach (var t in e.Text)
            {
                string? s = t?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    screenTexts.Add(s.ToLowerInvariant());
            }
        }

        string? eventDesc = e.ContentDescription?.ToString();
        if (!string.IsNullOrWhiteSpace(eventDesc))
        {
            screenTexts.Add(eventDesc.ToLowerInvariant());
        }

        if (root != null)
        {
            CollectScreenData(root, screenTexts, viewIds, 0);
        }

        if (screenTexts.Count == 0 && viewIds.Count == 0)
            return false;

        // 1. Reparaturmodus & Nutzerwechsel / Gastmodus in den Einstellungen immer blockieren
        foreach (var text in screenTexts)
        {
            foreach (var repairKw in RepairModeKeywords)
            {
                if (text.Contains(repairKw))
                {
                    ExecuteSelfDefenseBlock(root, $"Schutz: Reparaturmodus in {pkg}");
                    return true;
                }
            }
            foreach (var userKw in UserManagementKeywords)
            {
                if (text.Contains(userKw))
                {
                    ExecuteSelfDefenseBlock(root, $"Schutz: Nutzerverwaltung in {pkg}");
                    return true;
                }
            }
        }

        // 2. Prüfen, ob unsere eigene App ("Language Data") auf diesem Bildschirm ausgewählt/betroffen ist
        bool hasOwnAppRef = false;
        foreach (var text in screenTexts)
        {
            foreach (var id in OwnAppIdentifiers)
            {
                if (text.Contains(id))
                {
                    hasOwnAppRef = true;
                    break;
                }
            }
            if (hasOwnAppRef) break;
        }

        // WICHTIG: Wenn unsere App NICHT auf dem Bildschirm steht, wird gerade eine ANDERE App
        // angeschaut oder deinstalliert -> Komplett erlauben!
        if (!hasOwnAppRef)
            return false;

        // 3. Ausnahme für APK-Updates (z.B. über Visual Studio oder manuelles Update):
        //    Wenn der PackageInstaller "Aktualisieren" / "Update" anzeigt und NICHT "Deinstallieren", erlauben!
        bool hasUpdateText = screenTexts.Any(t => AllowedUpdateTexts.Any(u => t.Contains(u)));
        bool hasUninstallText = screenTexts.Any(t => t.Contains("deinstallieren") || t.Contains("uninstall") || t.Contains("entfernen"));
        if (hasUpdateText && !hasUninstallText &&
            (pkg.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase) || cls.Contains("PackageInstaller", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // 4. Geräteadministrator-Fenster (DeviceAdminAdd):
        //    - Wenn der Geräteadmin noch NICHT aktiv ist -> Erlauben, damit man auf "Aktivieren" klicken kann!
        //    - Sobald der Geräteadmin aktiv ist -> Sofort blockieren, damit niemand auf "Deaktivieren" klicken kann!
        bool isAdminActive = DeviceAdminReceiver.IsAdminActive(this);
        if (cls.Contains("DeviceAdminAdd", StringComparison.OrdinalIgnoreCase))
        {
            if (isAdminActive)
            {
                ExecuteSelfDefenseBlock(root, $"Schutz: Geräteadmin-Deaktivierung verhindert ({pkg})");
                return true;
            }
            return false;
        }

        // 5. Bekannte Detail-Klassen für App-Info, Deinstallations-Dialog, Force-Stop oder Accessibility-Detailseite
        foreach (var protectedCls in AlwaysBlockedForOwnAppClasses)
        {
            if (cls.Contains(protectedCls, StringComparison.OrdinalIgnoreCase))
            {
                ExecuteSelfDefenseBlock(root, $"Schutz: {protectedCls} für Language Data blockiert");
                return true;
            }
        }

        // 6. Inhaltsbasierte Prüfung (greift auch bei Android 14/15/16 SpaActivity & SubSettings):
        //    Unsere App ist ausgewählt UND es sind gefährliche Buttons/Aktionen (Deinstallieren, Stoppen, Deaktivieren, Speicher) sichtbar
        bool hasDangerousText = false;
        foreach (var text in screenTexts)
        {
            foreach (var action in DangerousActionTexts)
            {
                if (text.Contains(action))
                {
                    if (!isAdminActive && action.Contains("deaktivieren") && screenTexts.Any(t => t.Equals("aktivieren") || t.Equals("activate")))
                        continue;

                    hasDangerousText = true;
                    break;
                }
            }
            if (hasDangerousText) break;
        }

        bool hasDangerousViewId = false;
        foreach (var id in viewIds)
        {
            foreach (var dangerId in DangerousViewIds)
            {
                if (id.Contains(dangerId))
                {
                    hasDangerousViewId = true;
                    break;
                }
            }
            if (hasDangerousViewId) break;
        }

        if (hasDangerousText || hasDangerousViewId)
        {
            ExecuteSelfDefenseBlock(root, $"Schutz: Zugriff auf Deinstallieren/Stoppen/Admin blockiert in {pkg}");
            return true;
        }

        return false;
    }

    private void CollectScreenData(AccessibilityNodeInfo? node, List<string> texts, List<string> viewIds, int depth)
    {
        if (node == null || depth > 30) return;

        string? t = node.Text?.ToString();
        if (!string.IsNullOrWhiteSpace(t))
            texts.Add(t.ToLowerInvariant());

        string? desc = node.ContentDescription?.ToString();
        if (!string.IsNullOrWhiteSpace(desc))
            texts.Add(desc.ToLowerInvariant());

        string? id = node.ViewIdResourceName;
        if (!string.IsNullOrWhiteSpace(id))
            viewIds.Add(id.ToLowerInvariant());

        int count = node.ChildCount;
        for (int i = 0; i < count; i++)
        {
            AccessibilityNodeInfo? child = null;
            try
            {
                child = node.GetChild(i);
                if (child != null)
                {
                    CollectScreenData(child, texts, viewIds, depth + 1);
                }
            }
            catch { }
            finally
            {
                SafeRecycle(child);
            }
        }
    }

    private void ExecuteSelfDefenseBlock(AccessibilityNodeInfo? root, string reason)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - _lastSelfDefenseBlockMs < 150) return;
        _lastSelfDefenseBlockMs = nowMs;

        KeywordBlockCount++;
        LastBlockInfo = reason;
        AddDiag(reason);

        // 1. Falls ein Bestätigungsdialog ("Abbrechen" / "Cancel") offen ist, sofort darauf klicken
        if (root != null)
        {
            TryClickCancelButton(root, 0);
        }

        // 2. Sofort aus dem Einstellungs-/Deinstallationsfenster zurückspringen & auf den Homescreen wechseln
        PerformGlobalAction(GlobalAction.Back);
        PerformGlobalAction(GlobalAction.Home);

        // 3. Zusätzlich per Intent hart auf den Startbildschirm wechseln, damit die Einstellungen geschlossen werden
        try
        {
            var homeIntent = new Intent(Intent.ActionMain);
            homeIntent.AddCategory(Intent.CategoryHome);
            homeIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            StartActivity(homeIntent);
        }
        catch { }
    }

    private bool TryClickCancelButton(AccessibilityNodeInfo? node, int depth)
    {
        if (node == null || depth > 20) return false;

        string? text = node.Text?.ToString()?.Trim().ToLowerInvariant();
        string? id = node.ViewIdResourceName?.ToLowerInvariant();

        if (text == "abbrechen" || text == "cancel" || text == "nein" ||
            (id != null && (id.EndsWith(":id/button2") || id.Contains("cancel_button"))))
        {
            if (node.Clickable)
            {
                node.PerformAction(global::Android.Views.Accessibility.Action.Click);
                return true;
            }
            var parent = node.Parent;
            if (parent != null)
            {
                try
                {
                    if (parent.Clickable)
                    {
                        parent.PerformAction(global::Android.Views.Accessibility.Action.Click);
                        return true;
                    }
                }
                finally
                {
                    SafeRecycle(parent);
                }
            }
        }

        int count = node.ChildCount;
        for (int i = 0; i < count; i++)
        {
            AccessibilityNodeInfo? child = null;
            try
            {
                child = node.GetChild(i);
                if (child != null && TryClickCancelButton(child, depth + 1))
                    return true;
            }
            catch { }
            finally
            {
                SafeRecycle(child);
            }
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════
    // BROWSER- & KEYWORD/URL-BLOCKIERUNG
    // ═══════════════════════════════════════════════════════════
    private void BlockBrowserOrApp(AccessibilityNodeInfo? editableNode, string reason)
    {
        KeywordBlockCount++;
        LastBlockInfo = reason;
        AddDiag(reason);

        // Falls der Nutzer gerade in eine Adress- oder Suchleiste tippt: Textfeld sofort leeren!
        if (editableNode != null && editableNode.Editable)
        {
            try
            {
                var args = new Bundle();
                string argKey = AccessibilityNodeInfo.ActionArgumentSetTextCharsequence ?? "ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE";
                args.PutCharSequence(argKey, "");
                editableNode.PerformAction(global::Android.Views.Accessibility.Action.SetText, args);
            }
            catch { }
        }

        PerformGlobalAction(GlobalAction.Back);
        PerformGlobalAction(GlobalAction.Home);
    }

    private bool ContainsForbiddenText(AccessibilityNodeInfo? node, List<BlockedKeyword> keywords, int depth)
    {
        if (node == null || keywords == null || keywords.Count == 0 || depth > 30)
            return false;

        string? text = node.Text?.ToString();
        string? desc = node.ContentDescription?.ToString();

        if (CheckTextAgainstKeywords(text, keywords)) return true;
        if (CheckTextAgainstKeywords(desc, keywords)) return true;

        int count = node.ChildCount;
        for (int i = 0; i < count; i++)
        {
            AccessibilityNodeInfo? child = null;
            try
            {
                child = node.GetChild(i);
                if (child != null && ContainsForbiddenText(child, keywords, depth + 1))
                    return true;
            }
            catch { }
            finally
            {
                SafeRecycle(child);
            }
        }
        return false;
    }

    private static bool IsUrlOrDomainKeyword(string kw)
    {
        return kw.Contains('.') || kw.Contains('/') || kw.Contains(':') || kw.Contains('?') || kw.Contains('=');
    }

    private static bool CheckTextAgainstKeywords(string? text, List<BlockedKeyword> keywords)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw.Value)) continue;
            string cleanKw = kw.Value.Trim();

            // 1. Aggressive Keywords ODER URLs/Domains (enthalten Punkt, Slash etc.) -> Direktes Substring-Matching
            if (kw.IsAggressive || IsUrlOrDomainKeyword(cleanKw))
            {
                if (text.Contains(cleanKw, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // 2. Normales Wort: Prüft auf exakte Wortgrenzen (Buchstabe/Zahl) ODER Vorkommen als Domain-Bestandteil
                int idx = text.IndexOf(cleanKw, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    bool startOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
                    int end = idx + cleanKw.Length;
                    bool endOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);

                    if (startOk && endOk)
                        return true;

                    idx = text.IndexOf(cleanKw, idx + 1, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        return false;
    }
}