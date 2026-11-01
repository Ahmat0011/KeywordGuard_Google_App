using Android.Content;
using Android.Content.PM;
using System.Collections.Concurrent;

namespace LanguageData.Platforms.Android;

public static class BrowserDetector
{
    private static readonly ConcurrentDictionary<string, bool> _browserCache = new(StringComparer.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════
    // SYSTEM-, EINSTELLUNGS- & INSTALLER-PAKETE (FÜR DEN SELBSTSCHUTZ)
    // ═══════════════════════════════════════════════════════════
    private static readonly string[] SystemAndInstallerIndicators =
    {
        "settings",
        "packageinstaller",
        "permissioncontroller",
        "vending",
        "security",
        "safecenter",
        "deviceadmin",
        "uninstaller"
    };

    public static bool IsSystemOrInstallerPackage(string pkg)
    {
        if (string.IsNullOrEmpty(pkg)) return false;
        foreach (var indicator in SystemAndInstallerIndicators)
        {
            if (pkg.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════
    // 1. EXPLIZITE AUSNAHME-LISTE FÜR DIE BROWSER-KEYWORD-SUCHE
    // ═══════════════════════════════════════════════════════════
    private static readonly HashSet<string> ExcludedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        // KI- / AI-Apps
        "com.google.android.apps.bard",             // Google Gemini
        "com.openai.chatgpt",                       // ChatGPT
        "com.anthropic.claude",                     // Claude
        "com.microsoft.copilot",                    // Microsoft Copilot
        "ai.perplexity.app.android",                // Perplexity AI
        "com.poe.android",                          // Poe
        "com.characterai.chat",                     // Character.ai
        "ai.replika.app",                           // Replika

        // E-Mail- & Kommunikations-Apps
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

        // System & Launcher (werden oben separat im Selbstschutz geprüft, aber nicht als Web-Browser gewertet)
        "com.android.settings",
        "com.google.android.packageinstaller",
        "com.android.packageinstaller",
        "com.google.android.permissioncontroller",
        "com.android.permissioncontroller",
        "com.android.systemui",
        "com.google.android.apps.nexuslauncher",
        "com.android.launcher",
        "com.android.launcher3",
        "com.sec.android.app.launcher",
        "com.google.android.calculator",
        "com.google.android.deskclock",
        "com.google.android.contacts",
        "com.android.vending",

        // Tastaturen (IMEs)
        "com.google.android.inputmethod.latin",
        "com.samsung.android.honeyboard",
        "com.swiftkey.inputmethod",
        "com.microsoft.swiftkey",
        "com.touchtype.swiftkey",
        "com.sec.android.inputmethod",
        "com.anysoftkeyboard",

        // Eigene App
        "com.android.languagedata.service"
    };

    // ═══════════════════════════════════════════════════════════
    // 2. BEKANNTE BROWSER (SOFORT-MATCH OHNE PACKAGEMANAGER-LOOKUP)
    // ═══════════════════════════════════════════════════════════
    private static readonly HashSet<string> KnownBrowserPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.android.chrome",
        "com.chrome.beta",
        "com.chrome.dev",
        "com.chrome.canary",
        "org.mozilla.firefox",
        "org.mozilla.firefox_beta",
        "org.mozilla.fenix",
        "org.mozilla.focus",
        "org.mozilla.klar",
        "org.torproject.torbrowser",
        "net.waterfox.android.release",
        "us.spotco.fennec_dos",
        "io.github.forkmaintainers.iceraven",
        "com.microsoft.emmx",
        "com.microsoft.emmx.canary",
        "com.microsoft.emmx.dev",
        "com.microsoft.emmx.beta",
        "com.opera.browser",
        "com.opera.browser.beta",
        "com.opera.mini.native",
        "com.opera.mini.native.beta",
        "com.opera.touch",
        "com.opera.gx",
        "com.brave.browser",
        "com.brave.browser_beta",
        "com.brave.browser_nightly",
        "com.sec.android.app.sbrowser",
        "com.sec.android.app.sbrowser.beta",
        "com.sec.android.app.sbrowser.lite",
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

    public static bool IsBrowser(string pkg)
    {
        if (string.IsNullOrEmpty(pkg)) return false;

        if (IsExcluded(pkg)) return false;

        if (_browserCache.TryGetValue(pkg, out bool isCachedBrowser))
        {
            return isCachedBrowser;
        }

        if (KnownBrowserPackages.Contains(pkg))
        {
            _browserCache[pkg] = true;
            return true;
        }

        if (pkg.EndsWith(".browser", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains(".browser.", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains("sbrowser", StringComparison.OrdinalIgnoreCase))
        {
            _browserCache[pkg] = true;
            return true;
        }

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

            var browserCategoryIntent = new Intent(Intent.ActionMain);
            browserCategoryIntent.AddCategory(Intent.CategoryAppBrowser);
            browserCategoryIntent.SetPackage(pkg);
            var categoryActivities = pm.QueryIntentActivities(browserCategoryIntent, 0);
            if (categoryActivities != null && categoryActivities.Count > 0)
            {
                return true;
            }

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

    public static bool IsMonitoredApp(string pkg, List<string>? userTargetApps)
    {
        if (string.IsNullOrEmpty(pkg)) return false;

        if (IsBrowser(pkg)) return true;

        if (userTargetApps != null && userTargetApps.Count > 0)
        {
            return userTargetApps.Contains(pkg, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }

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