using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Files7734.App;

/// <summary>Icône de la zone de notification et alerte « disque presque plein ».</summary>
public partial class MainWindow
{
    private const string TaskName = "7734 Files Checker";
    private Forms.NotifyIcon? _tray;
    private readonly DispatcherTimer _diskTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly Dictionary<string, DateTime> _lastAlert = new(StringComparer.OrdinalIgnoreCase);
    private bool _quitting;
    private string? _alertDrive;

    /// <summary>Lancé au démarrage de Windows avec --tray : la fenêtre reste cachée.</summary>
    public static bool StartHidden => Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

    private void InitTray()
    {
        _diskTimer.Tick += (_, _) => CheckDisks();
        Closing += OnClosingToTray;
        Closed += (_, _) => { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } };
        ApplyMonitor();
        if (StartHidden && _settings.MonitorDisks)
        {
            ShowInTaskbar = false;
            Loaded += (_, _) => { Hide(); ShowInTaskbar = true; };
        }
    }

    private void ApplyMonitor()
    {
        if (_settings.MonitorDisks)
        {
            EnsureTray();
            _diskTimer.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(CheckDisks));
        }
        else
        {
            _diskTimer.Stop();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        }
    }

    private void EnsureTray()
    {
        if (_tray != null) return;
        _tray = new Forms.NotifyIcon
        {
            Text = "7734 Files Checker",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir 7734 Files Checker", null, (_, _) => ShowFromTray());
        menu.Items.Add("Vérifier l'espace maintenant", null, (_, _) => CheckDisks(force: true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => { _quitting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.BalloonTipClicked += (_, _) =>
        {
            ShowFromTray();
            if (_alertDrive != null && !_progressTimer.IsEnabled) { ShowPage("analysis"); StartScan(_alertDrive); }
        };
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosingToTray(object? sender, CancelEventArgs e)
    {
        // Quand la surveillance est active, fermer la fenêtre la range dans la zone de notification.
        if (_settings.MonitorDisks && !_quitting && _tray != null)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.TrayHintShown)
            {
                _tray.ShowBalloonTip(4000, "7734 Files Checker continue en arrière-plan",
                    "Tu seras prévenu si un disque est presque plein. Clic droit sur l'icône pour quitter.", Forms.ToolTipIcon.Info);
                _settings.TrayHintShown = true;
                _settings.Save();
            }
        }
    }

    /// <summary>Alerte si un disque a moins de 10 % ou moins de 10 Go libres (au plus une fois toutes les 12 h par disque).</summary>
    private void CheckDisks() => CheckDisks(force: false);

    private void CheckDisks(bool force)
    {
        if (_tray == null) return;
        var full = new List<(string Root, long Free, long Total)>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed || d.TotalSize < 20L * 1024 * 1024 * 1024) continue;
                long free = d.TotalFreeSpace;
                if (free < 10L * 1024 * 1024 * 1024 || free < d.TotalSize / 10) full.Add((d.RootDirectory.FullName, free, d.TotalSize));
            }
            catch (Exception) { }
        }
        var toAlert = full.Where(f => force || !_lastAlert.TryGetValue(f.Root, out var t) || DateTime.UtcNow - t > TimeSpan.FromHours(12)).ToList();
        if (toAlert.Count == 0)
        {
            if (force) _tray.ShowBalloonTip(3000, "Tout va bien", "Aucun disque n'est presque plein.", Forms.ToolTipIcon.Info);
            return;
        }
        foreach (var f in toAlert) _lastAlert[f.Root] = DateTime.UtcNow;
        var worst = toAlert.OrderBy(f => (double)f.Free / f.Total).First();
        _alertDrive = worst.Root;
        string letter = worst.Root.TrimEnd('\\');
        _tray.ShowBalloonTip(8000, $"Le disque {letter} est presque plein",
            $"Il reste {ByteSize.Format(worst.Free)} sur {ByteSize.Format(worst.Total)}. Clique ici pour voir ce qui prend de la place.",
            Forms.ToolTipIcon.Warning);
    }

    // ───────────── Options ─────────────

    private void ToggleMonitor()
    {
        _settings.MonitorDisks = !_settings.MonitorDisks;
        _settings.Save();
        ApplyMonitor();
        ApplyViewOptions();
        StatusText.Text = _settings.MonitorDisks
            ? "Surveillance activée : tu seras prévenu quand un disque a moins de 10 % d'espace libre. Fermer la fenêtre la range près de l'horloge."
            : "Surveillance de l'espace disque désactivée.";
    }

    private void ToggleStartup()
    {
        bool enable = !_settings.StartWithWindows;
        // Tâche planifiée à l'ouverture de session (avec les droits élevés, pour le scan rapide).
        string args = enable
            ? $"/Create /TN \"{TaskName}\" /TR \"\\\"{Environment.ProcessPath}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F"
            : $"/Delete /TN \"{TaskName}\" /F";
        bool ok = RunSchtasks(args);
        if (!ok && enable)
        {
            MessageBox.Show(this, "Impossible d'ajouter le démarrage automatique. Relance l'application en administrateur puis réessaie.",
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.StartWithWindows = enable;
        if (enable && !_settings.MonitorDisks) { _settings.MonitorDisks = true; ApplyMonitor(); }
        _settings.Save();
        ApplyViewOptions();
        StatusText.Text = enable
            ? "7734 Files Checker démarrera avec Windows, discrètement dans la zone de notification."
            : "Démarrage avec Windows désactivé.";
    }

    private static bool RunSchtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            if (p == null) return false;
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch (Exception) { return false; }
    }
}
