namespace DataMigrator.Models;

internal sealed class ScanContext
{
    public TransferMode Mode { get; init; }
    public string SourceProfileRoot { get; init; } = "";
    public string DestinationProfileRoot { get; init; } = "";
    public string ScannedPath { get; init; } = "";
    public bool FromManifest { get; init; }
    public bool ManifestWasIncomplete { get; init; }
    public string SourceUser { get; init; } = "";
    public string SourceMachine { get; init; } = "";
}

internal sealed class ScanBundle
{
    public required ScanContext Context { get; init; }
    public required List<ScanItem> Items { get; init; }
    public List<string> Warnings { get; init; } = new();
}

internal sealed class CopyProgressReport
{
    public string CurrentFile { get; init; } = "";
    public double Percent { get; init; }
    public string? LogLine { get; init; }
}

internal sealed class CopyPlan
{
    public TransferMode Mode { get; init; }
    public string BackupRoot { get; init; } = "";
    public string SourceProfileRoot { get; init; } = "";
    public string DestinationProfileRoot { get; init; } = "";
    public string SourceMachine { get; init; } = "";
    public string SourceUser { get; init; } = "";
    public bool Overwrite { get; init; }
    public bool KeepBak { get; init; }
    public bool ImportRegistry { get; init; }
    public IReadOnlyList<ScanItem> Items { get; init; } = Array.Empty<ScanItem>();
}

internal sealed class CopyResult
{
    public int FilesCopied { get; set; }
    public int Locked { get; set; }
    public int Conflicts { get; set; }
    public int Errors { get; set; }
    public bool Cancelled { get; set; }
    public bool DiskFull { get; set; }
}
