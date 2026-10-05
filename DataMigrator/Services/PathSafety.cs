namespace DataMigrator.Services;

internal static class PathSafety
{
    private static readonly string[] DriveFolderNames = ["Windows", "Program Files", "Program Files (x86)", "ProgramData"];

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && full.Equals(root, StringComparison.OrdinalIgnoreCase))
            return root;
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool Same(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        try
        {
            return Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUnder(string path, string root)
    {
        try
        {
            var p = Normalize(path);
            var r = Normalize(root);
            if (p.Equals(r, StringComparison.OrdinalIgnoreCase))
                return true;
            var prefix = r.EndsWith(Path.DirectorySeparatorChar) ? r : r + Path.DirectorySeparatorChar;
            return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool Overlaps(string a, string b) => Same(a, b) || IsUnder(a, b) || IsUnder(b, a);

    public static string? RelativeTo(string root, string path)
    {
        if (!IsUnder(path, root))
            return null;
        var r = Normalize(root);
        var p = Normalize(path);
        if (p.Equals(r, StringComparison.OrdinalIgnoreCase))
            return "";
        return p[(r.Length + 1)..];
    }

    public static bool IsBlocked(string path)
    {
        string full;
        try
        {
            full = Normalize(path);
        }
        catch
        {
            return true;
        }

        foreach (var root in LiveBlockedRoots())
        {
            if (!string.IsNullOrEmpty(root) && IsUnder(full, root))
                return true;
        }

        var drive = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(drive))
            return false;
        foreach (var name in DriveFolderNames)
        {
            var blocked = Path.Combine(drive, name);
            if (IsUnder(full, blocked))
                return true;
        }

        try
        {
            var temp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(temp) && IsUnder(full, temp))
                return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static IEnumerable<string> LiveBlockedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }
}
