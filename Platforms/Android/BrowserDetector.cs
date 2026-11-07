#pragma warning disable CA1416, CA1422
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Collections.Concurrent;

namespace LanguageData.Platforms.Android;

public static class BrowserDetector
{
    private static readonly ConcurrentDictionary<string, bool> _browserCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SystemAndInstallerIndicators =
    {
        "settings",
        "packageinstaller",
        "permissioncontroller",
        "vending",
        "security",
        "safecenter",
        "deviceadmin",
        "uninstaller",
        "miui.securitycenter",
        "coloros.safecenter",
        "oplus.safecenter"
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
    // 1. STRIKTE AUSNAHME-LISTE (NIEMALS ALS BROWSER BLOCKIEREN)
    // ═══════════════════════════════════════════════════════════
    private static readonly HashSet<string> ExcludedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        // KI- / AI-Chatbots & Assistenten
        "com.google.android.apps.bard",             // Google Gemini
        "com.openai.chatgpt",                       // ChatGPT
        "com.anthropic.claude",                     // Claude
        "com.microsoft.copilot",                    // Microsoft Copilot
        "ai.perplexity.app.android",                // Perplexity AI
        "com.deepseek.chat",                        // DeepSeek
        "ai.x.grok",                                // Grok
        "ai.mistral.chat",                          // Mistral Le Chat
        "com.poe.android",                          // Poe
        "com.characterai.chat",                     // Character.ai
        "ai.replika.app",                           // Replika
        "ai.inflection.pi",                         // Pi AI

        // Notiz-, Aufgaben- & Office-Apps
        "com.google.android.keep",                  // Google Notizen / Keep
        "com.samsung.android.app.notes",            // Samsung Notes
        "com.microsoft.office.onenote",             // Microsoft OneNote
        "notion.id",                                // Notion
        "md.obsidian",                              // Obsidian
        "com.evernote",                             // Evernote
        "com.automattic.simplenote",                // Simplenote
        "net.cozic.joplin",                         // Joplin
        "com.standardnotes",                        // Standard Notes
        "com.ticktick.task",                        // TickTick
        "com.todoist",                              // Todoist
        "com.anydo",                                // Any.do
        "com.google.android.apps.docs",             // Google Drive / Docs
        "com.google.android.apps.docs.editors.docs",// Google Docs
        "com.google.android.apps.docs.editors.sheets", // Google Sheets
        "com.google.android.apps.docs.editors.slides", // Google Slides
        "com.google.android.apps.tasks",            // Google Tasks
        "com.google.android.calendar",              // Google Kalender
        "com.google.android.apps.photos",           // Google Fotos
        "com.microsoft.office.word",                // MS Word
        "com.microsoft.office.excel",               // MS Excel
        "com.microsoft.office.powerpoint",          // MS PowerPoint
        "com.microsoft.office.officehubrow",        // Microsoft 365
        "com.microsoft.todos",                      // Microsoft To Do

        // E-Mail-Apps
        "com.google.android.gm",                    // Gmail
        "com.microsoft.office.outlook",             // Outlook
        "ch.protonmail.android",                    // Proton Mail
        "com.fsck.k9",                              // K-9 Mail / Thunderbird
        "net.thunderbird.android",                  // Thunderbird
        "eu.faircode.email",                        // FairEmail
        "com.readdle.spark",                        // Spark Mail
        "me.bluemail.mail",                         // BlueMail
        "com.yahoo.mobile.client.android.mail",     // Yahoo Mail
        "de.gmx.mobile.android.mail",               // GMX Mail
        "de.web.mobile.android.mail",               // WEB.DE Mail
        "com.samsung.android.email.provider",       // Samsung Email

        // Messenger & Kommunikation
        "com.whatsapp",                             // WhatsApp
        "com.whatsapp.w4b",                         // WhatsApp Business
        "org.telegram.messenger",                   // Telegram
        "org.telegram.messenger.web",               // Telegram
        "org.thoughtcrime.securesms",               // Signal
        "org.whispersystems.signal",                // Signal
        "ch.threema.app",                           // Threema
        "com.discord",                              // Discord
        "com.slack",                                // Slack
        "com.microsoft.teams",                      // Microsoft Teams
        "com.facebook.orca",                        // Facebook Messenger
        "com.google.android.apps.messaging",        // Google Messages (SMS)
        "com.samsung.android.messaging",            // Samsung Nachrichten
        "com.google.android.dialer",                // Telefon
        "com.samsung.android.dialer",               // Samsung Telefon
        "com.google.android.contacts",              // Kontakte

        // Übersetzer & Passwort-Manager
        "com.deepl.mobiletranslator",
        "com.google.android.apps.translate",
        "com.x8bit.bitwarden",
        "com.onepassword.android",
        "proton.android.pass",
        "com.google.android.apps.authenticator2",
        "com.azure.authenticator",

        // System, Launcher & Tools
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
        "com.miui.home",
        "com.teslacoilsw.launcher",
        "com.google.android.calculator",
        "com.google.android.deskclock",
        "com.android.vending",
        "com.google.android.gms",

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

    private static readonly string[] ExcludedPackagePatterns =
    {
        "com.google.android.apps.bard",
        "com.openai.",
        "com.anthropic.",
        "ai.perplexity.",
        "com.deepseek.",
        "inputmethod",
        "keyboard",
        "launcher",
        ".notes",
        ".keep",
        ".calendar",
        ".contacts",
        ".dialer",
        ".calculator",
        ".deskclock"
    };

    // ═══════════════════════════════════════════════════════════
    // 2. BEKANNTE BROWSER (SOFORT-ERKENNUNG)
    // ═══════════════════════════════════════════════════════════
    private static readonly HashSet<string> KnownBrowserPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.android.chrome",
        "com.chrome.beta",
        "com.chrome.dev",
        "com.chrome.canary",
        "org.chromium.chrome",
        "org.mozilla.firefox",
        "org.mozilla.firefox_beta",
        "org.mozilla.fenix",
        "org.mozilla.focus",
        "org.mozilla.klar",
        "org.torproject.torbrowser",
        "org.torproject.torbrowser_alpha",
        "net.waterfox.android.release",
        "us.spotco.fennec_dos",
        "io.github.forkmaintainers.iceraven",
        "us.spotco.mulch",
        "org.mozilla.reference.browser",
        "com.microsoft.emmx",
        "com.microsoft.emmx.canary",
        "com.microsoft.emmx.dev",
        "com.microsoft.emmx.beta",
        "com.brave.browser",
        "com.brave.browser_beta",
        "com.brave.browser_nightly",
        "com.opera.browser",
        "com.opera.browser.beta",
        "com.opera.mini.native",
        "com.opera.mini.native.beta",
        "com.opera.touch",
        "com.opera.gx",
        "com.opera.cryptobrowser",
        "com.sec.android.app.sbrowser",
        "com.sec.android.app.sbrowser.beta",
        "com.sec.android.app.sbrowser.lite",
        "com.duckduckgo.mobile.android",
        "com.vivaldi.browser",
        "com.vivaldi.browser.snapshot",
        "com.kiwibrowser.browser",
        "com.yandex.browser",
        "com.yandex.browser.alpha",
        "com.yandex.browser.beta",
        "com.yandex.browser.lite",
        "com.ecosia.android",
        "com.aloha.browser",
        "com.alohamobile.browser.lite",
        "com.UCMobile.intl",
        "com.uc.browser.en",
        "com.UCMobile",
        "com.uc.browser.hd",
        "mark.via.gp",
        "mark.via",
        "com.cloudmosa.puffinFree",
        "com.cloudmosa.puffin",
        "mobi.mgeek.TunnyBrowser",
        "company.thebrowser.arc",
        "org.cromite.cromite",
        "org.bromite.bromite",
        "com.mi.globalbrowser",
        "com.android.browser",
        "com.mi.globalbrowser.mini",
        "com.huawei.browser",
        "com.heytap.browser",
        "com.coloros.browser",
        "com.vivo.browser",
        "com.transsion.phoenix",
        "com.mycompany.app.soulbrowser",
        "com.ghostery.android.ghostery",
        "com.naver.whale",
        "com.lemurbrowser.exts",
        "jp.co.fenrir.android.sleipnir",
        "net.slions.fulguris.full",
        "de.baumann.browser",
        "com.chimbori.hermitcrab",
        "com.kagi.orion",
        "net.quetta.browser",
        "one.yuzu.browser",
        "acr.browser.barebones",
        "acr.browser.lightning",
        "com.stoutner.privacybrowser.standard",
        "com.stoutner.privacybrowser.free",
        "info.plateaukao.einkbro",
        "org.midorinext.android",
        "com.xbrowser.play",
        "com.stargon.browser",
        "com.pure.mini.browser",
        "com.ohr.berrybrowser"
    };

