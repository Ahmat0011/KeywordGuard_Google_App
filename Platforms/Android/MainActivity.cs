using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using LanguageData.Services;

namespace LanguageData;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = false, // MUSS false bleiben, da der XML-Alias in der AndroidManifest.xml als Launcher dient!
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

        if (!ConfigService.IsTimerActive())
        {
            try { SetAppIconVisible(true); } catch { }
            ConfigService.SetDeepLinkOpened(false);
            ConfigService.SetIconHidden(false);
        }

        HandleIntent(Intent);

        if (ConfigService.IsTimerActive() && !ConfigService.WasOpenedViaDeepLink())
        {
            Finish();
            return;
        }

        base.OnCreate(savedInstanceState);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent?.Data == null) return;
        var data = intent.Data;

        if (data.Scheme == "kg" && data.Host == "secret")
        {
            ConfigService.SetDeepLinkOpened(true);
        }

        if (data.Scheme == "https" && data.Host == "languagedata.app" && data.Path?.Contains("secret") == true)
        {
            ConfigService.SetDeepLinkOpened(true);
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

            // FIX: Nutzt jetzt den sicheren Type-Verweis für MAUI statt des fehleranfälligen Strings
            var componentName = new ComponentName(context, Java.Lang.Class.FromType(typeof(LanguageData.Platforms.Android.DeviceAdminReceiver)));

            var intent = new Intent(global::Android.App.Admin.DevicePolicyManager.ActionAddDeviceAdmin);
            intent.PutExtra(global::Android.App.Admin.DevicePolicyManager.ExtraDeviceAdmin, componentName);
            intent.PutExtra(global::Android.App.Admin.DevicePolicyManager.ExtraAddExplanation, "Administratorrechte werden fuer den Deinstallationsschutz benoetigt.");
            intent.AddFlags(ActivityFlags.NewTask);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
        }
        catch { }
    }
}