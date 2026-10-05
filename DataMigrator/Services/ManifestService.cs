using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataMigrator.Models;

namespace DataMigrator.Services;

internal static class ManifestService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static void Save(string path, BackupManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, WriteOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static BackupManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(json, ReadOptions);
        if (manifest?.Items == null)
            throw new InvalidDataException("Файл копии повреждён или создан не этой программой.");
        return manifest;
    }

    public static ItemCategory ParseCategory(string? value) => value switch
    {
        "UserFolder" => ItemCategory.UserFolder,
        "Browser" => ItemCategory.Browser,
        "OutlookSettings" => ItemCategory.OutlookSettings,
        "OutlookStore" => ItemCategory.OutlookStore,
        "OneCSettings" => ItemCategory.OneCSettings,
        "OneCDatabase" => ItemCategory.OneCDatabase,
        "AppConfig" => ItemCategory.AppConfig,
        _ => ItemCategory.AppConfig
    };

    public static ItemKind ParseKind(string? value) => (value ?? "").ToLowerInvariant() switch
    {
        "file" => ItemKind.File,
        "reg" or "registry" => ItemKind.Registry,
        _ => ItemKind.Folder
    };

    public static string KindName(ItemKind kind) => kind switch
    {
        ItemKind.File => "file",
        ItemKind.Registry => "reg",
        _ => "folder"
    };
}
