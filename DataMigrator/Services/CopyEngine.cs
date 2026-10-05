using System.IO;
using System.Text;
using DataMigrator.Models;

namespace DataMigrator.Services;

internal sealed class CopyEngine
{
    private sealed class DiskFullException : Exception
    {
    }

    public CopyResult Copy(CopyPlan plan, IProgress<CopyProgressReport> progress, CancellationToken ct)
    {
        var result = new CopyResult();
        var logs = new List<string>();
        var manifestItems = new List<ManifestItem>();
        var total = plan.Items.Where(i => i.IsSelected && i.IsEnabled).Sum(i => Math.Max(0L, i.SizeBytes));
        long done = 0;
        var lastReport = DateTime.UtcNow;
        var current = "";
        var conflictsLogged = 0;
        var lockedLogged = 0;
        var errorsLogged = 0;

        void Report(string? line, bool force)
        {
            if (line != null)
                logs.Add(line);
            var now = DateTime.UtcNow;
            if (!force && line == null && (now - lastReport).TotalMilliseconds < 80)
                return;
            lastReport = now;
            progress.Report(new CopyProgressReport
            {
                CurrentFile = Shorten(current),
                Percent = Percent(done, total),
                LogLine = line
            });
        }

        try
        {
            foreach (var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (!item.IsSelected || !item.IsEnabled)
                    continue;

                current = item.DisplayName;
                Report("Копирую: " + item.DisplayName, force: true);
                var copiedBefore = result.FilesCopied;
                var errorsBefore = result.Errors;
                CopyItem(plan, item, result, ref done, ref current, Report, manifestItems, ref conflictsLogged, ref lockedLogged, ref errorsLogged, ct);
                if (result.DiskFull)
                    break;

                var copiedNow = result.FilesCopied - copiedBefore;
                if (result.Errors > errorsBefore)
                    Report("Завершено с ошибками: " + item.DisplayName, true);
                else if (item.Kind == ItemKind.Registry)
                    Report("Готово: " + item.DisplayName, true);
                else
                    Report("Готово: " + item.DisplayName + " — " + ByteSize.Files(copiedNow, partial: false), true);
            }
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            Report("Остановлено пользователем.", true);
        }
        catch (DiskFullException)
        {
            result.DiskFull = true;
            Report("На диске закончилось место. Копирование остановлено.", true);
        }

        var summary =
            $"Итог: скопировано {result.FilesCopied}, занятых пропущено {result.Locked}, конфликтов {result.Conflicts}, ошибок {result.Errors}.";
        Report(summary, true);

        if (plan.Mode == TransferMode.LiveToFolder && !string.IsNullOrWhiteSpace(plan.BackupRoot))
            WriteReceipt(plan, manifestItems, result, logs);

        return result;
    }

    private static void CopyItem(
        CopyPlan plan,
        ScanItem item,
        CopyResult result,
        ref long done,
        ref string current,
        Action<string?, bool> report,
        List<ManifestItem> manifestItems,
        ref int conflictsLogged,
        ref int lockedLogged,
        ref int errorsLogged,
        CancellationToken ct)
    {
        if (item.Kind == ItemKind.Registry)
        {
            CopyRegistry(plan, item, result, report, manifestItems, ref errorsLogged);
            return;
        }

        var source = item.ReadPath;
        var sourceOk = item.Kind == ItemKind.File ? File.Exists(source) : Directory.Exists(source);
        if (string.IsNullOrWhiteSpace(source) || !sourceOk)
        {
            result.Errors++;
            LogLimited(report, ref errorsLogged, "Не найдено: " + item.DisplayName);
            return;
        }

        string destination;
        if (plan.Mode == TransferMode.LiveToFolder)
        {
            destination = Path.Combine(plan.BackupRoot, item.StoredRelativePath);
        }
        else
        {
            var mapped = PathMapper.MapToLive(item, plan.DestinationProfileRoot);
            destination = mapped.Path;
            if (mapped.RedirectedToDocuments)
                report("Исходный путь недоступен, копирую в Документы: " + item.DisplayName, true);
        }

        if (PathSafety.IsBlocked(destination))
        {
            result.Errors++;
            LogLimited(report, ref errorsLogged, "Назначение в системной папке, пропуск: " + destination);
            return;
        }

        if (PathSafety.Overlaps(source, destination))
        {
            result.Errors++;
            LogLimited(report, ref errorsLogged, "Источник и назначение совпадают, пропуск: " + item.DisplayName);
            return;
        }

        var copiedBefore = result.FilesCopied;
        if (item.Kind == ItemKind.File)
        {
            current = source;
            CopyOne(source, destination, item.SizeBytes, plan, result, ref done, report, ref conflictsLogged, ref lockedLogged, ref errorsLogged, ct);
        }
        else
        {
            CopyFolder(source, destination, item.Exclusions, plan, result, ref done, ref current, report, ref conflictsLogged, ref lockedLogged, ref errorsLogged, ct);
        }

        if (plan.Mode == TransferMode.LiveToFolder && result.FilesCopied > copiedBefore)
            manifestItems.Add(ToManifest(item));
    }

