#pragma warning disable CA1416, CA1422
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

/// <summary>
/// Durchgehender Echtzeit-Wächter (80ms-Takt):
/// Läuft ab Timer-Start ununterbrochen im Hintergrund mit.
/// Sobald die Bedienungshilfe ausgeschaltet wird, reagiert die bereits laufende Schleife in 0–80 ms!
/// </summary>
public static class AccessibilityGuardEnforcer
{
    private static Handler? _handler;
    private static Java.Lang.IRunnable? _loopRunnable;
    private static global::Android.Views.View? _overlayView;
    private static IWindowManager? _windowManager;
    private static bool _isMonitoring;
    private static long _lastRedirectMs = 0;
    private static long _fallbackButtonAllowUntilMs = 0;

    public static void StartMonitoring(Context context)
    {
        var appCtx = context.ApplicationContext ?? context;

        if (_handler == null)
            _handler = new Handler(Looper.MainLooper!);

        if (_isMonitoring)
        {
            CheckAndEnforce(appCtx);
            return;
        }

        _isMonitoring = true;
        _loopRunnable = new Java.Lang.Runnable(() =>
        {
            if (!_isMonitoring) return;

            try
            {
                CheckAndEnforce(appCtx);
            }
            catch { }

            if (_isMonitoring)
            {
                // Läuft durchgehend im 80-ms-Takt weiter, solange der Timer aktiv ist!
                _handler?.PostDelayed(_loopRunnable!, 80);
            }
        });

        CheckAndEnforce(appCtx);
        _handler.PostDelayed(_loopRunnable, 80);
    }

    public static void StopMonitoring()
    {
        _isMonitoring = false;
        if (_handler != null && _loopRunnable != null)
        {
            _handler.RemoveCallbacks(_loopRunnable);
        }
        RemoveOverlay();
    }

    /// <summary>
    /// Sofort-Sperre (0 ms): Aktiviert augenblicklich das Vollbild-Overlay und startet die Schleife.
    /// </summary>
    public static void SlamOverlayShut(Context context)
    {
        _fallbackButtonAllowUntilMs = 0;
        var appCtx = context.ApplicationContext ?? context;

        if (_handler == null)
            _handler = new Handler(Looper.MainLooper!);

        _handler.Post(() =>
        {
            ShowBlockingOverlay(appCtx);
            StartMonitoring(appCtx);
        });
    }

    private static void CheckAndEnforce(Context appCtx)
    {
        // 1. Wenn der Timer abgelaufen ist -> Überwachung komplett beenden und alles freigeben!
        if (!ConfigService.IsTimerActive())
        {
            StopMonitoring();
            try { MainActivity.SetAppIconVisible(true); } catch { }
            return;
        }

        // App-Icon während des Timers immer versteckt halten
        MainActivity.SetAppIconVisible(false);

        // 2. Prüfen, ob die Bedienungshilfe SOWOHL verbunden ALS AUCH in den Android-Settings eingeschaltet ist:
        bool isServiceAlive = ContentGuardService.IsServiceConnected &&
                              ContentGuardService.Instance != null &&
                              IsAccessibilitySettingEnabled(appCtx);

        if (isServiceAlive)
        {
            // WICHTIG: Nicht StopMonitoring() aufrufen! Die Schleife bleibt im Hintergrund aktiv,
            // damit sie beim Ausschalten der Bedienungshilfe ohne Verzögerung (0 ms) zuschlagen kann!
            RemoveOverlay();
            try { ForceAccessibilityActivity.CurrentInstance?.Finish(); } catch { }
            return;
        }

        // 3. BEDIENUNGSHILFE IST AUS!
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (ConfigService.HasUsageStatsPermission())
        {
            bool inAllowedSettings = ConfigService.IsUserCurrentlyInAccessibilitySettings(out _, out _);
            if (inAllowedSettings)
            {
                // Nutzer ist im Bedienungshilfen-Menü -> Bildschirm frei lassen zum Einschalten
                if (_overlayView != null && _overlayView.Visibility != ViewStates.Gone)
                {
                    _overlayView.Visibility = ViewStates.Gone;
                }
                return;
            }

            // Nutzer ist NICHT in den Bedienungshilfen (oder hat minimiert/Zurück gedrückt) -> SOFORT SPERREN!
            ShowBlockingOverlay(appCtx);

            if (now - _lastRedirectMs > 300)
            {
                _lastRedirectMs = now;
                LaunchForceActivityIntent(appCtx);
            }
            return;
        }

        // Fallback (falls Nutzungszugriff nicht aktiv ist):
        if (now < _fallbackButtonAllowUntilMs)
        {
            if (_overlayView != null && _overlayView.Visibility != ViewStates.Gone)
            {
                _overlayView.Visibility = ViewStates.Gone;
            }
            return;
        }

        ShowBlockingOverlay(appCtx);
        if (now - _lastRedirectMs > 500)
        {
            _lastRedirectMs = now;
            LaunchForceActivityIntent(appCtx);
        }
    }

