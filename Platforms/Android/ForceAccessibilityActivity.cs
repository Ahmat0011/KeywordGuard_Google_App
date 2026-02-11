using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

/// <summary>
/// Fullscreen-Activity die den User ZWINGT, die Bedienungshilfe wieder zu aktivieren.
/// Wird gestartet wenn der Service deaktiviert wird waehrend der Timer aktiv ist.
/// Kann NICHT minimiert oder geschlossen werden (Override Back + Pause).
/// Aggressives Re-Launch bei Pause (500ms) + automatische Weiterleitung (1s).
/// </summary>
[Activity(
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop,
    Exported = false,
    NoHistory = false,
    ExcludeFromRecents = true)]
public class ForceAccessibilityActivity : global::Android.App.Activity
{
    private Handler? _handler;
    private bool _redirecting;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Pruefen ob Timer ueberhaupt noch aktiv ist
        if (!ConfigService.IsTimerActive())
        {
            ContentGuardService.CancelRecoveryAlarm();
            Finish();
            return;
        }

        // Pruefen ob Bedienungshilfe bereits aktiv (Race-Condition vermeiden)
        if (IsAccessibilityEnabled())
        {
            ContentGuardService.CancelRecoveryAlarm();
            Finish();
            return;
        }

        // Window-Flags: Ueber Lockscreen anzeigen + Screen anlassen
        try
        {
#pragma warning disable CA1416
            if (Build.VERSION.SdkInt >= BuildVersionCodes.OMr1)
            {
                SetShowWhenLocked(true);
                SetTurnScreenOn(true);
            }
            else
            {
                Window?.AddFlags(WindowManagerFlags.ShowWhenLocked | WindowManagerFlags.TurnScreenOn);
            }
#pragma warning restore CA1416
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            Window?.AddFlags(WindowManagerFlags.DismissKeyguard);
        }
        catch { }

        // Einfaches Layout: Schwarzer Hintergrund mit Warnung
        var layout = new global::Android.Widget.LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        layout.SetGravity(GravityFlags.Center);
        layout.SetBackgroundColor(global::Android.Graphics.Color.Black);
        layout.SetPadding(60, 60, 60, 60);

        var title = new global::Android.Widget.TextView(this)
        {
            Text = "\u26a0 BEDIENUNGSHILFE DEAKTIVIERT",
            TextSize = 22,
            Gravity = GravityFlags.Center
        };
        title.SetTextColor(global::Android.Graphics.Color.Red);
        title.SetPadding(0, 0, 0, 40);

        var msg = new global::Android.Widget.TextView(this)
        {
            Text = "Die Bedienungshilfe wurde deaktiviert.\n\n" +
                   "Der Schutztimer ist noch aktiv.\n\n" +
                   "Bitte aktiviere die Bedienungshilfe erneut,\n" +
                   "um das Geraet normal nutzen zu koennen.\n\n" +
                   "Du wirst automatisch weitergeleitet...",
            TextSize = 16,
            Gravity = GravityFlags.Center
        };
        msg.SetTextColor(global::Android.Graphics.Color.White);
        msg.SetPadding(0, 0, 0, 60);

        var btn = new global::Android.Widget.Button(this)
        {
            Text = "BEDIENUNGSHILFE AKTIVIEREN"
        };
        btn.SetTextColor(global::Android.Graphics.Color.White);
        btn.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#238636"));
        btn.Click += (s, e) => OpenAccessibilitySettings();

        layout.AddView(title);
        layout.AddView(msg);
        layout.AddView(btn);
        SetContentView(layout);

        _handler = new Handler(Looper.MainLooper!);

        // Automatische Weiterleitung nach 1 Sekunde (schneller als vorher)
        _handler.PostDelayed(() => OpenAccessibilitySettings(), 1000);

        // Periodischer Check alle 3 Sekunden: Falls User woanders navigiert,
        // erneut zur Bedienungshilfe weiterleiten
        SchedulePeriodicCheck();
    }

    private void SchedulePeriodicCheck()
    {
        _handler?.PostDelayed(() =>
        {
            try
            {
                if (ConfigService.IsTimerActive() && !IsAccessibilityEnabled())
                {
                    // Erneut weiterleiten wenn nicht auf Accessibility-Einstellungen
                    OpenAccessibilitySettings();
                    SchedulePeriodicCheck();
                }
                else if (!ConfigService.IsTimerActive() || IsAccessibilityEnabled())
                {
                    ContentGuardService.CancelRecoveryAlarm();
                    Finish();
                }
            }
            catch { }
        }, 3000);
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Bei jedem Resume pruefen ob Service inzwischen aktiv
        if (!ConfigService.IsTimerActive() || IsAccessibilityEnabled())
        {
            ContentGuardService.CancelRecoveryAlarm();
            Finish();
            return;
        }

        // Sofort zur Bedienungshilfe weiterleiten bei Resume
        _handler?.PostDelayed(() =>
        {
            if (ConfigService.IsTimerActive() && !IsAccessibilityEnabled())
                OpenAccessibilitySettings();
        }, 500);
    }

    public override void OnBackPressed()
    {
        // BACK blockieren – User MUSS Bedienungshilfe aktivieren
        // Stattdessen Einstellungen oeffnen
        OpenAccessibilitySettings();
    }

    protected override void OnPause()
    {
        base.OnPause();

        // Wenn die Activity pausiert wird (User drueckt Home etc.),
        // SCHNELL wieder in den Vordergrund bringen (500ms statt 2000ms)
        if (ConfigService.IsTimerActive() && !IsAccessibilityEnabled())
        {
            _handler?.PostDelayed(() =>
            {
                try
                {
                    if (ConfigService.IsTimerActive() && !IsAccessibilityEnabled())
                    {
                        var intent = new Intent(this, typeof(ForceAccessibilityActivity));
                        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront
                            | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                        StartActivity(intent);
                    }
                }
                catch { }
            }, 500);
        }
    }

    protected override void OnUserLeaveHint()
    {
        base.OnUserLeaveHint();

        // User drueckt Home-Button → sofort zurueckkommen
        if (ConfigService.IsTimerActive() && !IsAccessibilityEnabled())
        {
            _handler?.PostDelayed(() =>
            {
                try
                {
                    if (ConfigService.IsTimerActive() && !IsAccessibilityEnabled())
                    {
                        var intent = new Intent(this, typeof(ForceAccessibilityActivity));
                        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront
                            | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                        StartActivity(intent);
                    }
                }
                catch { }
            }, 300);
        }
    }

    private void OpenAccessibilitySettings()
    {
        if (_redirecting) return;
        _redirecting = true;

        try
        {
            var intent = new Intent(Settings.ActionAccessibilitySettings);
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
        }
        catch { }

        // Redirecting-Flag nach 1.5 Sekunden zuruecksetzen (schneller)
        _handler?.PostDelayed(() => _redirecting = false, 1500);
    }

    private bool IsAccessibilityEnabled()
    {
        try
        {
            var enabled = Settings.Secure.GetString(
                ContentResolver, Settings.Secure.EnabledAccessibilityServices);
            return enabled?.Contains(PackageName ?? "") == true;
        }
        catch { return false; }
    }
}
