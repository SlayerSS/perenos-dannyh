using System.Collections.ObjectModel;
using DataMigrator.Models;

namespace DataMigrator.ViewModels;

public sealed class ItemSection
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Hint { get; init; }
    public ObservableCollection<ScanItem> Items { get; init; } = new();
    public int Count => Items.Count;
    public string CountText => Count.ToString();
}
