namespace DataMigrator.Services;

internal readonly record struct FoundFile(string Path, long Length);

internal static class DirectoryWalker
{
    private static readonly HashSet<string> AlwaysSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information",
        "$Recycle.Bin",
        "$WinREAgent",
        "Config.Msi",
        "$SysReset",
        "$Windows.~BT",
        "$Windows.~WS"
    };

    public static IEnumerable<FoundFile> EnumerateFiles(string root, ExclusionRules rules, CancellationToken ct)
    {
        if (rules.ChromiumUserData)
        {
            foreach (var file in EnumerateChromiumUserData(root, ct))
                yield return file;
            yield break;
        }

        if (rules.FirefoxUserData)
        {
            foreach (var file in EnumerateFirefoxUserData(root, ct))
                yield return file;
            yield break;
        }

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(LongPath.Wrap(dir));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            foreach (var raw in entries)
            {
                ct.ThrowIfCancellationRequested();
                var full = LongPath.Unwrap(raw);
                string name;
                try
                {
                    name = Path.GetFileName(full);
                }
                catch
                {
                    continue;
                }
                if (string.IsNullOrEmpty(name))
                    continue;

                FileAttributes attr;
                try
                {
                    attr = File.GetAttributes(LongPath.Wrap(full));
                }
                catch
                {
                    continue;
                }

                // Junctions and symlinks are not followed, otherwise a profile link can pull in the whole disk.
                if ((attr & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attr & FileAttributes.Directory) != 0)
                {
                    if (AlwaysSkip.Contains(name) || rules.ShouldSkipDirectory(name))
                        continue;
                    if (PathSafety.IsBlocked(full))
                        continue;
                    stack.Push(full);
                    continue;
                }

                if (IsHiveOrSystemFile(name) || rules.ShouldSkipFile(name))
                    continue;

                long length = 0;
                try
                {
                    length = new FileInfo(LongPath.Wrap(full)).Length;
                }
                catch
                {
                    length = 0;
                }
                yield return new FoundFile(full, length);
            }
        }
    }

    public static IEnumerable<string> FindByExtension(string root, string[] extensions, CancellationToken ct)
    {
        foreach (var file in EnumerateFiles(root, ExclusionRules.None, ct))
        {
            foreach (var ext in extensions)
            {
                if (file.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    yield return file.Path;
                    break;
                }
            }
        }
    }

    private static readonly HashSet<string> ChromiumProfileFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bookmarks", "Bookmarks.bak",
        "Preferences", "Secure Preferences",
        "Login Data", "Login Data-journal",
        "Login Data For Account", "Login Data For Account-journal",
        "History", "History-journal", "Archived History", "Visited Links",
        "Cookies", "Cookies-journal",
        "Web Data", "Web Data-journal",
        "Favicons", "Favicons-journal",
        "Top Sites", "Top Sites-journal",
        "Shortcuts", "Shortcuts-journal",
        "Current Session", "Current Tabs", "Last Session", "Last Tabs",
        "Extension Cookies", "Extension Cookies-journal",
        "Custom Dictionary.txt",
        "Network Persistent State", "TransportSecurity",
        "Trusted Vault", "Trusted Vault-journal",
        "Affiliation Database", "Affiliation Database-journal",
        "Account Web Data", "Account Web Data-journal",
        "BookmarkMergedSurfaceOrdering",
        "DownloadMetadata",
        "Ya Passman Data", "Ya Passman Data-journal",
        "Ya Autofill Data", "Ya Autofill Data-journal"
    };

    private static readonly HashSet<string> ChromiumProfileDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sessions", "Sessions_Encrypted",
        "Local Extension Settings", "Sync Extension Settings", "Managed Extension Settings",
        "Extension State", "Extension Rules", "Extension Scripts",
        "Local Storage", "Sync Data"
    };

    private static readonly HashSet<string> ChromiumNetworkFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cookies", "Cookies-journal",
        "Device Bound Sessions", "Device Bound Sessions-journal",
        "Trust Tokens", "Trust Tokens-journal",
        "TransportSecurity",
        "Network Persistent State"
    };

    // User Data holds several profiles plus gigabytes of models and component caches.
    // Only the profile files that Chrome needs to restore the person are copied.
    private static IEnumerable<FoundFile> EnumerateChromiumUserData(string root, CancellationToken ct)
    {
        var sawProfile = false;
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(LongPath.Wrap(root));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            yield break;
        }

        foreach (var raw in children)
        {
            ct.ThrowIfCancellationRequested();
            var full = LongPath.Unwrap(raw);
            if (!IsChromiumProfileName(Path.GetFileName(full)) || IsReparse(full))
                continue;
            sawProfile = true;
            foreach (var file in EnumerateChromiumProfile(full, ct))
                yield return file;
        }

        if (!sawProfile)
        {
            foreach (var file in EnumerateChromiumProfile(root, ct))
                yield return file;
            yield break;
        }

        if (TryStatFile(Path.Combine(root, "Local State"), out var localState))
            yield return localState;
    }

    private static IEnumerable<FoundFile> EnumerateChromiumProfile(string profile, CancellationToken ct)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(LongPath.Wrap(profile));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            yield break;
        }

        foreach (var raw in entries)
        {
            ct.ThrowIfCancellationRequested();
            var full = LongPath.Unwrap(raw);
            var name = Path.GetFileName(full);
            if (string.IsNullOrEmpty(name) || IsReparse(full))
                continue;

            if (name.Equals("Network", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in EnumerateNamedFiles(full, ChromiumNetworkFiles, ct))
                    yield return file;
                continue;
            }

            if (ChromiumProfileDirectories.Contains(name))
            {
                foreach (var file in EnumerateFiles(full, ExclusionRules.Chromium, ct))
                    yield return file;
                continue;
            }

            if (!ChromiumProfileFiles.Contains(name))
                continue;
            if (TryStatFile(full, out var found))
                yield return found;
        }
    }

    private static IEnumerable<FoundFile> EnumerateNamedFiles(string directory, HashSet<string> names, CancellationToken ct)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFiles(LongPath.Wrap(directory));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            yield break;
        }

        foreach (var raw in entries)
        {
            ct.ThrowIfCancellationRequested();
            var full = LongPath.Unwrap(raw);
            if (!names.Contains(Path.GetFileName(full)) || IsReparse(full))
                continue;
            if (TryStatFile(full, out var found))
                yield return found;
        }
    }

    private static bool IsChromiumProfileName(string name)
    {
        return name.Equals("Default", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparse(string path)
    {
        try
        {
            var attr = File.GetAttributes(LongPath.Wrap(path));
            return (attr & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStatFile(string path, out FoundFile found)
    {
        found = default;
        try
        {
            var attr = File.GetAttributes(LongPath.Wrap(path));
            if ((attr & FileAttributes.Directory) != 0 || (attr & FileAttributes.ReparsePoint) != 0)
                return false;
            var length = new FileInfo(LongPath.Wrap(path)).Length;
            found = new FoundFile(path, length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static readonly HashSet<string> FirefoxProfileFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "places.sqlite", "places.sqlite-wal", "places.sqlite-shm",
        "favicons.sqlite", "favicons.sqlite-wal", "favicons.sqlite-shm",
        "key4.db", "logins.json", "logins-backup.json", "cert9.db",
        "cookies.sqlite", "cookies.sqlite-wal", "cookies.sqlite-shm",
        "formhistory.sqlite", "permissions.sqlite", "content-prefs.sqlite",
        "prefs.js", "user.js", "xulstore.json",
        "sessionstore.jsonlz4", "sessionCheckpoints.json",
        "handlers.json", "search.json.mozlz4", "containers.json",
        "extensions.json", "extension-preferences.json",
        "signedInUser.json", "pkcs11.txt"
    };

    private static readonly HashSet<string> FirefoxProfileDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bookmarkbackups", "sessionstore-backups"
    };

    private static IEnumerable<FoundFile> EnumerateFirefoxUserData(string root, CancellationToken ct)
    {
        foreach (var name in new[] { "profiles.ini", "installs.ini" })
        {
            if (TryStatFile(Path.Combine(root, name), out var file))
                yield return file;
        }

        if (IsFirefoxProfile(root))
        {
            foreach (var file in EnumerateFirefoxProfile(root, ct))
                yield return file;
            yield break;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(LongPath.Wrap(root));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            yield break;
        }

        foreach (var raw in children)
        {
            ct.ThrowIfCancellationRequested();
            var full = LongPath.Unwrap(raw);
            if (IsReparse(full))
                continue;
            var name = Path.GetFileName(full);
            if (name.Equals("Profiles", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var profile in EnumerateChildProfiles(full, ct))
                    yield return profile;
                continue;
            }

            if (!IsFirefoxProfile(full))
                continue;
            foreach (var file in EnumerateFirefoxProfile(full, ct))
                yield return file;
        }
    }

    private static IEnumerable<FoundFile> EnumerateChildProfiles(string profilesRoot, CancellationToken ct)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(LongPath.Wrap(profilesRoot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            yield break;
        }

        foreach (var raw in children)
        {
            ct.ThrowIfCancellationRequested();
            var full = LongPath.Unwrap(raw);
            if (IsReparse(full) || !IsFirefoxProfile(full))
                continue;
            foreach (var file in EnumerateFirefoxProfile(full, ct))
                yield return file;
        }
    }

    private static IEnumerable<FoundFile> EnumerateFirefoxProfile(string profile, CancellationToken ct)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(LongPath.Wrap(profile));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            yield break;
        }

        foreach (var raw in entries)
        {
            ct.ThrowIfCancellationRequested();
            var full = LongPath.Unwrap(raw);
            var name = Path.GetFileName(full);
            if (string.IsNullOrEmpty(name) || IsReparse(full))
                continue;
            if (FirefoxProfileDirectories.Contains(name))
            {
                foreach (var file in EnumerateFiles(full, ExclusionRules.None, ct))
                    yield return file;
                continue;
            }

            if (!FirefoxProfileFiles.Contains(name))
                continue;
            if (TryStatFile(full, out var found))
                yield return found;
        }
    }

    private static bool IsFirefoxProfile(string path)
    {
        return File.Exists(LongPath.Wrap(Path.Combine(path, "prefs.js")))
            || File.Exists(LongPath.Wrap(Path.Combine(path, "places.sqlite")));
    }

    private static bool IsHiveOrSystemFile(string name)
    {
        if (name.Equals("NTUSER.DAT", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("NTUSER.DAT", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("UsrClass.dat", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("UsrClass.dat", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)
            || name.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase);
    }
}
