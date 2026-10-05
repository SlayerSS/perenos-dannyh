using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class OfficeDiscovery
{
    private sealed record FolderDef(string Id, string Title, string Relative, string Explanation);

    private static readonly FolderDef[] Folders =
    [
        new("office-templates", "Office — шаблоны", @"Microsoft\Templates", "Normal.dotm и другие шаблоны Word, Excel и PowerPoint."),
        new("office-blocks", "Word — экспресс-блоки", @"Microsoft\Document Building Blocks", "Экспресс-блоки и стандартные блоки Word."),
        new("office-dictionary", "Office — словарь", @"Microsoft\UProof", "Пользовательский словарь проверки орфографии."),
        new("word-startup", "Word — автозагрузка", @"Microsoft\Word\STARTUP", "Шаблоны и надстройки, которые Word открывает вместе с собой."),
        new("excel-startup", "Excel — автозагрузка", @"Microsoft\Excel\XLSTART", "Личные книги и надстройки, которые Excel открывает при запуске."),
        new("office-addins", "Office — надстройки", @"Microsoft\AddIns", "Надстройки, установленные для текущего пользователя."),
        new("office-recent", "Office — недавние файлы", @"Microsoft\Office\Recent", "Список недавно открытых документов.")
    ];

    private static readonly string[] SettingKeys =
    [
        @"HKCU\Software\Microsoft\Office\16.0\Word",
        @"HKCU\Software\Microsoft\Office\16.0\Excel",
        @"HKCU\Software\Microsoft\Office\16.0\PowerPoint",
        @"HKCU\Software\Microsoft\Office\16.0\Common",
        @"HKCU\Software\Microsoft\Office\15.0\Word",
        @"HKCU\Software\Microsoft\Office\15.0\Excel",
        @"HKCU\Software\Microsoft\Office\15.0\PowerPoint",
        @"HKCU\Software\Microsoft\Office\15.0\Common"
    ];

    public static IEnumerable<ItemSeed> Discover(ScanTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.Roaming))
        {
            foreach (var folder in Folders)
            {
                var path = Path.Combine(target.Roaming, folder.Relative);
                if (!Directory.Exists(path) || PathSafety.IsBlocked(path))
                    continue;
                yield return new ItemSeed
                {
                    Id = folder.Id,
                    Category = ItemCategory.AppConfig,
                    SummaryName = "Office",
                    DisplayName = folder.Title,
                    Explanation = folder.Explanation,
                    Path = path,
                    StoredName = folder.Title,
                    ShowPath = true
                };
            }
        }

        if (!target.AllowRegistry)
            yield break;

        var officeKeys = SettingKeys.Where(RegistryTransfer.KeyExists).ToList();
        if (officeKeys.Count > 0)
        {
            yield return new ItemSeed
            {
                Id = "office-settings",
                Category = ItemCategory.AppConfig,
                SummaryName = "Office",
                DisplayName = "Office — настройки пользователя",
                Explanation = "Личные настройки Word, Excel и PowerPoint. Сами программы нужно установить отдельно.",
                Path = string.Join("; ", officeKeys),
                Kind = ItemKind.Registry,
                StoredName = @"Office\office.reg",
                RegistryKeys = officeKeys
            };
        }

        if (RegistryTransfer.KeyExists(@"HKCU\Network"))
        {
            yield return new ItemSeed
            {
                Id = "network-drives",
                Category = ItemCategory.AppConfig,
                SummaryName = "Сетевые диски",
                DisplayName = "Сетевые диски",
                Explanation = "Подключённые сетевые папки этого пользователя. На новом компьютере они появятся после входа в сеть.",
                Path = @"HKCU\Network",
                Kind = ItemKind.Registry,
                StoredName = @"Network\network-drives.reg",
                RegistryKeys = [@"HKCU\Network"]
            };
        }
    }
}
