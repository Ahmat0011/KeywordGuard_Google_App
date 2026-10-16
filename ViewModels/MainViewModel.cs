using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private string _serviceStatusText = "";
    private string _serviceStatusColor = "#0d1117";
    private bool _isTimerRunning;
    private bool _isCountdownVisible;
    private bool _isAggressiveInput;
    private bool _keywordsVisible = false;
    private bool _isProtected;
    private bool _isTrapActive;
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

    // Properties
    public string KeywordInput { get => _keywordInput; set { _keywordInput = value; OnPropertyChanged(); } }
    public string TargetAppInput { get => _targetAppInput; set { _targetAppInput = value; OnPropertyChanged(); } }
    public bool IsAggressiveInput { get => _isAggressiveInput; set { _isAggressiveInput = value; OnPropertyChanged(); } }
    public string Days { get => _days; set { _days = value; OnPropertyChanged(); } }
    public string Hours { get => _hours; set { _hours = value; OnPropertyChanged(); } }
    public string Minutes { get => _minutes; set { _minutes = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string CountdownText { get => _countdownText; set { _countdownText = value; OnPropertyChanged(); } }
    public string ServiceStatusText { get => _serviceStatusText; set { _serviceStatusText = value; OnPropertyChanged(); } }
    public string ServiceStatusColor { get => _serviceStatusColor; set { _serviceStatusColor = value; OnPropertyChanged(); } }

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
    public bool IsProtected { get => _isProtected; set { _isProtected = value; OnPropertyChanged(); } }
    public bool IsTrapActive { get => _isTrapActive; set { _isTrapActive = value; OnPropertyChanged(); } }
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
            IsProtected = true;
            IsTrapActive = true;
            KeywordsVisible = false;
            StatusText = "";
            ServiceStatusText = "";
            ServiceStatusColor = "#0d1117";
        }
        else
        {
            KeywordsVisible = false;
            IsProtected = true;
            StatusText = "";
        }
    }

    public void AddKeyword()
    {
        if (string.IsNullOrWhiteSpace(KeywordInput)) return;
        string val = KeywordInput.Trim();

        var cfg = ConfigService.LoadConfig();
        if (cfg.Keywords.Any(k => k.Value.Equals(val, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "Keyword existiert bereits!";
            return;
        }

        cfg.Keywords.Add(new BlockedKeyword { Value = val, IsAggressive = IsAggressiveInput });
        ConfigService.SaveConfig(cfg);

        if (KeywordsVisible)
            Keywords.Add(new BlockedKeyword { Value = val, IsAggressive = IsAggressiveInput });

        KeywordInput = "";
        IsAggressiveInput = false;
        StatusText = IsTimerRunning ? "Keyword hinzugefuegt (sofort aktiv!)" : "Keyword hinzugefuegt.";
        OnPropertyChanged(nameof(CanClearAll));
    }

    public void RemoveKeyword(BlockedKeyword item)
    {
        if (IsTimerRunning)
        {
            StatusText = "Loeschen waehrend Sperre nicht moeglich!";
            return;
        }

        if (Keywords.Contains(item))
        {
            Keywords.Remove(item);
            var cfg = ConfigService.LoadConfig();
            cfg.Keywords.RemoveAll(k => k.Value == item.Value);
            ConfigService.SaveConfig(cfg);
        }
        OnPropertyChanged(nameof(CanClearAll));
    }

    public void ClearAllKeywords()
    {
        if (IsTimerRunning)
        {
            StatusText = "Loeschen waehrend Sperre nicht moeglich!";
            return;
        }

        Keywords.Clear();
        var cfg = ConfigService.LoadConfig();
        cfg.Keywords.Clear();
        ConfigService.SaveConfig(cfg);
        StatusText = "Alle Keywords geloescht.";
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

        // Im App-Picker ebenfalls synchronisieren
        var appItem = _allInstalledApps.FirstOrDefault(a => a.PackageName.Equals(val, StringComparison.OrdinalIgnoreCase));
        if (appItem != null)
        {
            appItem.IsSelected = true;
            appItem.BadgeText = "Überwacht";
            appItem.BadgeColor = "#da3633";
        }

        TargetAppInput = "";
        StatusText = IsTimerRunning ? "App hinzugefuegt (sofort aktiv!)" : "App hinzugefuegt.";
    }

    public void RemoveTargetApp(string item)
    {
        if (IsTimerRunning)
        {
            StatusText = "Loeschen waehrend Sperre nicht moeglich!";
            return;
        }

        if (TargetApps.Contains(item))
        {
            TargetApps.Remove(item);
            var cfg = ConfigService.LoadConfig();
            cfg.TargetApps.RemoveAll(t => t.Equals(item, StringComparison.OrdinalIgnoreCase));
            ConfigService.SaveConfig(cfg);

            // Im App-Picker ebenfalls synchronisieren
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
            StatusText = "Webbrowser werden bereits automatisch lückenlos geschützt!";
            return;
        }

        if (item.IsSelected)
        {
            // Abwählen / Entfernen
            if (IsTimerRunning)
            {
                StatusText = "Loeschen waehrend Sperre nicht moeglich!";
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
            // Auswählen / Hinzufügen
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

    public void StartTimer()
    {
        int d = int.TryParse(Days, out int days) ? days : 0;
        int h = int.TryParse(Hours, out int hours) ? hours : 0;
        int m = int.TryParse(Minutes, out int mins) ? mins : 0;
        int total = (d * 1440) + (h * 60) + m;

        if (total <= 0) { StatusText = "Zeit zu kurz!"; return; }

        var cfg = ConfigService.LoadConfig();

        // Session-Keywords zur Config hinzufuegen
        foreach (var kw in Keywords)
        {
            if (!cfg.Keywords.Any(k => k.Value.Equals(kw.Value, StringComparison.OrdinalIgnoreCase)))
                cfg.Keywords.Add(new BlockedKeyword { Value = kw.Value, IsAggressive = kw.IsAggressive });
        }

        cfg.EndTime = DateTime.Now.AddMinutes(total);
        cfg.IsIconHidden = true;
        ConfigService.SaveConfig(cfg);

        _endTime = cfg.EndTime;
        IsTimerRunning = true;
        IsCountdownVisible = true;
        IsProtected = true;
        IsTrapActive = true;
        KeywordsVisible = false;
        IsAppPickerVisible = false;
        ServiceStatusText = "";
        ServiceStatusColor = "#0d1117";
        StatusText = "";

        Keywords.Clear();

#if ANDROID
        try { MainActivity.SetAppIconVisible(false); } catch { }
#endif
    }

    public void UpdateCountdown()
    {
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
                IsProtected = true;
                IsTrapActive = true;
                KeywordsVisible = false;
                ServiceStatusText = "";
                ServiceStatusColor = "#0d1117";
                StatusText = "";
            }
        }
    }

    private void OnTimerExpired()
    {
        IsTimerRunning = false;
        IsCountdownVisible = false;
        IsProtected = true;
        IsTrapActive = false;
        KeywordsVisible = false;
        IsAppPickerVisible = false;
        _protectTapCount = 0;
        _endTime = null;

        ServiceStatusText = "";
        ServiceStatusColor = "#0d1117";
        StatusText = "";
        CountdownText = "";

        ConfigService.SetDeepLinkOpened(false);

#if ANDROID
        try { MainActivity.SetAppIconVisible(true); } catch { }
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
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
