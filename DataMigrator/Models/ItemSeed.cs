using DataMigrator.Services;

namespace DataMigrator.Models;

internal sealed class ItemSeed
{
    public required string Id { get; init; }
    public required ItemCategory Category { get; init; }
    public required string SummaryName { get; init; }
    public required string DisplayName { get; init; }
    public required string Explanation { get; init; }
    public string Path { get; init; } = "";
    public ItemKind Kind { get; init; } = ItemKind.Folder;
    public bool Selected { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public bool Exists { get; init; } = true;
    public string? Notes { get; init; }
    public string? StoredName { get; init; }
    public string? SizeOverride { get; init; }
    public bool ShowPath { get; init; }
    public ExclusionRules Exclusions { get; init; } = ExclusionRules.None;
    public IReadOnlyList<string>? RegistryKeys { get; init; }
}
