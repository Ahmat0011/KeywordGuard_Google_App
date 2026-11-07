#pragma warning disable CA1416, CA1422
using Android.App;
using Android.Content;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = true)]
[IntentFilter(new[] {
    Intent.ActionMyPackageReplaced
})]
public class ServiceRecoveryReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;

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
            ContentGuardService.CancelRecoveryAlarm();
            AccessibilityGuardEnforcer.StopMonitoring();
            try
            {
                MainActivity.SetAppIconVisible(true);
                ConfigService.SetIconHidden(false);
                DeviceAdminReceiver.EnforceUninstallProtection(context);
            }
            catch { }
            return;
        }

        // Timer ist AKTIV -> Icon sofort ausblenden & Vollbild-Wächter starten!
        MainActivity.SetAppIconVisible(false);
        ConfigService.SetIconHidden(true);
        DeviceAdminReceiver.EnforceUninstallProtection(context);

        if (!ConfigService.IsAccessibilityServiceEnabled())
        {
            AccessibilityGuardEnforcer.SlamOverlayShut(context);
            AccessibilityGuardEnforcer.OpenAccessibilitySettingsDirect(context);
            ContentGuardService.ScheduleRecoveryAlarm(1500);
        }
        else
        {
            AccessibilityGuardEnforcer.StopMonitoring();
            ContentGuardService.CancelRecoveryAlarm();
        }
    }
}