using Android.App;
using Android.Content;
using Android.Provider;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] {
    Intent.ActionMyPackageReplaced,
    Intent.ActionBootCompleted,
    "android.intent.action.QUICKBOOT_POWERON"
})]
public class ServiceRecoveryReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;

        try
        {
            // CryptoHelper initialisieren (falls noch nicht geschehen)
            try
            {
                var filesDir = global::Android.App.Application.Context.FilesDir;
                CryptoHelper.Initialize(filesDir?.AbsolutePath
                    ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal));
            }
            catch { }

            // Deinstallationsschutz auf Systemebene (falls Device Owner) sofort nach Update/Boot erneuern
            DeviceAdminReceiver.EnforceUninstallProtection(context);

            // Browser-Cache nach App-Update direkt vorladen
            if (intent?.Action == Intent.ActionMyPackageReplaced)
            {
                try { BrowserDetector.PreloadInstalledBrowsers(context); } catch { }
            }

            // Timer noch aktiv?
            if (!ConfigService.IsTimerActive())
            {
                // Timer abgelaufen -> Alarm abbrechen, Icon wiederherstellen
                ContentGuardService.CancelRecoveryAlarm();
                try { MainActivity.SetAppIconVisible(true); } catch { }
                return;
            }

            // Wenn der Timer aktiv ist und die App über Visual Studio aktualisiert wurde,
            // bleibt das App-Icon weiterhin versteckt
            if (intent?.Action == Intent.ActionMyPackageReplaced)
            {
                try
                {
                    MainActivity.SetAppIconVisible(false);
                    ConfigService.SetIconHidden(true);
                }
                catch { }
            }

            // Bedienungshilfe aktiv?
            bool serviceEnabled = false;
            try
            {
                var enabled = Settings.Secure.GetString(
                    context.ContentResolver, Settings.Secure.EnabledAccessibilityServices);
                serviceEnabled = enabled?.Contains(context.PackageName ?? "") == true;
            }
            catch { }

            if (!serviceEnabled)
            {
                if (ConfigService.IsAccessibilityRecoveryGraceActive())
                {
                    global::Android.Util.Log.Info("LDService",
                        "Recovery: grace active, skip force activity relaunch.");
                    ContentGuardService.ScheduleRecoveryAlarm();
                    return;
                }

                global::Android.Util.Log.Warn("LDService",
                    "Recovery: Service disabled while timer active! Starting ForceAccessibilityActivity");

                var activityIntent = new Intent(context, typeof(ForceAccessibilityActivity));
                activityIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                context.StartActivity(activityIntent);

                ContentGuardService.ScheduleRecoveryAlarm();
            }
            else
            {
                ContentGuardService.CancelRecoveryAlarm();
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("LDService", $"Recovery receiver error: {ex.Message}");

            try
            {
                if (ConfigService.IsTimerActive())
                    ContentGuardService.ScheduleRecoveryAlarm();
            }
            catch { }
        }
    }
}