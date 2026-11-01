using Android.App;
using Android.App.Admin;
using Android.Content;

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
                dpm.SetUninstallBlocked(adminComponent, context.PackageName, true);
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

        // Keine 120s-Grace-Period mehr! Stattdessen sofort wieder die Aktivierung anfordern
        try
        {
            MainActivity.RequestDeviceAdmin();
        }
        catch { }
    }

    public override Java.Lang.ICharSequence OnDisableRequestedFormatted(Context context, Intent intent)
    {
        // Sofort den Schutz im AccessibilityService auslösen und Einstellungen schließen!
        try
        {
            ContentGuardService.Instance?.TriggerImmediateLockdown("Schutz: Geräteadmin-Deaktivierung blockiert");

            var homeIntent = new Intent(Intent.ActionMain);
            homeIntent.AddCategory(Intent.CategoryHome);
            homeIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            context.StartActivity(homeIntent);
        }
        catch { }

        return new Java.Lang.String("SICHERHEITSSPERRE: Das Deaktivieren des Geräteadministrators ist blockiert!");
    }
}