using DataMigrator.Models;

namespace DataMigrator.Services;

internal sealed class DataScanner
{
    public ScanBundle ScanProfile(ScanTarget target, TransferMode mode, IProgress<string>? progress, CancellationToken ct)
    {
        var known = target.UseLiveKnownFolders
            ? KnownFolders.DiscoverLive()
            : KnownFolders.DiscoverOffline(target.ProfileRoot);

        var warnings = new List<string>();
        var seeds = new List<ItemSeed>();

        progress?.Report("Ищу папки пользователя…");
        seeds.AddRange(UserFolders(known));
        progress?.Report("Ищу браузеры…");
        seeds.AddRange(BrowserDiscovery.Discover(target));
        progress?.Report("Ищу Outlook…");
        seeds.AddRange(OutlookDiscovery.Discover(target, known, ct, progress, warnings));
        progress?.Report("Ищу 1С…");
        seeds.AddRange(OneCDiscovery.Discover(target));
        progress?.Report("Ищу программы…");
        seeds.AddRange(OfficeDiscovery.Discover(target));
        seeds.AddRange(AppDiscovery.Discover(target));

        var allocator = new RelativePathAllocator();
        var items = new List<ScanItem>();
        foreach (var seed in seeds)
        {
            ct.ThrowIfCancellationRequested();
            items.Add(Materialize(seed, target, known, allocator, progress, ct));
        }

        var user = target.SourceIsLive
            ? Environment.UserName
            : new DirectoryInfo(target.ProfileRoot).Name;

        return new ScanBundle
        {
            Context = new ScanContext
            {
                Mode = mode,
                SourceProfileRoot = target.ProfileRoot,
                DestinationProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ScannedPath = target.ProfileRoot,
                SourceUser = user,
                SourceMachine = target.SourceIsLive ? Environment.MachineName : ""
            },
            Items = items,
            Warnings = warnings
        };
    }

