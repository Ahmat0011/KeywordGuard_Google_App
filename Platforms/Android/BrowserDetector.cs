using Android.Content;
using Android.Content.PM;
using System.Collections.Concurrent;

namespace LanguageData.Platforms.Android;

/// <summary>
/// Intelligente und hochperformante Erkennung aller Web-Browser auf dem Gerät.
/// - Automatische Erkennung JEDES installierten Browsers (auch tausende neue aus dem Play Store).
/// - Dynamische Überprüfung via Android PackageManager (Intent https:// mit BROWSABLE auf neutraler Test-Domain).
/// - Schneller HashSet-Cache für bekannte Browser zur sofortigen Zuordnung.
/// - Striktes Whitelisting/Ausschluss für KI-Apps (Gemini, ChatGPT etc.), Notiz-Apps (Google Keep etc.),
///   E-Mail-Apps (Gmail etc.) und Messaging-Apps, sodass dort unter keinen Umständen versehentliche Blockaden entstehen.
/// </summary>
public static class BrowserDetector
{
    private static readonly ConcurrentDictionary<string, bool> _browserCache = new(StringComparer.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════
    // 1. EXPLIZITE AUSNAHME-LISTE (DÜRFEN NIEMALS ALS BROWSER BLOCKIERT WERDEN)
    // ═══════════════════════════════════════════════════════════
    private static readonly HashSet<string> ExcludedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        // KI- / AI-Apps
        "com.google.android.apps.bard",             // Google Gemini
        "com.openai.chatgpt",                       // ChatGPT
        "com.anthropic.claude",                     // Claude
        "com.microsoft.copilot",                    // Microsoft Copilot
        "ai.perplexity.app.android",                // Perplexity AI
        "com.poe.android",                          // Poe - Fast AI Chat
        "com.characterai.chat",                     // Character.ai
        "ai.replika.app",                           // Replika

        // E-Mail- & Kommunikations-Apps (E-Mail-Adressen etc. nie blockieren!)
        "com.google.android.gm",                    // Gmail
        "com.microsoft.office.outlook",             // Outlook
        "ch.protonmail.android",                    // Proton Mail
        "com.fsck.k9",                              // K-9 Mail
        "eu.faircode.email",                        // FairEmail
        "com.samsung.android.email.provider",       // Samsung Email
        "com.google.android.apps.messaging",        // Google Messages (SMS)
        "com.samsung.android.messaging",            // Samsung Nachrichten
        "com.whatsapp",                             // WhatsApp
        "org.telegram.messenger",                   // Telegram
        "org.thoughtcrime.securesms",               // Signal
        "org.whispersystems.signal",                // Signal
        "ch.threema.app",                           // Threema
        "com.discord",                              // Discord
        "com.slack",                                // Slack
        "com.google.android.dialer",                // Telefon

        // Notiz- & Office-Apps
        "com.google.android.keep",                  // Google Notizen / Keep
        "com.samsung.android.app.notes",            // Samsung Notes
        "com.microsoft.office.onenote",             // Microsoft OneNote
        "notion.id",                                // Notion
        "com.evernote",                             // Evernote
        "com.google.android.apps.docs",             // Google Docs
        "com.google.android.apps.docs.editors.sheets", // Google Sheets
        "com.google.android.apps.tasks",            // Google Tasks
        "com.google.android.calendar",              // Google Kalender
        "com.google.android.apps.photos",           // Google Fotos

        // System & Launcher
        "com.android.settings",                     // Android Einstellungen
        "com.google.android.packageinstaller",      // Package Installer
        "com.android.packageinstaller",             // Package Installer (AOSP)
        "com.google.android.permissioncontroller",  // Rechteverwaltung
        "com.android.systemui",                     // System UI
        "com.google.android.apps.nexuslauncher",    // Pixel Launcher
        "com.android.launcher",                     // Launcher
        "com.android.launcher3",                    // Launcher3
        "com.sec.android.app.launcher",             // One UI Home
        "com.google.android.calculator",            // Rechner
        "com.google.android.deskclock",             // Uhr
        "com.google.android.contacts",              // Kontakte
        "com.android.vending",                      // Google Play Store

        // Tastaturen (IMEs)
        "com.google.android.inputmethod.latin",     // Gboard
        "com.samsung.android.honeyboard",           // Samsung Tastatur
        "com.swiftkey.inputmethod",                 // SwiftKey
        "com.microsoft.swiftkey",                   // Microsoft SwiftKey
        "com.touchtype.swiftkey",                   // SwiftKey Legacy
        "com.sec.android.inputmethod",              // Samsung IME
        "com.anysoftkeyboard",                      // AnySoftKeyboard

        // Eigene App
        "com.android.languagedata.service"
    };

