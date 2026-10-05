namespace DataMigrator.Models;

public sealed class DetectedProfile
{
    public string Display { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsBackup { get; set; }
}
