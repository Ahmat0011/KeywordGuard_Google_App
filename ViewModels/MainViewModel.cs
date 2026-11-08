#pragma warning disable CA1416, CA1422
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using LanguageData.Models;
using LanguageData.Services;

namespace LanguageData.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _keywordInput = "";
    private string _targetAppInput = "";
    private string _days = "0";
    private string _hours = "0";
    private string _minutes = "30";
    private string _statusText = "";
    private string _countdownText = "";
    private string _accessibilityStatusText = "Bedienungshilfen aktivieren";
    private string _accessibilityButtonColor = "#238636";
    private string _adminStatusText = "Geräteadministrator aktivieren";
    private string _adminButtonColor = "#238636";
    private string _overlayStatusText = "Hintergrund-Overlay erlauben";
    private string _overlayButtonColor = "#6e4017";
    private string _usageStatsStatusText = "Nutzungszugriff (0ms Sofort-Schutz) erlauben";
    private string _usageStatsButtonColor = "#6e4017";
    private bool _isTimerRunning;
    private bool _isCountdownVisible;
    private bool _isAggressiveInput;
    private bool _keywordsVisible = false;
    private DateTime? _endTime;
    private int _protectTapCount;

    // App-Picker Properties
    private bool _isAppPickerVisible;
    private string _appSearchFilter = "";
    private bool _isLoadingApps;
    private List<AppItemModel> _allInstalledApps = new();

    public ObservableCollection<BlockedKeyword> Keywords { get; } = new();
    public ObservableCollection<string> TargetApps { get; } = new();
    public ObservableCollection<AppItemModel> InstalledApps { get; } = new();

    public string KeywordInput { get => _keywordInput; set { _keywordInput = value; OnPropertyChanged(); } }
    public string TargetAppInput { get => _targetAppInput; set { _targetAppInput = value; OnPropertyChanged(); } }
    public bool IsAggressiveInput { get => _isAggressiveInput; set { _isAggressiveInput = value; OnPropertyChanged(); } }
    public string Days { get => _days; set { _days = value; OnPropertyChanged(); } }
    public string Hours { get => _hours; set { _hours = value; OnPropertyChanged(); } }
    public string Minutes { get => _minutes; set { _minutes = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string CountdownText { get => _countdownText; set { _countdownText = value; OnPropertyChanged(); } }

    public string AccessibilityStatusText { get => _accessibilityStatusText; set { _accessibilityStatusText = value; OnPropertyChanged(); } }
    public string AccessibilityButtonColor { get => _accessibilityButtonColor; set { _accessibilityButtonColor = value; OnPropertyChanged(); } }
    public string AdminStatusText { get => _adminStatusText; set { _adminStatusText = value; OnPropertyChanged(); } }
    public string AdminButtonColor { get => _adminButtonColor; set { _adminButtonColor = value; OnPropertyChanged(); } }
    public string OverlayStatusText { get => _overlayStatusText; set { _overlayStatusText = value; OnPropertyChanged(); } }
    public string OverlayButtonColor { get => _overlayButtonColor; set { _overlayButtonColor = value; OnPropertyChanged(); } }
    public string UsageStatsStatusText { get => _usageStatsStatusText; set { _usageStatsStatusText = value; OnPropertyChanged(); } }
    public string UsageStatsButtonColor { get => _usageStatsButtonColor; set { _usageStatsButtonColor = value; OnPropertyChanged(); } }

    public bool IsAppPickerVisible
    {
        get => _isAppPickerVisible;
        set { _isAppPickerVisible = value; OnPropertyChanged(); }
    }

    public bool IsLoadingApps
    {
        get => _isLoadingApps;
        set { _isLoadingApps = value; OnPropertyChanged(); }
    }

    public string AppSearchFilter
    {
        get => _appSearchFilter;
        set
        {
            if (_appSearchFilter != value)
            {
                _appSearchFilter = value;
                OnPropertyChanged();
                FilterInstalledApps();
            }
        }
    }

    public bool IsTimerRunning
    {
        get => _isTimerRunning;
        set
        {
            _isTimerRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartTimer));
            OnPropertyChanged(nameof(CanRemoveItems));
            OnPropertyChanged(nameof(CanClearAll));
        }
    }

    public bool IsCountdownVisible { get => _isCountdownVisible; set { _isCountdownVisible = value; OnPropertyChanged(); } }
    public bool KeywordsVisible { get => _keywordsVisible; set { _keywordsVisible = value; OnPropertyChanged(); } }
    public bool CanStartTimer => !IsTimerRunning;
    public bool CanRemoveItems => !IsTimerRunning;
    public bool CanClearAll => !IsTimerRunning && Keywords.Count > 0;

    public MainViewModel()
    {
        var cfg = ConfigService.LoadConfig();
        bool isLocked = cfg.EndTime.HasValue && DateTime.Now < cfg.EndTime.Value;

        if (isLocked)
        {
            _endTime = cfg.EndTime;
            IsTimerRunning = true;
            IsCountdownVisible = true;
            KeywordsVisible = false;
            StatusText = "🔒 Schutz ist aktiv.";
        }
        else
        {
            KeywordsVisible = false;
            StatusText = "Bereit. Timer ist inaktiv (Voller Zugriff).";
        }

        RefreshPermissionStates();
    }

    public void RefreshPermissionStates()
    {
#if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            bool accOk = ConfigService.IsAccessibilityServiceEnabled();
            AccessibilityStatusText = accOk ? "✓ 1. Bedienungshilfe ist AKTIV" : "⚠ 1. Bedienungshilfe aktivieren (Pflicht)";
            AccessibilityButtonColor = accOk ? "#1f6feb" : "#da3633";

            bool adminOk = Platforms.Android.DeviceAdminReceiver.IsAdminActive(ctx);
            AdminStatusText = adminOk ? "✓ 2. Geräteadministrator ist AKTIV" : "⚠ 2. Geräteadministrator aktivieren (Pflicht)";
            AdminButtonColor = adminOk ? "#1f6feb" : "#da3633";

            bool overlayOk = global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.M ||
                             global::Android.Provider.Settings.CanDrawOverlays(ctx);
            OverlayStatusText = overlayOk ? "✓ 3. Hintergrund-Overlay ist AKTIV" : "⚠ 3. Hintergrund-Overlay erlauben (Pflicht)";
            OverlayButtonColor = overlayOk ? "#1f6feb" : "#da3633";

            bool usageOk = ConfigService.HasUsageStatsPermission();
            UsageStatsStatusText = usageOk ? "✓ 4. Nutzungszugriff (0ms-Wächter) AKTIV" : "⚠ 4. Nutzungszugriff erlauben (Pflicht)";
            UsageStatsButtonColor = usageOk ? "#1f6feb" : "#da3633";
        }
        catch { }
