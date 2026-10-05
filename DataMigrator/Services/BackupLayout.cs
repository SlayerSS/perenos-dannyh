using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class BackupLayout
{
    public static string Folder(ItemCategory category) => category switch
    {
        ItemCategory.UserFolder => "Папки пользователя",
        ItemCategory.Browser => "Браузеры",
        ItemCategory.OutlookSettings => "Outlook",
        ItemCategory.OutlookStore => "Базы Outlook",
        ItemCategory.OneCSettings => "1С",
        ItemCategory.OneCDatabase => "Базы 1С",
        ItemCategory.AppConfig => "Программы",
        _ => "Прочее"
    };

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
            chars[i] = invalid.Contains(name[i]) ? '_' : name[i];
        var text = new string(chars).Trim().Trim('.');
        if (text.Length > 80)
            text = text[..80].Trim();
        return string.IsNullOrWhiteSpace(text) ? "элемент" : text;
    }

    public static string SanitizeFile(string name)
    {
        var file = Path.GetFileName(name);
        var ext = Path.GetExtension(file);
        if (string.IsNullOrEmpty(ext) || ext.Length > 12)
            return Sanitize(file);
        return Sanitize(Path.GetFileNameWithoutExtension(file)) + ext;
    }
}

internal sealed class RelativePathAllocator
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Alloc(string relative)
    {
        relative = relative.TrimStart('\\', '/');
        if (_used.Add(relative))
            return relative;

        var dir = Path.GetDirectoryName(relative) ?? "";
        var file = Path.GetFileNameWithoutExtension(relative);
        var ext = Path.GetExtension(relative);
        for (var i = 2; i < 1000; i++)
        {
            var name = $"{file} ({i}){ext}";
            var candidate = string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
            if (_used.Add(candidate))
                return candidate;
        }

        var fallback = relative + "-" + Guid.NewGuid().ToString("N")[..6];
        _used.Add(fallback);
        return fallback;
    }
}
