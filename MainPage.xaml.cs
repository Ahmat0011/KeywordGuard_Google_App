using LanguageData.Models;
using LanguageData.Services;
using LanguageData.ViewModels;

namespace LanguageData;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private IDispatcherTimer? _timer;

    public MainPage()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_timer == null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) =>
            {
                _vm.UpdateCountdown();
            };
        }
        _timer.Start();

        _vm.UpdateCountdown();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
        _vm.HideKeywords();
    }

    private void OnStartTimerClicked(object? sender, EventArgs e)
    {
        bool started = _vm.StartTimer();
        if (!started)
        {
            return;
        }

#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var activity = Platform.CurrentActivity;
                if (activity != null)
                {
                    var homeIntent = new Android.Content.Intent(Android.Content.Intent.ActionMain);
                    homeIntent.AddCategory(Android.Content.Intent.CategoryHome);
                    homeIntent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTop);
                    activity.StartActivity(homeIntent);

                    activity.FinishAndRemoveTask();
                }
            }
            catch { }
        });
#endif
    }

    private void OnAddKeywordClicked(object? sender, EventArgs e)
    {
        _vm.AddKeyword();
    }

    private async void OnPasteClipboardClicked(object? sender, EventArgs e)
    {
        await _vm.PasteFromClipboardAsync();
    }

    private async void OnExportClipboardClicked(object? sender, EventArgs e)
    {
        await _vm.ExportKeywordsToClipboardAsync();
    }

    private void OnToggleKeywordModeClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is BlockedKeyword kw)
        {
            _vm.ToggleKeywordAggressive(kw);
        }
    }

    private void OnAllAggressiveClicked(object? sender, EventArgs e)
    {
        _vm.SetAllKeywordsAggressive(true);
    }

    private void OnAllNormalClicked(object? sender, EventArgs e)
    {
        _vm.SetAllKeywordsAggressive(false);
    }

    private void OnRemoveKeywordClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is BlockedKeyword kw)
        {
            _vm.RemoveKeyword(kw);
        }
    }

    private void OnAddTargetAppClicked(object? sender, EventArgs e)
    {
        _vm.AddTargetApp();
    }

    private void OnRemoveTargetAppClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string app)
        {
            _vm.RemoveTargetApp(app);
        }
    }

    private void OnClearAllClicked(object? sender, EventArgs e)
    {
        _vm.ClearAllKeywords();
    }

    private void OnProtectTapped(object? sender, EventArgs e)
    {
        _vm.OnProtectButtonTapped();
    }

    private void OnOpenAccessibilityClicked(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionAccessibilitySettings);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Platform.CurrentActivity?.StartActivity(intent);
        }
        catch { }
#endif
    }

    private void OnOpenDeviceAdminClicked(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            MainActivity.RequestDeviceAdmin();
        }
        catch { }
#endif
    }

    private void OnOpenOverlayClicked(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            MainActivity.RequestOverlayPermission();
        }
        catch { }
#endif
    }

    private void OnOpenUsageStatsClicked(object? sender, EventArgs e)
    {
#if ANDROID
        try
        {
            MainActivity.RequestUsageStatsPermission();
        }
        catch { }
#endif
    }

    private async void OnToggleAppPickerClicked(object? sender, EventArgs e)
    {
        await _vm.ToggleAppPickerAsync();
    }

    private void OnAppItemClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is AppItemModel item)
        {
            _vm.ToggleAppItem(item);
        }
    }
}