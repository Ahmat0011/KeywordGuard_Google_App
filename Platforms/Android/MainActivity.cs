#pragma warning disable CA1416, CA1422
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using LanguageData.Platforms.Android;
using LanguageData.Services;

namespace LanguageData;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = false,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "kg",
    DataHost = "secret")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "https",
    DataHost = "languagedata.app",
    DataPathPrefix = "/secret")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        try
        {
            var filesDir = global::Android.App.Application.Context.FilesDir;
            if (filesDir != null)
            {
                CryptoHelper.Initialize(filesDir.AbsolutePath);
            }
        }
        catch { }

        HandleIntent(Intent);

        if (!ConfigService.IsTimerActive())
        {
            try { SetAppIconVisible(true); } catch { }
            ConfigService.SetDeepLinkOpened(false);
            ConfigService.SetIconHidden(false);
            ContentGuardService.CancelRecoveryAlarm();
            AccessibilityGuardEnforcer.StopMonitoring();
        }
        else
        {
            // TIMER IST AKTIV -> App-Icon sofort verstecken!
            SetAppIconVisible(false);

            // 1. Wenn Bedienungshilfe deaktiviert ist (z. B. direkt nach Visual-Studio-Update):
            //    In 0 ms Overlay schließen und ForceAccessibilityActivity starten!
            if (!ConfigService.IsAccessibilityServiceEnabled())
            {
                AccessibilityGuardEnforcer.SlamOverlayShut(this);
                AccessibilityGuardEnforcer.LaunchForceActivityIntent(this);
                base.OnCreate(savedInstanceState);
                Finish();
                return;
            }

            // 2. Wenn NICHT über kg://secret geöffnet -> Sofort minimieren & schließen!
            if (!ConfigService.WasOpenedViaDeepLink())
            {
                MinimizeAndCloseImmediately();
                base.OnCreate(savedInstanceState);
                return;
            }
        }

        base.OnCreate(savedInstanceState);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();

        if (ConfigService.IsTimerActive())
        {
            SetAppIconVisible(false);

            if (!ConfigService.IsAccessibilityServiceEnabled())
            {
                AccessibilityGuardEnforcer.SlamOverlayShut(this);
                AccessibilityGuardEnforcer.LaunchForceActivityIntent(this);
                Finish();
                return;
            }

            if (!ConfigService.WasOpenedViaDeepLink())
            {
                MinimizeAndCloseImmediately();
                return;
            }
        }
    }

    protected override void OnStop()
    {
        base.OnStop();
        if (ConfigService.IsTimerActive())
        {
            ConfigService.SetDeepLinkOpened(false);
        }
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent?.Data == null) return;
        var data = intent.Data;

        if (string.Equals(data.Scheme, "kg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(data.Host, "secret", StringComparison.OrdinalIgnoreCase))
        {
            ConfigService.SetDeepLinkOpened(true);
        }
        else if (string.Equals(data.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(data.Host, "languagedata.app", StringComparison.OrdinalIgnoreCase) &&
                 data.Path?.Contains("secret", StringComparison.OrdinalIgnoreCase) == true)
        {
            ConfigService.SetDeepLinkOpened(true);
        }
    }

    private void MinimizeAndCloseImmediately()
    {
        try
        {
            SetAppIconVisible(false);
            var homeIntent = new Intent(Intent.ActionMain);
            homeIntent.AddCategory(Intent.CategoryHome);
            homeIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            StartActivity(homeIntent);
        }
        catch { }

        try
        {
            FinishAndRemoveTask();
        }
        catch
        {
            Finish();
        }
    }

    public static void SetAppIconVisible(bool visible)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var pm = context.PackageManager;

            var compName = new ComponentName(context, context.PackageName + ".MainLauncher");
            var state = visible ? ComponentEnabledState.Enabled : ComponentEnabledState.Disabled;

            pm?.SetComponentEnabledSetting(compName, state, ComponentEnableOption.DontKillApp);
        }
        catch { }
    }

    public static void RequestDeviceAdmin()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var componentName = new ComponentName(context, Java.Lang.Class.FromType(typeof(DeviceAdminReceiver)));

            var intent = new Intent(global::Android.App.Admin.DevicePolicyManager.ActionAddDeviceAdmin);
            intent.PutExtra(global::Android.App.Admin.DevicePolicyManager.ExtraDeviceAdmin, componentName);
            intent.PutExtra(global::Android.App.Admin.DevicePolicyManager.ExtraAddExplanation, "Administratorrechte verhindern das Deinstallieren der App, solange der Timer aktiv ist.");
            intent.AddFlags(ActivityFlags.NewTask);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
        }
        catch { }
    }

    public static void RequestOverlayPermission()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && !Settings.CanDrawOverlays(context))
            {
                var intent = new Intent(
                    Settings.ActionManageOverlayPermission,
                    global::Android.Net.Uri.Parse("package:" + context.PackageName));
                intent.AddFlags(ActivityFlags.NewTask);
                Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
            }
        }
        catch { }
    }

    public static void RequestUsageStatsPermission()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(
                Settings.ActionUsageAccessSettings,
                global::Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
        }
        catch
        {
            try
            {
                var fallback = new Intent(Settings.ActionUsageAccessSettings);
                fallback.AddFlags(ActivityFlags.NewTask);
                Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(fallback);
            }
            catch { }
        }
    }
}