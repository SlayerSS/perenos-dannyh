namespace DataMigrator.Services;

internal static class ProfilesIni
{
    public static IEnumerable<(string Name, string Path)> AbsoluteProfiles(string appRoot)
    {
        var ini = Path.Combine(appRoot, "profiles.ini");
        if (!File.Exists(ini))
            yield break;

        string text;
        try
        {
            text = TextFile.ReadAuto(ini);
        }
        catch
        {
            yield break;
        }

        string? name = null;
        string? path = null;
        var relative = true;

        void Flush()
        {
            name = null;
            path = null;
            relative = true;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (!relative && !string.IsNullOrWhiteSpace(path))
                {
                    var full = Normalize(path);
                    if (full != null && Directory.Exists(full) && !PathSafety.IsUnder(full, appRoot) && !PathSafety.IsBlocked(full))
                        yield return (string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(full).Name : name, full);
                }
                Flush();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                name = value;
            else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                path = value;
            else if (key.Equals("IsRelative", StringComparison.OrdinalIgnoreCase))
                relative = value != "0";
        }

        if (!relative && !string.IsNullOrWhiteSpace(path))
        {
            var full = Normalize(path);
            if (full != null && Directory.Exists(full) && !PathSafety.IsUnder(full, appRoot) && !PathSafety.IsBlocked(full))
                yield return (string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(full).Name : name, full);
        }
    }

    private static string? Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        }
        catch
        {
            return null;
        }
    }
}
