using System.Text.RegularExpressions;

namespace DataMigrator.Services;

internal static class IbasesParser
{
    public sealed record InfoBase(string Name, bool IsFile, string? FilePath, string ConnectRaw);

    private static readonly Regex FileQuoted = new(
        @"File\s*=\s*""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex FileBare = new(
        @"File\s*=\s*([^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ServerMarker = new(
        @"(^|[;])\s*Srvr\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static IReadOnlyList<InfoBase> Parse(string filePath)
    {
        return ParseText(TextFile.ReadAuto(filePath));
    }

    public static IReadOnlyList<InfoBase> ParseText(string text)
    {
        var list = new List<InfoBase>();
        string? section = null;
        string? connect = null;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(connect))
            {
                section = null;
                connect = null;
                return;
            }
            list.Add(Interpret(section.Trim(), connect.Trim()));
            section = null;
            connect = null;
        }

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;
            if (trimmed.Length > 2 && trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                Flush();
                section = trimmed[1..^1];
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0 || section == null)
                continue;
            var key = trimmed[..eq].Trim();
            if (key.Equals("Connect", StringComparison.OrdinalIgnoreCase))
                connect = trimmed[(eq + 1)..].Trim();
        }

        Flush();
        return list;
    }

    private static InfoBase Interpret(string name, string connect)
    {
        var quoted = FileQuoted.Match(connect);
        if (quoted.Success)
        {
            var path = quoted.Groups[1].Value.Trim();
            if (path.Length > 0)
                return new InfoBase(name, true, path, connect);
        }

        if (connect.Contains("File", StringComparison.OrdinalIgnoreCase))
        {
            var bare = FileBare.Match(connect);
            if (bare.Success)
            {
                var path = bare.Groups[1].Value.Trim().Trim('"');
                if (path.Length > 0 && !path.Contains('=', StringComparison.Ordinal))
                    return new InfoBase(name, true, path, connect);
            }
        }

        return new InfoBase(name, false, null, connect);
    }

    public static bool IsServer(string connect)
    {
        try
        {
            return ServerMarker.IsMatch(connect);
        }
        catch (RegexMatchTimeoutException)
        {
            return connect.Contains("Srvr", StringComparison.OrdinalIgnoreCase);
        }
    }
}
