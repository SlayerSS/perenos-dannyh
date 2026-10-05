namespace DataMigrator.Models;

public sealed class BackupManifest
{
    public string ToolVersion { get; set; } = ToolInfo.Version;
    public DateTime CreatedUtc { get; set; }
    public bool Completed { get; set; }
    public int ErrorCount { get; set; }
    public int LockedCount { get; set; }
    public int ConflictCount { get; set; }
    public string SourceMachine { get; set; } = "";
    public string SourceUser { get; set; } = "";
    public string SourceProfileRoot { get; set; } = "";
    public List<ManifestItem> Items { get; set; } = new();
}

public sealed class ManifestItem
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string StoredRelativePath { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Notes { get; set; }
    public string? Explanation { get; set; }
    public string? KnownFolderId { get; set; }
    public string? PathInsideKnownFolder { get; set; }
    public string? ProfileRelativePath { get; set; }
}
