namespace DataMigrator.Models;

public static class ToolInfo
{
    public const string Version = "1.0.0";
    public const string RegMarker = "; DataMigrator-Export v1";
    public const string ManifestFileName = "backup-manifest.json";
    public const string JournalFileName = "журнал.txt";

    public const string ServerBaseNote =
        "серверная, копируется только строка подключения в ibases.v8i, сама база на сервере";
}
