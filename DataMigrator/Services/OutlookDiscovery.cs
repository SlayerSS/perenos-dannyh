using System.Text;
using DataMigrator.Models;
using Microsoft.Win32;

namespace DataMigrator.Services;

internal static class OutlookDiscovery
{
    private static readonly string[] ProfileKeys =
    [
        @"Software\Microsoft\Office\16.0\Outlook",
        @"Software\Microsoft\Office\15.0\Outlook",
        @"Software\Microsoft\Office\14.0\Outlook",
        @"Software\Microsoft\Windows NT\CurrentVersion\Windows Messaging Subsystem\Profiles",
        @"Software\Microsoft\Office\Outlook"
    ];

    private static readonly string[] ExportKeys =
    [
        @"HKCU\Software\Microsoft\Office\16.0\Outlook",
        @"HKCU\Software\Microsoft\Office\15.0\Outlook",
        @"HKCU\Software\Microsoft\Office\14.0\Outlook",
        @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Windows Messaging Subsystem\Profiles",
        @"HKCU\Software\Microsoft\Office\Outlook"
    ];

    public static IEnumerable<ItemSeed> Discover(
        ScanTarget target,
        IReadOnlyList<(string Id, string Path)> known,
        CancellationToken ct,
        IProgress<string>? progress,
        List<string> warnings)
    {
        var outlook = Path.Combine(target.Roaming, "Microsoft", "Outlook");
        if (Directory.Exists(outlook) && !PathSafety.IsBlocked(outlook))
        {
            yield return new ItemSeed
            {
                Id = "outlook-settings",
                Category = ItemCategory.OutlookSettings,
                SummaryName = "Outlook",
                DisplayName = "Outlook — настройки",
                Explanation = "Профиль, автозаполнение адресов (NK2) и служебные файлы. Файлы OST не копируются.",
                Path = outlook,
                StoredName = "Настройки",
                ShowPath = true,
                Exclusions = ExclusionRules.Outlook
            };
        }

        var signatures = Path.Combine(target.Roaming, "Microsoft", "Signatures");
        if (Directory.Exists(signatures) && !PathSafety.IsBlocked(signatures))
        {
            yield return new ItemSeed
            {
                Id = "outlook-signatures",
                Category = ItemCategory.OutlookSettings,
                SummaryName = "подписи Outlook",
                DisplayName = "Подписи Outlook",
                Explanation = "Подписи, которые подставляются в письма.",
                Path = signatures,
                StoredName = "Подписи",
                ShowPath = true
            };
        }

        var microsoft = Path.Combine(target.Roaming, "Microsoft");
        if (Directory.Exists(microsoft))
        {
            IEnumerable<string> nk2;
            try
            {
                nk2 = Directory.EnumerateFiles(microsoft, "*.nk2", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                nk2 = Array.Empty<string>();
            }

            foreach (var file in nk2)
            {
                yield return new ItemSeed
                {
                    Id = "outlook-nk2-" + Path.GetFileName(file),
                    Category = ItemCategory.OutlookSettings,
                    SummaryName = "Outlook",
                    DisplayName = "Автозаполнение " + Path.GetFileName(file),
                    Explanation = "Старый файл автозаполнения адресов Outlook.",
                    Path = file,
                    Kind = ItemKind.File,
                    StoredName = Path.GetFileName(file),
                    ShowPath = true
                };
            }
        }

        if (target.AllowRegistry)
        {
            var existing = ExportKeys.Where(RegistryTransfer.KeyExists).ToList();
            if (existing.Count > 0)
            {
                yield return new ItemSeed
                {
                    Id = "outlook-reg",
                    Category = ItemCategory.OutlookSettings,
                    SummaryName = "реестр Outlook",
                    DisplayName = "Реестр профилей Outlook",
                    Explanation = "Разделы реестра текущего пользователя. При восстановлении программа спросит подтверждение и не примет чужой файл.",
                    Path = string.Join("; ", existing),
                    Kind = ItemKind.Registry,
                    StoredName = "outlook.reg",
                    RegistryKeys = existing,
                    Notes = "Импорт только после подтверждения."
                };
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = 0;
        foreach (var found in FindStoreFiles(target, known, ct, progress))
        {
            var resolved = LocationResolver.ResolveFile(found, target);
            if (resolved == null)
            {
                if (File.Exists(found))
                    resolved = found;
                else
                {
                    missing++;
                    continue;
                }
            }
            if (!seen.Add(resolved))
                continue;

            var ost = resolved.EndsWith(".ost", StringComparison.OrdinalIgnoreCase);
            if (PathSafety.IsBlocked(resolved))
            {
                yield return StoreItem(resolved, enabled: false, selected: false, "Файл лежит в папке программ и не копируется.");
                continue;
            }

            if (ost)
            {
                yield return StoreItem(resolved, enabled: false, selected: false, "кэш Exchange, обычно не нужен");
            }
            else
            {
                yield return StoreItem(resolved, enabled: true, selected: false, null);
            }
        }

        if (missing > 0)
            warnings.Add($"В профиле Outlook указаны файлы, которые не найдены: {missing}.");
    }

    private static ItemSeed StoreItem(string path, bool enabled, bool selected, string? notes)
    {
        var ost = path.EndsWith(".ost", StringComparison.OrdinalIgnoreCase);
        return new ItemSeed
        {
            Id = "store-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..10],
            Category = ItemCategory.OutlookStore,
            SummaryName = ost ? "OST" : "PST",
            DisplayName = Path.GetFileName(path),
            Explanation = ost
                ? "Кэш Exchange, обычно не нужен."
                : "Файл почты Outlook. Копируется как обычный файл, программа Outlook не копируется.",
            Path = path,
            Kind = ItemKind.File,
            StoredName = Path.GetFileName(path),
            Selected = selected,
            Enabled = enabled,
            ShowPath = true,
            Notes = notes
        };
    }

    private static IEnumerable<string> FindStoreFiles(
        ScanTarget target,
        IReadOnlyList<(string Id, string Path)> known,
        CancellationToken ct,
        IProgress<string>? progress)
    {
        var roots = new List<string>();
        foreach (var folder in known)
        {
            if (folder.Id is "Documents" or "Desktop")
                roots.Add(folder.Path);
        }

        var localOutlook = Path.Combine(target.Local, "Microsoft", "Outlook");
        if (Directory.Exists(localOutlook))
            roots.Add(localOutlook);

        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!seenRoots.Add(root))
                continue;
            progress?.Report("Ищу файлы Outlook в «" + Path.GetFileName(root) + "»…");
            foreach (var file in DirectoryWalker.FindByExtension(root, [".pst", ".ost"], ct))
                yield return file;
        }

        if (!target.AllowRegistry)
            yield break;

        foreach (var path in ReadRegistryStorePaths())
            yield return path;
    }

    private static IEnumerable<string> ReadRegistryStorePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ProfileKeys)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(root);
                if (key != null)
                    Walk(key, seen, 0);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
        return seen;
    }

    private static void Walk(RegistryKey key, HashSet<string> seen, int depth)
    {
        if (depth > 8)
            return;
        try
        {
            foreach (var name in key.GetValueNames())
            {
                object? value;
                try
                {
                    value = key.GetValue(name);
                }
                catch
                {
                    continue;
                }

                if (value is string text)
                    Collect(text, seen);
                else if (value is string[] many)
                {
                    foreach (var textValue in many)
                        Collect(textValue, seen);
                }
                else if (value is byte[] bytes && bytes.Length > 8)
                    Collect(Encoding.Unicode.GetString(bytes), seen);
            }

            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var child = key.OpenSubKey(sub);
                    if (child != null)
                        Walk(child, seen, depth + 1);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    private static void Collect(string text, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(text))
            return;
        foreach (var part in text.Split('\0', '\r', '\n'))
        {
            var value = part.Trim().Trim('"');
            if (value.Length < 5)
                continue;
            if (!value.EndsWith(".pst", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith(".ost", StringComparison.OrdinalIgnoreCase))
                continue;
            var path = ExtractPath(value);
            if (path != null)
                seen.Add(path);
        }
    }

    private static string? ExtractPath(string value)
    {
        var idx = value.LastIndexOf(@":\", StringComparison.Ordinal);
        if (idx >= 1 && char.IsLetter(value[idx - 1]))
            return value[(idx - 1)..].Trim();
        var unc = value.IndexOf(@"\\", StringComparison.Ordinal);
        if (unc >= 0)
            return value[unc..].Trim();
        return null;
    }
}
