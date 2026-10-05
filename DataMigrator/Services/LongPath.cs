namespace DataMigrator.Services;

internal static class LongPath
{
    public static string Wrap(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            full = path;
        }

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + full[2..];
        return @"\\?\" + full;
    }

    public static string Unwrap(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[@"\\?\UNC\".Length..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }
}