    public static bool IsExcluded(string pkg)
    {
        if (string.IsNullOrEmpty(pkg)) return true;
        if (ExcludedApps.Contains(pkg)) return true;

        foreach (var pattern in ExcludedPackagePatterns)
        {
            if (pkg.Contains(pattern, StringComparison.OrdinalIgnoreCase))
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
            pkg.Contains("sbrowser", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
            pkg.Contains("webview", StringComparison.OrdinalIgnoreCase))
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

            PackageInfoFlags flags = PackageInfoFlags.MatchAll;

            // 1. Kategorie APP_BROWSER prüfen
            var browserCategoryIntent = new Intent(Intent.ActionMain);
            browserCategoryIntent.AddCategory(Intent.CategoryAppBrowser);
            browserCategoryIntent.SetPackage(pkg);
            var categoryActivities = QueryIntentActivitiesCompat(pm, browserCategoryIntent, flags);
            if (categoryActivities != null && categoryActivities.Count > 0)
            {
                return true;
            }

            // 2. Generischer HTTPS-Web-Intent mit gültiger .org-Domain prüfen
            var testIntent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://www.test-generic-web-browser-check-xyz987.org"));
            testIntent.AddCategory(Intent.CategoryBrowsable);

            var resolveList = QueryIntentActivitiesCompat(pm, testIntent, flags);
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

    private static IList<ResolveInfo>? QueryIntentActivitiesCompat(PackageManager pm, Intent intent, PackageInfoFlags flags)
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                return pm.QueryIntentActivities(intent, PackageManager.ResolveInfoFlags.Of((long)flags));
            }
            return pm.QueryIntentActivities(intent, flags);
        }
        catch
        {
            return null;
        }
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

            PackageInfoFlags flags = PackageInfoFlags.MatchAll;
            var testIntent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse("https://www.test-generic-web-browser-check-xyz987.org"));
            testIntent.AddCategory(Intent.CategoryBrowsable);

            var resolveList = QueryIntentActivitiesCompat(pm, testIntent, flags);
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