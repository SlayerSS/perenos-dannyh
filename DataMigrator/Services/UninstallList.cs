using Microsoft.Win32;

namespace DataMigrator.Services;

internal static class UninstallList
{
    private static readonly string[] Keys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static IReadOnlyList<string> Read()
    {
        var names = new List<string>();
        try
        {
            ReadHive(Registry.LocalMachine, names);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // HKLM may be unreadable; HKCU is enough to continue.
        }

        try
        {
            ReadHive(Registry.CurrentUser, names);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Matches(IReadOnlyList<string> installed, IEnumerable<string> hints)
    {
        foreach (var name in installed)
        {
            foreach (var hint in hints)
            {
                if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static void ReadHive(RegistryKey hive, List<string> names)
    {
        foreach (var sub in Keys)
            ReadKey(hive, sub, names);
    }

    private static void ReadKey(RegistryKey hive, string sub, List<string> names)
    {
        try
        {
            using var key = hive.OpenSubKey(sub);
            if (key == null)
                return;
            foreach (var childName in key.GetSubKeyNames())
            {
                try
                {
                    using var app = key.OpenSubKey(childName);
                    if (app?.GetValue("DisplayName") is string display && !string.IsNullOrWhiteSpace(display))
                        names.Add(display.Trim());
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