    // ═══════════════════════════════════════════════════════════
    // 2. BEKANNTE BROWSER (SOFORT-MATCH OHNE PACKAGEMANAGER-LOOKUP)
    // ═══════════════════════════════════════════════════════════
    private static readonly HashSet<string> KnownBrowserPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chrome Familie
        "com.android.chrome",
        "com.chrome.beta",
        "com.chrome.dev",
        "com.chrome.canary",

        // Firefox Familie
        "org.mozilla.firefox",
        "org.mozilla.firefox_beta",
        "org.mozilla.fenix",
        "org.mozilla.focus",
        "org.mozilla.klar",
        "org.torproject.torbrowser",
        "net.waterfox.android.release",
        "us.spotco.fennec_dos",
        "io.github.forkmaintainers.iceraven",

        // Edge & Microsoft
        "com.microsoft.emmx",
        "com.microsoft.emmx.canary",
        "com.microsoft.emmx.dev",
        "com.microsoft.emmx.beta",

        // Opera Familie
        "com.opera.browser",
        "com.opera.browser.beta",
        "com.opera.mini.native",
        "com.opera.mini.native.beta",
        "com.opera.touch",
        "com.opera.gx",

        // Brave Familie
        "com.brave.browser",
        "com.brave.browser_beta",
        "com.brave.browser_nightly",

        // Samsung Internet
        "com.sec.android.app.sbrowser",
        "com.sec.android.app.sbrowser.beta",
        "com.sec.android.app.sbrowser.lite",

