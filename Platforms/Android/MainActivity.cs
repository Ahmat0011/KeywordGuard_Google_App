using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
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
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "kg",
    DataHost = "secret")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // CryptoHelper initialisieren BEVOR irgendein Config-Zugriff
        // Android-nativer Pfad fuer garantierte Konsistenz mit AccessibilityService
        var filesDir = Android.App.Application.Context.FilesDir;
        CryptoHelper.Initialize(filesDir?.AbsolutePath
            ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal));

        // SOFORT Icon-Recovery prüfen: Wenn Timer NICHT aktiv, Icon IMMER wiederherstellen
        // MUSS ganz am Anfang sein, sonst landet User in App-Info wenn Timer abgelaufen ist!
        if (!ConfigService.IsTimerActive())
        {
            try { SetAppIconVisible(true); } catch { }
            ConfigService.SetDeepLinkOpened(false);
            ConfigService.SetIconHidden(false);
        }

        // Deep-Link-Intent prüfen
        HandleIntent(Intent);

        // KRITISCHER CHECK: Timer aktiv + kein Deep Link = sofort beenden
        // Finish() VOR base.OnCreate() verhindert App-Info-Redirect
        if (ConfigService.IsTimerActive() && !ConfigService.WasOpenedViaDeepLink())
        {
            Finish();
            return;
        }

        base.OnCreate(savedInstanceState);
        RequestOverlayPermissionIfNeeded();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    protected override void OnResume()
    {
        // Timer aktiv + kein Deep Link = sofort beenden
        if (ConfigService.IsTimerActive() && !ConfigService.WasOpenedViaDeepLink())
        {
            Finish();
            return;
        }

        base.OnResume();

        // Icon-Recovery bei Resume wenn Timer nicht aktiv
        if (!ConfigService.IsTimerActive())
        {
            try { SetAppIconVisible(true); } catch { }
        }

        if (ConfigService.IsTimerActive() && ConfigService.WasOpenedViaDeepLink())
            CheckAccessibilityServiceEnabled();
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent?.Data?.Scheme == "kg" && intent?.Data?.Host == "secret")
            ConfigService.SetDeepLinkOpened(true);
    }

    private void RequestOverlayPermissionIfNeeded()
    {
        try
        {
#pragma warning disable CA1416
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && !Settings.CanDrawOverlays(this))
            {
                var intent = new Intent(Settings.ActionManageOverlayPermission,
                    Android.Net.Uri.Parse($"package:{PackageName}"));
                intent.AddFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
#pragma warning restore CA1416
        }
        catch { }
    }

    private void CheckAccessibilityServiceEnabled()
    {
        try
        {
            var enabled = Settings.Secure.GetString(
                ContentResolver, Settings.Secure.EnabledAccessibilityServices);
            if (enabled?.Contains(PackageName ?? "") != true)
            {
                var intent = new Intent(Settings.ActionAccessibilitySettings);
                intent.AddFlags(ActivityFlags.NewTask);
                StartActivity(intent);
            }
        }
        catch { }
    }

    public static void SetAppIconVisible(bool visible)
    {
        try
        {
            var context = Android.App.Application.Context;
            var pm = context.PackageManager;
            if (pm == null) return;

            var alias = new ComponentName(context,
                "com.android.languagedata.service.MainLauncher");

            pm.SetComponentEnabledSetting(alias,
                visible ? ComponentEnabledState.Enabled : ComponentEnabledState.Disabled,
                ComponentEnableOption.DontKillApp);

            // Launcher zum Refresh zwingen (Xiaomi/MIUI cacht Icon-Zustand aggressiv)
            if (visible)
            {
                try
                {
                    var intent = new Intent(Intent.ActionPackageChanged);
                    intent.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
                    intent.PutExtra(Intent.ExtraDontKillApp, true);
                    context.SendBroadcast(intent);
                }
                catch { }
            }
        }
        catch { }
    }

    public static bool IsDeviceAdminActive()
    {
        try
        {
            var context = Android.App.Application.Context;
            var dpm = (DevicePolicyManager?)context.GetSystemService(Context.DevicePolicyService);
            if (dpm == null) return false;
            var admin = new ComponentName(context,
                Java.Lang.Class.FromType(typeof(Platforms.Android.DeviceAdminReceiver)));
            return dpm.IsAdminActive(admin);
        }
        catch { return false; }
    }

    public static void RequestDeviceAdmin()
    {
        try
        {
            var context = Android.App.Application.Context;
            var admin = new ComponentName(context,
                Java.Lang.Class.FromType(typeof(Platforms.Android.DeviceAdminReceiver)));
            var intent = new Intent(DevicePolicyManager.ActionAddDeviceAdmin);
            intent.PutExtra(DevicePolicyManager.ExtraDeviceAdmin, admin);
            intent.PutExtra(DevicePolicyManager.ExtraAddExplanation,
                "Language Data benötigt Administratorrechte für stabile Systemdienst-Konfiguration.");
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch { }
    }
}
