using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class BrowserDiscovery
{
    private sealed record BrowserDef(string Id, string Title, string Summary, string Scope, string Relative, ExclusionRules Rules);

    private static readonly BrowserDef[] Browsers =
    [
        new("chrome", "Google Chrome", "Chrome", "Local", @"Google\Chrome\User Data", ExclusionRules.ChromiumProfile),
        new("edge", "Microsoft Edge", "Edge", "Local", @"Microsoft\Edge\User Data", ExclusionRules.ChromiumProfile),
        new("yandex", "Яндекс Браузер", "Яндекс Браузер", "Local", @"Yandex\YandexBrowser\User Data", ExclusionRules.ChromiumProfile),
        new("brave", "Brave", "Brave", "Local", @"BraveSoftware\Brave-Browser\User Data", ExclusionRules.ChromiumProfile),
        new("opera", "Opera", "Opera", "Roaming", @"Opera Software\Opera Stable", ExclusionRules.ChromiumProfile),
        new("firefox", "Mozilla Firefox", "Firefox", "Roaming", @"Mozilla\Firefox", ExclusionRules.FirefoxProfile)
    ];

    private const string Explanation = "Закладки, пароли, история, последние вкладки и настройки расширений. Кэш и служебные файлы браузера не копируются.";

    public static IEnumerable<ItemSeed> Discover(ScanTarget target)
    {
        foreach (var browser in Browsers)
        {
            var root = browser.Scope == "Local" ? target.Local : target.Roaming;
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var path = Path.Combine(root, browser.Relative);
            if (!Directory.Exists(path) || PathSafety.IsBlocked(path))
                continue;

            yield return Folder(browser.Id, browser.Title, browser.Summary, path, browser.Rules);

            if (browser.Id is "firefox")
            {
                var index = 1;
                foreach (var extra in ProfilesIni.AbsoluteProfiles(path))
                {
                    yield return Folder(
                        $"firefox-ext-{index++}",
                        $"Firefox — {extra.Name}",
                        "Firefox",
                        extra.Path,
                        ExclusionRules.FirefoxProfile,
                        "Профиль Firefox, который лежит вне обычной папки. Кэш не копируется.");
                }
            }
        }
    }

    private static ItemSeed Folder(string id, string title, string summary, string path, ExclusionRules rules, string? explanation = null)
    {
        return new ItemSeed
        {
            Id = id,
            Category = ItemCategory.Browser,
            SummaryName = summary,
            DisplayName = title,
            Explanation = explanation ?? Explanation,
            Path = path,
            StoredName = title,
            ShowPath = true,
            Exclusions = rules
        };
    }
}