    private static void CopyRegistry(
        CopyPlan plan,
        ScanItem item,
        CopyResult result,
        Action<string?, bool> report,
        List<ManifestItem> manifestItems,
        ref int errorsLogged)
    {
        if (plan.Mode == TransferMode.LiveToFolder)
        {
            var dest = Path.Combine(plan.BackupRoot, item.StoredRelativePath);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            if (!RegistryTransfer.Export(item.RegistryKeys, dest, out var error))
            {
                result.Errors++;
                LogLimited(report, ref errorsLogged, "Реестр не выгружен: " + error);
                return;
            }

            result.FilesCopied++;
            manifestItems.Add(ToManifest(item));
            return;
        }

        if (!plan.ImportRegistry)
        {
            report("Импорт реестра пропущен: " + item.DisplayName, true);
            return;
        }

        if (!RegistryTransfer.Validate(item.ReadPath, out var validateError))
        {
            result.Errors++;
            LogLimited(report, ref errorsLogged, "Реестр отклонён: " + validateError);
            return;
        }

        if (!RegistryTransfer.Import(item.ReadPath, out var importError))
        {
            result.Errors++;
            LogLimited(report, ref errorsLogged, "Реестр не импортирован: " + importError);
            return;
        }

        result.FilesCopied++;
    }

    private static void CopyFolder(
        string source,
        string destination,
        ExclusionRules rules,
        CopyPlan plan,
        CopyResult result,
        ref long done,
        ref string current,
        Action<string?, bool> report,
        ref int conflictsLogged,
        ref int lockedLogged,
        ref int errorsLogged,
        CancellationToken ct)
    {
        var any = false;
        foreach (var file in DirectoryWalker.EnumerateFiles(source, rules, ct))
        {
            any = true;
            var relative = Path.GetRelativePath(source, file.Path);
            if (relative.StartsWith("..", StringComparison.Ordinal))
                continue;
            current = file.Path;
            var target = Path.Combine(destination, relative);
            CopyOne(file.Path, target, file.Length, plan, result, ref done, report, ref conflictsLogged, ref lockedLogged, ref errorsLogged, ct);
            if (result.DiskFull)
                return;
        }

        if (!any)
            report("Пусто, файлов нет: " + source, true);
    }

    private static void CopyOne(
        string source,
        string destination,
        long length,
        CopyPlan plan,
        CopyResult result,
        ref long done,
        Action<string?, bool> report,
        ref int conflictsLogged,
        ref int lockedLogged,
        ref int errorsLogged,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var src = LongPath.Wrap(source);
            var dst = LongPath.Wrap(destination);
            if (File.Exists(dst))
            {
                if (!plan.Overwrite)
                {
                    result.Conflicts++;
                    done += length;
                    if (conflictsLogged < 25)
                    {
                        conflictsLogged++;
                        report("Уже есть, оставлен: " + destination, false);
                    }
                    return;
                }

                if (plan.KeepBak)
                {
                    var bak = destination + ".bak";
                    var bakWrapped = LongPath.Wrap(bak);
                    if (!File.Exists(bakWrapped))
                    {
                        try
                        {
                            File.Copy(dst, bakWrapped, overwrite: false);
                        }
                        catch (Exception ex)
                        {
                            LogLimited(report, ref errorsLogged, "Не удалось сохранить .bak: " + destination + " — " + ex.Message);
                        }
                    }
                }

                try
                {
                    var attr = File.GetAttributes(dst);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(dst, attr & ~FileAttributes.ReadOnly);
                }
                catch
                {
                    // copy will report the real error
                }
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(LongPath.Wrap(parent));

            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output, 128 * 1024);
                if (length <= 0)
                    length = input.Length;
            }

