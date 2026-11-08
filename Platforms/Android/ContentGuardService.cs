#pragma warning disable CA1416, CA1422
using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
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

    private static string? _lastKeywordsRaw;
    private static List<BlockedKeyword> _cachedKeywords = new();
    private static string? _lastTargetAppsRaw;
    private static List<string> _cachedTargetApps = new();
    private static bool _wasTimerActiveLastCheck = false;

    public static void InvalidateCache()
    {
        _lastKeywordsRaw = null;
        _lastTargetAppsRaw = null;
    }

    private (bool isTimerActive, List<BlockedKeyword> keywords, List<string> targetApps) LoadCurrentState(long nowMs)
    {
        long timerEndMs = 0;
        string? kwRaw = null;
        string? taRaw = null;

        try
        {
            var prefs = global::Android.App.Application.Context.GetSharedPreferences(PREFS_NAME, FileCreationMode.Private);
            if (prefs != null)
            {
                timerEndMs = prefs.GetLong(PREF_TIMER_END, 0);
                kwRaw = prefs.GetString(PREF_KEYWORDS_JSON, null);
                taRaw = prefs.GetString(PREF_TARGETAPPS_JSON, null);
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(kwRaw))
        {
            if (kwRaw != _lastKeywordsRaw)
            {
                try
                {
                    _cachedKeywords = JsonSerializer.Deserialize<List<BlockedKeyword>>(kwRaw, _jsonOptions) ?? new List<BlockedKeyword>();
                    _lastKeywordsRaw = kwRaw;
                }
                catch { }
            }
        }
        else if (_cachedKeywords.Count == 0)
        {
            try
            {
                var cfg = ConfigService.LoadConfig();
                if (cfg.Keywords.Count > 0)
                    _cachedKeywords = cfg.Keywords;
                if (timerEndMs == 0 && cfg.EndTime.HasValue)
                    timerEndMs = new DateTimeOffset(cfg.EndTime.Value).ToUnixTimeMilliseconds();
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(taRaw) && taRaw != _lastTargetAppsRaw)
        {
            try
            {
                _cachedTargetApps = JsonSerializer.Deserialize<List<string>>(taRaw, _jsonOptions) ?? new List<string>();
                _lastTargetAppsRaw = taRaw;
            }
            catch { }
        }

        bool active = timerEndMs > 0 && nowMs < timerEndMs;

        if (_wasTimerActiveLastCheck && !active)
        {
            _wasTimerActiveLastCheck = false;
            try
            {
                MainActivity.SetAppIconVisible(true);
                ConfigService.SetIconHidden(false);
                CancelRecoveryAlarm();
                AccessibilityGuardEnforcer.StopMonitoring();
                DeviceAdminReceiver.EnforceUninstallProtection(this);
            }
            catch { }
        }
        else if (active)
        {
            _wasTimerActiveLastCheck = true;
        }

        return (active, _cachedKeywords, _cachedTargetApps);
    }

    private static readonly string[] OwnAppIdentifiers =
    {
        "language data",
        "com.android.languagedata.service",
        "languagedata"
    };

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
        "ForceStopDialogFragment",
        "SpaActivity"
    };

    private static readonly string[] DangerousActionTexts =
    {
        "app-info", "app info", "anwendungsinfo",
        "deinstallieren", "uninstall",
        "updates deinstallieren", "uninstall updates",
        "app deinstallieren", "app entfernen",
        "möchten sie diese app deinstallieren",
        "do you want to uninstall",
        "beenden erzwingen", "stopp erzwingen", "stoppen erzwingen",
        "force stop", "force-stop", "ausführung beenden",
        "deaktivieren", "disable", "deactivate",
        "geräte-admin-app deaktivieren", "geräteadministrator deaktivieren",
        "deaktivieren und deinstallieren", "deactivate and uninstall",
        "language data service verwenden", "use language data service",
        "language data service beenden", "stop language data service",
        "dienst verwenden", "bedienungshilfe verwenden",
        "berechtigung entfernen", "zugriff entfernen",
        "daten loeschen", "daten löschen", "clear data",
        "speicher loeschen", "speicher löschen", "speicherinhalt löschen",
        "clear storage", "speicher und cache", "storage & cache",
        "cache loeschen", "cache löschen", "clear cache", "cache leeren"
    };

    private static readonly string[] DangerousViewIds =
    {
        "uninstall_button",
        "force_stop_button",
        "clear_data_button",
        "clear_cache_button",
        "action_buttons",
        "admin_warning",
        "restricted_action",
        "entity_header"
    };

    private static readonly string[] AllowedUpdateTexts =
    {
        "aktualisieren",
        "update",
        "möchten sie diese app aktualisieren",
        "do you want to update this app"
    };

    private static readonly string[] RepairModeKeywords =
    {
        "reparaturmodus",
        "repair mode",
        "in den reparaturmodus wechseln",
        "reparaturmodus beenden",
        "wartungsmodus",
        "maintenance mode"
    };

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
    private long _lastBrowserBlockMs = 0;
    private const long CONTENT_CHECK_INTERVAL_MS = 150;

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

        InvalidateCache();
        CancelRecoveryAlarm();
        DeviceAdminReceiver.EnforceUninstallProtection(this);

        // WICHTIG: Wenn der Timer aktiv ist, die 80-ms-Überwachungsschleife direkt starten,
        // damit sie beim Ausschalten der Bedienungshilfe in 0 ms bereitsteht!
        if (ConfigService.IsTimerActive())
        {
            AccessibilityGuardEnforcer.StartMonitoring(this);
        }

        Task.Run(() =>
        {
            try { BrowserDetector.PreloadInstalledBrowsers(this); } catch { }
        });

        try
        {
            AccessibilityGuardEnforcer.RemoveOverlay();
            ForceAccessibilityActivity.CurrentInstance?.Finish();
        }
        catch { }

        AddDiag("Service CONNECTED.");
    }

    public override bool OnUnbind(Intent? intent)
    {
        IsServiceConnected = false;
        if (Instance == this) Instance = null;

        // SOFORTIGE 0-MS-REAKTION BEIM AUSSCHALTEN DER BEDIENUNGSHILFE:
        TriggerInstantDeactivationLock();

        return base.OnUnbind(intent);
    }

    public override void OnDestroy()
    {
        IsServiceConnected = false;
        if (Instance == this) Instance = null;
        AddDiag("Service DESTROYED");

        // SOFORTIGE 0-MS-REAKTION BEIM BEENDEN DES DIENSTES:
        TriggerInstantDeactivationLock();

        base.OnDestroy();
    }

    private void TriggerInstantDeactivationLock()
    {
        try
        {
            if (ConfigService.IsTimerActive())
            {
                MainActivity.SetAppIconVisible(false);
                // 1. In 0 ms das Vollbild-Overlay schließen!
                AccessibilityGuardEnforcer.SlamOverlayShut(this);
                // 2. In 0 ms sofort zurück in die Bedienungshilfen zwingen!
                AccessibilityGuardEnforcer.LaunchForceActivityIntent(this);
                // 3. Alarm zusätzlich als Backup
                ScheduleRecoveryAlarm(1000);
            }
        }
        catch { }
    }

    public static void ScheduleRecoveryAlarm(long delayMs = 1500)
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

            long triggerMs = Java.Lang.JavaSystem.CurrentTimeMillis() + delayMs;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                am.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, pi);
            else
                am.Set(AlarmType.RtcWakeup, triggerMs, pi);
        }
        catch { }
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
            }
        }
        catch { }
    }

    public void TriggerImmediateLockdown(string reason)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var state = LoadCurrentState(nowMs);
        if (!state.isTimerActive) return;
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

            if (string.Equals(pkg, OWN_PACKAGE, StringComparison.OrdinalIgnoreCase))
                return;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var (isTimerActive, keywords, targetApps) = LoadCurrentState(nowMs);

            // 1. SYSTEM- & SELBSTSCHUTZ (NUR WÄHREND AKTIVEM TIMER)
            if (isTimerActive && (BrowserDetector.IsSystemOrInstallerPackage(pkg) || IsProtectedClass(cls)))
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

            if (BrowserDetector.IsExcluded(pkg) && !targetApps.Contains(pkg, StringComparer.OrdinalIgnoreCase))
                return;

            foreach (var kb in KeyboardPackages)
            {
                if (pkg.Contains(kb, StringComparison.OrdinalIgnoreCase)) return;
            }

            if (keywords.Count == 0) return;

            if (!BrowserDetector.IsMonitoredApp(pkg, targetApps)) return;

            // 2. KEYWORD- & URL-BLOCKIERUNG BEIM TIPPEN (ViewTextChanged)
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

                if (string.IsNullOrEmpty(eventText) || eventText.Length <= 1)
                {
                    if (_lastTextBySource.TryGetValue(sourceKey, out string? prevText) &&
                        !string.IsNullOrEmpty(prevText) && prevText.Length > 1)
                    {
                        if (CheckTextAgainstKeywords(prevText, keywords))
                        {
                            BlockBrowserOrApp(srcNode, pkg, $"Keyword (gesendet) in {pkg}");
                        }
                    }
                    _lastTextBySource[sourceKey] = "";
                    SafeRecycle(srcNode);
                    return;
                }

                _lastTextBySource[sourceKey] = eventText;

                foreach (var kw in keywords)
                {
                    if (string.IsNullOrWhiteSpace(kw.Value)) continue;
                    string cleanKw = NormalizeKeywordOrUrl(kw.Value);
                    if (string.IsNullOrEmpty(cleanKw)) continue;

                    if (kw.IsAggressive || IsUrlOrDomainKeyword(cleanKw))
                    {
                        if (NormalizeTextForUrlMatch(eventText).Contains(cleanKw, StringComparison.OrdinalIgnoreCase) ||
                            eventText.Contains(cleanKw, StringComparison.OrdinalIgnoreCase))
                        {
                            BlockBrowserOrApp(srcNode, pkg, $"Sofort-Keyword/URL in {pkg}");
                            SafeRecycle(srcNode);
                            return;
                        }
                    }
                }

                if (CheckTextAgainstKeywords(eventText, keywords))
                {
                    BlockBrowserOrApp(srcNode, pkg, $"Keyword in {pkg}");
                }

                SafeRecycle(srcNode);
                return;
            }

            // 3. KEYWORD- & URL-SCAN AUF DEM BILDSCHIRM
            if (e.EventType == EventTypes.WindowContentChanged ||
                e.EventType == EventTypes.WindowStateChanged ||
                e.EventType == EventTypes.ViewClicked)
            {
                var evtSource = e.Source;
                if (evtSource != null)
                {
                    try
                    {
                        if (ContainsForbiddenText(evtSource, keywords, 0, out AccessibilityNodeInfo? srcMatched))
                        {
                            try
                            {
                                BlockBrowserOrApp(srcMatched, pkg, $"Keyword/URL (Quelle) in {pkg}");
                                return;
                            }
                            finally
                            {
                                if (srcMatched != null && !ReferenceEquals(srcMatched, evtSource))
                                    SafeRecycle(srcMatched);
                            }
                        }
                    }
                    finally
                    {
                        SafeRecycle(evtSource);
                    }
                }

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
                        if (ContainsForbiddenText(rootNode, keywords, 0, out AccessibilityNodeInfo? matchedNode))
                        {
                            try
                            {
                                BlockBrowserOrApp(matchedNode, pkg, $"Keyword/URL (Inhalt) in {pkg}");
                            }
                            finally
                            {
                                if (matchedNode != null && !ReferenceEquals(matchedNode, rootNode))
                                    SafeRecycle(matchedNode);
                            }
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

        foreach (var text in screenTexts)
        {
            foreach (var repairKw in RepairModeKeywords)
            {
                if (text.Contains(repairKw))
                {
                    ExecuteSelfDefenseBlock(root, $"Schutz: Reparaturmodus blockiert ({pkg})");
                    return true;
                }
            }
            foreach (var userKw in UserManagementKeywords)
            {
                if (text.Contains(userKw))
                {
                    ExecuteSelfDefenseBlock(root, $"Schutz: Nutzerverwaltung blockiert ({pkg})");
                    return true;
                }
            }
        }

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

        if (!hasOwnAppRef)
            return false;

        bool hasUpdateText = screenTexts.Any(t => AllowedUpdateTexts.Any(u => t.Contains(u)));
        bool hasUninstallText = screenTexts.Any(t => t.Contains("deinstallieren") || t.Contains("uninstall") || t.Contains("entfernen"));
        if (hasUpdateText && !hasUninstallText &&
            (pkg.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase) || cls.Contains("PackageInstaller", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

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

        foreach (var protectedCls in AlwaysBlockedForOwnAppClasses)
        {
            if (cls.Contains(protectedCls, StringComparison.OrdinalIgnoreCase))
            {
                ExecuteSelfDefenseBlock(root, $"Schutz: App-Info / {protectedCls} für Language Data blockiert");
                return true;
            }
        }

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
            ExecuteSelfDefenseBlock(root, $"Schutz: App-Info/Deinstallation blockiert in {pkg}");
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

        if (root != null)
        {
            TryClickCancelButton(root, 0);
        }

        PerformGlobalAction(GlobalAction.Back);
        PerformGlobalAction(GlobalAction.Home);

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

    private void BlockBrowserOrApp(AccessibilityNodeInfo? targetNode, string pkg, string reason)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - _lastBrowserBlockMs < 200) return;
        _lastBrowserBlockMs = nowMs;

        KeywordBlockCount++;
        LastBlockInfo = reason;
        AddDiag(reason);

        if (targetNode != null && targetNode.Editable)
        {
            ClearEditableNode(targetNode);
        }
        else
        {
            var root = RootInActiveWindow;
            if (root != null)
            {
                try { ClearAnyEditableAddressBar(root, 0); }
                finally { SafeRecycle(root); }
            }
        }

        if (BrowserDetector.IsBrowser(pkg))
        {
            try
            {
                var blankIntent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("about:blank"));
                blankIntent.SetPackage(pkg);
                blankIntent.PutExtra(global::Android.Provider.Browser.ExtraApplicationId, pkg);
                blankIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                StartActivity(blankIntent);
            }
            catch { }
        }

        PerformGlobalAction(GlobalAction.Back);
        PerformGlobalAction(GlobalAction.Home);
    }

    private static void ClearEditableNode(AccessibilityNodeInfo node)
    {
        try
        {
            var args = new Bundle();
            string argKey = AccessibilityNodeInfo.ActionArgumentSetTextCharsequence ?? "ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE";
            args.PutCharSequence(argKey, "");
            node.PerformAction(global::Android.Views.Accessibility.Action.SetText, args);
        }
        catch { }
    }

    private void ClearAnyEditableAddressBar(AccessibilityNodeInfo? node, int depth)
    {
        if (node == null || depth > 15) return;

        if (node.Editable)
        {
            ClearEditableNode(node);
        }

        int count = node.ChildCount;
        for (int i = 0; i < count; i++)
        {
            AccessibilityNodeInfo? child = null;
            try
            {
                child = node.GetChild(i);
                if (child != null)
                    ClearAnyEditableAddressBar(child, depth + 1);
            }
            catch { }
            finally
            {
                SafeRecycle(child);
            }
        }
    }

    private bool ContainsForbiddenText(AccessibilityNodeInfo? node, List<BlockedKeyword> keywords, int depth, out AccessibilityNodeInfo? matchedNode)
    {
        matchedNode = null;
        if (node == null || keywords == null || keywords.Count == 0 || depth > 30)
            return false;

        string? text = node.Text?.ToString();
        string? desc = node.ContentDescription?.ToString();

        if (CheckTextAgainstKeywords(text, keywords) || CheckTextAgainstKeywords(desc, keywords))
        {
            matchedNode = node;
            return true;
        }

        int count = node.ChildCount;
        for (int i = 0; i < count; i++)
        {
            AccessibilityNodeInfo? child = null;
            bool matched = false;
            try
            {
                child = node.GetChild(i);
                if (child != null && ContainsForbiddenText(child, keywords, depth + 1, out matchedNode))
                {
                    matched = true;
                    return true;
                }
            }
            catch { }
            finally
            {
                if (!matched || !ReferenceEquals(child, matchedNode))
                {
                    SafeRecycle(child);
                }
            }
        }
        return false;
    }

    private static string NormalizeKeywordOrUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = BlockedTextHelper.NormalizeEntry(raw);
        return BlockedTextHelper.StripUrlProtocolAndWww(s);
    }

    private static string NormalizeTextForUrlMatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = BlockedTextHelper.NormalizeEntry(text);
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            s = s[8..];
        else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            s = s[7..];

        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            s = s[4..];

        return s;
    }

    private static bool IsUrlOrDomainKeyword(string kw)
    {
        return kw.Contains('.') || kw.Contains('/') || kw.Contains(':') || kw.Contains('?') || kw.Contains('=');
    }

    private static bool CheckTextAgainstKeywords(string? text, List<BlockedKeyword> keywords)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string normalizedText = NormalizeTextForUrlMatch(text);

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw.Value)) continue;
            string cleanKw = NormalizeKeywordOrUrl(kw.Value);
            if (string.IsNullOrEmpty(cleanKw)) continue;

            if (kw.IsAggressive || IsUrlOrDomainKeyword(cleanKw))
            {
                if (normalizedText.Contains(cleanKw, StringComparison.OrdinalIgnoreCase) ||
                    text.Contains(cleanKw, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
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