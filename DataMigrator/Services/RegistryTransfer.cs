using System.Diagnostics;
using System.Text;
using DataMigrator.Models;
using Microsoft.Win32;

namespace DataMigrator.Services;

internal static class RegistryTransfer
{
    private static readonly string[] AllowedPrefixes =
    [
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Outlook",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\15.0\Outlook",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\14.0\Outlook",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\Windows Messaging Subsystem\Profiles",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\Outlook",
        @"HKEY_CURRENT_USER\Software\Crypto Pro",
        @"HKEY_CURRENT_USER\Software\CryptoPro",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Word",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Excel",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\PowerPoint",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\16.0\Common",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\15.0\Word",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\15.0\Excel",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\15.0\PowerPoint",
        @"HKEY_CURRENT_USER\Software\Microsoft\Office\15.0\Common",
        @"HKEY_CURRENT_USER\Network"
    ];

    public static bool KeyExists(string regPath)
    {
        var sub = ToCurrentUserSubKey(regPath);
        if (sub == null)
            return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(sub);
            return key != null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static bool Export(IReadOnlyList<string> keys, string destination, out string error)
    {
        error = "";
        var chunks = new List<string>();
        foreach (var key in keys)
        {
            if (!KeyExists(key))
                continue;
            var temp = Path.Combine(Path.GetTempPath(), "dm-" + Guid.NewGuid().ToString("N") + ".reg");
            try
            {
                if (!Run($"export \"{key}\" \"{temp}\" /y", out var runError))
                {
                    error = runError;
                    continue;
                }
                var body = File.ReadAllText(temp);
                var split = body.IndexOf('\n');
                if (split >= 0)
                    chunks.Add(body[(split + 1)..].Trim());
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // temp file is harmless
                }
            }
        }

        if (chunks.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(error))
                error = "Подходящие разделы реестра не найдены.";
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine(ToolInfo.RegMarker);
        sb.AppendLine();
        foreach (var chunk in chunks)
        {
            sb.AppendLine(chunk);
            sb.AppendLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, sb.ToString(), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        error = "";
        return true;
    }

    public static bool Validate(string path, out string error)
    {
        error = "";
        if (!File.Exists(path))
        {
            error = "Файл реестра не найден.";
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!text.Contains("Windows Registry Editor Version 5.00", StringComparison.Ordinal))
        {
            error = "Это не файл реестра Windows.";
            return false;
        }

        if (!text.Contains(ToolInfo.RegMarker, StringComparison.Ordinal))
        {
            error = "Файл реестра создан не этой программой. Импорт отменён.";
            return false;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length < 3 || line[0] != '[' || line[^1] != ']')
                continue;
            var key = line[1..^1];
            if (key.StartsWith('-') || !IsAllowed(key))
            {
                error = "В файле есть посторонний раздел реестра. Импорт отменён.";
                return false;
            }
        }

        return true;
    }

    public static bool Import(string path, out string error)
    {
        if (!Validate(path, out error))
            return false;
        return Run($"import \"{path}\"", out error);
    }

    private static bool IsAllowed(string key)
    {
        foreach (var prefix in AllowedPrefixes)
        {
            if (key.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            if (key.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? ToCurrentUserSubKey(string path)
    {
        const string shortName = @"HKCU\";
        const string longName = @"HKEY_CURRENT_USER\";
        if (path.StartsWith(shortName, StringComparison.OrdinalIgnoreCase))
            return path[shortName.Length..];
        if (path.StartsWith(longName, StringComparison.OrdinalIgnoreCase))
            return path[longName.Length..];
        return null;
    }

    private static bool Run(string args, out string error)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                error = "Не удалось запустить reg.exe.";
                return false;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // already exiting
                }
                error = "reg.exe не завершился вовремя.";
                return false;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                if (string.IsNullOrWhiteSpace(error))
                    error = "reg.exe завершился с ошибкой " + process.ExitCode;
                return false;
            }

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