    private static bool IsAccessibilitySettingEnabled(Context context)
    {
        try
        {
            string? enabled = Settings.Secure.GetString(
                context.ContentResolver,
                Settings.Secure.EnabledAccessibilityServices);

            string pkg = context.PackageName ?? "com.android.languagedata.service";
            return !string.IsNullOrEmpty(enabled) &&
                   enabled.Contains(pkg, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ShowBlockingOverlay(Context appCtx)
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && !Settings.CanDrawOverlays(appCtx))
            {
                LaunchForceActivityIntent(appCtx);
                return;
            }

            if (_overlayView != null)
            {
                if (_overlayView.Visibility != ViewStates.Visible)
                {
                    _overlayView.Visibility = ViewStates.Visible;
                }
                return;
            }

            _windowManager = appCtx.GetSystemService(Context.WindowService)?.JavaCast<IWindowManager>();
            if (_windowManager == null) return;

            var layout = new LinearLayout(appCtx)
            {
                Orientation = Orientation.Vertical
            };
            layout.SetGravity(GravityFlags.Center);
            layout.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#0D1117"));
            layout.SetPadding(60, 80, 60, 80);

            var title = new TextView(appCtx)
            {
                Text = "🔒 BEDIENUNGSHILFE DEAKTIVIERT",
                TextSize = 23,
                Gravity = GravityFlags.Center
            };
            title.SetTextColor(global::Android.Graphics.Color.ParseColor("#DA3633"));
            title.SetTypeface(null, TypefaceStyle.Bold);

            var msg = new TextView(appCtx)
            {
                Text = "Der Schutz-Timer ist aktiv!\n\nBitte aktiviere jetzt 'Language Data Service' in den Bedienungshilfen.\n\nSolange du in den Bedienungshilfen bleibst, kannst du den Schalter frei umlegen. Sobald du minimierst oder zurückgehst, sperrt sich der Bildschirm sofort wieder.",
                TextSize = 15,
                Gravity = GravityFlags.Center
            };
            msg.SetTextColor(global::Android.Graphics.Color.ParseColor("#E6EDF3"));
            msg.SetPadding(0, 35, 0, 55);

            var btn = new global::Android.Widget.Button(appCtx)
            {
                Text = "👉 Zu den Bedienungshilfen",
                TextSize = 16
            };
            btn.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#238636"));
            btn.SetTextColor(global::Android.Graphics.Color.White);
            btn.SetPadding(40, 35, 40, 35);
            btn.Click += (s, e) =>
            {
                _fallbackButtonAllowUntilMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2500;
                if (_overlayView != null)
                {
                    _overlayView.Visibility = ViewStates.Gone;
                }
                LaunchForceActivityIntent(appCtx);
            };

            layout.AddView(title);
            layout.AddView(msg);
            layout.AddView(btn);

            WindowManagerTypes type = Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone;

            var lp = new WindowManagerLayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent,
                type,
                WindowManagerFlags.LayoutInScreen | WindowManagerFlags.KeepScreenOn | WindowManagerFlags.HardwareAccelerated,
                Format.Translucent)
            {
                Gravity = GravityFlags.Center
            };

            _windowManager.AddView(layout, lp);
            _overlayView = layout;
        }
        catch { }
    }

    public static void RemoveOverlay()
    {
        try
        {
            if (_overlayView != null && _windowManager != null)
            {
                _windowManager.RemoveViewImmediate(_overlayView);
            }
        }
        catch { }
        finally
        {
            _overlayView = null;
        }
    }

    public static Intent CreateAccessibilitySettingsIntent(Context context)
    {
        var intent = new Intent(Settings.ActionAccessibilitySettings);
        try
        {
            var comp = new ComponentName(context.PackageName ?? "com.android.languagedata.service",
                Java.Lang.Class.FromType(typeof(ContentGuardService)).Name);
            string flat = comp.FlattenToString();
            var bundle = new Bundle();
            bundle.PutString(":settings:fragment_args_key", flat);
            intent.PutExtra(":settings:fragment_args_key", flat);
            intent.PutExtra(":settings:show_fragment_args", bundle);
        }
        catch { }
        return intent;
    }

    public static void OpenAccessibilitySettingsDirect(Context context)
    {
        try
        {
            var intent = CreateAccessibilitySettingsIntent(context);
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            context.StartActivity(intent);
        }
        catch { }
    }

    public static void LaunchForceActivityIntent(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(ForceAccessibilityActivity));
            intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop | ActivityFlags.NoAnimation);
            context.StartActivity(intent);
        }
        catch
        {
            OpenAccessibilitySettingsDirect(context);
        }
    }
}

