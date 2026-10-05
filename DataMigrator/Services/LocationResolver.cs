using DataMigrator.Models;

namespace DataMigrator.Services;

internal sealed class ScanTarget
{
    public required string ProfileRoot { get; init; }
    public required string Roaming { get; init; }
    public required string Local { get; init; }
    public required bool UseLiveKnownFolders { get; init; }
    public required bool AllowRegistry { get; init; }
    public required bool RequireInstallMatch { get; init; }
    public required bool SourceIsLive { get; init; }
    public IReadOnlyList<string> InstalledNames { get; init; } = Array.Empty<string>();
    public string? MountedDriveRoot { get; init; }

    public static ScanTarget ForLiveProfile()
    {
        var names = UninstallList.Read();
        return new ScanTarget
        {
            ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UseLiveKnownFolders = true,
            AllowRegistry = true,
            RequireInstallMatch = names.Count > 0,
            SourceIsLive = true,
            InstalledNames = names,
            MountedDriveRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        };
    }

    public static ScanTarget ForProfile(string profileRoot)
    {
        string full;
        try
        {
            full = Path.GetFullPath(profileRoot);
        }
        catch
        {
            full = profileRoot;
        }

        if (full.Equals(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), StringComparison.OrdinalIgnoreCase))
            return ForLiveProfile();

        var roaming = FirstExisting(
            Path.Combine(full, "AppData", "Roaming"),
            Path.Combine(full, "Application Data"));
        var local = FirstExisting(
            Path.Combine(full, "AppData", "Local"),
            Path.Combine(full, "Local Settings", "Application Data"));

        return new ScanTarget
        {
            ProfileRoot = full,
            Roaming = roaming ?? Path.Combine(full, "AppData", "Roaming"),
            Local = local ?? Path.Combine(full, "AppData", "Local"),
            UseLiveKnownFolders = false,
            AllowRegistry = false,
            RequireInstallMatch = false,
            SourceIsLive = false,
            MountedDriveRoot = Path.GetPathRoot(full)
        };
    }

    private static string? FirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }
}

internal static class LocationResolver
{
    public static string? ResolveDirectory(string configured, ScanTarget target) =>
        Resolve(configured, target, directory: true);

    public static string? ResolveFile(string configured, ScanTarget target) =>
        Resolve(configured, target, directory: false);

    private static string? Resolve(string configured, ScanTarget target, bool directory)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        string path;
        try
        {
            path = Path.GetFullPath(configured);
        }
        catch
        {
            return null;
        }

        if (target.SourceIsLive)
            return Ok(path, directory) ? PathSafety.Normalize(path) : null;

        if (Ok(path, directory) &&
            (PathSafety.IsUnder(path, target.ProfileRoot) || IsOnMounted(path, target.MountedDriveRoot)))
            return PathSafety.Normalize(path);

        var remapped = RemapUsers(path, target.ProfileRoot);
        if (remapped != null && Ok(remapped, directory))
            return PathSafety.Normalize(remapped);

        var swapped = SwapDrive(path, target.MountedDriveRoot);
        if (swapped != null && Ok(swapped, directory))
            return PathSafety.Normalize(swapped);

        if (Ok(path, directory) && !IsOnLiveSystemDrive(path))
            return PathSafety.Normalize(path);

        return null;
    }

    private static bool Ok(string candidate, bool directory)
    {
        try
        {
            if (directory ? !Directory.Exists(candidate) : !File.Exists(candidate))
                return false;
            return !PathSafety.IsBlocked(candidate);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOnMounted(string path, string? mountedRoot)
    {
        if (string.IsNullOrEmpty(mountedRoot))
            return false;
        var a = Path.GetPathRoot(path);
        var b = Path.GetPathRoot(mountedRoot);
        return !string.IsNullOrEmpty(a) && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOnLiveSystemDrive(string path)
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(system) && system.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? RemapUsers(string configured, string profileRoot)
    {
        const string marker = @"\Users\";
        var idx = configured.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var after = configured[(idx + marker.Length)..];
        var slash = after.IndexOf('\\');
        if (slash < 0)
            return null;
        var tail = after[(slash + 1)..];
        if (string.IsNullOrEmpty(tail))
            return null;
        return Path.Combine(profileRoot, tail);
    }

    private static string? SwapDrive(string configured, string? mountedRoot)
    {
        if (string.IsNullOrEmpty(mountedRoot) || configured.Length < 3 || configured[1] != ':')
            return null;
        var root = Path.GetPathRoot(mountedRoot);
        if (string.IsNullOrEmpty(root))
            return null;
        return root.TrimEnd('\\') + configured[2..];
    }
}
