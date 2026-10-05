using System.Windows;
using DataMigrator.Models;
using DataMigrator.Services;

namespace DataMigrator;

public partial class PreviewWindow : Window
{
    public PreviewWindow(IReadOnlyList<ScanItem> items)
    {
        InitializeComponent();
        var bytes = items.Sum(i => Math.Max(0, i.SizeBytes));
        var files = items.Sum(i => Math.Max(0, i.FileCount));
        var partial = items.Any(i => i.SizeIsPartial);
        var text = $"Будет перенесено пунктов: {items.Count}. Ориентировочно {ByteSize.Format(bytes)}, {ByteSize.Files(files, partial)}.";
        if (items.Any(i => i.Category == ItemCategory.OneCDatabase))
            text += " Выбраны файловые базы 1С — они могут быть очень большими.";
        HeaderText.Text = text;
        List.ItemsSource = items;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
