using System.Collections.ObjectModel;
using System.Text;
using DataMigrator.Models;

namespace DataMigrator.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly StringBuilder _log = new();
    private bool _isBusy;
    private bool _overwrite;
    private bool _keepBak = true;
    private string _pathText = "";
    private string _pathLabel = "Куда сохранить копию";
    private string _statusText = "";
    private string _sourceCaption = "";
    private string _currentFileText = "";
    private string _logText = "";
    private string _liveSourceLine = "";
    private string _currentUserLine = "";
    private double _progress;
    private bool _progressIndeterminate;
    private bool _showLiveSource = true;
    private bool _showProfileRow;
    private bool _showCurrentUserDestination;
    private bool _hasUserFolders;
    private bool _hasBrowsers;
    private bool _hasOutlookSettings;
    private bool _hasOutlookStores;
    private bool _hasOneCSettings;
    private bool _hasOneCBases;
    private bool _hasApps;
    private bool _hasAnyItems;
    private bool _showEmptyHint = true;
    private bool _needsAccess;
    private DriveEntry? _selectedDrive;
    private DetectedProfile? _selectedProfile;
    private ItemSection? _selectedSection;

    public ObservableCollection<DriveEntry> Drives { get; } = new();
    public ObservableCollection<DetectedProfile> Profiles { get; } = new();
    public ObservableCollection<ScanItem> UserFolders { get; } = new();
    public ObservableCollection<ScanItem> Browsers { get; } = new();
    public ObservableCollection<ScanItem> OutlookSettings { get; } = new();
    public ObservableCollection<ScanItem> OutlookStores { get; } = new();
    public ObservableCollection<ScanItem> OneCSettings { get; } = new();
    public ObservableCollection<ScanItem> OneCBases { get; } = new();
    public ObservableCollection<ScanItem> Apps { get; } = new();
    public ObservableCollection<ItemSection> Sections { get; } = new();

    public TransferMode Mode { get; set; } = TransferMode.LiveToFolder;

    public string PathText { get => _pathText; set => Set(ref _pathText, value); }
    public string PathLabel { get => _pathLabel; set => Set(ref _pathLabel, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string SourceCaption { get => _sourceCaption; set => Set(ref _sourceCaption, value); }
    public string CurrentFileText { get => _currentFileText; set => Set(ref _currentFileText, value); }
    public string LogText { get => _logText; set => Set(ref _logText, value); }
    public string LiveSourceLine { get => _liveSourceLine; set => Set(ref _liveSourceLine, value); }
    public string CurrentUserLine { get => _currentUserLine; set => Set(ref _currentUserLine, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool ProgressIndeterminate { get => _progressIndeterminate; set => Set(ref _progressIndeterminate, value); }
    public bool ShowLiveSource { get => _showLiveSource; set => Set(ref _showLiveSource, value); }
    public bool ShowProfileRow { get => _showProfileRow; set => Set(ref _showProfileRow, value); }
    public bool ShowCurrentUserDestination { get => _showCurrentUserDestination; set => Set(ref _showCurrentUserDestination, value); }

    public bool HasUserFolders { get => _hasUserFolders; private set => Set(ref _hasUserFolders, value); }
    public bool HasBrowsers { get => _hasBrowsers; private set => Set(ref _hasBrowsers, value); }
    public bool HasOutlookSettings { get => _hasOutlookSettings; private set => Set(ref _hasOutlookSettings, value); }
    public bool HasOutlookStores { get => _hasOutlookStores; private set => Set(ref _hasOutlookStores, value); }
    public bool HasOneCSettings { get => _hasOneCSettings; private set => Set(ref _hasOneCSettings, value); }
    public bool HasOneCBases { get => _hasOneCBases; private set => Set(ref _hasOneCBases, value); }
    public bool HasApps { get => _hasApps; private set => Set(ref _hasApps, value); }
    public bool HasAnyItems { get => _hasAnyItems; private set => Set(ref _hasAnyItems, value); }
    public bool ShowEmptyHint { get => _showEmptyHint; private set => Set(ref _showEmptyHint, value); }
    public bool NeedsAccess { get => _needsAccess; set => Set(ref _needsAccess, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            Set(ref _isBusy, value);
            Raise(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public bool OverwriteExisting
    {
        get => _overwrite;
        set
        {
            Set(ref _overwrite, value);
            Raise(nameof(PolicyText));
        }
    }

    public bool KeepBak
    {
        get => _keepBak;
        set
        {
            Set(ref _keepBak, value);
            Raise(nameof(PolicyText));
        }
    }

    public string PolicyText => OverwriteExisting
        ? KeepBak
            ? "Политика: заменять существующие файлы и один раз сохранять прежний файл как .bak."
            : "Политика: заменять существующие файлы."
        : "Политика: не затирать существующие, записать конфликт в журнал.";

    public DriveEntry? SelectedDrive
    {
        get => _selectedDrive;
        set => Set(ref _selectedDrive, value);
    }

    public DetectedProfile? SelectedProfile
    {
        get => _selectedProfile;
        set => Set(ref _selectedProfile, value);
    }

    public ItemSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (Set(ref _selectedSection, value))
                Raise(nameof(HasSelectedSection));
        }
    }

    public bool HasSelectedSection => SelectedSection != null;

    public string SelectionSummary
    {
        get
        {
            var all = AllItems().ToList();
            if (all.Count == 0)
                return "";
            var marked = all.Count(i => i.IsSelected && i.IsEnabled);
            return $"Отмечено {marked} из {all.Count}";
        }
    }

    public void AppendLog(string line)
    {
        _log.AppendLine(line);
        LogText = _log.ToString();
    }

    public IEnumerable<ScanItem> AllItems()
    {
        return UserFolders
            .Concat(Browsers)
            .Concat(OutlookSettings)
            .Concat(OutlookStores)
            .Concat(OneCSettings)
            .Concat(OneCBases)
            .Concat(Apps);
    }

    public void ReplaceItems(IEnumerable<ScanItem> items)
    {
        Clear(UserFolders);
        Clear(Browsers);
        Clear(OutlookSettings);
        Clear(OutlookStores);
        Clear(OneCSettings);
        Clear(OneCBases);
        Clear(Apps);

        foreach (var item in items)
        {
            switch (item.Category)
            {
                case ItemCategory.UserFolder: UserFolders.Add(item); break;
                case ItemCategory.Browser: Browsers.Add(item); break;
                case ItemCategory.OutlookSettings: OutlookSettings.Add(item); break;
                case ItemCategory.OutlookStore: OutlookStores.Add(item); break;
                case ItemCategory.OneCSettings: OneCSettings.Add(item); break;
                case ItemCategory.OneCDatabase: OneCBases.Add(item); break;
                default: Apps.Add(item); break;
            }
        }

        HasUserFolders = UserFolders.Count > 0;
        HasBrowsers = Browsers.Count > 0;
        HasOutlookSettings = OutlookSettings.Count > 0;
        HasOutlookStores = OutlookStores.Count > 0;
        HasOneCSettings = OneCSettings.Count > 0;
        HasOneCBases = OneCBases.Count > 0;
        HasApps = Apps.Count > 0;
        HasAnyItems = AllItems().Any();
        ShowEmptyHint = !HasAnyItems;
        RebuildSections();
        Raise(nameof(SelectionSummary));
    }

    public void SetAll(bool selected)
    {
        foreach (var item in AllItems())
        {
            if (item.IsEnabled)
                item.IsSelected = selected;
        }
    }

    public void SetGroup(string group, bool selected)
    {
        var items = group switch
        {
            "UserFolders" => UserFolders,
            "Browsers" => Browsers,
            "OutlookSettings" => OutlookSettings,
            "OutlookStores" => OutlookStores,
            "OneCSettings" => OneCSettings,
            "OneCBases" => OneCBases,
            "Apps" => Apps,
            _ => null
        };
        if (items == null)
            return;
        foreach (var item in items)
        {
            if (item.IsEnabled)
                item.IsSelected = selected;
        }
    }

    private void RebuildSections()
    {
        var previous = SelectedSection?.Key;
        Sections.Clear();
        AddSection("UserFolders", "Папки пользователя", "Рабочий стол, документы и остальные личные папки", UserFolders);
        AddSection("Browsers", "Браузеры", "Закладки, пароли и настройки. Кэш не копируется", Browsers);
        AddSection("OutlookSettings", "Outlook", "Подписи, учётные записи и настройки", OutlookSettings);
        AddSection("OutlookStores", "Базы Outlook", "Файлы PST. Кэш Exchange (OST) не переносится", OutlookStores);
        AddSection("OneCSettings", "1С", "Список баз ibases.v8i и настройки пользователя", OneCSettings);
        AddSection("OneCBases", "Базы 1С", "Файловые базы. Серверные базы остаются на сервере", OneCBases);
        AddSection("Apps", "Программы", "Только папки настроек известных программ", Apps);

        SelectedSection = Sections.FirstOrDefault(s => s.Key == previous) ?? Sections.FirstOrDefault();
    }

    private void AddSection(string key, string title, string hint, ObservableCollection<ScanItem> items)
    {
        if (items.Count == 0)
            return;
        foreach (var item in items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ScanItem.IsSelected))
                    Raise(nameof(SelectionSummary));
            };
        }
        Sections.Add(new ItemSection
        {
            Key = key,
            Title = title,
            Hint = hint,
            Items = items
        });
    }

    private static void Clear(ObservableCollection<ScanItem> items)
    {
        items.Clear();
    }
}
