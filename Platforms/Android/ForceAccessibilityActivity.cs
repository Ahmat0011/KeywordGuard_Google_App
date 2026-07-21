using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace LanguageData.Platforms.Android;

[Activity(Label = "Schutz aktivieren", Theme = "@style/Maui.SplashTheme", NoHistory = true)]
public class ForceAccessibilityActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CheckAndRedirect();
    }

    protected override void OnResume()
    {
        base.OnResume();
        CheckAndRedirect();
    }

    private void CheckAndRedirect()
    {
        if (ContentGuardService.IsServiceConnected)
        {
            Finish();
            return;
        }

        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
            Toast.MakeText(this, "Bitte aktiviere die Bedienungshilfe für 'Language Data', um den Schutz zu starten.", ToastLength.Long)?.Show();
        }
        catch
        {
            Finish();
        }
    }

    // FIX: Die 3-Versuche-Bremse wurde entfernt. Die Zurück-Taste ist wieder komplett gesperrt.
    // Maximale Sicherheit ist hiermit wiederhergestellt!
#pragma warning disable CA1422
    public override void OnBackPressed()
    {
        // Blockiert das Schließen der Activity, um die Aktivierung zu erzwingen.
    }
#pragma warning restore CA1422
}