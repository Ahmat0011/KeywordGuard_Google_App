using LanguageData.Services;

namespace LanguageData;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Initialize encryption with Android-native FilesDir path
#if ANDROID
        var filesDir = Android.App.Application.Context.FilesDir;
        CryptoHelper.Initialize(filesDir?.AbsolutePath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal));
#else
        CryptoHelper.Initialize(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal));
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
