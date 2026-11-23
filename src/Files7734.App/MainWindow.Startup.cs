using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Files7734.App;

/// <summary>Pages « Démarrage » (programmes lancés avec Windows) et « Historique » (courbe de l'espace disque), plus le résumé hebdomadaire.</summary>
public partial class MainWindow
{
    // ───────────── Programmes au démarrage ─────────────

    private bool _startupLoaded;
    private List<StartupRow> _startup = new();

    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private void StartupRefresh_Click(object sender, RoutedEventArgs e) => LoadStartup();

    private void LoadStartup()
    {
        _startupLoaded = true;
        var rows = new List<StartupRow>();
        void FromRun(RegistryKey hive, string path, string approved, string where)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    if (name.Length == 0 || key.GetValue(name) is not string cmd || cmd.Trim().Length == 0) continue;
                    rows.Add(MakeStartupRow(hive, approved, name, name, cmd, where));
                }
            }
            catch (Exception) { }
        }
        void FromFolder(RegistryKey hive, string folder, string where)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var f in Directory.EnumerateFiles(folder))
                {
                    string file = System.IO.Path.GetFileName(f);
                    if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(MakeStartupRow(hive, "StartupFolder", file, System.IO.Path.GetFileNameWithoutExtension(f), ShortcutTarget(f) ?? f, where));
                }
            }
            catch (Exception) { }
        }

        FromRun(Registry.CurrentUser, RunPath, "Run", "Ton compte");
        FromRun(Registry.LocalMachine, RunPath, "Run", "Tous les comptes");
        FromRun(Registry.LocalMachine, Run32Path, "Run32", "Tous les comptes");
        FromFolder(Registry.CurrentUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Dossier Démarrage");
        FromFolder(Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Dossier Démarrage (tous)");

        _startup = rows.OrderBy(r => r.Verdict == StartupVerdict.CanDisable ? 0 : r.Verdict == StartupVerdict.Unknown ? 1 : 2)
                       .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        StartupList.ItemsSource = _startup;
        UpdateStartupHint();
    }

    private StartupRow MakeStartupRow(RegistryKey hive, string approvedSub, string valueName, string display, string command, string where)
    {
        bool machine = hive == Registry.LocalMachine;
        string? exe = ExeOf(command);
        string? publisher = null;
        try { if (exe != null && File.Exists(exe)) publisher = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim(); } catch (Exception) { }

        bool enabled = true;
        try
        {
            using var k = hive.OpenSubKey(ApprovedPath + approvedSub);
            enabled = StartupAdvice.IsEnabled(k?.GetValue(valueName) as byte[]);
        }
        catch (Exception) { }

        var row = new StartupRow(display, command, where, publisher, machine, (_, on) => SetStartupEnabled(hive, approvedSub, valueName, on))
        {
            Writable = !machine || _elevated,
        };
        row.IsChecked = enabled;
        bool reverting = false;
        row.CheckChanged = () =>
        {
            if (reverting) return;
            if (!row.Apply(row.IsChecked))
            {
                reverting = true;
                row.IsChecked = !row.IsChecked;
                reverting = false;
                StatusText.Text = $"Windows a refusé de modifier « {row.Name} ».";
            }
            else StatusText.Text = row.IsChecked
                ? $"« {row.Name} » se lancera de nouveau au prochain démarrage."
                : $"« {row.Name} » ne se lancera plus au démarrage. Tu peux toujours l'ouvrir toi-même.";
            UpdateStartupHint();
        };
        return row;
    }

    private static bool SetStartupEnabled(RegistryKey hive, string approvedSub, string valueName, bool enabled)
    {
        try
        {
            using var k = hive.CreateSubKey(ApprovedPath + approvedSub, writable: true);
            k.SetValue(valueName, StartupAdvice.ApprovedValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Chemin de l'exécutable dans une ligne de commande (« "C:\x\a.exe" --min » ou « C:\x\a.exe -silent »).</summary>
    private static string? ExeOf(string command)
    {
        string c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (i > 0) return c[..(i + 4)];
        int sp = c.IndexOf(' ');
        return sp > 0 ? c[..sp] : c;
    }

    /// <summary>Cible d'un raccourci .lnk (via le composant Shell de Windows).</summary>
    private static string? ShortcutTarget(string lnk)
    {
        if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            var sc = shell.CreateShortcut(lnk);
            string target = sc.TargetPath;
            string args = sc.Arguments;
            if (string.IsNullOrWhiteSpace(target)) return null;
            return string.IsNullOrWhiteSpace(args) ? $"\"{target}\"" : $"\"{target}\" {args}";
        }
        catch (Exception) { return null; }
    }

    private void UpdateStartupHint()
    {
        int on = _startup.Count(r => r.IsChecked);
        int optional = _startup.Count(r => r.IsChecked && r.CanCheck && r.Verdict == StartupVerdict.CanDisable);
        string admin = !_elevated && _startup.Any(r => !r.Writable) ? " Les lignes grisées concernent tous les comptes : relance en administrateur pour les modifier." : "";
        StartupHint.Text = _startup.Count == 0
            ? "Aucun programme ne se lance avec Windows."
            : $"{on} programme(s) sur {_startup.Count} se lancent avec Windows. Décoche une ligne pour l'empêcher de démarrer (rien n'est désinstallé, l'effet est visible au prochain démarrage).{admin}";
        StartupSafe.IsEnabled = optional > 0;
        StartupSafe.Content = optional > 0 ? $"Désactiver les inutiles ({optional})" : "Désactiver les inutiles";
    }

    private void StartupSafe_Click(object sender, RoutedEventArgs e)
    {
        var targets = _startup.Where(r => r.IsChecked && r.CanCheck && r.Verdict == StartupVerdict.CanDisable).ToList();
        if (targets.Count == 0) return;
        string list = string.Join("\n", targets.Select(t => "•  " + t.Name));
        if (!Ask($"Ces programmes ne se lanceront plus avec Windows :\n\n{list}\n\nIls restent installés et tu peux les ouvrir quand tu veux. Tu peux aussi les réactiver ici à tout moment. Continuer ?")) return;
        foreach (var t in targets) t.IsChecked = false;
        int done = targets.Count(t => !t.IsChecked);
        StatusText.Text = $"{done} programme(s) ne se lanceront plus au démarrage. Windows démarrera plus vite à partir du prochain redémarrage.";
    }

    // ───────────── Historique de l'espace disque ─────────────

    private static string HistoryFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7734FilesChecker", "history.json");
    private SpaceHistory? _history;
    private string? _historyDrive;
    private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromHours(1) };

    private SpaceHistory History => _history ??= SpaceHistory.Load(HistoryFile);

    private void InitHistory()
    {
        _historyTimer.Tick += (_, _) => { RecordSpace(); MaybeWeeklyReport(); };
        _historyTimer.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RecordSpace));
        // On laisse l'app démarrer tranquillement avant un éventuel résumé de la semaine.
        var once = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        once.Tick += (_, _) => { once.Stop(); MaybeWeeklyReport(); };
        once.Start();
    }

    private static IEnumerable<DriveInfo> FixedDrives() => DriveInfo.GetDrives().Where(d =>
    {
        try { return d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0; } catch (Exception) { return false; }
    });

    private void RecordSpace()
    {
        try
        {
            foreach (var d in FixedDrives())
                History.Record(d.Name.TrimEnd('\\'), DateTime.UtcNow, d.TotalSize, d.TotalFreeSpace);
            History.Save(HistoryFile);
        }
        catch (Exception) { }
        if (_page == "history") ShowHistory();
    }

    private void ShowHistory()
    {
        var drives = History.Drives.Keys.Where(k => History.Drives[k].Count > 0).OrderBy(k => k).ToList();
        if (_historyDrive == null || !drives.Contains(_historyDrive))
            _historyDrive = drives.Contains(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") ? (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") : drives.FirstOrDefault();
        HistoryDrives.Children.Clear();
        foreach (var d in drives)
        {
            var rb = new RadioButton { Content = d, GroupName = "historydrive", Style = (Style)FindResource("Segment"), IsChecked = d == _historyDrive };
            rb.Checked += (_, _) => { _historyDrive = d; DrawHistory(); };
            HistoryDrives.Children.Add(rb);
        }
        DrawHistory();
    }

    private void HistoryChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawHistory();

    private void DrawHistory()
    {
        HistoryChart.Children.Clear();
        HistoryStats.Children.Clear();
        if (_historyDrive == null || !History.Drives.TryGetValue(_historyDrive, out var pts) || pts.Count == 0)
        {
            HistoryHint.Text = "L'app note l'espace de tes disques chaque jour où elle est ouverte. La courbe apparaîtra ici.";
            return;
        }
        var last = pts[^1];
        HistoryHint.Text = pts.Count < 2
            ? $"Première mesure enregistrée le {last.Day.ToLocalTime():d MMMM}. La courbe se dessinera au fil des jours (l'app note l'espace chaque jour où elle est ouverte)."
            : $"Espace utilisé sur {_historyDrive} depuis le {pts[0].Day:d MMMM yyyy}, mesuré chaque jour où l'app est ouverte.";

        // Chiffres clés
        HistoryStats.Children.Add(StatTile("Utilisé aujourd'hui", ByteSize.Format(last.Used), $"sur {ByteSize.Format(last.Total)}"));
        HistoryStats.Children.Add(StatTile("Libre", ByteSize.Format(last.Free), $"{100.0 * last.Free / last.Total:0} % du disque"));
        var growth = History.DailyGrowth(_historyDrive);
        if (growth is double g)
        {
            double month = g * 30;
            string sign = month >= 0 ? "+" : "−";
            HistoryStats.Children.Add(StatTile("Par mois", sign + ByteSize.Format((long)Math.Abs(month)), month >= 0 ? "au rythme actuel" : "le disque se libère"));
            var full = History.DaysUntilFull(_historyDrive);
            HistoryStats.Children.Add(StatTile("Disque plein dans", full is double days ? FormatDays(days) : "jamais",
                full is double ? "si tu continues comme ça" : "il ne se remplit pas en ce moment", full is < 60));
        }
        else HistoryStats.Children.Add(StatTile("Prévision", "bientôt", "il faut au moins 3 jours de mesures"));

        double w = HistoryChart.ActualWidth, h = HistoryChart.ActualHeight;
        if (w < 50 || h < 50) return;
        const double left = 58, bottom = 22, top = 8;
        double pw = w - left - 6, ph = h - bottom - top;
        long max = pts.Max(p => p.Total);
        if (max <= 0) return;
        DateTime d0 = pts[0].Day, d1 = pts.Count > 1 ? pts[^1].Day : pts[0].Day.AddDays(1);
        double span = Math.Max(1, (d1 - d0).TotalDays);
        double X(DateTime d) => left + pw * (d - d0).TotalDays / span;
        double Y(long v) => top + ph * (1 - (double)v / max);

        Brush muted = (Brush)FindResource("MutedBrush"), hair = (Brush)FindResource("HairlineBrush"), accent = (Brush)FindResource("AccentBrush");
        for (int i = 0; i <= 4; i++)
        {
            long v = max * i / 4;
            double y = Y(v);
            HistoryChart.Children.Add(new Line { X1 = left, X2 = left + pw, Y1 = y, Y2 = y, Stroke = hair, StrokeThickness = 1 });
            var t = new TextBlock { Text = ByteSize.Format(v), FontSize = 10.5, Foreground = muted, Width = left - 8, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(t, 0); Canvas.SetTop(t, y - 8);
            HistoryChart.Children.Add(t);
        }
        // Capacité du disque (pointillés) — elle peut changer si on agrandit la partition.
        var cap = new Polyline { Stroke = muted, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
        foreach (var p in pts) cap.Points.Add(new Point(X(p.Day), Y(p.Total)));
        if (pts.Count == 1) cap.Points.Add(new Point(left + pw, Y(pts[0].Total)));
        HistoryChart.Children.Add(cap);

        // Aire de l'espace utilisé
        var line = new Polyline { Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        var area = new Polygon { Fill = accent, Opacity = 0.18 };
        area.Points.Add(new Point(X(pts[0].Day), Y(0)));
        foreach (var p in pts)
        {
            var pt = new Point(X(p.Day), Y(p.Used));
            line.Points.Add(pt); area.Points.Add(pt);
        }
        if (pts.Count == 1) { var pt = new Point(left + pw, Y(pts[0].Used)); line.Points.Add(pt); area.Points.Add(pt); }
        area.Points.Add(new Point(line.Points[^1].X, Y(0)));
        HistoryChart.Children.Add(area);
        HistoryChart.Children.Add(line);
        var dot = new Ellipse { Width = 7, Height = 7, Fill = accent };
        Canvas.SetLeft(dot, line.Points[^1].X - 3.5); Canvas.SetTop(dot, line.Points[^1].Y - 3.5);
        HistoryChart.Children.Add(dot);

        // Dates en bas
        foreach (var (d, align) in new[] { (d0, 0.0), (d0.AddDays(span / 2), 0.5), (d1, 1.0) })
        {
            var t = new TextBlock { Text = d.ToString("d MMM"), FontSize = 10.5, Foreground = muted };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, X(d) - t.DesiredSize.Width * align); Canvas.SetTop(t, top + ph + 4);
            HistoryChart.Children.Add(t);
        }
    }

    private static string FormatDays(double days) => days switch
    {
        < 1 => "moins d'un jour",
        < 60 => $"~{Math.Round(days):0} jours",
        < 730 => $"~{Math.Round(days / 30):0} mois",
        _ => "plus de 2 ans",
    };

    private Border StatTile(string label, string value, string sub, bool warn = false)
    {
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1), MinWidth = 150 };
        b.SetResourceReference(Border.BackgroundProperty, "ContentBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var s = new StackPanel();
        var l = new TextBlock { Text = label, FontSize = 11.5 };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        s.Children.Add(l);
        var v = new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) };
        if (warn) v.Foreground = ExtensionColors.BrushOf(0xFFFF9F0Au);
        s.Children.Add(v);
        var u = new TextBlock { Text = sub, FontSize = 11.5 };
        u.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        s.Children.Add(u);
        b.Child = s;
        return b;
    }

    // ───────────── Résumé de la semaine ─────────────

    private bool _weeklyRunning;

    /// <summary>Une fois par semaine, en arrière-plan : analyse du disque système et bulle « ce qui a grossi cette semaine ».</summary>
    private void MaybeWeeklyReport()
    {
        if (!_settings.WeeklyReport || !_settings.MonitorDisks || _tray == null || _weeklyRunning) return;
        if (_progressTimer.IsEnabled) return; // une analyse est déjà en cours
        if (DateTime.UtcNow - History.LastAutoScanUtc < TimeSpan.FromDays(7)) return;
        _ = RunWeeklyReport();
    }

    private async Task RunWeeklyReport()
    {
        _weeklyRunning = true;
        try
        {
            string root = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
            string file = System.IO.Path.Combine(SnapshotDir, Snapshot.FileNameFor(root));
            var (previous, current) = await Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                var prev = Snapshot.Load(file);
                var scan = DiskScanner.Scan(root);
                var cur = Snapshot.Create(scan.Root);
                try { cur.Save(file); } catch (Exception) { }
                return (prev, cur);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            History.LastAutoScanUtc = DateTime.UtcNow;
            RecordSpace();
            if (_tray == null) return;
            string letter = root.TrimEnd('\\');
            if (previous == null)
            {
                _tray.ShowBalloonTip(6000, $"Première analyse de {letter} enregistrée",
                    "La semaine prochaine, je te dirai ce qui a pris de la place.", Forms.ToolTipIcon.Info);
                return;
            }
            long delta = current.TotalSize - previous.TotalSize;
            var top = SnapshotDiff.Compare(previous, current).Where(c => c.Delta > 0).Take(2).ToList();
            string what = top.Count == 0 ? "Rien de notable n'a grossi."
                : "Surtout " + string.Join(" et ", top.Select(c => $"{System.IO.Path.GetFileName(c.RelativePath.TrimEnd('\\'))} (+{ByteSize.Format(c.Delta)})")) + ".";
            string days = $"{Math.Max(1, Math.Round((current.TakenUtc - previous.TakenUtc).TotalDays)):0} jours";
            _alertDrive = root;
            _tray.ShowBalloonTip(10000,
                delta >= 0 ? $"Ces {days} sur {letter} : +{ByteSize.Format(delta)}" : $"Ces {days} sur {letter} : −{ByteSize.Format(-delta)}",
                what + " Clique pour voir le détail.", Forms.ToolTipIcon.Info);
        }
        catch (Exception) { }
        finally { _weeklyRunning = false; }
    }

    private void ToggleWeekly()
    {
        _settings.WeeklyReport = !_settings.WeeklyReport;
        _settings.Save();
        ApplyViewOptions();
        StatusText.Text = _settings.WeeklyReport
            ? "Chaque semaine, l'app analysera ton disque système en arrière-plan et te dira ce qui a grossi (la surveillance doit être activée)."
            : "Résumé de la semaine désactivé.";
    }
}
