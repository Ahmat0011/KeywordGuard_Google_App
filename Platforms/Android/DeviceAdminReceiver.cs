using Android.App;
using Android.App.Admin;
using Android.Content;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

[BroadcastReceiver(
    Label = "Language Data Admin",
    Permission = "android.permission.BIND_DEVICE_ADMIN",
    Exported = true)]
[IntentFilter(new[] { "android.app.action.DEVICE_ADMIN_ENABLED" })]
[MetaData("android.app.device_admin", Resource = "@xml/device_admin")]
public class DeviceAdminReceiver : global::Android.App.Admin.DeviceAdminReceiver
{
    public override void OnEnabled(Context context, Intent intent)
    {
        base.OnEnabled(context, intent);
    }

    public override void OnDisabled(Context context, Intent intent)
    {
        base.OnDisabled(context, intent);
        if (ConfigService.IsTimerActive())
        {
            try
            {
                ConfigService.SetAccessibilityRecoveryGracePeriodSeconds(120);
                var i = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
                i.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(i);
            }
            catch { }
        }
    }

    public override Java.Lang.ICharSequence OnDisableRequestedFormatted(Context context, Intent intent)
    {
        if (ConfigService.IsTimerActive())
        {
            return new Java.Lang.String(
                "WARNUNG: Schutz-Timer aktiv! Deaktivieren entfernt den Deinstallationsschutz.");
        }
        return base.OnDisableRequestedFormatted(context, intent) ?? new Java.Lang.String("");
    }
}
