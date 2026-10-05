using System.IO;

namespace DataMigrator.Models;

public sealed class DriveEntry
{
    public string Root { get; set; } = "";
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    public string TypeText { get; set; } = "";
    public string FreeText { get; set; } = "";
    public bool IsRemovable { get; set; }
    public bool IsReady { get; set; }
    public DriveType DriveKind { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Label) ? Letter : $"{Letter}  {Label}";
}
