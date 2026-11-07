#pragma warning disable CA1416, CA1422
using Android.App;
using Android.Runtime;
using LanguageData.Platforms.Android;
using LanguageData.Services;

namespace LanguageData;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        try
        {
            var filesDir = Context?.FilesDir;
            if (filesDir != null)
            {
                CryptoHelper.Initialize(filesDir.AbsolutePath);
            }
        }
        catch { }

        // Sofortige Prüfung (0 ms) beim Prozessstart (z. B. direkt nach Visual-Studio-Installation)
        try
        {
            if (ConfigService.IsTimerActive())
            {
                MainActivity.SetAppIconVisible(false);
                DeviceAdminReceiver.EnforceUninstallProtection(this);
                AccessibilityGuardEnforcer.StartMonitoring(this);
            }
        }
        catch { }
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}