[Activity(
    Label = "Schutz-Aktivierung erforderlich",
    Theme = "@style/Maui.SplashTheme",
    LaunchMode = LaunchMode.SingleTask,
    ExcludeFromRecents = true,
    Exported = false,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class ForceAccessibilityActivity : Activity
{
    public static ForceAccessibilityActivity? CurrentInstance { get; private set; }
    private bool _settingsLaunchedInStack = false;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CurrentInstance = this;

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetGravity(GravityFlags.Center);
        layout.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#0D1117"));
        SetContentView(layout);

        AccessibilityGuardEnforcer.StartMonitoring(this);
    }

    protected override void OnResume()
    {
        base.OnResume();

        if (!ConfigService.IsTimerActive() || ConfigService.IsAccessibilityServiceEnabled())
        {
            Finish();
            return;
        }

        LaunchSettingsInSameTask();
    }

    private void LaunchSettingsInSameTask()
    {
        try
        {
            _settingsLaunchedInStack = true;
            var intent = AccessibilityGuardEnforcer.CreateAccessibilitySettingsIntent(this);
            StartActivityForResult(intent, 1001);
        }
        catch
        {
            AccessibilityGuardEnforcer.OpenAccessibilitySettingsDirect(this);
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        _settingsLaunchedInStack = false;

        if (ConfigService.IsTimerActive() && !ConfigService.IsAccessibilityServiceEnabled())
        {
            AccessibilityGuardEnforcer.SlamOverlayShut(this);
            LaunchSettingsInSameTask();
        }
        else
        {
            Finish();
        }
    }

    protected override void OnUserLeaveHint()
    {
        base.OnUserLeaveHint();
        if (ConfigService.IsTimerActive() && !ConfigService.IsAccessibilityServiceEnabled() && !_settingsLaunchedInStack)
        {
            AccessibilityGuardEnforcer.SlamOverlayShut(this);
        }
    }

    public override void OnBackPressed()
    {
        if (ConfigService.IsTimerActive() && !ConfigService.IsAccessibilityServiceEnabled())
        {
            AccessibilityGuardEnforcer.SlamOverlayShut(this);
            LaunchSettingsInSameTask();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (CurrentInstance == this) CurrentInstance = null;
    }
}