namespace DataMigrator.Services;

public sealed class ExclusionRules
{
    private readonly HashSet<string> _directories;
    private readonly string[] _prefixes;
    private readonly string[] _filePatterns;

    public static ExclusionRules None { get; } = new([], [], []);

    public ExclusionRules(
        IEnumerable<string> directories,
        IEnumerable<string> prefixes,
        IEnumerable<string> filePatterns,
        bool chromiumUserData = false,
        bool firefoxUserData = false)
    {
        _directories = new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase);
        _prefixes = prefixes.ToArray();
        _filePatterns = filePatterns.ToArray();
        ChromiumUserData = chromiumUserData;
        FirefoxUserData = firefoxUserData;
    }

    public bool ChromiumUserData { get; }
    public bool FirefoxUserData { get; }

    public bool ShouldSkipDirectory(string name)
    {
        if (_directories.Contains(name))
            return true;
        foreach (var prefix in _prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool ShouldSkipFile(string name)
    {
        foreach (var pattern in _filePatterns)
        {
            if (Matches(name, pattern))
                return true;
        }
        return false;
    }

    private static bool Matches(string name, string pattern)
    {
        if (pattern.Length > 2 && pattern.StartsWith('*') && pattern.EndsWith('*'))
            return name.Contains(pattern[1..^1], StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*'))
            return name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*'))
            return name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    // Name filter for anything walked inside an allowed profile folder.
    // The browser item itself uses ChromiumUserData and copies only the whitelist.
    public static ExclusionRules Chromium { get; } = ChromiumRules(userData: false);

    public static ExclusionRules ChromiumProfile { get; } = ChromiumRules(userData: true);

    private static ExclusionRules ChromiumRules(bool userData) => new(
        directories:
        [
            "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
            "Service Worker", "Crashpad", "BrowserMetrics", "Safe Browsing",
            "DawnGraphiteCache", "DawnWebGPUCache", "GraphiteDawnCache", "DawnCache",
            "Media Cache", "Crash Reports", "component_crx_cache", "extensions_crx_cache",
            "OptimizationHints", "OnDeviceHeadSuggestModel", "Crowd Deny", "MEIPreload",
            "SSLErrorAssistant", "Subresource Filter", "FileTypePolicies", "hyphen-data",
            "ZxcvbnData", "OriginTrials", "CertificateRevocation", "SafetyTips",
            "FirstPartySetsPreloaded", "PrivacySandboxAttestationsPreloaded",
            "AmountExtractionHeuristicRegexes", "AutofillStates", "TrustTokenKeyCommitments",
            "PKIMetadata", "WasmTtsEngine", "WidevineCdm", "pnacl", "PepperFlash"
        ],
        prefixes:
        [
            "optimization_guide", "OptGuide", "BrowserMetrics", "Crashpad",
            "Safe Browsing", "OnDevice", "screen_ai", "segmentation_platform"
        ],
        filePatterns: ["*.tmp", "lockfile", "SingletonLock", "SingletonCookie", "SingletonSocket", "BrowserMetrics*"],
        chromiumUserData: userData);

    public static ExclusionRules WorkFiles { get; } = new(
        directories:
        [
            "Steam", "steamapps", "Games", "Игры", "Epic Games", "XboxGames",
            "Wallpaper", "Wallpapers", "Обои"
        ],
        prefixes: [],
        filePatterns: ["TranscodedWallpaper"]);

    public static ExclusionRules Firefox { get; } = new(
        directories:
        [
            "cache2", "startupCache", "shader-cache", "thumbnails", "crashes",
            "datareporting", "saved-telemetry-pings", "minidumps", "OfflineCache",
            "jumpListCache", "cache"
        ],
        prefixes: [],
        filePatterns: ["parent.lock", ".parentlock"]);

    public static ExclusionRules FirefoxProfile { get; } = new(
        directories: [],
        prefixes: [],
        filePatterns: ["parent.lock", ".parentlock"],
        firefoxUserData: true);

    public static ExclusionRules OneC { get; } = new(
        directories: ["cache", "logs", "log", "dumps", "dump", "tmpl"],
        prefixes: [],
        filePatterns: ["*.dmp", "*.log", "*.lgp"]);

    public static ExclusionRules Outlook { get; } = new(
        directories: [],
        prefixes: [],
        filePatterns: ["*.ost", "*.tmp"]);
}
