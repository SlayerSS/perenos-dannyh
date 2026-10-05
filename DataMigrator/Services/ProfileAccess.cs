using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace DataMigrator.Services;

internal static class ProfileAccess
{
    public enum State
    {
        Readable,
        Denied,
        Missing
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static string CurrentAccount()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    public static State Probe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return State.Missing;

        try
        {
            var attr = File.GetAttributes(LongPath.Wrap(path));
            if ((attr & FileAttributes.Directory) == 0)
                return State.Missing;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return State.Denied;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return State.Missing;
        }

        return CanList(path) ? State.Readable : State.Denied;
    }

    /// <summary>
    /// Folder that must be opened. A readable disk root can still hide a locked Users folder or profile.
    /// </summary>
    public static string? FindDenied(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (Probe(path) == State.Denied)
            return path;

        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && PathSafety.Same(path, root))
        {
            var users = Path.Combine(path, "Users");
            if (Probe(users) == State.Denied)
                return users;
        }

        return null;
    }

    public static bool IsSafeToGrant(string path, out string reason)
    {
        reason = "";
        string full;
        try
        {
            full = PathSafety.Normalize(path);
        }
        catch
        {
            reason = "Путь не распознан.";
            return false;
        }

        if (!Directory.Exists(full) && Probe(full) == State.Missing)
        {
            reason = "Папка не найдена.";
            return false;
        }

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && PathSafety.Same(full, root))
        {
            reason = "Нельзя менять права на весь диск. Выберите папку пользователя.";
            return false;
        }

        if (PathSafety.IsBlocked(full))
        {
            reason = "Это системная папка. Права на неё не меняются.";
            return false;
        }

        var liveProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var liveUsers = Path.GetDirectoryName(liveProfile);
        if (!string.IsNullOrEmpty(liveUsers) && (PathSafety.Same(full, liveUsers) || PathSafety.IsUnder(full, liveUsers)))
        {
            reason = "Это пользователи текущего компьютера. Их права не меняются.";
            return false;
        }

        return true;
    }

    public static void RelaunchElevated(string grantPath)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException("Не найден файл программы.");

        var start = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "--grant-access \"" + grantPath.Replace("\"", "") + "\""
        };
        Process.Start(start);
    }

    public static async Task GrantAsync(string path, IProgress<string>? progress, CancellationToken ct)
    {
        var account = CurrentAccount();
        var takeown = Path.Combine(Environment.SystemDirectory, "takeown.exe");
        var icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
        if (!File.Exists(takeown) || !File.Exists(icacls))
            throw new InvalidOperationException("Не найдены takeown.exe или icacls.exe.");

        progress?.Report("Назначение владельца…");
        var takeownCode = await RunAsync(
            takeown,
            ["/F", path, "/R", "/D", "Y", "/A"],
            progress,
            ct);

        progress?.Report("Выдача права чтения пользователю " + account + "…");
        var grantCode = await RunAsync(
            icacls,
            [path, "/grant", account + ":(OI)(CI)RX", "/grant", "*S-1-5-32-544:(OI)(CI)F", "/T", "/C"],
            progress,
            ct);

        if (takeownCode != 0 && grantCode != 0)
            throw new InvalidOperationException("Не удалось изменить права. Коды: takeown " + takeownCode + ", icacls " + grantCode + ".");
    }

    private static bool CanList(string path)
    {
        try
        {
            using var enumerator = Directory.EnumerateFileSystemEntries(LongPath.Wrap(path)).GetEnumerator();
            _ = enumerator.MoveNext();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
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

    private static async Task<int> RunAsync(string file, string[] args, IProgress<string>? progress, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        try
        {
            start.StandardOutputEncoding = Console.OutputEncoding;
            start.StandardErrorEncoding = Console.OutputEncoding;
        }
        catch
        {
            // keep UTF-8
        }

        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var seen = 0;
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            seen++;
            if (seen == 1 || seen % 250 == 0)
                progress?.Report(Path.GetFileName(file) + ": обработано " + seen);
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить " + Path.GetFileName(file) + ".");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // already gone
            }
        });

        await process.WaitForExitAsync(ct);
        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
