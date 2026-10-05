using DataMigrator.Models;

namespace DataMigrator.Services;

internal sealed class SourceResolution
{
    public string? Path { get; init; }
    public bool IsBackup { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<DetectedProfile> Options { get; init; } = Array.Empty<DetectedProfile>();
}

internal static class ProfileDetector
{
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Public", "Default", "Default User", "All Users", "WDAGUtilityAccount",
        "defaultuser0", "Все пользователи", "Общие", "DefaultAppPool"
    };

    public static bool LooksLikeProfile(string dir)
    {
        try
        {
            return Directory.Exists(Path.Combine(dir, "AppData"))
                || Directory.Exists(Path.Combine(dir, "Application Data"))
                || File.Exists(Path.Combine(dir, "NTUSER.DAT"));
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<DetectedProfile> ListOptions(string path)
    {
        var result = new List<DetectedProfile>();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return result;

        AddIfMatch(result, path);

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Dedup(result);
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (name.Equals("Users", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var userDir in Directory.EnumerateDirectories(child))
                        AddProfile(result, userDir);
                }
                catch
                {
                    // ignore unreadable Users
                }
                continue;
            }

            if (name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase))
                continue;

            AddIfMatch(result, child);
        }

        return Dedup(result);
    }

    public static SourceResolution Resolve(string? typedPath, DetectedProfile? selected)
    {
        if (selected != null && Directory.Exists(selected.Path))
        {
            var typed = typedPath?.Trim() ?? "";
            if (string.IsNullOrEmpty(typed)
                || PathSafety.Same(typed, selected.Path)
                || PathSafety.IsUnder(selected.Path, typed))
            {
                return new SourceResolution { Path = selected.Path, IsBackup = selected.IsBackup };
            }
        }

        var path = typedPath?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return new SourceResolution { Error = "Укажите существующую папку или диск." };
        }

        var manifest = Path.Combine(path, ToolInfo.ManifestFileName);
        if (File.Exists(manifest))
            return new SourceResolution { Path = path, IsBackup = true };
        if (LooksLikeProfile(path))
            return new SourceResolution { Path = path, IsBackup = false };

        var options = ListOptions(path);
        if (options.Count == 1)
            return new SourceResolution { Path = options[0].Path, IsBackup = options[0].IsBackup, Options = options };
        if (options.Count > 1)
        {
            return new SourceResolution
            {
                Options = options,
                Error = "Найдено несколько профилей или копий. Выберите нужную строку в списке и снова нажмите «Найти данные»."
            };
        }

        return new SourceResolution
        {
            Error = "В этой папке нет профиля Windows (Users\\имя\\AppData) и нет файла backup-manifest.json."
        };
    }

    private static void AddIfMatch(List<DetectedProfile> result, string dir)
    {
        var manifest = Path.Combine(dir, ToolInfo.ManifestFileName);
        if (File.Exists(manifest))
        {
            result.Add(DescribeBackup(dir, manifest));
            return;
        }
        AddProfile(result, dir);
    }

    private static void AddProfile(List<DetectedProfile> result, string dir)
    {
        if (!LooksLikeProfile(dir))
            return;
        var name = new DirectoryInfo(dir).Name;
        if (SkipNames.Contains(name))
            return;
        result.Add(new DetectedProfile
        {
            Display = $"{name} — {dir}",
            Path = dir,
            IsBackup = false
        });
    }

    private static DetectedProfile DescribeBackup(string dir, string manifestPath)
    {
        var display = $"Копия — {dir}";
        try
        {
            var manifest = ManifestService.Load(manifestPath);
            var when = manifest.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            var who = string.IsNullOrWhiteSpace(manifest.SourceUser) ? "" : manifest.SourceUser + ", ";
            display = $"Копия {when}: {who}{dir}";
        }
        catch
        {
            display = $"Копия — {new DirectoryInfo(dir).Name}";
        }

        return new DetectedProfile { Display = display, Path = dir, IsBackup = true };
    }

    private static IReadOnlyList<DetectedProfile> Dedup(List<DetectedProfile> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DetectedProfile>();
        foreach (var item in items)
        {
            string key;
            try
            {
                key = Path.GetFullPath(item.Path);
            }
            catch
            {
                key = item.Path;
            }
            if (seen.Add(key))
                list.Add(item);
        }
        return list;
    }
}
