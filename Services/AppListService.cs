using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LanguageData.Services;

/// <summary>
/// Repräsentiert eine installierte App für die Klick-Auswahl in der Benutzeroberfläche.
/// </summary>
public class AppItemModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _badgeText = "";
    private string _badgeColor = "#8b949e";

    public string AppName { get; set; } = "";
    public string PackageName { get; set; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(ActionButtonColor));
                OnPropertyChanged(nameof(IsActionButtonEnabled));
            }
        }
    }

    public bool IsBrowser { get; set; }
    public bool IsSafeExcluded { get; set; }

    public string BadgeText
    {
        get => _badgeText;
        set { _badgeText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBadge)); }
    }

    public string BadgeColor
    {
        get => _badgeColor;
        set { _badgeColor = value; OnPropertyChanged(); }
    }

    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    public string ActionText
    {
        get
        {
            if (IsBrowser) return "🌐 Auto-Browser";
            return IsSelected ? "✓ Überwacht" : "+ Überwachen";
        }
    }

    public string ActionButtonColor
    {
        get
        {
            if (IsBrowser) return "#30363d";
            return IsSelected ? "#6e4017" : "#238636";
        }
    }

    public bool IsActionButtonEnabled => !IsBrowser;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Ermittelt die auf dem Android-Gerät installierten Apps für die Auswahl-Liste.
/// </summary>
public static class AppListService
{
    public static async Task<List<AppItemModel>> GetInstalledAppsAsync(List<string> currentTargetApps)
    {
        return await Task.Run(() =>
        {
            var list = new List<AppItemModel>();

#if ANDROID
            try
            {
                var context = global::Android.App.Application.Context;
                var pm = context?.PackageManager;
                if (pm != null)
                {
                    var apps = pm.GetInstalledApplications((global::Android.Content.PM.PackageInfoFlags)0);
                    if (apps != null)
                    {
                        foreach (var app in apps)
                        {
                            if (app == null || string.IsNullOrEmpty(app.PackageName)) continue;

                            // Eigene App nicht in der Liste anzeigen
                            if (string.Equals(app.PackageName, context?.PackageName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Nur Apps mit Launcher-Activity berücksichtigen (echte Nutzer-Apps, keine internen Daemons)
                            var launchIntent = pm.GetLaunchIntentForPackage(app.PackageName);
                            bool isTarget = currentTargetApps.Contains(app.PackageName, StringComparer.OrdinalIgnoreCase);

                            if (launchIntent == null && !isTarget)
                                continue;

                            string appName = app.LoadLabel(pm)?.ToString() ?? app.PackageName;
                            bool isBrowser = Platforms.Android.BrowserDetector.IsBrowser(app.PackageName);
                            bool isSafe = Platforms.Android.BrowserDetector.IsExcluded(app.PackageName);

                            string badge = isBrowser ? "Auto-Browser" : (isSafe ? "Sicher (KI/Notiz)" : (isTarget ? "Überwacht" : ""));
                            string badgeColor = isBrowser ? "#58a6ff" : (isSafe ? "#3fb950" : "#da3633");

                            list.Add(new AppItemModel
                            {
                                AppName = appName,
                                PackageName = app.PackageName,
                                IsBrowser = isBrowser,
                                IsSafeExcluded = isSafe,
                                IsSelected = isTarget,
                                BadgeText = badge,
                                BadgeColor = badgeColor
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("LDService", $"AppList error: {ex.Message}");
            }
#endif

            return list.OrderBy(a => a.AppName).ToList();
        });
    }
}
