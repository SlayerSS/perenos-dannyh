using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DataMigrator.Models;
using DataMigrator.Services;
using DataMigrator.ViewModels;
using Microsoft.Win32;

namespace DataMigrator;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DataScanner _scanner = new();
    private readonly CopyEngine _engine = new();
    private CancellationTokenSource? _cts;
    private ScanContext? _scan;
    private TransferMode? _applied;
    private bool _ready;
    private bool _suppressDrive;
    private bool _suppressProfile;
    private int _profileLoadVersion;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _vm.LiveSourceLine = "Откуда: " + profile;
        _vm.CurrentUserLine = "Куда: текущий пользователь " + profile;
        _vm.KeepBak = true;
        _ready = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(StartupOptions.GrantPath))
        {
            RbDisk.IsChecked = true;
            _vm.PathText = StartupOptions.GrantPath;
            await RefreshDrivesAsync();
            await GrantCoreAsync(StartupOptions.GrantPath, alreadyConfirmed: true);
            return;
        }

        RbBackup.IsChecked = true;
        await RefreshDrivesAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_vm.IsBusy)
            return;
        var answer = MessageBox.Show(
            "Операция ещё выполняется. Прервать и закрыть окно?",
            Brand.WindowTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _cts?.Cancel();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { IsChecked: true, Tag: string tag })
            return;
        if (!Enum.TryParse(tag, out TransferMode mode))
            return;
        ApplyMode(mode);
    }

    private void ApplyMode(TransferMode mode)
    {
        if (_applied == mode)
            return;
        _applied = mode;
        _vm.Mode = mode;
        _scan = null;
        _vm.ReplaceItems(Array.Empty<ScanItem>());
        _vm.StatusText = "";
        _vm.CurrentFileText = "";
        _vm.Progress = 0;

        switch (mode)
        {
            case TransferMode.LiveToFolder:
                _vm.PathLabel = "Куда сохранить копию";
                _vm.ShowLiveSource = true;
                _vm.ShowProfileRow = false;
                _vm.ShowCurrentUserDestination = false;
                break;
            case TransferMode.FolderToLive:
                _vm.PathLabel = "Папка с копией";
                _vm.ShowLiveSource = false;
                _vm.ShowProfileRow = true;
                _vm.ShowCurrentUserDestination = true;
                break;
            default:
                _vm.PathLabel = "Диск, профиль или папка копии";
                _vm.ShowLiveSource = false;
                _vm.ShowProfileRow = true;
                _vm.ShowCurrentUserDestination = true;
                break;
        }

        RefreshAccess();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDrivesAsync();

    private async Task RefreshDrivesAsync()
    {
        IReadOnlyList<DriveEntry> drives;
        try
        {
            drives = await Task.Run(DriveScanner.Scan);
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Не удалось прочитать диски: " + ex.Message;
            return;
        }

        var keep = _vm.SelectedDrive?.Root;
        _suppressDrive = true;
        _vm.Drives.Clear();
        foreach (var drive in drives)
            _vm.Drives.Add(drive);
        _vm.SelectedDrive = keep == null
            ? null
            : _vm.Drives.FirstOrDefault(d => d.Root.Equals(keep, StringComparison.OrdinalIgnoreCase));
        _suppressDrive = false;
    }

    private async void Drive_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDrive || _vm.SelectedDrive is not { IsReady: true } drive)
            return;
        _vm.PathText = drive.Root;
        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            var reserved = ReservedWarning(drive);
            ForgetSearch(reserved ?? $"Диск {drive.Root.TrimEnd('\\')} — только куда сохранить копию. На этом диске ничего не искали. Предыдущий список сброшен: он был с этого компьютера.");
            if (reserved != null)
                _vm.SourceCaption = reserved;
            RefreshAccess();
            return;
        }

        ForgetSearch("");
        await LoadProfilesAsync(drive.Root);
    }

    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfile || _vm.SelectedProfile == null || _vm.Mode == TransferMode.LiveToFolder)
            return;
        _vm.PathText = _vm.SelectedProfile.Path;
        ForgetSearch("Выбран другой профиль. Нажмите «Найти данные».");
        RefreshAccess();
    }

    private async Task LoadProfilesAsync(string path)
    {
        var version = ++_profileLoadVersion;
        IReadOnlyList<DetectedProfile> options;
        try
        {
            options = await Task.Run(() => ProfileDetector.ListOptions(path));
        }
        catch
        {
            options = Array.Empty<DetectedProfile>();
        }

        if (version != _profileLoadVersion)
            return;

        _suppressProfile = true;
        _vm.Profiles.Clear();
        foreach (var option in options)
            _vm.Profiles.Add(option);
        _vm.SelectedProfile = options.Count == 1 ? options[0] : null;
        _suppressProfile = false;

        if (options.Count == 1)
            _vm.PathText = options[0].Path;

        RefreshAccess();
        if (_vm.NeedsAccess)
            return;

        if (options.Count == 0)
            _vm.StatusText = $"На {path.TrimEnd('\\')} нет пользователей Windows и нет сохранённой копии.";
        else if (options.Count == 1)
            _vm.StatusText = "Найден один вариант: " + options[0].Display + ". Нажмите «Найти данные».";
        else
            _vm.StatusText = $"Найдено вариантов: {options.Count}. Выберите профиль или копию и нажмите «Найти данные».";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _vm.Mode == TransferMode.LiveToFolder ? "Куда сохранить копию" : "Выберите папку источника",
            Multiselect = false
        };
        if (Directory.Exists(_vm.PathText))
            dialog.InitialDirectory = _vm.PathText;
        if (dialog.ShowDialog(this) != true)
            return;

        _vm.PathText = dialog.FolderName;
        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            ForgetSearch("Папка назначения изменена. Предыдущий список сброшен.");
            RefreshAccess();
            return;
        }

        ForgetSearch("");
        _ = LoadProfilesAsync(dialog.FolderName);
    }

    private static string? ReservedWarning(DriveEntry? drive)
    {
        if (drive == null)
            return null;
        var label = drive.Label ?? "";
        if (!label.Contains("Зарезервировано", StringComparison.OrdinalIgnoreCase)
            && !label.Contains("System Reserved", StringComparison.OrdinalIgnoreCase))
            return null;
        return $"Диск {drive.Letter} — служебный раздел «{label}». Windows там нет, папка пустая, копию на него класть нельзя. Выберите обычный диск или флешку.";
    }

    private void ForgetSearch(string status)
    {
        _scan = null;
        _vm.ReplaceItems(Array.Empty<ScanItem>());
        _vm.SourceCaption = "";
        _vm.CurrentFileText = "";
        _vm.Progress = 0;
        if (!string.IsNullOrEmpty(status))
            _vm.StatusText = status;
    }

    private void RefreshAccess(bool writeStatus = true)
    {
        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            _vm.NeedsAccess = false;
            return;
        }

        var candidate = _vm.SelectedProfile?.Path;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = _vm.PathText;
        var denied = ProfileAccess.FindDenied(candidate);
        _vm.NeedsAccess = denied != null;
        if (writeStatus && denied != null)
            _vm.StatusText = "Нет доступа к «" + denied + "». Можно нажать «Дать доступ» или сразу «Найти данные».";
    }

    private async void Grant_Click(object sender, RoutedEventArgs e)
    {
        var target = AccessTarget();
        if (target == null)
        {
            RefreshAccess();
            if (!_vm.NeedsAccess)
                _vm.StatusText = "Доступ уже есть.";
            return;
        }

        await GrantCoreAsync(target, alreadyConfirmed: false);
    }

    private string? AccessTarget()
    {
        if (_vm.Mode == TransferMode.LiveToFolder)
            return null;
        var candidate = _vm.SelectedProfile?.Path;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = _vm.PathText;
        return ProfileAccess.FindDenied(candidate);
    }

    private async Task GrantCoreAsync(string target, bool alreadyConfirmed)
    {
        if (!ProfileAccess.IsSafeToGrant(target, out var reason))
        {
            MessageBox.Show(reason, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!alreadyConfirmed)
        {
            var answer = MessageBox.Show(
                "Открыть эту папку для текущего пользователя?\n\n" + target +
                "\n\nБудет выдано право чтения. Файлы не удаляются и не переносятся.\n" +
                "Windows спросит разрешение администратора.",
                Brand.WindowTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        if (!ProfileAccess.IsElevated())
        {
            try
            {
                ProfileAccess.RelaunchElevated(target);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                _vm.StatusText = "Разрешение администратора не дано. Без него чужой профиль не открыть.";
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось запросить права администратора.\n" + ex.Message, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Application.Current.Shutdown();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        LogExpander.IsExpanded = true;
        _vm.IsBusy = true;
        _vm.ProgressIndeterminate = true;
        _vm.Progress = 0;
        _vm.CurrentFileText = "Выдача прав…";
        _vm.AppendLog("");
        _vm.AppendLog("——— Доступ: " + target + " ———");
        _vm.AppendLog("Учётная запись: " + ProfileAccess.CurrentAccount());
        var progress = new Progress<string>(text =>
        {
            _vm.CurrentFileText = text;
            _vm.StatusText = text;
        });

        try
        {
            await ProfileAccess.GrantAsync(target, progress, ct);
            var still = ProfileAccess.Probe(target);
            if (still == ProfileAccess.State.Denied)
            {
                _vm.NeedsAccess = true;
                _vm.StatusText = "Папка всё ещё закрыта. Нажмите «Найти данные»: программа пропустит то, что не читается.";
                _vm.AppendLog("После выдачи прав папка всё ещё не читается целиком. Поиск можно запустить.");
            }
            else
            {
                _vm.NeedsAccess = false;
                _vm.StatusText = "Доступ открыт. Нажмите «Найти данные».";
                _vm.AppendLog("Доступ открыт.");
                await LoadProfilesAsync(target);
            }
        }
        catch (OperationCanceledException)
        {
            _vm.StatusText = "Выдача прав отменена.";
            _vm.AppendLog("Выдача прав отменена.");
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Не удалось выдать доступ.";
            _vm.AppendLog("Ошибка доступа: " + ex.Message);
            MessageBox.Show("Не удалось выдать доступ.\n" + ex.Message, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
            _vm.ProgressIndeterminate = false;
            _vm.CurrentFileText = "";
            RefreshAccess(writeStatus: false);
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
            _vm.SetGroup(tag, true);
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
            _vm.SetGroup(tag, false);
    }

    private void SelectEverything_Click(object sender, RoutedEventArgs e) => _vm.SetAll(true);

    private void ClearEverything_Click(object sender, RoutedEventArgs e) => _vm.SetAll(false);

    private async void Find_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy)
            return;

        RefreshAccess();

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _scan = null;
        _vm.ReplaceItems(Array.Empty<ScanItem>());
        _vm.IsBusy = true;
        _vm.Progress = 0;
        _vm.ProgressIndeterminate = true;
        _vm.CurrentFileText = "";
        _vm.StatusText = "Идёт поиск…";
        var progress = new Progress<string>(text => _vm.StatusText = text);

        try
        {
            ScanBundle bundle;
            if (_vm.Mode == TransferMode.LiveToFolder)
            {
                bundle = await Task.Run(() => _scanner.ScanProfile(ScanTarget.ForLiveProfile(), _vm.Mode, progress, ct), ct);
            }
            else
            {
                var selected = _vm.SelectedProfile;
                var typed = _vm.PathText;
                var resolution = await Task.Run(() => ProfileDetector.Resolve(typed, selected), ct);
                if (resolution.Options.Count > 0)
                {
                    _suppressProfile = true;
                    _vm.Profiles.Clear();
                    foreach (var option in resolution.Options)
                        _vm.Profiles.Add(option);
                    _vm.SelectedProfile = resolution.Options.Count == 1 ? resolution.Options[0] : _vm.SelectedProfile;
                    _suppressProfile = false;
                }

                if (!string.IsNullOrEmpty(resolution.Error) || string.IsNullOrEmpty(resolution.Path))
                {
                    _vm.StatusText = resolution.Error ?? "Источник не найден.";
                    return;
                }

                _vm.PathText = resolution.Path;
                if (resolution.IsBackup)
                    bundle = await Task.Run(() => _scanner.ScanBackup(resolution.Path, _vm.Mode, progress, ct), ct);
                else
                    bundle = await Task.Run(() => _scanner.ScanProfile(ScanTarget.ForProfile(resolution.Path), _vm.Mode, progress, ct), ct);
            }

            ApplyBundle(bundle);
        }
        catch (OperationCanceledException)
        {
            _vm.StatusText = "Поиск отменён.";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "Не удалось просмотреть данные.";
            _vm.AppendLog("Ошибка поиска: " + ex.Message);
            MessageBox.Show("Не удалось просмотреть данные.\n" + ex.Message, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
            _vm.ProgressIndeterminate = false;
        }
    }

    private void ApplyBundle(ScanBundle bundle)
    {
        _scan = bundle.Context;
        _vm.ReplaceItems(bundle.Items);
        var found = bundle.Items.Count(i => i.IsEnabled && i.Exists);
        var place = _vm.Mode == TransferMode.LiveToFolder
            ? "этом компьютере"
            : bundle.Context.ScannedPath;
        var status = found == 0
            ? $"На {place} ничего подходящего не найдено."
            : $"Найдено на {place}: {found}. Отметьте, что копировать.";
        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            var disk = string.IsNullOrWhiteSpace(_vm.PathText) ? "не выбран" : _vm.PathText.Trim().TrimEnd('\\');
            _vm.SourceCaption = $"Список с этого компьютера ({bundle.Context.SourceProfileRoot}). Диск {disk} здесь ни при чём: туда только сохраняется копия.";
            var reserved = ReservedWarning(_vm.Drives.FirstOrDefault(d =>
                d.Root.Equals(Path.GetPathRoot(_vm.PathText) ?? "", StringComparison.OrdinalIgnoreCase)));
            if (reserved != null)
                _vm.SourceCaption += " " + reserved;
        }
        else
        {
            _vm.SourceCaption = "Список с " + bundle.Context.ScannedPath + ".";
        }
        if (bundle.Warnings.Count > 0)
            status += " " + string.Join(" ", bundle.Warnings);
        _vm.StatusText = status;
        _vm.AppendLog(status);
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            MessageBox.Show("Отметьте хотя бы один пункт.", Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var window = new PreviewWindow(selected) { Owner = this };
        window.ShowDialog();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy)
            return;
        if (_scan == null)
        {
            MessageBox.Show("Сначала нажмите «Найти данные».", Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_vm.Mode != _scan.Mode)
        {
            MessageBox.Show("Режим изменился. Нажмите «Найти данные» ещё раз.", Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            MessageBox.Show("Отметьте хотя бы один пункт.", Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_vm.Mode != TransferMode.LiveToFolder &&
            !_scan.FromManifest &&
            PathSafety.Same(_scan.SourceProfileRoot, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
        {
            MessageBox.Show(
                "Источник и текущий профиль — это одна и та же папка. Выберите старый диск или другого пользователя.",
                Brand.WindowTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var backupRoot = "";
        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            var chosen = _vm.PathText.Trim();
            if (string.IsNullOrEmpty(chosen))
            {
                MessageBox.Show("Укажите папку, куда сохранить копию.", Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var reservedDrive = _vm.Drives.FirstOrDefault(d =>
                d.Root.Equals(Path.GetPathRoot(chosen) ?? "", StringComparison.OrdinalIgnoreCase));
            var reserved = ReservedWarning(reservedDrive);
            if (reserved != null)
            {
                MessageBox.Show(reserved, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!Directory.Exists(chosen))
            {
                var create = MessageBox.Show($"Папки нет:\n{chosen}\n\nСоздать её?", Brand.WindowTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (create != MessageBoxResult.Yes)
                    return;
                try
                {
                    Directory.CreateDirectory(chosen);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось создать папку.\n" + ex.Message, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            foreach (var item in selected)
            {
                if (item.Kind == ItemKind.Registry || string.IsNullOrWhiteSpace(item.ReadPath))
                    continue;
                if (PathSafety.Same(chosen, item.ReadPath) || PathSafety.IsUnder(chosen, item.ReadPath))
                {
                    MessageBox.Show(
                        "Папка назначения находится внутри копируемых данных. Выберите другой диск или папку вне этих данных.",
                        Brand.WindowTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            backupRoot = Path.Combine(chosen, "Перенос_" + stamp);
            if (Directory.Exists(backupRoot))
                backupRoot = Path.Combine(chosen, "Перенос_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        }
        else if (!PathSafety.Same(_vm.PathText.Trim(), _scan.ScannedPath))
        {
            MessageBox.Show("Папка источника изменилась. Нажмите «Найти данные» ещё раз.", Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var bytes = selected.Sum(i => Math.Max(0, i.SizeBytes));
        if (MessageBox.Show(BuildConfirm(selected, bytes, backupRoot), "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var importRegistry = true;
        if (_vm.Mode != TransferMode.LiveToFolder && selected.Any(i => i.Kind == ItemKind.Registry))
        {
            var regAnswer = MessageBox.Show(
                "В выбранном есть файл реестра, созданный этой программой.\n" +
                "Будут импортированы только разделы Outlook и CryptoPro, которые она сама выгрузила. Чужой файл реестра не принимается.\n\n" +
                "Импортировать их текущему пользователю?\n\n" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Реестр",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            importRegistry = regAnswer == MessageBoxResult.Yes;
        }

        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            try
            {
                Directory.CreateDirectory(backupRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось создать папку копии.\n" + ex.Message, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        LogExpander.IsExpanded = true;
        _vm.IsBusy = true;
        _vm.Progress = 0;
        _vm.ProgressIndeterminate = selected.Any(i => i.SizeIsPartial);
        _vm.CurrentFileText = "Подготовка…";
        _vm.AppendLog("");
        _vm.AppendLog("——— Старт ———");
        var progress = new Progress<CopyProgressReport>(report =>
        {
            if (!_vm.ProgressIndeterminate)
                _vm.Progress = report.Percent;
            if (!string.IsNullOrEmpty(report.CurrentFile))
                _vm.CurrentFileText = report.CurrentFile;
            if (!string.IsNullOrEmpty(report.LogLine))
                _vm.AppendLog(report.LogLine);
        });

        try
        {
            var plan = new CopyPlan
            {
                Mode = _vm.Mode,
                BackupRoot = backupRoot,
                SourceProfileRoot = _scan.SourceProfileRoot,
                DestinationProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                SourceMachine = string.IsNullOrWhiteSpace(_scan.SourceMachine) ? Environment.MachineName : _scan.SourceMachine,
                SourceUser = string.IsNullOrWhiteSpace(_scan.SourceUser) ? Environment.UserName : _scan.SourceUser,
                Overwrite = _vm.OverwriteExisting,
                KeepBak = _vm.KeepBak,
                ImportRegistry = importRegistry,
                Items = selected
            };
            var result = await Task.Run(() => _engine.Copy(plan, progress, ct), ct);
            _vm.ProgressIndeterminate = false;
            if (!result.Cancelled && !result.DiskFull)
                _vm.Progress = 100;
            _vm.CurrentFileText = result.Cancelled ? "Отменено" : "Готово";
            var summary = $"Итог: скопировано {result.FilesCopied}, занятых пропущено {result.Locked}, конфликтов {result.Conflicts}, ошибок {result.Errors}.";
            _vm.StatusText = summary;
            var box = result.Cancelled ? "Остановлено.\n" + summary : summary;
            if (_vm.Mode == TransferMode.LiveToFolder)
                box += "\n\nПапка копии:\n" + backupRoot;
            MessageBox.Show(box, Brand.WindowTitle, MessageBoxButton.OK, result.Errors > 0 || result.Cancelled ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            _vm.StatusText = "Отменено.";
            _vm.AppendLog("Отменено.");
        }
        catch (Exception ex)
        {
            _vm.AppendLog("Ошибка: " + ex.Message);
            _vm.StatusText = "Копирование прервано из-за ошибки.";
            MessageBox.Show("Не удалось завершить копирование.\n" + ex.Message, Brand.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
            _vm.ProgressIndeterminate = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();

    private List<ScanItem> SelectedItems() =>
        _vm.AllItems().Where(i => i.IsSelected && i.IsEnabled).ToList();

    private string BuildConfirm(IReadOnlyList<ScanItem> selected, long bytes, string backupRoot)
    {
        var sb = new StringBuilder();
        if (_vm.Mode == TransferMode.LiveToFolder)
        {
            sb.AppendLine("Будет создана папка:");
            sb.AppendLine(backupRoot);
            sb.AppendLine();
            sb.AppendLine("Исходные файлы не удаляются и не изменяются.");
            AppendFreeSpace(sb, backupRoot);
        }
        else
        {
            sb.AppendLine("Данные будут записаны в профиль:");
            sb.AppendLine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            sb.AppendLine();
            sb.AppendLine("Источник не изменяется и не удаляется.");
            AppendFreeSpace(sb, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        sb.AppendLine();
        sb.AppendLine($"Выбрано пунктов: {selected.Count}. Ориентировочно {ByteSize.Format(bytes)}.");
        if (selected.Any(i => i.SizeIsPartial))
            sb.AppendLine("Размер части папок посчитан не до конца — фактический объём может быть больше.");
        if (selected.Any(i => i.Category == ItemCategory.OneCDatabase))
            sb.AppendLine("Выбраны файловые базы 1С. Они могут быть очень большими.");
        if (selected.Any(i => i.Id.StartsWith("cryptopro", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine("Выбран CryptoPro. В копии могут оказаться закрытые ключи.");
        sb.AppendLine();
        sb.AppendLine(_vm.PolicyText);
        sb.AppendLine();
        sb.Append("Продолжить?");
        return sb.ToString();
    }

    private static void AppendFreeSpace(StringBuilder sb, string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return;
            var drive = new DriveInfo(root);
            if (drive.IsReady)
                sb.AppendLine("Свободно на диске: " + ByteSize.Format(drive.AvailableFreeSpace));
        }
        catch
        {
            // the confirm dialog still makes sense without free space
        }
    }
}
