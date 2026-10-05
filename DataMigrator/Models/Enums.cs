namespace DataMigrator.Models;

public enum TransferMode
{
    LiveToFolder,
    FolderToLive,
    DiskToLive
}

public enum ItemKind
{
    Folder,
    File,
    Registry
}

public enum ItemCategory
{
    UserFolder,
    Browser,
    OutlookSettings,
    OutlookStore,
    OneCSettings,
    OneCDatabase,
    AppConfig
}
