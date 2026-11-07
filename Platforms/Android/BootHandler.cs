using Android.App;
using Android.Content;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[]
{
    Intent.ActionBootCompleted,
    "android.intent.action.QUICKBOOT_POWERON",
    "com.htc.intent.action.QUICKBOOT_POWERON"
})]
public class BootHandler : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        string? action = intent.Action;
        if (action != Intent.ActionBootCompleted &&
            action != "android.intent.action.QUICKBOOT_POWERON" &&
            action != "com.htc.intent.action.QUICKBOOT_POWERON")
            return;

        try
        {
            var filesDir = global::Android.App.Application.Context.FilesDir;
            if (filesDir != null)
            {
                CryptoHelper.Initialize(filesDir.AbsolutePath);
            }
        }
        catch
        {
            // Falls das Gerät noch im Direct-Boot (vor erster PIN-Eingabe) ist: kurz warten
            return;
        }

        if (ConfigService.IsTimerActive())
        {
            MainActivity.SetAppIconVisible(false);
            DeviceAdminReceiver.EnforceUninstallProtection(context);

            if (!ConfigService.IsAccessibilityServiceEnabled())
            {
                try
                {
                    var activityIntent = new Intent(context, typeof(ForceAccessibilityActivity));
                    activityIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                    context.StartActivity(activityIntent);
                }
                catch { }

                ContentGuardService.ScheduleRecoveryAlarm(2500);
            }
        }
        else
        {
            MainActivity.SetAppIconVisible(true);
            ContentGuardService.CancelRecoveryAlarm();
        }
    }
}