using DataMigrator.Models;

namespace DataMigrator.Services;

internal readonly record struct MappedPath(string Path, bool RedirectedToDocuments);

internal static class PathMapper
{
    public static void Annotate(ScanItem item, string path, string profileRoot, IReadOnlyList<(string Id, string Path)> known)
    {
        string? bestId = null;
        string? bestRel = null;
        var bestLen = -1;
        foreach (var folder in known)
        {
            var rel = PathSafety.RelativeTo(folder.Path, path);
            if (rel == null)
                continue;
            if (folder.Path.Length > bestLen)
            {
                bestLen = folder.Path.Length;
                bestId = folder.Id;
                bestRel = rel;
            }
        }

        if (bestId != null)
        {
            item.KnownFolderId = bestId;
            item.PathInsideKnownFolder = string.IsNullOrEmpty(bestRel) ? null : bestRel;
        }

        var profileRel = PathSafety.RelativeTo(profileRoot, path);
        item.ProfileRelativePath = string.IsNullOrEmpty(profileRel) ? null : profileRel;
    }

    public static MappedPath MapToLive(ScanItem item, string destProfileRoot)
    {
        if (!string.IsNullOrEmpty(item.KnownFolderId))
        {
            var live = KnownFolders.GetLive(item.KnownFolderId);
            if (!string.IsNullOrEmpty(live))
            {
                var dest = string.IsNullOrEmpty(item.PathInsideKnownFolder)
                    ? live
                    : Path.Combine(live, item.PathInsideKnownFolder);
                return new MappedPath(dest, false);
            }
        }

        if (!string.IsNullOrEmpty(item.ProfileRelativePath))
            return new MappedPath(Path.Combine(destProfileRoot, item.ProfileRelativePath), false);

        if (CanUseOriginal(item.OriginalPath, item.Kind))
            return new MappedPath(item.OriginalPath, false);

        var docs = KnownFolders.GetLive("Documents");
        if (string.IsNullOrEmpty(docs))
            docs = Path.Combine(destProfileRoot, "Documents");
        var redirected = Path.Combine(
            docs,
            "Перенесенные данные",
            BackupLayout.Folder(item.Category),
            item.Kind == ItemKind.File ? BackupLayout.SanitizeFile(Path.GetFileName(item.OriginalPath)) : BackupLayout.Sanitize(item.DisplayName));
        return new MappedPath(redirected, true);
    }

    private static bool CanUseOriginal(string path, ItemKind kind)
    {
        if (string.IsNullOrWhiteSpace(path) || kind == ItemKind.Registry)
            return false;
        try
        {
            if (PathSafety.IsBlocked(path))
                return false;
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return false;
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return false;
            var info = new DriveInfo(root);
            return info.IsReady;
        }
        catch
        {
            return false;
        }
    }
}
