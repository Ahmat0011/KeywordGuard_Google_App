using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LanguageData.Models;

/// <summary>
/// Einzelnes Keyword mit Aggressiv-Flag und Live-UI-Benachrichtigung für den Listen-Umschalter.
/// </summary>
public class BlockedKeyword : INotifyPropertyChanged
{
    private string _value = "";
    private bool _isAggressive;

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsAggressive
    {
        get => _isAggressive;
        set
        {
            if (_isAggressive != value)
            {
                _isAggressive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModeText));
                OnPropertyChanged(nameof(ModeColor));
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ModeText => IsAggressive ? "AGGRESSIV" : "NORMAL";

    [System.Text.Json.Serialization.JsonIgnore]
    public string ModeColor => IsAggressive ? "#da3633" : "#238636";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Zentrale App-Konfiguration.
/// </summary>
public class AppConfig
{
    public DateTime? EndTime { get; set; }
    public List<BlockedKeyword> Keywords { get; set; } = new();
    public List<string> TargetApps { get; set; } = new();
    public bool IsIconHidden { get; set; }
}