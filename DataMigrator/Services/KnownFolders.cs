using System.Runtime.InteropServices;

namespace DataMigrator.Services;

internal sealed record KnownFolderDef(string Id, string Title, string Explanation, string Summary, string[] OfflineNames, bool Selected = true);

internal static class KnownFolders
{
    public static readonly KnownFolderDef[] Definitions =
    [
        new("Desktop", "Рабочий стол", "Файлы и ярлыки с рабочего стола.", "рабочий стол", ["Desktop", "Рабочий стол"]),
        new("Documents", "Документы", "Папка «Документы».", "документы", ["Documents", "Документы", "Мои документы"]),
        new("Downloads", "Загрузки", "Скачанные файлы.", "загрузки", ["Downloads", "Загрузки"]),
        new("Pictures", "Изображения", "Картинки и фотографии.", "изображения", ["Pictures", "Изображения", "Мои рисунки"]),
        new("Music", "Музыка", "Музыкальные файлы. Для офисного компьютера по умолчанию не отмечается.", "музыка", ["Music", "Музыка", "Моя музыка"], false),
        new("Videos", "Видео", "Видеофайлы. Для офисного компьютера по умолчанию не отмечается.", "видео", ["Videos", "Видео", "Мои видео"], false),
        new("Favorites", "Избранное", "Папка «Избранное».", "избранное", ["Favorites", "Избранное"])
    ];

    private static readonly Dictionary<string, Guid> Guids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Desktop"] = new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        ["Documents"] = new Guid("FDD39AD0-38BE-4931-A608-9DA7D1C0A6B0"),
        ["Downloads"] = new Guid("374DE290-123F-4565-9164-39C4925E467B"),
        ["Pictures"] = new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        ["Music"] = new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"),
        ["Videos"] = new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        ["Favorites"] = new Guid("1777F761-68AD-4D8A-87BD-30B759FA33DD")
    };

    public static string? GetLive(string id)
    {
        if (!Guids.TryGetValue(id, out var guid))
            return null;
        var g = guid;
        var hr = SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero)
            return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    public static IReadOnlyList<(string Id, string Path)> DiscoverLive()
    {
        var list = new List<(string, string)>();
        foreach (var def in Definitions)
        {
            var path = GetLive(def.Id);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                list.Add((def.Id, path));
        }
        return list;
    }

    public static IReadOnlyList<(string Id, string Path)> DiscoverOffline(string profileRoot)
    {
        var list = new List<(string, string)>();
        foreach (var def in Definitions)
        {
            foreach (var name in def.OfflineNames)
            {
                var path = Path.Combine(profileRoot, name);
                if (Directory.Exists(path))
                {
                    list.Add((def.Id, path));
                    break;
                }
            }
        }
        return list;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
