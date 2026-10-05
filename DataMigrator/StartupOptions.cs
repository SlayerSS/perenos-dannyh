namespace DataMigrator;

internal static class StartupOptions
{
    public static string? GrantPath { get; private set; }

    public static void Read(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], "--grant-access", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Count)
                GrantPath = args[i + 1].Trim().Trim('"');
        }
    }
}
