using DataMigrator.Services;

namespace DataMigrator.Models;

public sealed class ScanItem : ObservableObject
{
    private bool _isSelected;

    public string Id { get; set; } = "";
    public ItemCategory Category { get; set; }
    public string SummaryName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string? Notes { get; set; }
    public string? PathDisplay { get; set; }
    public string? SizeOverride { get; set; }
    public ItemKind Kind { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool Exists { get; set; } = true;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool SizeIsPartial { get; set; }
    public string ReadPath { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string StoredRelativePath { get; set; } = "";
    public string? KnownFolderId { get; set; }
    public string? PathInsideKnownFolder { get; set; }
    public string? ProfileRelativePath { get; set; }
    public ExclusionRules Exclusions { get; set; } = ExclusionRules.None;
    public IReadOnlyList<string> RegistryKeys { get; set; } = Array.Empty<string>();

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string SizeText
    {
        get
        {
            if (!string.IsNullOrEmpty(SizeOverride))
                return SizeOverride;
            if (!Exists)
                return "не найдена";
            if (Kind == ItemKind.Registry)
                return "реестр";
            if (SizeIsPartial)
                return $"{ByteSize.Format(SizeBytes)} · {ByteSize.Files(FileCount, partial: true)}";
            if (FileCount <= 0 && SizeBytes <= 0)
                return "пусто";
            if (Kind == ItemKind.File)
                return ByteSize.Format(SizeBytes);
            return $"{ByteSize.Format(SizeBytes)} · {ByteSize.Files(FileCount, partial: false)}";
        }
    }
}
