using Android.App;
using Android.Content;
using Android.Provider;
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

        // CryptoHelper initialisieren (wird fuer ConfigService benoetigt)
        try
        {
            var filesDir = global::Android.App.Application.Context.FilesDir;
            CryptoHelper.Initialize(filesDir?.AbsolutePath
                ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal));
        }
        catch { }

        if (ConfigService.IsTimerActive())
        {
            // Timer aktiv → Icon versteckt halten
            MainActivity.SetAppIconVisible(false);

            // Bedienungshilfe pruefen
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
                    ContentGuardService.ScheduleRecoveryAlarm();
                    return;
                }

                // Bedienungshilfe NICHT aktiv nach Neustart
                // (Safe Mode, manuell deaktiviert, oder System-Reset)
                // → Aggressiv: ForceAccessibilityActivity + Recovery-Alarm
                global::Android.Util.Log.Warn("LDService",
                    "Boot: Timer active but accessibility disabled! Starting recovery.");

                try
                {
                    // ForceAccessibilityActivity starten (Fullscreen-Zwang)
                    var activityIntent = new Intent(context, typeof(ForceAccessibilityActivity));
                    activityIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                    context.StartActivity(activityIntent);
                }
                catch
                {
                    // Fallback: Direkt zu Bedienungshilfe-Einstellungen
                    try
                    {
                        var i = new Intent(Settings.ActionAccessibilitySettings);
                        i.AddFlags(ActivityFlags.NewTask);
                        context.StartActivity(i);
                    }
                    catch { }
                }

                // Recovery-Alarm setzen (falls Activity geschlossen wird)
                ContentGuardService.ScheduleRecoveryAlarm();
            }
        }
        else
        {
            // Timer abgelaufen → Icon wiederherstellen, Alarm abbrechen
            MainActivity.SetAppIconVisible(true);
            ContentGuardService.CancelRecoveryAlarm();
        }
    }
}
