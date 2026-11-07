#pragma warning disable CA1416, CA1422
using Android.App;
using Android.App.Admin;
using Android.Content;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

[BroadcastReceiver(
    Label = "Language Data Admin",
    Permission = "android.permission.BIND_DEVICE_ADMIN",
    Exported = true)]
[IntentFilter(new[] {
    "android.app.action.DEVICE_ADMIN_ENABLED",
    "android.app.action.DEVICE_ADMIN_DISABLE_REQUESTED",
    "android.app.action.DEVICE_ADMIN_DISABLED"
})]
[MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
public class DeviceAdminReceiver : global::Android.App.Admin.DeviceAdminReceiver
{
    public static ComponentName GetComponentName(Context context)
    {
        return new ComponentName(context, Java.Lang.Class.FromType(typeof(DeviceAdminReceiver)));
    }

    public static bool IsAdminActive(Context context)
    {
        try
        {
            var dpm = (DevicePolicyManager?)context.GetSystemService(Context.DevicePolicyService);
            return dpm != null && dpm.IsAdminActive(GetComponentName(context));
        }
        catch
        {
            return false;
        }
    }

    public static void EnforceUninstallProtection(Context context)
    {
        try
        {
            var dpm = (DevicePolicyManager?)context.GetSystemService(Context.DevicePolicyService);
            var adminComponent = GetComponentName(context);

            if (dpm != null && dpm.IsDeviceOwnerApp(context.PackageName))
            {
                bool blockUninstall = ConfigService.IsTimerActive();
                dpm.SetUninstallBlocked(adminComponent, context.PackageName, blockUninstall);
            }
        }
        catch { }
    }

    public override void OnEnabled(Context context, Intent intent)
    {
        base.OnEnabled(context, intent);
        EnforceUninstallProtection(context);
    }

    public override void OnDisabled(Context context, Intent intent)
    {
        base.OnDisabled(context, intent);

        if (ConfigService.IsTimerActive())
        {
            try
            {
                AccessibilityGuardEnforcer.SlamOverlayShut(context);
                MainActivity.RequestDeviceAdmin();
            }
            catch { }
        }
    }

    public override Java.Lang.ICharSequence OnDisableRequestedFormatted(Context context, Intent intent)
    {
        if (!ConfigService.IsTimerActive())
        {
            return new Java.Lang.String("Der Timer ist abgelaufen. Der Geräteadministrator kann nun deaktiviert werden.");
        }

        // SOFORT-SCHUTZ (funktioniert auch, wenn die Bedienungshilfe gerade AUS ist!):
        try
        {
            // 1. Vollbild-Overlay in 0 ms schließen, damit kein Klick auf "Deaktivieren" möglich ist
            AccessibilityGuardEnforcer.SlamOverlayShut(context);

            // 2. Im AccessibilityService (falls verbunden) Lockdown auslösen
            ContentGuardService.Instance?.TriggerImmediateLockdown("Schutz: Geräteadmin-Deaktivierung blockiert");

            // 3. Auf den Home-Screen zwingen
            var homeIntent = new Intent(Intent.ActionMain);
            homeIntent.AddCategory(Intent.CategoryHome);
            homeIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            context.StartActivity(homeIntent);

            // 4. Hardware-Bildschirm sofort sperren (erfordert <force-lock /> aus device_admin.xml)
            var dpm = (DevicePolicyManager?)context.GetSystemService(Context.DevicePolicyService);
            dpm?.LockNow();
        }
        catch { }

        return new Java.Lang.String("SICHERHEITSSPERRE: Solange der Timer läuft, ist das Deaktivieren und Deinstallieren gesperrt!");
    }
}