#endif
    }

    /// <summary>
    /// Fügt ein einzelnes Wort oder eine komplette mehrzeilige Liste (untereinander geschrieben) hinzu.
    /// Jede Zeile wird als eigener Eintrag gespeichert (auch Wortgruppen mit Leerzeichen!).
    /// </summary>
    public void AddKeyword()
    {
        if (string.IsNullOrWhiteSpace(KeywordInput)) return;

        var batch = BlockedTextHelper.ParseBatchInput(KeywordInput, IsAggressiveInput);
        if (batch.Count == 0) return;

        var cfg = ConfigService.LoadConfig();
        int addedCount = 0;
        int updatedCount = 0;

        foreach (var item in batch)
        {
            var existing = cfg.Keywords.FirstOrDefault(k => k.Value.Equals(item.Value, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                cfg.Keywords.Add(item);
                if (KeywordsVisible)
                    Keywords.Add(item);
                addedCount++;
            }
            else if (item.IsAggressive && !existing.IsAggressive)
            {
                existing.IsAggressive = true;
                var uiItem = Keywords.FirstOrDefault(k => k.Value.Equals(item.Value, StringComparison.OrdinalIgnoreCase));
                if (uiItem != null) uiItem.IsAggressive = true;
                updatedCount++;
            }
        }

        if (addedCount > 0 || updatedCount > 0)
        {
            ConfigService.SaveConfig(cfg);
            StatusText = IsTimerRunning
                ? $"✓ {addedCount} neu hinzugefügt, {updatedCount} aktualisiert (sofort aktiv!)."
                : $"✓ {addedCount} Wort/Wörter hinzugefügt ({updatedCount} aktualisiert).";
        }
        else
        {
            StatusText = "Alle eingegebenen Wörter existieren bereits!";
        }

        KeywordInput = "";
        OnPropertyChanged(nameof(CanClearAll));
    }

    /// <summary>
    /// Schaltet ein einzelnes Wort in der Liste per Klick zwischen NORMAL und AGGRESSIV um.
    /// </summary>
    public void ToggleKeywordAggressive(BlockedKeyword item)
    {
        if (item == null) return;

        if (IsTimerRunning && item.IsAggressive)
        {
            StatusText = "🔒 Während der Timer läuft, kann 'Aggressiv' nicht auf 'Normal' herabgestuft werden!";
            return;
        }

        bool newState = !item.IsAggressive;
        item.IsAggressive = newState;

        var cfg = ConfigService.LoadConfig();
        var target = cfg.Keywords.FirstOrDefault(k => k.Value.Equals(item.Value, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            target.IsAggressive = newState;
            ConfigService.SaveConfig(cfg);
        }

        StatusText = $"'{item.Value}' ist jetzt {(newState ? "AGGRESSIV (Teilwort)" : "NORMAL (Exakt)")}.";
    }

    /// <summary>
    /// Setzt alle aktuell gespeicherten Wörter auf einmal auf AGGRESSIV oder NORMAL.
    /// </summary>
    public void SetAllKeywordsAggressive(bool makeAggressive)
    {
        if (IsTimerRunning && !makeAggressive)
        {
            StatusText = "🔒 Während der Timer läuft, können Wörter nicht auf 'Normal' herabgestuft werden!";
            return;
        }

        var cfg = ConfigService.LoadConfig();
        if (cfg.Keywords.Count == 0) return;

        foreach (var k in cfg.Keywords)
            k.IsAggressive = makeAggressive;

        ConfigService.SaveConfig(cfg);

        foreach (var uiKw in Keywords)
            uiKw.IsAggressive = makeAggressive;

        StatusText = makeAggressive
            ? $"✓ Alle {cfg.Keywords.Count} Wörter auf AGGRESSIV gesetzt."
            : $"✓ Alle {cfg.Keywords.Count} Wörter auf NORMAL gesetzt.";
    }

    /// <summary>
    /// Fügt Text direkt aus der Zwischenablage in das mehrzeilige Eingabefeld ein.
    /// </summary>
    public async Task PasteFromClipboardAsync()
    {
        try
        {
            if (Clipboard.Default.HasText)
            {
                string? text = await Clipboard.Default.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    KeywordInput = string.IsNullOrWhiteSpace(KeywordInput)
                        ? text.Trim()
                        : KeywordInput.TrimEnd() + Environment.NewLine + text.Trim();

                    int lineCount = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    StatusText = $"📋 {lineCount} Zeile(n) eingefügt. Tippe jetzt auf '+ Liste speichern'!";
                    return;
                }
            }
            StatusText = "Zwischenablage ist leer.";
        }
        catch
        {
            StatusText = "Konnte Zwischenablage nicht lesen.";
        }
    }

    /// <summary>
    /// Kopiert alle gespeicherten Wörter untereinander in die Zwischenablage (für Notizen-Backup).
    /// </summary>
    public async Task ExportKeywordsToClipboardAsync()
    {
        try
        {
            var cfg = ConfigService.LoadConfig();
            if (cfg.Keywords.Count == 0)
            {
                StatusText = "Keine Wörter zum Kopieren vorhanden.";
                return;
            }

            string exportText = BlockedTextHelper.FormatForBackupClipboard(cfg.Keywords);
            await Clipboard.Default.SetTextAsync(exportText);
            StatusText = $"📤 Alle {cfg.Keywords.Count} Wörter in die Zwischenablage kopiert (inkl. [AGG]-Markierung)!";
        }
        catch
        {
            StatusText = "Fehler beim Kopieren in die Zwischenablage.";
        }
    }

    public void RemoveKeyword(BlockedKeyword item)
    {
        if (IsTimerRunning)
        {
            StatusText = "Löschen während aktivem Timer gesperrt!";
            return;
        }

        Keywords.Remove(item);
        var cfg = ConfigService.LoadConfig();
        cfg.Keywords.RemoveAll(k => k.Value.Equals(item.Value, StringComparison.OrdinalIgnoreCase));
        ConfigService.SaveConfig(cfg);
        StatusText = $"'{item.Value}' wurde gelöscht.";
        OnPropertyChanged(nameof(CanClearAll));
    }

    public void ClearAllKeywords()
    {
        if (IsTimerRunning)
        {
            StatusText = "Löschen während aktivem Timer gesperrt!";
            return;
        }

        Keywords.Clear();
        var cfg = ConfigService.LoadConfig();
        cfg.Keywords.Clear();
        ConfigService.SaveConfig(cfg);
        StatusText = "Alle Keywords & URLs gelöscht.";
        OnPropertyChanged(nameof(CanClearAll));
    }

    public void AddTargetApp()
    {
        if (string.IsNullOrWhiteSpace(TargetAppInput)) return;
        string val = TargetAppInput.Trim();

        var cfg = ConfigService.LoadConfig();
        if (cfg.TargetApps.Any(t => t.Equals(val, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "App existiert bereits!";
            return;
        }

        cfg.TargetApps.Add(val);
        ConfigService.SaveConfig(cfg);

        if (KeywordsVisible)
            TargetApps.Add(val);

        var appItem = _allInstalledApps.FirstOrDefault(a => a.PackageName.Equals(val, StringComparison.OrdinalIgnoreCase));
        if (appItem != null)
        {
            appItem.IsSelected = true;
            appItem.BadgeText = "Überwacht";
            appItem.BadgeColor = "#da3633";
        }

        TargetAppInput = "";
        StatusText = IsTimerRunning ? "App hinzugefügt (sofort aktiv!)" : "App hinzugefügt.";
    }

    public void RemoveTargetApp(string item)
    {
        if (IsTimerRunning)
        {
            StatusText = "Löschen während aktivem Timer gesperrt!";
            return;
        }

        if (TargetApps.Contains(item))
        {
            TargetApps.Remove(item);
            var cfg = ConfigService.LoadConfig();
            cfg.TargetApps.RemoveAll(t => t.Equals(item, StringComparison.OrdinalIgnoreCase));
            ConfigService.SaveConfig(cfg);

            var appItem = _allInstalledApps.FirstOrDefault(a => a.PackageName.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (appItem != null)
            {
                appItem.IsSelected = false;
                appItem.BadgeText = appItem.IsSafeExcluded ? "Sicher (KI/Notiz)" : "";
                appItem.BadgeColor = appItem.IsSafeExcluded ? "#3fb950" : "#8b949e";
            }

            StatusText = "App entfernt.";
        }
    }

    public async Task ToggleAppPickerAsync()
    {
        IsAppPickerVisible = !IsAppPickerVisible;
        if (IsAppPickerVisible && _allInstalledApps.Count == 0)
        {
            await LoadInstalledAppsAsync();
        }
    }

    public async Task LoadInstalledAppsAsync()
    {
        IsLoadingApps = true;
        try
        {
            var cfg = ConfigService.LoadConfig();
            var apps = await AppListService.GetInstalledAppsAsync(cfg.TargetApps);
            _allInstalledApps = apps;
            FilterInstalledApps();
        }
        finally
        {
            IsLoadingApps = false;
        }
    }

    private void FilterInstalledApps()
    {
        InstalledApps.Clear();
        string q = _appSearchFilter?.Trim() ?? "";

        var filtered = string.IsNullOrEmpty(q)
            ? _allInstalledApps
            : _allInstalledApps.Where(a =>
                a.AppName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.PackageName.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var app in filtered)
        {
            InstalledApps.Add(app);
        }
    }

    public void ToggleAppItem(AppItemModel item)
    {
        if (item.IsBrowser)
        {
            StatusText = "Alle Webbrowser werden bereits automatisch überwacht!";
            return;
        }

        if (item.IsSelected)
        {
            if (IsTimerRunning)
            {
                StatusText = "Entfernen während aktivem Timer gesperrt!";
                return;
            }

            var cfg = ConfigService.LoadConfig();
            cfg.TargetApps.RemoveAll(t => t.Equals(item.PackageName, StringComparison.OrdinalIgnoreCase));
            ConfigService.SaveConfig(cfg);

            TargetApps.Remove(item.PackageName);
            item.IsSelected = false;
            item.BadgeText = item.IsSafeExcluded ? "Sicher (KI/Notiz)" : "";
            item.BadgeColor = item.IsSafeExcluded ? "#3fb950" : "#8b949e";

            StatusText = $"{item.AppName} entfernt.";
        }
        else
        {
            var cfg = ConfigService.LoadConfig();
            if (!cfg.TargetApps.Any(t => t.Equals(item.PackageName, StringComparison.OrdinalIgnoreCase)))
            {
                cfg.TargetApps.Add(item.PackageName);
                ConfigService.SaveConfig(cfg);
            }

            if (!TargetApps.Contains(item.PackageName))
                TargetApps.Add(item.PackageName);

            item.IsSelected = true;
            item.BadgeText = "Überwacht";
            item.BadgeColor = "#da3633";

            StatusText = IsTimerRunning ? $"{item.AppName} hinzugefügt (sofort aktiv!)" : $"{item.AppName} hinzugefügt.";
        }
    }

    public bool StartTimer()
    {
#if ANDROID
        var ctx = global::Android.App.Application.Context;
        if (!ConfigService.IsAccessibilityServiceEnabled())
        {
            StatusText = "⚠ Bitte zuerst Button 1 (Bedienungshilfe) aktivieren!";
            return false;
        }
        if (!Platforms.Android.DeviceAdminReceiver.IsAdminActive(ctx))
        {
            StatusText = "⚠ Bitte zuerst Button 2 (Geräteadministrator) aktivieren!";
            return false;
        }
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.M &&
            !global::Android.Provider.Settings.CanDrawOverlays(ctx))
        {
            StatusText = "⚠ Bitte zuerst Button 3 (Hintergrund-Overlay) erlauben!";
            return false;
        }
        if (!ConfigService.HasUsageStatsPermission())
        {
            StatusText = "⚠ Bitte zuerst Button 4 (Nutzungszugriff für 0ms-Schutz) erlauben!";
            return false;
        }
#endif

        int d = int.TryParse(Days, out int days) ? days : 0;
        int h = int.TryParse(Hours, out int hours) ? hours : 0;
        int m = int.TryParse(Minutes, out int mins) ? mins : 0;
        int total = (d * 1440) + (h * 60) + m;

        if (total <= 0)
        {
            StatusText = "Bitte mindestens 1 Minute einstellen!";
            return false;
        }

        var cfg = ConfigService.LoadConfig();

        foreach (var kw in Keywords)
        {
            if (!cfg.Keywords.Any(k => k.Value.Equals(kw.Value, StringComparison.OrdinalIgnoreCase)))
                cfg.Keywords.Add(new BlockedKeyword { Value = kw.Value, IsAggressive = kw.IsAggressive });
        }

        cfg.EndTime = DateTime.Now.AddMinutes(total);
        cfg.IsIconHidden = true;
        ConfigService.SaveConfig(cfg);
        ConfigService.SetDeepLinkOpened(false);

        _endTime = cfg.EndTime;
        IsTimerRunning = true;
        IsCountdownVisible = true;
        KeywordsVisible = false;
        IsAppPickerVisible = false;
        StatusText = "🔒 Schutz gestartet!";

        Keywords.Clear();

#if ANDROID
        try
        {
            MainActivity.SetAppIconVisible(false);
            Platforms.Android.DeviceAdminReceiver.EnforceUninstallProtection(global::Android.App.Application.Context);
            Platforms.Android.AccessibilityGuardEnforcer.StartMonitoring(global::Android.App.Application.Context);
        }
        catch { }
#endif
        return true;
    }

    public void UpdateCountdown()
    {
        RefreshPermissionStates();

        if (_endTime.HasValue)
        {
            var ts = _endTime.Value - DateTime.Now;
            if (ts.TotalSeconds > 0)
            {
                CountdownText = $"{ts.Days}d {ts.Hours:00}h {ts.Minutes:00}m {ts.Seconds:00}s";
                IsTimerRunning = true;
                IsCountdownVisible = true;
            }
            else
            {
                OnTimerExpired();
            }
        }
        else
        {
            var cfg = ConfigService.LoadConfig();
            if (cfg.EndTime.HasValue && DateTime.Now < cfg.EndTime.Value)
            {
                _endTime = cfg.EndTime;
                IsTimerRunning = true;
                IsCountdownVisible = true;
                KeywordsVisible = false;
            }
        }
    }

    private void OnTimerExpired()
    {
        IsTimerRunning = false;
        IsCountdownVisible = false;
        KeywordsVisible = false;
        IsAppPickerVisible = false;
        _protectTapCount = 0;
        _endTime = null;

        StatusText = "✓ Timer abgelaufen! Voller Zugriff & Bearbeitung freigeschaltet.";
        CountdownText = "";

        ConfigService.SetDeepLinkOpened(false);

#if ANDROID
        try
        {
            MainActivity.SetAppIconVisible(true);
            Platforms.Android.ContentGuardService.CancelRecoveryAlarm();
            Platforms.Android.AccessibilityGuardEnforcer.StopMonitoring();
            Platforms.Android.DeviceAdminReceiver.EnforceUninstallProtection(global::Android.App.Application.Context);
        }
        catch { }
#endif

        var cfg = ConfigService.LoadConfig();
        cfg.EndTime = null;
        cfg.IsIconHidden = false;
        ConfigService.SaveConfig(cfg);

        Keywords.Clear();
        OnPropertyChanged(nameof(CanClearAll));
    }

    public void HideKeywords()
    {
        KeywordsVisible = false;
        IsAppPickerVisible = false;
        Keywords.Clear();
        TargetApps.Clear();
        InstalledApps.Clear();
        _protectTapCount = 0;
    }

    public void OnProtectButtonTapped()
    {
        _protectTapCount++;

        if (_protectTapCount >= 5)
        {
            _protectTapCount = 0;
            if (KeywordsVisible)
            {
                KeywordsVisible = false;
                IsAppPickerVisible = false;
                Keywords.Clear();
                TargetApps.Clear();
                InstalledApps.Clear();
            }
            else
            {
                var cfg = ConfigService.LoadConfig();
                Keywords.Clear();
                foreach (var kw in cfg.Keywords)
                    Keywords.Add(new BlockedKeyword { Value = kw.Value, IsAggressive = kw.IsAggressive });

                TargetApps.Clear();
                foreach (var ta in cfg.TargetApps)
                    TargetApps.Add(ta);

                KeywordsVisible = true;
                OnPropertyChanged(nameof(CanClearAll));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}