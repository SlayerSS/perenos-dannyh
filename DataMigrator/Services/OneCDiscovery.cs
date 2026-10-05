using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class OneCDiscovery
{
    public static IEnumerable<ItemSeed> Discover(ScanTarget target)
    {
        var start = Path.Combine(target.Roaming, "1C", "1CEStart");
        if (Directory.Exists(start) && !PathSafety.IsBlocked(start))
        {
            yield return new ItemSeed
            {
                Id = "onec-start",
                Category = ItemCategory.OneCSettings,
                SummaryName = "1С",
                DisplayName = "1С — список баз и запуск",
                Explanation = "Папка 1CEStart: список баз ibases.v8i и 1CEStart.cfg. Платформа 1С не копируется.",
                Path = start,
                StoredName = "1CEStart",
                ShowPath = true,
                Exclusions = ExclusionRules.OneC
            };
        }

        var v8 = Path.Combine(target.Roaming, "1C", "1cv8");
        if (Directory.Exists(v8) && !PathSafety.IsBlocked(v8))
        {
            yield return new ItemSeed
            {
                Id = "onec-v8",
                Category = ItemCategory.OneCSettings,
                SummaryName = "1С",
                DisplayName = "1С — настройки пользователя",
                Explanation = "Личные настройки 1cv8: 1cv8.pfl, def.usr и небольшие файлы. Дампы, журналы и кэш шаблонов пропускаются.",
                Path = v8,
                StoredName = "1cv8",
                ShowPath = true,
                Exclusions = ExclusionRules.OneC
            };
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        foreach (var listFile in new[]
                 {
                     Path.Combine(start, "ibases.v8i"),
                     Path.Combine(v8, "ibases.v8i")
                 })
        {
            if (!File.Exists(listFile))
                continue;

            IReadOnlyList<IbasesParser.InfoBase> bases;
            try
            {
                bases = IbasesParser.Parse(listFile);
            }
            catch
            {
                continue;
            }

            foreach (var info in bases)
            {
                if (!seen.Add(info.Name + "|" + (info.FilePath ?? info.ConnectRaw)))
                    continue;

                if (!info.IsFile || string.IsNullOrWhiteSpace(info.FilePath))
                {
                    var note = IbasesParser.IsServer(info.ConnectRaw)
                        ? ToolInfo.ServerBaseNote
                        : "веб или другой тип подключения, сама база не копируется — только строка в ibases.v8i";
                    yield return new ItemSeed
                    {
                        Id = "onec-remote-" + index++,
                        Category = ItemCategory.OneCDatabase,
                        SummaryName = "базы 1С",
                        DisplayName = info.Name,
                        Explanation = note,
                        Path = info.FilePath ?? info.ConnectRaw,
                        Enabled = false,
                        Selected = false,
                        Exists = false,
                        ShowPath = false,
                        SizeOverride = "на сервере"
                    };
                    continue;
                }

                var resolved = LocationResolver.ResolveDirectory(info.FilePath, target);
                if (resolved == null)
                {
                    yield return new ItemSeed
                    {
                        Id = "onec-missing-" + index++,
                        Category = ItemCategory.OneCDatabase,
                        SummaryName = "базы 1С",
                        DisplayName = info.Name,
                        Explanation = "Файловая база из списка 1С. Папка по этому пути не найдена.",
                        Path = info.FilePath,
                        Enabled = false,
                        Selected = false,
                        Exists = false,
                        ShowPath = true,
                        SizeOverride = "не найдена",
                        Notes = "Папка не найдена. Строка подключения всё равно сохранится в ibases.v8i, если отмечены настройки 1С."
                    };
                    continue;
                }

                if (PathSafety.IsBlocked(resolved))
                {
                    yield return new ItemSeed
                    {
                        Id = "onec-blocked-" + index++,
                        Category = ItemCategory.OneCDatabase,
                        SummaryName = "базы 1С",
                        DisplayName = info.Name,
                        Explanation = "Путь находится в папке программ или Windows и не копируется.",
                        Path = resolved,
                        Enabled = false,
                        Selected = false,
                        ShowPath = true,
                        SizeOverride = "пропуск",
                        Notes = "Папка программ не копируется."
                    };
                    continue;
                }

                yield return new ItemSeed
                {
                    Id = "onec-base-" + index++,
                    Category = ItemCategory.OneCDatabase,
                    SummaryName = "базы 1С",
                    DisplayName = info.Name,
                    Explanation = "Файловая база. Будет скопирована вся папка. Это может занять много места.",
                    Path = resolved,
                    StoredName = info.Name,
                    Selected = false,
                    ShowPath = true
                };
            }
        }
    }
}
