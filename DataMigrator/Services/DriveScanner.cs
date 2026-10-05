using System.IO;
using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class DriveScanner
{
    public static IReadOnlyList<DriveEntry> Scan()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return Array.Empty<DriveEntry>();
        }

        var list = new List<DriveEntry>();
        foreach (var drive in drives)
        {
            var entry = new DriveEntry
            {
                Root = drive.Name,
                Letter = drive.Name.TrimEnd('\\')
            };
            try
            {
                entry.DriveKind = drive.DriveType;
            }
            catch
            {
                entry.DriveKind = DriveType.Unknown;
            }

            var probed = Probe(drive);
            if (probed == null)
            {
                entry.IsReady = false;
                entry.Label = "";
                entry.FreeText = "не отвечает";
            }
            else
            {
                entry.IsReady = probed.Value.Ready;
                entry.Label = probed.Value.Label;
                entry.FreeText = probed.Value.Free;
            }

            entry.IsRemovable = entry.DriveKind == DriveType.Removable;
            entry.TypeText = TypeName(entry.DriveKind);
            list.Add(entry);
        }

        return list
            .OrderBy(d => d.IsReady ? 0 : 1)
            .ThenBy(d => d.IsRemovable ? 0 : d.DriveKind == DriveType.Fixed ? 1 : 2)
            .ThenBy(d => d.Letter, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TypeName(DriveType type) => type switch
    {
        DriveType.Removable => "съёмный",
        DriveType.Fixed => "локальный",
        DriveType.Network => "сетевой",
        DriveType.CDRom => "оптический",
        DriveType.Ram => "в памяти",
        _ => "неизвестный"
    };

    private static (bool Ready, string Label, string Free)? Probe(DriveInfo drive)
    {
        (bool Ready, string Label, string Free)? result = null;
        var failed = false;
        var thread = new Thread(() =>
        {
            try
            {
                if (!drive.IsReady)
                {
                    result = (false, "", "не готов");
                    return;
                }
                var label = drive.VolumeLabel;
                var free = ByteSize.Format(drive.AvailableFreeSpace);
                result = (true, string.IsNullOrWhiteSpace(label) ? "без имени" : label, "свободно " + free);
            }
            catch
            {
                failed = true;
            }
        })
        { IsBackground = true };
        thread.Start();
        if (!thread.Join(2000) || failed)
            return null;
        return result;
    }
}
