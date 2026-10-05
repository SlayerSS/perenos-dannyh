using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class AppDiscovery
{
    private static readonly string[] CryptoProKeys =
    [
        @"HKCU\Software\Crypto Pro",
        @"HKCU\Software\CryptoPro"
    ];

    public static IEnumerable<ItemSeed> Discover(ScanTarget target)
    {
        foreach (var app in AppCatalog.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.RelativePath))
                continue;
            if (target.RequireInstallMatch && !UninstallList.Matches(target.InstalledNames, app.InstallHints))
                continue;

            var scope = AppCatalog.ScopePath(target.Roaming, target.Local, app.Scope);
            if (string.IsNullOrWhiteSpace(scope))
                continue;
            var path = Path.Combine(scope, app.RelativePath);
            if (!Directory.Exists(path) || PathSafety.IsBlocked(path))
                continue;

            var index = 1;
            yield return new ItemSeed
            {
                Id = app.Id,
                Category = ItemCategory.AppConfig,
                SummaryName = app.SummaryName,
                DisplayName = app.DisplayName,
                Explanation = app.Explanation,
                Path = path,
                StoredName = app.DisplayName,
                Selected = app.DefaultSelected,
                ShowPath = true,
                Notes = app.Warning,
                Exclusions = app.Exclusions
            };

            if (app.Id == "thunderbird")
            {
                foreach (var extra in ProfilesIni.AbsoluteProfiles(path))
                {
                    yield return new ItemSeed
                    {
                        Id = "thunderbird-ext-" + index++,
                        Category = ItemCategory.AppConfig,
                        SummaryName = "Thunderbird",
                        DisplayName = "Thunderbird — " + extra.Name,
                        Explanation = "Профиль Thunderbird вне обычной папки. Кэш не копируется.",
                        Path = extra.Path,
                        StoredName = "Thunderbird " + extra.Name,
                        ShowPath = true,
                        Exclusions = ExclusionRules.Firefox
                    };
                }
            }
        }

        if (!target.AllowRegistry)
            yield break;
        if (target.RequireInstallMatch && !UninstallList.Matches(target.InstalledNames, ["CryptoPro", "КриптоПро", "Crypto Pro"]))
            yield break;

        var keys = CryptoProKeys.Where(RegistryTransfer.KeyExists).ToList();
        if (keys.Count == 0)
            yield break;

        yield return new ItemSeed
        {
            Id = "cryptopro-reg",
            Category = ItemCategory.AppConfig,
            SummaryName = "CryptoPro",
            DisplayName = "CryptoPro — реестр пользователя",
            Explanation = "Разделы HKCU\\Software\\Crypto Pro. Там могут быть контейнеры ключей. По умолчанию не копируется.",
            Path = string.Join("; ", keys),
            Kind = ItemKind.Registry,
            StoredName = @"CryptoPro\cryptopro.reg",
            Selected = false,
            RegistryKeys = keys,
            Notes = "Закрытые ключи. Импорт при восстановлении только после отдельного подтверждения."
        };
    }
}
