namespace DataMigrator.Services;

internal sealed class AppDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string SummaryName { get; init; }
    public required string Explanation { get; init; }
    public required string[] InstallHints { get; init; }
    public required string Scope { get; init; }
    public required string RelativePath { get; init; }
    public bool DefaultSelected { get; init; } = true;
    public string? Warning { get; init; }
    public ExclusionRules Exclusions { get; init; } = ExclusionRules.None;
}

internal static class AppCatalog
{
    // Only paths that are well-known user config. Kontur, Diadoc and SBIS are not listed:
    // their stable user folders vary, and the web part already lives in the browser profile.
    public static readonly AppDefinition[] Apps =
    [
        new()
        {
            Id = "thunderbird",
            DisplayName = "Thunderbird",
            SummaryName = "Thunderbird",
            Explanation = "Почта и профиль Thunderbird. Кэш не копируется.",
            InstallHints = ["Thunderbird"],
            Scope = "Roaming",
            RelativePath = "Thunderbird",
            DefaultSelected = true,
            Exclusions = ExclusionRules.Firefox
        },
        new()
        {
            Id = "thebat",
            DisplayName = "The Bat!",
            SummaryName = "The Bat!",
            Explanation = "Почтовые данные The Bat!. Объём может быть большим.",
            InstallHints = ["The Bat"],
            Scope = "Roaming",
            RelativePath = "The Bat!",
            DefaultSelected = true
        },
        new()
        {
            Id = "thebat-ritlabs",
            DisplayName = "The Bat! (Ritlabs)",
            SummaryName = "The Bat!",
            Explanation = "Почтовые данные The Bat!. Объём может быть большим.",
            InstallHints = ["The Bat"],
            Scope = "Roaming",
            RelativePath = @"Ritlabs\The Bat!",
            DefaultSelected = true
        },
        new()
        {
            Id = "cryptopro-local",
            DisplayName = "CryptoPro — контейнеры ключей",
            SummaryName = "CryptoPro",
            Explanation = "Стандартная папка контейнеров ключей CryptoPro. По умолчанию не копируется: это закрытые ключи подписи.",
            InstallHints = ["CryptoPro", "КриптоПро", "Crypto Pro"],
            Scope = "Local",
            RelativePath = "Crypto Pro",
            DefaultSelected = false,
            Warning = "Содержит закрытые ключи. Копируйте только если подписи нужно перенести, и храните копию в надёжном месте."
        },
        new()
        {
            Id = "cryptopro-roaming",
            DisplayName = "CryptoPro — настройки пользователя",
            SummaryName = "CryptoPro",
            Explanation = "Папка настроек CryptoPro в профиле. Может содержать ключи, поэтому по умолчанию выключена.",
            InstallHints = ["CryptoPro", "КриптоПро", "Crypto Pro"],
            Scope = "Roaming",
            RelativePath = "Crypto Pro",
            DefaultSelected = false,
            Warning = "Проверьте содержимое перед копированием. Закрытые ключи переносить только осознанно."
        }
    ];

    public static string? ScopePath(string profileRoaming, string profileLocal, string scope)
    {
        return scope.Equals("Local", StringComparison.OrdinalIgnoreCase) ? profileLocal : profileRoaming;
    }
}
