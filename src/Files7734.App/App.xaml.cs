using System.Windows;
using System.Windows.Threading;

namespace Files7734.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        bool restart = e.Args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase));
        if (!SingleInstance.Acquire(waitForPrevious: restart))
        {
            // Déjà ouverte : on lui passe la main (et le dossier à analyser, s'il y en a un).
            if (!SingleInstance.SendToRunning(e.Args))
                MessageBox.Show("7734 Files Checker est déjà ouvert (regarde aussi près de l'horloge).", "7734 Files Checker",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
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