            try
            {
                File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
            }
            catch
            {
                // timestamp is not required
            }

            result.FilesCopied++;
            done += length;
            report(null, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            result.DiskFull = true;
            result.Errors++;
            throw new DiskFullException();
        }
        catch (IOException ex) when (IsLocked(ex))
        {
            result.Locked++;
            done += length;
            if (lockedLogged < 40)
            {
                lockedLogged++;
                report("Занят, пропущен: " + source, false);
            }
        }
        catch (Exception ex)
        {
            result.Errors++;
            done += length;
            LogLimited(report, ref errorsLogged, "Ошибка: " + source + " — " + ex.Message);
        }
    }

    private static void WriteReceipt(CopyPlan plan, List<ManifestItem> items, CopyResult result, List<string> logs)
    {
        try
        {
            Directory.CreateDirectory(plan.BackupRoot);
            var manifest = new BackupManifest
            {
                ToolVersion = ToolInfo.Version,
                CreatedUtc = DateTime.UtcNow,
                Completed = !result.Cancelled && !result.DiskFull,
                ErrorCount = result.Errors,
                LockedCount = result.Locked,
                ConflictCount = result.Conflicts,
                SourceMachine = string.IsNullOrWhiteSpace(plan.SourceMachine) ? Environment.MachineName : plan.SourceMachine,
                SourceUser = string.IsNullOrWhiteSpace(plan.SourceUser) ? Environment.UserName : plan.SourceUser,
                SourceProfileRoot = plan.SourceProfileRoot,
                Items = items
            };
            ManifestService.Save(Path.Combine(plan.BackupRoot, ToolInfo.ManifestFileName), manifest);
        }
        catch (Exception ex)
        {
            logs.Add("Не удалось записать backup-manifest.json: " + ex.Message);
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(Brand.Name + ", " + Brand.City + " · " + Brand.Phone);
            sb.AppendLine("Перенос данных, версия " + ToolInfo.Version);
            sb.AppendLine("Компьютер: " + plan.SourceMachine);
            sb.AppendLine("Пользователь: " + plan.SourceUser);
            sb.AppendLine("Профиль: " + plan.SourceProfileRoot);
            sb.AppendLine("Папка копии: " + plan.BackupRoot);
            sb.AppendLine();
            foreach (var line in logs)
                sb.AppendLine(line);
            File.WriteAllText(
                Path.Combine(plan.BackupRoot, ToolInfo.JournalFileName),
                sb.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
            // the on-screen log still has the text
        }
    }

    private static ManifestItem ToManifest(ScanItem item)
    {
        return new ManifestItem
        {
            Id = item.Id,
            Category = item.Category.ToString(),
            DisplayName = item.DisplayName,
            SourcePath = item.OriginalPath,
            StoredRelativePath = item.StoredRelativePath,
            Kind = ManifestService.KindName(item.Kind),
            Notes = item.Notes,
            Explanation = item.Explanation,
            KnownFolderId = item.KnownFolderId,
            PathInsideKnownFolder = item.PathInsideKnownFolder,
            ProfileRelativePath = item.ProfileRelativePath
        };
    }

    private static void LogLimited(Action<string?, bool> report, ref int logged, string line)
    {
        if (logged >= 40)
            return;
        logged++;
        report(line, true);
    }

    private static bool IsDiskFull(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 0x70 or 0x27;
    }

    private static bool IsLocked(IOException ex)
    {
        var code = ex.HResult & 0xFFFF;
        return code is 0x20 or 0x21;
    }

    private static double Percent(long done, long total)
    {
        if (total <= 0)
            return 0;
        var value = done * 100.0 / total;
        if (value < 0)
            return 0;
        if (value > 99 && done < total)
            return 99;
        return Math.Min(100, value);
    }

    private static string Shorten(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= 96)
            return path;
        return "…" + path[^95..];
    }
}
