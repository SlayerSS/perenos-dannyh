using System.Windows;

namespace DataMigrator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        StartupOptions.Read(e.Args);
        base.OnStartup(e);
    }
}
