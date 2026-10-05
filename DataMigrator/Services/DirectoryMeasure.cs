namespace DataMigrator.Services;

internal readonly record struct MeasureResult(long Bytes, int Count, bool Partial);

internal static class DirectoryMeasure
{
    public const int MaxFilesToCount = 100_000;

    public static MeasureResult Measure(
        string root,
        ExclusionRules rules,
        CancellationToken ct,
        IProgress<string>? progress,
        string label)
    {
        long bytes = 0;
        var count = 0;
        foreach (var file in DirectoryWalker.EnumerateFiles(root, rules, ct))
        {
            bytes += file.Length;
            count++;
            if (count >= MaxFilesToCount)
                return new MeasureResult(bytes, count, Partial: true);
            if (progress != null && count % 2000 == 0)
                progress.Report($"{label}: уже {count:N0} файлов…");
        }
        return new MeasureResult(bytes, count, Partial: false);
    }
}
