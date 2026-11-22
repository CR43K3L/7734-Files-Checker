using System.Windows;
using System.Windows.Threading;

namespace Files7734.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        Theme.Apply(AppSettings.Load().Theme);
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Une erreur inattendue s'est produite :\n\n" + e.Exception.Message, "7734 Files Checker",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
