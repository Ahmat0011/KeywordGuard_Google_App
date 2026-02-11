using Android.App;
using Android.Content;
using Android.Provider;
using LanguageData.Services;

namespace LanguageData.Platforms.Android;

/// <summary>
/// BroadcastReceiver der vom AlarmManager getriggert wird.
/// Prueft ob die Bedienungshilfe deaktiviert wurde waehrend der Timer aktiv ist.
/// Wenn ja → startet ForceAccessibilityActivity (Fullscreen-Zwang).
/// Wenn Timer abgelaufen → bricht den Alarm ab.
///
/// KETTEN-ALARM: Plant sich selbst erneut (alle 5 Sekunden) solange
/// Timer aktiv UND Service nicht laeuft. Dadurch haeufigeres Pruefen
/// als mit setRepeating (min 60s) moeglich.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
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

            // Timer noch aktiv?
            if (!ConfigService.IsTimerActive())
            {
                // Timer abgelaufen → Alarm abbrechen, Icon wiederherstellen
                ContentGuardService.CancelRecoveryAlarm();
                try { MainActivity.SetAppIconVisible(true); } catch { }
                return;
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
                // Service NICHT aktiv + Timer aktiv → ForceAccessibilityActivity starten
                global::Android.Util.Log.Warn("LDService",
                    "Recovery: Service disabled while timer active! Starting ForceAccessibilityActivity");

                var activityIntent = new Intent(context, typeof(ForceAccessibilityActivity));
                activityIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
                context.StartActivity(activityIntent);

                // KETTEN-ALARM: Erneut in 5 Sekunden pruefen
                // (da setExactAndAllowWhileIdle nur einmalig feuert)
                ContentGuardService.ScheduleRecoveryAlarm();
            }
            else
            {
                // Service laeuft wieder → Alarm abbrechen
                ContentGuardService.CancelRecoveryAlarm();
            }
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("LDService", $"Recovery receiver error: {ex.Message}");

            // Bei Fehler trotzdem Ketten-Alarm planen (Sicherheit)
            try
            {
                if (ConfigService.IsTimerActive())
                    ContentGuardService.ScheduleRecoveryAlarm();
            }
            catch { }
        }
    }
}