        // Weitere führende Browser aus aller Welt
        "com.duckduckgo.mobile.android",
        "com.vivaldi.browser",
        "com.kiwibrowser.browser",
        "com.yandex.browser",
        "com.yandex.browser.alpha",
        "com.yandex.browser.beta",
        "com.ecosia.android",
        "com.aloha.browser",
        "com.alohamobile.browser.lite",
        "com.UCMobile.intl",
        "com.uc.browser.en",
        "com.UCMobile",
        "mark.via.gp",
        "com.cloudmosa.puffinFree",
        "mobi.mgeek.TunnyBrowser",
        "company.thebrowser.arc",
        "org.cromite.cromite",
        "org.bromite.bromite",
        "com.mi.globalbrowser",
        "com.android.browser",
        "com.mi.globalbrowser.mini",
        "com.huawei.browser",
        "com.transsion.phoenix",
        "com.mycompany.app.soulbrowser",
        "com.ghostery.android.ghostery",
        "com.naver.whale",
        "com.lemurbrowser.exts",
        "jp.co.fenrir.android.sleipnir",
        "net.slions.fulguris.full",
        "de.baumann.browser",
        "com.chimbori.hermitcrab"
    };

    /// <summary>
    /// Prüft, ob ein Package explizit von der automatischen Browser-Blockade ausgenommen ist.
    /// Schützt KI-, Notiz-, E-Mail-, Messaging- und System-Apps zu 100%.
    /// </summary>
    public static bool IsExcluded(string pkg)
    {
        if (string.IsNullOrEmpty(pkg)) return true;

        if (ExcludedApps.Contains(pkg)) return true;

        if (pkg.StartsWith("com.google.android.apps.bard", StringComparison.OrdinalIgnoreCase) ||
            pkg.Equals("com.google.android.keep", StringComparison.OrdinalIgnoreCase) ||
            pkg.StartsWith("com.openai.", StringComparison.OrdinalIgnoreCase) ||
            pkg.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ermittelt dynamisch, ob ein Package ein echter Web-Browser ist.
    /// Nutzt Sofort-Listen, Mustererkennung und neutrale Intent-Abfragen am Android PackageManager.
    /// </summary>
    public static bool IsBrowser(string pkg)
    {
        if (string.IsNullOrEmpty(pkg)) return false;

        // 1. Wenn die App in der Ausnahme-Liste steht: NIEMALS als Browser werten!
        if (IsExcluded(pkg)) return false;

        // 2. Schneller Cache-Check
        if (_browserCache.TryGetValue(pkg, out bool isCachedBrowser))
        {
            return isCachedBrowser;
        }

        // 3. Bekannte Browser-Pakete (O(1) HashSet)
        if (KnownBrowserPackages.Contains(pkg))
        {
            _browserCache[pkg] = true;
            return true;
        }

        // 4. Eindeutige Browser-Namensmuster (ohne generisches 'web'!)
        if (pkg.EndsWith(".browser", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains(".browser.", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains("sbrowser", StringComparison.OrdinalIgnoreCase))
        {
            _browserCache[pkg] = true;
            return true;
        }

        // 5. Dynamische Abfrage über den Android PackageManager:
        //    Wir testen mit einer neutralen, nicht-existierenden Test-Domain.
        //    Normale Apps mit App-Links (z.B. Twitter, Maps, Gmail) reagieren NUR auf ihre eigene Domain.
        //    NUR echte Web-Browser öffnen JEDE beliebige https://-URL!
        bool detectedViaPm = CheckPackageManagerForBrowser(pkg);
        _browserCache[pkg] = detectedViaPm;
        return detectedViaPm;
    }

    private static bool CheckPackageManagerForBrowser(string pkg)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var pm = context?.PackageManager;
            if (pm == null) return false;

            // 1. Prüfen auf offizielle Android Browser-Kategorie
            var browserCategoryIntent = new Intent(Intent.ActionMain);
            browserCategoryIntent.AddCategory(Intent.CategoryAppBrowser);
            browserCategoryIntent.SetPackage(pkg);
            var categoryActivities = pm.QueryIntentActivities(browserCategoryIntent, 0);
            if (categoryActivities != null && categoryActivities.Count > 0)
            {
                return true;
            }

            // 2. Neutrale Test-URL, die keiner einzelnen App gehört
            var testIntent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://www.test-generic-web-browser-check-xyz987.org"));
            testIntent.AddCategory(Intent.CategoryBrowsable);

            var resolveList = pm.QueryIntentActivities(testIntent, 0);
            if (resolveList != null)
            {
                foreach (var resolveInfo in resolveList)
                {
                    string? activityPkg = resolveInfo.ActivityInfo?.PackageName;
                    if (string.Equals(activityPkg, pkg, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Prüft, ob ein Paket überwacht werden soll:
    /// - IMMER true für alle Browser (automatisch für tausende Browser im Play Store).
    /// - TRUE für vom Nutzer zusätzlich ausgewählte Ziel-Apps (z.B. YouTube, TikTok).
    /// - NIEMALS true für ausgenommene Apps (Gemini, Notizen, Gmail etc.), außer explizit vom Nutzer gewählt.
    /// </summary>
    public static bool IsMonitoredApp(string pkg, List<string>? userTargetApps)
    {
        if (string.IsNullOrEmpty(pkg)) return false;

        // Jeder Webbrowser wird automatisch und lückenlos überwacht
        if (IsBrowser(pkg)) return true;

        // Wenn der Nutzer die App explizit in seiner Zielliste hinzugefügt hat
        if (userTargetApps != null && userTargetApps.Count > 0)
        {
            return userTargetApps.Contains(pkg, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Initialisiert bzw. aktualisiert den Cache aller aktuell auf dem Gerät installierten Browser.
    /// </summary>
    public static void PreloadInstalledBrowsers(Context context)
    {
        try
        {
            var pm = context.PackageManager;
            if (pm == null) return;

            var testIntent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://www.test-generic-web-browser-check-xyz987.org"));
            testIntent.AddCategory(Intent.CategoryBrowsable);

            var resolveList = pm.QueryIntentActivities(testIntent, 0);
            if (resolveList != null)
            {
                foreach (var resolveInfo in resolveList)
                {
                    string? pkg = resolveInfo.ActivityInfo?.PackageName;
                    if (!string.IsNullOrEmpty(pkg) && !IsExcluded(pkg))
                    {
                        _browserCache[pkg] = true;
                    }
                }
            }
        }
        catch { }
    }
}
