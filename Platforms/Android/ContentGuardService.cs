using Android.AccessibilityServices;
using Android.App;
using Android.Content;
using Android.Preferences;
using Android.Views.Accessibility;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanguageData.Models;

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

    // ═══════════════════════════════════════════════════════════
    // Statische Diagnose-Properties (von UI lesbar)
    // ═══════════════════════════════════════════════════════════
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

    // Geschuetzte ClassNames: Bei diesen Seiten wird blockiert
    // wenn unsere eigene App auf der Seite referenziert wird
    private static readonly string[] ProtectedClassNames =
    {
        // App-Info (Deinstallation/Deaktivierung)
        "InstalledAppDetails",
        "AppInfoDashboardFragment",
        "InstalledAppDetailsTop",
        "AppInfoActivity",
        "ApplicationDetailsActivity",
        "AppButtonsPreferenceController",
        "AppStorageSettings",
        // Geraete-Admin: NUR die Detail-Seite (nicht die Uebersicht)
        "DeviceAdminAdd",
        // Accessibility: NUR die spezifische Toggle-Seite fuer einzelnen Service
        "ToggleAccessibilityServicePreference",
        // Deinstallation
        "UninstallAppProgress",
        "UninstallerActivity",
        "PackageInstallerActivity",
        "UninstallUninstalling",
        "UninstallAlertDialogFragment",
        // Force-Stop Dialog
        "ForceStopDialogFragment"
    };

    // Gefaehrliche Aktions-Texte in der Settings-App
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

    // Keyboard-/IME-Pakete: Suggestions verursachen False Positives
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

    // ═══════════════════════════════════════════════════════════
    // Text-Tracking fuer Send-Erkennung
    // Wenn ein Textfeld geloescht wird (Nachricht gesendet),
    // pruefen wir den vorherigen Text
    // ═══════════════════════════════════════════════════════════
    private readonly Dictionary<string, string> _lastTextBySource = new();

    // Throttle fuer WindowContentChanged (max alle 2 Sekunden pro Paket)
    private long _lastContentCheckMs = 0;
    private string _lastContentCheckPkg = "";
    private const long CONTENT_CHECK_INTERVAL_MS = 2000;

    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        IsServiceConnected = true;

        // Recovery-Alarm abbrechen (Service laeuft wieder)
        CancelRecoveryAlarm();

        // Wenn Timer aktiv, proaktiv Recovery-Alarm planen
        // (fuer den Fall dass Service spaeter getoetet wird)
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
            string pkgs = info.PackageNames != null
                ? string.Join(",", info.PackageNames)
                : "null (=ALL)";
            AddDiag($"Service CONNECTED. Events={info.EventTypes}, Flags={info.Flags}, Pkgs={pkgs}");
        }
        else
        {
            AddDiag("Service CONNECTED but ServiceInfo is NULL!");
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        IsServiceConnected = false;
        AddDiag("Service DESTROYED");

        // Notfall-Recovery: Wenn Timer aktiv und Service wird getoetet,
        // AlarmManager starten der haeufig prueft und zur Bedienungshilfe weiterleitet
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

    /// <summary>
    /// Setzt einen exakten Alarm der in 5 Sekunden den ServiceRecoveryReceiver triggert.
    /// Der Receiver plant sich selbst erneut (Ketten-Alarm fuer haeufiges Pruefen).
    /// </summary>
    public static void ScheduleRecoveryAlarm()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var am = (AlarmManager?)context.GetSystemService(AlarmService);
            if (am == null) return;

            var intent = new Intent(context, typeof(ServiceRecoveryReceiver));
            var pi = PendingIntent.GetBroadcast(context, 9999, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            if (pi == null) return;

            // Exakter Alarm in 5 Sekunden (wird vom Receiver erneut geplant)
            long triggerMs = Java.Lang.JavaSystem.CurrentTimeMillis() + 5000;
            am.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMs, pi);

            AddDiag("Recovery alarm scheduled (exact, 5s)");
        }
        catch
        {
            // Fallback: setRepeating (min 60s)
            try
            {
                var context = global::Android.App.Application.Context;
                var am = (AlarmManager?)context.GetSystemService(AlarmService);
                if (am == null) return;
                var intent = new Intent(context, typeof(ServiceRecoveryReceiver));
                var pi = PendingIntent.GetBroadcast(context, 9999, intent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                if (pi == null) return;
                am.SetRepeating(AlarmType.RtcWakeup,
                    Java.Lang.JavaSystem.CurrentTimeMillis() + 5000, 60000, pi);
                AddDiag("Recovery alarm fallback (repeating 60s)");
            }
            catch (Exception ex2)
            {
                AddDiag($"Recovery alarm error: {ex2.Message}");
            }
        }
    }

    /// <summary>
    /// Stoppt den Recovery-Alarm (wenn Service wieder laeuft oder Timer abgelaufen)
    /// </summary>
    public static void CancelRecoveryAlarm()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var am = (AlarmManager?)context.GetSystemService(AlarmService);
            if (am == null) return;

            var intent = new Intent(context, typeof(ServiceRecoveryReceiver));
            var pi = PendingIntent.GetBroadcast(context, 9999, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            if (pi != null)
            {
                am.Cancel(pi);
                AddDiag("Recovery alarm cancelled");
            }
        }
        catch { }
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        try
        {
            if (e == null || e.PackageName == null) return;

            TotalEventsReceived++;
            string pkg = e.PackageName.ToString();
            LastEventPackage = pkg;

            // Eigenes Paket ignorieren
            if (pkg == OWN_PACKAGE) return;

            // Config aus SharedPreferences laden
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
            catch (Exception ex)
            {
                AddDiag($"SharedPrefs error: {ex.Message}");
            }

            // Timer-Check: Wenn Timer nicht aktiv → nichts tun
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool timerActive = timerEndMs > 0 && nowMs < timerEndMs;
            if (!timerActive) return;

            // ═══════════════════════════════════════════════════════════
            // A) WINDOW_STATE_CHANGED → App-Info/Admin/Accessibility-Schutz
            //    + Deinstallations-Schutz + Force-Stop-Schutz
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.WindowStateChanged)
            {
                string cls = e.ClassName?.ToString() ?? "";
                AddDiag($"WSC: pkg={pkg}, cls={cls}");

                // Schutz 1: ProtectedClassNames mit OwnAppReference-Check
                foreach (var name in ProtectedClassNames)
                {
                    if (cls.Contains(name))
                    {
                        var r = RootInActiveWindow;
                        if (r != null && ContainsOwnAppReference(r))
                        {
                            AddDiag($"BLOCKED: protected cls={name} in {pkg}");
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz: {name} in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }
                        break;
                    }
                }

                // Schutz 2: Settings- oder PackageInstaller-App → Unser Paket erkennen
                // (fuer Hersteller-spezifische Settings die nicht in ProtectedClassNames sind)
                if (pkg.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase))
                {
                    var root = RootInActiveWindow;
                    if (root != null && ContainsOwnAppReference(root))
                    {
                        if (ContainsDangerousAction(root))
                        {
                            AddDiag($"BLOCKED: dangerous action for our app in {pkg}");
                            KeywordBlockCount++;
                            LastBlockInfo = $"Schutz: Aktion in {pkg}";
                            PerformGlobalAction(GlobalAction.Back);
                            PerformGlobalAction(GlobalAction.Home);
                            return;
                        }
                    }
                }

                // WindowStateChanged → KEIN Keyword-Check (nur Settings-Schutz)
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // B) VIEW_TEXT_CHANGED → Keyword-Blockierung
            //    Prueft den eingegebenen Text gegen alle Keywords.
            //    Erkennt auch gesendete Nachrichten (Textfeld wird geloescht).
            //    Blockiert NUR wenn Wort abgeschlossen (Space/Enter/Satzzeichen).
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.ViewTextChanged)
            {
                // Target-App Check
                List<string>? targetApps = null;
                if (!string.IsNullOrEmpty(targetAppsJson))
                {
                    try { targetApps = JsonSerializer.Deserialize<List<string>>(targetAppsJson, _jsonOptions); }
                    catch { }
                }
                if (!IsTargetApp(pkg, targetApps)) return;

                // Keyboard-/IME-Pakete ignorieren (Suggestion-Bar)
                bool isKeyboard = false;
                foreach (var kb in KeyboardPackages)
                {
                    if (pkg.Contains(kb, StringComparison.OrdinalIgnoreCase))
                    { isKeyboard = true; break; }
                }
                if (isKeyboard) return;

                // Event-Text extrahieren
                string? eventText = null;
                if (e.Text != null && e.Text.Count > 0)
                {
                    var parts = new System.Text.StringBuilder();
                    foreach (var t in e.Text)
                    {
                        if (t != null) parts.Append(t.ToString());
                    }
                    eventText = parts.ToString();
                }

                // Keywords deserialisieren
                List<BlockedKeyword>? keywords = null;
                if (!string.IsNullOrEmpty(keywordsJson))
                {
                    try
                    {
                        keywords = JsonSerializer.Deserialize<List<BlockedKeyword>>(keywordsJson, _jsonOptions);
                    }
                    catch (Exception ex)
                    {
                        AddDiag($"KW deserialize error: {ex.Message}");
                    }
                }
                if (keywords == null || keywords.Count == 0) return;

                // Source-Key fuer Text-Tracking (Paket + ViewId)
                string sourceKey = pkg + ":" + (e.Source?.ViewIdResourceName ?? "unknown");

                // Send-Erkennung: Text leer/sehr kurz und vorher laenger
                // (Benutzer hat Nachricht gesendet → Textfeld geloescht)
                if (string.IsNullOrEmpty(eventText) || eventText.Length <= 1)
                {
                    if (_lastTextBySource.TryGetValue(sourceKey, out string? prevText)
                        && !string.IsNullOrEmpty(prevText) && prevText.Length > 1)
                    {
                        AddDiag($"Send detected: checking previous text ({prevText.Length} chars)");
                        if (CheckTextAgainstKeywords(prevText, keywords))
                        {
                            AddDiag($"BLOCKED: keyword in sent text from {pkg}");
                            KeywordBlockCount++;
                            LastBlockInfo = $"Keyword (gesendet) in {pkg} um {DateTime.Now:HH:mm:ss}";
                            PerformGlobalAction(GlobalAction.Back);
                        }
                    }
                    _lastTextBySource[sourceKey] = "";
                    return;
                }

                // Text speichern fuer naechstes Event
                _lastTextBySource[sourceKey] = eventText;

                // Wort-Completion-Check: Nur blockieren wenn letztes Zeichen ein Trennzeichen ist
                // (Space, Enter, Punkt, Komma, etc.) = User hat Eingabe abgeschlossen
                if (eventText.Length > 0)
                {
                    char lastChar = eventText[eventText.Length - 1];
                    bool isComplete = lastChar == ' ' || lastChar == '\n' || lastChar == '\r' ||
                                      lastChar == '.' || lastChar == ',' || lastChar == '!' ||
                                      lastChar == '?' || lastChar == ';' || lastChar == ':' ||
                                      lastChar == '\t';
                    if (!isComplete)
                    {
                        // Noch nicht fertig getippt → nicht blockieren
                        return;
                    }
                }

                // Keyword-Check auf aktuellen Text (nur bei abgeschlossener Eingabe)
                if (CheckTextAgainstKeywords(eventText, keywords))
                {
                    AddDiag($"BLOCKED: keyword in {pkg}");
                    KeywordBlockCount++;
                    LastBlockInfo = $"Keyword in {pkg} um {DateTime.Now:HH:mm:ss}";
                    PerformGlobalAction(GlobalAction.Back);
                }
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // C) WINDOW_CONTENT_CHANGED → Throttled Keyword-Scan
            //    Prueft den sichtbaren Text im Accessibility-Tree.
            //    Faengt Nachrichten ab die im Chat-Fenster erscheinen.
            //    Throttled: max alle 2 Sekunden pro Paket.
            // ═══════════════════════════════════════════════════════════
            if (e.EventType == EventTypes.WindowContentChanged)
            {
                // Keyboard-Pakete ignorieren
                bool isKb = false;
                foreach (var kb in KeyboardPackages)
                {
                    if (pkg.Contains(kb, StringComparison.OrdinalIgnoreCase))
                    { isKb = true; break; }
                }
                if (isKb) return;

                // Settings-Pakete → Schutz-Check statt Keyword-Check
                if (pkg.Contains("settings", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("packageinstaller", StringComparison.OrdinalIgnoreCase))
                {
                    var root = RootInActiveWindow;
                    if (root != null && ContainsOwnAppReference(root) && ContainsDangerousAction(root))
                    {
                        AddDiag($"BLOCKED (WCC): dangerous action for our app in {pkg}");
                        KeywordBlockCount++;
                        LastBlockInfo = $"Schutz (WCC): {pkg}";
                        PerformGlobalAction(GlobalAction.Back);
                        PerformGlobalAction(GlobalAction.Home);
                    }
                    return;
                }

                // Target-App Check
                List<string>? targetApps2 = null;
                if (!string.IsNullOrEmpty(targetAppsJson))
                {
                    try { targetApps2 = JsonSerializer.Deserialize<List<string>>(targetAppsJson, _jsonOptions); }
                    catch { }
                }
                if (!IsTargetApp(pkg, targetApps2)) return;

                // Throttle: Max alle 2 Sekunden pro Paket
                if (pkg == _lastContentCheckPkg && nowMs - _lastContentCheckMs < CONTENT_CHECK_INTERVAL_MS)
                    return;

                // Keywords deserialisieren
                List<BlockedKeyword>? keywords2 = null;
                if (!string.IsNullOrEmpty(keywordsJson))
                {
                    try
                    {
                        keywords2 = JsonSerializer.Deserialize<List<BlockedKeyword>>(keywordsJson, _jsonOptions);
                    }
                    catch { }
                }
                if (keywords2 == null || keywords2.Count == 0) return;

                _lastContentCheckMs = nowMs;
                _lastContentCheckPkg = pkg;

                // Accessibility-Tree scannen
                var rootNode = RootInActiveWindow;
                if (rootNode != null && ContainsForbiddenText(rootNode, keywords2))
                {
                    AddDiag($"BLOCKED (WCC): keyword found in tree of {pkg}");
                    KeywordBlockCount++;
                    LastBlockInfo = $"Keyword (Inhalt) in {pkg} um {DateTime.Now:HH:mm:ss}";
                    PerformGlobalAction(GlobalAction.Back);
                }
                return;
            }
        }
        catch (Exception ex)
        {
            AddDiag($"ERROR: {ex.Message}");
        }
    }

    public override void OnInterrupt()
    {
        AddDiag("Service INTERRUPTED");
    }

    private bool ContainsOwnAppReference(AccessibilityNodeInfo node)
    {
        if (node == null) return false;
        string? text = node.Text?.ToString();
        string? desc = node.ContentDescription?.ToString();

        if (text != null)
            foreach (var id in OwnAppIdentifiers)
                if (text.Contains(id, StringComparison.OrdinalIgnoreCase)) return true;

        if (desc != null)
            foreach (var id in OwnAppIdentifiers)
                if (desc.Contains(id, StringComparison.OrdinalIgnoreCase)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsOwnAppReference(child)) return true;
        }
        return false;
    }

    /// <summary>
    /// Prueft ob der Accessibility-Tree gefaehrliche Aktions-Buttons enthaelt
    /// (Deinstallieren, Deaktivieren, Beenden erzwingen, etc.)
    /// </summary>
    private bool ContainsDangerousAction(AccessibilityNodeInfo node)
    {
        if (node == null) return false;
        string? text = node.Text?.ToString()?.ToLowerInvariant();
        string? desc = node.ContentDescription?.ToString()?.ToLowerInvariant();

        if (text != null)
            foreach (var action in DangerousActionTexts)
                if (text.Contains(action)) return true;

        if (desc != null)
            foreach (var action in DangerousActionTexts)
                if (desc.Contains(action)) return true;

        for (int i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && ContainsDangerousAction(child)) return true;
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

    /// <summary>
    /// Prueft einen Text gegen alle Keywords.
    ///
    /// AGGRESSIV (IsAggressive=true):
    ///   Multi-Wort-Keywords werden in einzelne Woerter gesplittet.
    ///   Jedes einzelne Wort triggert die Blockierung (Substring-Match).
    ///   Beispiel: Keyword "hallo gut" → "hallo" blockt, "gut" blockt,
    ///   "hallo gut" blockt, "gut hallo" blockt.
    ///   Keyword "an" → jeder Text der "an" enthaelt blockt (z.B. "Antwort").
    ///
    /// NON-AGGRESSIV (IsAggressive=false):
    ///   Die exakte Phrase muss als vollstaendige Woerter vorkommen.
    ///   Beispiel: Keyword "hallo gut" → nur "hallo gut" (mit Wortgrenzen) blockt.
    ///   "hallo guten" blockt NICHT. "hallo" allein blockt NICHT.
    /// </summary>
    private static bool CheckTextAgainstKeywords(string? text, List<BlockedKeyword> keywords)
    {
        if (text == null) return false;

        foreach (var kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw.Value)) continue;

            if (kw.IsAggressive)
            {
                // AGGRESSIV: Harte Contains-Pruefung
                // Wenn der Text die exakte Buchstabenfolge enthalt, blockieren.
                if (text.Contains(kw.Value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // NON-AGGRESSIV: Exakte Phrase mit Wortgrenzen
                try
                {
                    string pattern = @"\b" + Regex.Escape(kw.Value) + @"\b";
                    if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                        return true;
                }
                catch
                {
                    // Sicherer Fallback: Manuelle Wortgrenzen-Pruefung
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

    private bool IsTargetApp(string pkg, List<string>? userTargetApps)
    {
        if (pkg.Contains("browser", StringComparison.OrdinalIgnoreCase)) return true;
        if (pkg.Contains("web", StringComparison.OrdinalIgnoreCase)) return true;

        if (userTargetApps != null && userTargetApps.Contains(pkg, StringComparer.OrdinalIgnoreCase)) return true;

        return false;
    }
}