    public ScanBundle ScanBackup(string backupRoot, TransferMode mode, IProgress<string>? progress, CancellationToken ct)
    {
        var manifestPath = Path.Combine(backupRoot, ToolInfo.ManifestFileName);
        var manifest = ManifestService.Load(manifestPath);
        var items = new List<ScanItem>();
        var warnings = new List<string>();
        if (!manifest.Completed)
            warnings.Add("Эта копия не была завершена. Часть файлов может отсутствовать.");
        if (manifest.ErrorCount > 0)
            warnings.Add("При создании копии были ошибки. Откройте журнал.txt в папке копии.");

        var missing = 0;
        foreach (var entry in manifest.Items)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.StoredRelativePath))
            {
                missing++;
                continue;
            }

            var read = Path.GetFullPath(Path.Combine(backupRoot, entry.StoredRelativePath));
            if (!PathSafety.IsUnder(read, backupRoot))
            {
                missing++;
                warnings.Add("Пропущен элемент вне папки копии: " + entry.DisplayName);
                continue;
            }

            var kind = ManifestService.ParseKind(entry.Kind);
            var exists = kind == ItemKind.Folder ? Directory.Exists(read) : File.Exists(read);
            if (!exists)
            {
                missing++;
                continue;
            }

            progress?.Report("Считаю размер: " + entry.DisplayName);
            long bytes = 0;
            var count = 0;
            var partial = false;
            if (kind == ItemKind.Folder)
            {
                var measured = DirectoryMeasure.Measure(read, ExclusionRules.None, ct, progress, entry.DisplayName);
                bytes = measured.Bytes;
                count = measured.Count;
                partial = measured.Partial;
            }
            else
            {
                try
                {
                    bytes = new FileInfo(read).Length;
                    count = 1;
                }
                catch
                {
                    missing++;
                    continue;
                }
            }

            items.Add(new ScanItem
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? entry.StoredRelativePath : entry.Id,
                Category = ManifestService.ParseCategory(entry.Category),
                SummaryName = entry.DisplayName,
                DisplayName = entry.DisplayName,
                Explanation = string.IsNullOrWhiteSpace(entry.Explanation) ? "Элемент из файла копии." : entry.Explanation,
                Notes = entry.Notes,
                PathDisplay = entry.SourcePath,
                Kind = kind,
                IsEnabled = true,
                IsSelected = true,
                Exists = true,
                SizeBytes = bytes,
                FileCount = count,
                SizeIsPartial = partial,
                ReadPath = read,
                OriginalPath = entry.SourcePath,
                StoredRelativePath = entry.StoredRelativePath,
                KnownFolderId = entry.KnownFolderId,
                PathInsideKnownFolder = entry.PathInsideKnownFolder,
                ProfileRelativePath = entry.ProfileRelativePath,
                Exclusions = ExclusionRules.None
            });
        }

        if (missing > 0)
            warnings.Add($"В списке копии нет на диске: {missing}.");

        return new ScanBundle
        {
            Context = new ScanContext
            {
                Mode = mode,
                SourceProfileRoot = manifest.SourceProfileRoot,
                DestinationProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ScannedPath = backupRoot,
                FromManifest = true,
                ManifestWasIncomplete = !manifest.Completed,
                SourceUser = manifest.SourceUser,
                SourceMachine = manifest.SourceMachine
            },
            Items = items,
            Warnings = warnings
        };
    }

    private static IEnumerable<ItemSeed> UserFolders(IReadOnlyList<(string Id, string Path)> known)
    {
        foreach (var def in KnownFolders.Definitions)
        {
            string? path = null;
            foreach (var folder in known)
            {
                if (folder.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase))
                {
                    path = folder.Path;
                    break;
                }
            }
            if (string.IsNullOrEmpty(path) || PathSafety.IsBlocked(path))
                continue;

            yield return new ItemSeed
            {
                Id = "user-" + def.Id.ToLowerInvariant(),
                Category = ItemCategory.UserFolder,
                SummaryName = def.Summary,
                DisplayName = def.Title,
                Explanation = def.Explanation,
                Path = path,
                StoredName = def.Title,
                Selected = def.Selected,
                Exclusions = ExclusionRules.WorkFiles
            };
        }
    }

    private static ScanItem Materialize(
        ItemSeed seed,
        ScanTarget target,
        IReadOnlyList<(string Id, string Path)> known,
        RelativePathAllocator allocator,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var item = new ScanItem
        {
            Id = seed.Id,
            Category = seed.Category,
            SummaryName = seed.SummaryName,
            DisplayName = seed.DisplayName,
            Explanation = seed.Explanation,
            Notes = seed.Notes,
            SizeOverride = seed.SizeOverride,
            Kind = seed.Kind,
            IsEnabled = seed.Enabled,
            IsSelected = seed.Selected && seed.Enabled,
            Exists = seed.Exists,
            ReadPath = seed.Path,
            OriginalPath = seed.Path,
            Exclusions = seed.Exclusions,
            RegistryKeys = seed.RegistryKeys ?? Array.Empty<string>(),
            StoredRelativePath = allocator.Alloc(BuildStored(seed))
        };

        if (seed.ShowPath)
            item.PathDisplay = seed.Path;

        if (!seed.Exists || seed.Kind == ItemKind.Registry)
            return item;

        if (PathSafety.IsBlocked(seed.Path))
        {
            item.IsEnabled = false;
            item.IsSelected = false;
            item.SizeOverride = "пропуск";
            item.Notes = "Системная папка, не копируется.";
            return item;
        }

        PathMapper.Annotate(item, seed.Path, target.ProfileRoot, known);

        if (seed.Kind == ItemKind.File)
        {
            try
            {
                item.SizeBytes = new FileInfo(seed.Path).Length;
                item.FileCount = 1;
            }
            catch
            {
                item.Exists = false;
                item.IsEnabled = false;
                item.IsSelected = false;
                item.SizeOverride = "не найдена";
            }
            return item;
        }

        try
        {
            var attr = File.GetAttributes(seed.Path);
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                item.IsEnabled = false;
                item.IsSelected = false;
                item.Notes = "Это ссылка на другую папку. Такие ссылки пропускаются, чтобы не скопировать лишний диск.";
                item.SizeOverride = "ссылка";
                return item;
            }
        }
        catch
        {
            item.Exists = false;
            item.IsEnabled = false;
            item.IsSelected = false;
            item.SizeOverride = "нет доступа";
            return item;
        }

        progress?.Report("Считаю размер: " + seed.DisplayName);
        var measured = DirectoryMeasure.Measure(seed.Path, seed.Exclusions, ct, progress, seed.DisplayName);
        item.SizeBytes = measured.Bytes;
        item.FileCount = measured.Count;
        item.SizeIsPartial = measured.Partial;

        if (item.SizeBytes >= 1L << 30 &&
            item.Category is ItemCategory.OneCDatabase or ItemCategory.AppConfig or ItemCategory.OutlookStore)
        {
            const string extra = "Большой объём — проверьте, что на диске хватает места.";
            item.Notes = string.IsNullOrEmpty(item.Notes) ? extra : item.Notes + " " + extra;
        }

        return item;
    }

    private static string BuildStored(ItemSeed seed)
    {
        var storedName = string.IsNullOrWhiteSpace(seed.StoredName) ? seed.DisplayName : seed.StoredName;
        var parts = storedName.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var built = BackupLayout.Folder(seed.Category);
        for (var i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            var part = last && seed.Kind != ItemKind.Folder
                ? BackupLayout.SanitizeFile(parts[i])
                : BackupLayout.Sanitize(parts[i]);
            built = Path.Combine(built, part);
        }
        return built;
    }
}
