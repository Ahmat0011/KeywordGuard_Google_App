using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using LanguageData.Platforms.Android;

namespace LanguageData.Platforms.Android;

[Activity(Label = "Schutz aktivieren", Theme = "@style/Maui.SplashTheme", NoHistory = true)]
public class ForceAccessibilityActivity : Activity
{
    private static int _redirectAttempts = 0;
    private const int MAX_ATTEMPTS = 3;

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
            _redirectAttempts = 0; // Reset bei Erfolg
            Finish();
            return;
        }

        if (_redirectAttempts >= MAX_ATTEMPTS)
        {
            // Sicherheits-Bremse: Zeige Erklärung statt Endlosschleife
            ShowBlockerExplanation();
            return;
        }

        _redirectAttempts++;
        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
            intent.AddFlags(ActivityFlags.NewTask);
            StartActivity(intent);
            Toast.MakeText(this, $"Bitte aktiviere die Bedienungshilfe für 'Language Data' (Versuch {_redirectAttempts}/{MAX_ATTEMPTS})", ToastLength.Long)?.Show();
        }
        catch
        {
            Finish();
        }
    }

    private void ShowBlockerExplanation()
    {
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Schutz blockiert");
        builder.SetMessage("Der erweiterte Schutz von Android (Advanced Protection) blockiert aktuell die Aktivierung.\n\nBitte deaktiviere den erweiterten Schutz kurz in den Einstellungen, installiere die App neu und lasse 'Eingeschränkte Einstellungen' in den App-Infos zu.");
        builder.SetCancelable(false);
        builder.SetPositiveButton("Verstanden / Beenden", (s, e) =>
        {
            _redirectAttempts = 0; // Reset für den nächsten manuellen Start
            Finish();
        });
        builder.Show();
    }

    public override void OnBackPressed()
    {
        // Zurück-Taste während des Setups nur erlauben, wenn wir in der Sicherheits-Bremse sind
        if (_redirectAttempts >= MAX_ATTEMPTS)
        {
            _redirectAttempts = 0;
            base.OnBackPressed();
        }
    }
}