using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Files7734.App;

/// <summary>Pages « Outils » : jeux, nettoyage, doublons, vieux fichiers, nouveautés.</summary>
public partial class MainWindow
{
    private readonly List<NavItem> _nav = new()
    {
        new("analysis", "", "Analyse"),
        new("space", "\uE945", "Faire de la place"),
        new("games", "", "Jeux"),
        new("clean", "", "Nettoyage"),
        new("dupes", "", "Doublons"),
        new("old", "", "Vieux fichiers"),
        new("news", "", "Nouveautés"),
        new("history", "\uE9D9", "Historique"),
        new("apps", "\uE71D", "Applications"),
        new("startup", "\uE7E8", "Démarrage"),
        new("caps", "\uE722", "Captures et clips"),
        new("system", "\uE770", "Fichiers Windows"),
        new("health", "\uE95E", "Santé des disques"),
    };

    private readonly List<NavItem> _fivemNav = new()
    {
        new("fivem", "\uE7FC", "Mes packs"),
        new("fivemgfx", "\uE7F4", "Réglages graphiques"),
        new("fivemfix", "\uE90F", "Réparer FiveM"),
    };

    private IEnumerable<NavItem> AllNav => _nav.Concat(_fivemNav);

    private string _page = "analysis";
    private string? _pendingReveal;
    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void InitPages()
    {
        NavList.ItemsSource = _nav;
        FiveMNav.ItemsSource = _fivemNav;
        NavList.SelectedIndex = 0;
        ApplyEmptyHints();
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is NavItem n) ShowPage(n.Key);
    }

    private void FiveMNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FiveMNav.SelectedItem is NavItem n) ShowPage(n.Key);
    }

    private void ShowPage(string key)
    {
        _page = key;
        var nav = AllNav.First(i => i.Key == key);
        object? main = _nav.Contains(nav) ? nav : null, five = _fivemNav.Contains(nav) ? nav : null;
        if (NavList.SelectedItem != main) NavList.SelectedItem = main;
        if (FiveMNav.SelectedItem != five) FiveMNav.SelectedItem = five;
        foreach (var (k, page) in Pages()) page.Visibility = k == key ? Visibility.Visible : Visibility.Collapsed;

        switch (key)
        {
            case "games" when _games == null: LoadGames(); break;
            case "clean" when _clean.Count == 0: MeasureCleanup(); break;
            case "old" when _oldDirty: RefreshOld(); break;
            case "news": Nav("news").Badge = ""; break;
            case "system" when !_systemLoaded: LoadSystemFiles(); break;
            case "health" when !_healthLoaded: LoadHealth(); break;
            case "apps" when !_appsLoaded: LoadApps(); break;
            case "caps" when !_capsLoaded: LoadCaptures(); break;
            case "space": PrepareSpace(); break;
            case "startup" when !_startupLoaded: LoadStartup(); break;
            case "history": ShowHistory(); break;
            case "fivem": LoadFiveM(); break;
            case "fivemfix": LoadFiveMFix(); break;
            case "fivemgfx": LoadGfx(); break;
        }
    }

    private NavItem Nav(string key) => AllNav.First(n => n.Key == key);

    private IEnumerable<(string, FrameworkElement)> Pages() => new (string, FrameworkElement)[]
    {
        ("analysis", AnalysisPage), ("games", GamesPage), ("clean", CleanPage), ("dupes", DupesPage), ("old", OldPage), ("news", NewsPage),
        ("system", SystemPage), ("health", HealthPage), ("apps", AppsPage), ("caps", CapsPage), ("space", SpacePage),
        ("startup", StartupPage), ("history", HistoryPage), ("fivem", FiveMPage), ("fivemfix", FiveMFixPage), ("fivemgfx", FiveMGfxPage),
    };

    private void ApplyEmptyHints()
    {
        if (_result != null) return;
        DupesHint.Text = "Analyse d'abord un disque (page Analyse), puis cherche les fichiers identiques.";
        OldHint.Text = "Analyse d'abord un disque (page Analyse) pour retrouver les gros fichiers oubliés.";
    }

    /// <summary>Un nouveau scan démarre : les listes basées sur l'ancien arbre sont vidées.</summary>
    private void OnScanStarting()
    {
        _dupesCts?.Cancel();
        DupesList.ItemsSource = null;
        _dupes.Clear();
        DupesHint.Text = "Analyse en cours…";
        UpdateDupesButtons();
        OldList.ItemsSource = null;
        _old.Clear();
        OldHint.Text = "Analyse en cours…";
        UpdateOldButtons();
        _oldDirty = true;
    }

    private void OnScanCompleted(ScanResult result, string path)
    {
        var root = result.Root;
        DupesHint.Text = $"Cherche les fichiers identiques dans {root.FullPath}. Le contenu est comparé octet par octet (SHA-256).";
        _oldDirty = true;
        if (_page == "old") RefreshOld();
        else OldHint.Text = "Gros fichiers que personne n'a modifiés depuis longtemps.";
        if (_games != null) _ = ComputeGameSizes();
        if (!root.IsVirtual && Directory.Exists(path)) UpdateSnapshot(root);
        RecordSpace();

        if (_pendingReveal != null)
        {
            var node = FindInResult(_pendingReveal);
            _pendingReveal = null;
            if (node != null) Dispatcher.BeginInvoke(DispatcherPriority.Background, () => RevealInTree(node, zoomMap: true));
        }
    }

    private void RefreshPageTexts()
    {
        foreach (var g in _games ?? new()) g.SetPercent(g.Percent);
        foreach (var c in _clean) c.RefreshAll();
        foreach (var d in _dupes) d.RefreshAll();
        foreach (var o in _old) o.RefreshAll();
        if (NewsList.ItemsSource is List<ChangeRow> news) NewsList.ItemsSource = news.Select(c => new ChangeRow(c.Change, c.FullPath[..^c.Path.Length])).ToList();
    }

    private FsNode? FindInResult(string fullPath)
    {
        if (_result == null || _result.Root.IsVirtual) return null;
        string rootPath = _result.Root.FullPath;
        string a = rootPath.TrimEnd('\\'), b = fullPath.TrimEnd('\\');
        if (!b.Equals(a, StringComparison.OrdinalIgnoreCase) && !b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase)) return null;
        return DiskScanner.FindByPath(_result.Root, rootPath, fullPath);
    }

    private void GoToNode(FsNode node)
    {
        if (_result == null || !FsTree.IsInside(node, _result.Root)) return;
        ShowPage("analysis");
        RevealInTree(node, zoomMap: true);
    }

    private void PageList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as ListView)?.SelectedItem;
        switch (item)
        {
            case DupeRow { File: { } f }: GoToNode(f); break;
            case OldFileRow o: GoToNode(o.Node); break;
            case ChangeRow c:
                var n = FindInResult(c.FullPath);
                if (n != null) GoToNode(n);
                else if (Directory.Exists(c.FullPath)) NativeMethods.Open(c.FullPath);
                break;
        }
    }

    /// <summary>Envoie des fichiers à la corbeille et retire de l'arbre ceux qui ont bien disparu.</summary>
    private List<FsNode> RecycleNodes(IReadOnlyList<FsNode> nodes)
    {
        NativeMethods.SendToRecycleBin(nodes.Select(n => n.FullPath).ToList(), Hwnd);
        var gone = nodes.Where(n => !File.Exists(n.FullPath) && !Directory.Exists(n.FullPath)).ToList();
        AfterRemoval(gone);
        return gone;
    }

    // ═════════════ Jeux ═════════════

    private List<GameRow>? _games;
    private int _gamesVersion;

    private void GamesRefresh_Click(object sender, RoutedEventArgs e) => LoadGames();

    private async void LoadGames()
    {
        _games ??= new();
        GamesHint.Text = "Recherche des jeux installés…";
        GamesRefresh.IsEnabled = false;
        try
        {
            var found = await Task.Run(GameDetector.Detect);
            _games = found.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => new GameRow(g)).ToList();
            MarkDuplicateGames(_games);
            GamesList.ItemsSource = _games;
            _ = LoadGhosts(found);
            if (_games.Count == 0)
            {
                GamesHint.Text = "Aucun jeu trouvé (Steam, Epic Games, Riot, Battle.net, Ubisoft, EA, GOG, Rockstar et Xbox sont reconnus).";
                return;
            }
            await ComputeGameSizes();
        }
        finally
        {
            GamesRefresh.IsEnabled = true;
        }
    }

    private async Task ComputeGameSizes()
    {
        var games = _games;
        if (games == null || games.Count == 0) return;
        int version = ++_gamesVersion;
        var pending = new List<GameRow>();
        foreach (var g in games)
        {
            var node = FindInResult(g.InstallPath);
            if (node != null) g.SetSize(node.Size);
            else pending.Add(g);
        }
        UpdateGamesSummary(games, pending.Count);

        foreach (var g in pending)
        {
            long size = await Task.Run(() =>
            {
                try { return DirectoryWalkScanner.Scan(g.InstallPath).Size; }
                catch (Exception) { return 0L; }
            });
            if (version != _gamesVersion) return;
            g.SetSize(size);
            UpdateGamesSummary(games, pending.Count - pending.IndexOf(g) - 1);
        }
    }

    private void UpdateGamesSummary(List<GameRow> games, int remaining)
    {
        long total = games.Sum(g => g.Size ?? 0);
        foreach (var g in games) g.SetPercent(total > 0 && g.Size is long s ? 100.0 * s / total : 0);
        string launchers = string.Join(", ", games.Select(g => g.Launcher).Distinct().OrderBy(l => l));
        GamesHint.Text = $"{games.Count} jeu{(games.Count > 1 ? "x" : "")} · {ByteSize.Format(total)} au total · {launchers}" +
                         (remaining > 0 ? $" · calcul des tailles ({remaining} restant{(remaining > 1 ? "s" : "")})…" : "");
        if (remaining == 0) GamesList.ItemsSource = games.OrderByDescending(g => g.Size ?? 0).ToList();
    }

    private static GameRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as GameRow;

    private void GameOpen_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } g) Safe(() => NativeMethods.Open(g.InstallPath));
    }

    private void GameReveal_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } g) return;
        var node = FindInResult(g.InstallPath);
        if (node != null) { GoToNode(node); return; }
        if (_progressTimer.IsEnabled) return;
        // Pas encore analysé : on analyse le disque du jeu puis on l'affiche.
        _pendingReveal = g.InstallPath;
        ShowPage("analysis");
        StartScan(Path.GetPathRoot(g.InstallPath) ?? g.InstallPath);
    }

    private void GameUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } g) return;
        string? cmd = g.Game.UninstallUri;
        string how = cmd switch
        {
            null => "Windows va ouvrir la liste des applications installées : cherches-y le jeu.",
            _ when cmd.StartsWith("steam:") => "Steam va te demander de confirmer.",
            _ when cmd.StartsWith("com.epicgames") => "Le lanceur Epic Games va s'ouvrir : fais clic droit sur le jeu puis Désinstaller.",
            _ when cmd.StartsWith("uplay:") => "Ubisoft Connect va te demander de confirmer.",
            _ when cmd.StartsWith("ms-settings:") => "Windows va ouvrir la liste des applications installées : cherches-y le jeu.",
            _ => "Le programme de désinstallation du jeu va s'ouvrir.",
        };
        var answer = MessageBox.Show(this, $"Désinstaller « {g.Name} » ({g.SizeText}) ?\n\n{how}", "Désinstaller un jeu",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        Safe(() => RunCommand(cmd ?? "ms-settings:appsfeatures"));
    }

    /// <summary>Lance une adresse (steam://…) ou une ligne de commande de désinstallation.</summary>
    private static void RunCommand(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.Contains("://") || cmd.StartsWith("ms-settings:"))
        {
            Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true });
            return;
        }
        string exe, args = "";
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            exe = end > 0 ? cmd[1..end] : cmd.Trim('"');
            args = end > 0 ? cmd[(end + 1)..].Trim() : "";
        }
        else
        {
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exe = i > 0 ? cmd[..(i + 4)] : cmd;
            args = i > 0 ? cmd[(i + 4)..].Trim() : "";
        }
        Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
    }

    // ═════════════ Nettoyage ═════════════

    private readonly List<CleanRow> _clean = new();
    private bool _cleaning;

    private void CleanMeasure_Click(object sender, RoutedEventArgs e) => MeasureCleanup();

    private async void MeasureCleanup()
    {
        if (_cleaning) return;
        if (_clean.Count == 0)
        {
            foreach (var c in Cleaner.WindowsCategories()) _clean.Add(new CleanRow(c) { CheckChanged = UpdateCleanButton });
            CleanList.ItemsSource = _clean;
        }
        CleanMeasure.IsEnabled = false;
        CleanHint.Text = "Calcul de la place récupérable…";
        foreach (var r in _clean) r.Measure = null;
        try
        {
            foreach (var r in _clean)
            {
                var cat = r.Category;
                r.Measure = await Task.Run(() => cat.IsSpecial ? new CleanupMeasure(NativeMethods.RecycleBinSize(), 0) : Cleaner.Measure(cat));
                if (!r.CanCheck) r.IsChecked = false;
            }
        }
        finally
        {
            CleanMeasure.IsEnabled = true;
        }
        long total = _clean.Sum(r => r.Measure?.Bytes ?? 0);
        CleanHint.Text = $"Jusqu'à {ByteSize.Format(total)} récupérables. Coche ce que tu veux supprimer : rien n'est coché d'office." +
                         (_elevated ? "" : " Sans les droits administrateur, certains dossiers de Windows ne sont pas comptés.");
        UpdateCleanButton();
    }

    private void UpdateCleanButton()
    {
        var sel = _clean.Where(r => r.IsChecked).ToList();
        CleanRun.IsEnabled = !_cleaning && sel.Count > 0;
        if (CleanRun.Content is StackPanel sp && sp.Children[1] is TextBlock t)
            t.Text = sel.Count > 0 ? $"Nettoyer ({ByteSize.Format(sel.Sum(r => r.Measure?.Bytes ?? 0))})" : "Nettoyer la sélection";
    }

    private async void CleanRun_Click(object sender, RoutedEventArgs e)
    {
        var sel = _clean.Where(r => r.IsChecked && r.CanCheck).ToList();
        if (sel.Count == 0 || _cleaning) return;
        string list = string.Join("\n", sel.Select(r => $"  • {r.Name} ({r.SizeText})"));
        string warn = sel.Any(r => r.Category.Id == "winold") ? "\n\nAttention : sans Windows.old, tu ne pourras plus revenir à l'ancienne version de Windows." : "";
        var answer = MessageBox.Show(this,
            $"Supprimer définitivement le contenu de :\n\n{list}\n\nCes fichiers ne passent pas par la corbeille. Les fichiers en cours d'utilisation sont laissés en place.{warn}",
            "Nettoyage", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _cleaning = true;
        UpdateCleanButton();
        CleanMeasure.IsEnabled = false;
        long freedTotal = 0;
        try
        {
            foreach (var r in sel)
            {
                r.SetState("Nettoyage…");
                if (r.Category.IsSpecial)
                {
                    long before = r.Measure?.Bytes ?? 0;
                    bool ok = NativeMethods.EmptyRecycleBin(Hwnd);
                    if (ok) freedTotal += before;
                    r.SetState(ok ? $"Corbeille vidée · {ByteSize.Format(before)} libérés" : "La corbeille n'a pas pu être vidée", !ok);
                    continue;
                }
                var cat = r.Category;
                var outcome = await Task.Run(() => Cleaner.Clean(cat));
                freedTotal += outcome.FreedBytes;
                string skipped = outcome.SkippedFiles > 0 ? $" · {Fmt.Count(outcome.SkippedFiles)} en cours d'utilisation, ignorés" : "";
                r.SetState($"{ByteSize.Format(outcome.FreedBytes)} libérés{skipped}");
            }
        }
        finally
        {
            _cleaning = false;
            foreach (var r in sel) r.IsChecked = false;
        }
        StatusText.Text = $"Nettoyage terminé · {ByteSize.Format(freedTotal)} libérés.";
        LoadDrives();
        var states = sel.ToDictionary(r => r, r => r.StateText);
        MeasureCleanup();
        foreach (var (r, s) in states) r.SetState(s);
    }

    // ═════════════ Doublons ═════════════

    private readonly List<DupeRow> _dupes = new();
    private long _dupesMin = 1024 * 1024;
    private CancellationTokenSource? _dupesCts;

    private void DupesMin_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mb }) _dupesMin = long.Parse(mb) * 1024 * 1024;
    }

    private async void DupesFind_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null)
        {
            ShowPage("analysis");
            StatusText.Text = "Analyse d'abord un disque ou un dossier, puis reviens dans Doublons.";
            return;
        }
        if (_dupesCts != null) { _dupesCts.Cancel(); return; }

        var root = _result.Root;
        var cts = _dupesCts = new CancellationTokenSource();
        var progress = new DuplicateProgress();
        long min = _dupesMin;
        SetDupesBusy(true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            DupesProgress.IsIndeterminate = progress.Total == 0;
            if (progress.Total > 0) DupesProgress.Value = (double)progress.Done / progress.Total;
            DupesHint.Text = $"{progress.Phase}… {Fmt.Count(progress.Done)} / {Fmt.Count(progress.Total)} fichiers";
        };
        timer.Start();
        try
        {
            var groups = await Task.Run(() => DuplicateFinder.Find(root, min, progress, cts.Token), cts.Token);
            _dupes.Clear();
            foreach (var g in groups.Take(1000))
            {
                _dupes.Add(new DupeRow(g, null));
                foreach (var f in g.Files) _dupes.Add(new DupeRow(g, f) { CheckChanged = UpdateDupesButtons });
            }
            DupesList.ItemsSource = _dupes.ToList();
            long wasted = groups.Sum(g => g.Wasted);
            DupesHint.Text = groups.Count == 0
                ? $"Aucun doublon de plus de {ByteSize.Format(min)} trouvé."
                : $"{Fmt.Count(groups.Count)} groupes de fichiers identiques · {ByteSize.Format(wasted)} récupérables. « Cocher les copies » garde le plus ancien de chaque groupe.";
        }
        catch (OperationCanceledException)
        {
            DupesHint.Text = "Recherche annulée.";
        }
        finally
        {
            timer.Stop();
            if (_dupesCts == cts) _dupesCts = null;
            SetDupesBusy(false);
        }
    }

    private void SetDupesBusy(bool busy)
    {
        DupesProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (DupesFind.Content is StackPanel sp && sp.Children[1] is TextBlock t) t.Text = busy ? "Arrêter" : "Chercher";
        UpdateDupesButtons();
    }

    private void DupesAuto_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in _dupes.Where(d => !d.IsHeader).GroupBy(d => d.Group))
        {
            bool first = true;
            foreach (var row in g) { row.IsChecked = !first; first = false; }
        }
    }

    private void UpdateDupesButtons()
    {
        var sel = _dupes.Where(d => d.IsChecked).ToList();
        DupesDelete.IsEnabled = sel.Count > 0 && _dupesCts == null;
        DupesAuto.IsEnabled = _dupes.Count > 0 && _dupesCts == null;
        if (DupesDelete.Content is StackPanel sp && sp.Children[1] is TextBlock t)
            t.Text = sel.Count > 0 ? $"Corbeille ({sel.Count} · {ByteSize.Format(sel.Sum(d => d.Group.FileSize))})" : "Envoyer à la corbeille";
    }

    private void DupesDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = _dupes.Where(d => d.IsChecked && d.File != null).ToList();
        if (sel.Count == 0) return;
        // Sécurité : on ne supprime jamais tous les exemplaires d'un fichier.
        var allGone = sel.GroupBy(d => d.Group).Where(g => g.Count() >= g.Key.Files.Count(f => f.Parent != null)).ToList();
        if (allGone.Count > 0)
        {
            MessageBox.Show(this, $"Tu as coché tous les exemplaires de « {allGone[0].Key.Files[0].Name} ». Garde au moins une copie de chaque fichier.",
                "Doublons", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        long size = sel.Sum(d => d.Group.FileSize);
        var answer = MessageBox.Show(this, $"Envoyer {sel.Count} copie{(sel.Count > 1 ? "s" : "")} à la corbeille ({ByteSize.Format(size)}) ?\n\nUn exemplaire de chaque fichier est conservé.",
            "Doublons", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var gone = RecycleNodes(sel.Select(d => d.File!).ToList()).ToHashSet();
        foreach (var g in _dupes.Select(d => d.Group).Distinct().ToList()) g.Files.RemoveAll(gone.Contains);
        _dupes.RemoveAll(d => d.File != null && gone.Contains(d.File));
        _dupes.RemoveAll(d => d.Group.Files.Count < 2);
        foreach (var d in _dupes) d.RefreshAll();
        DupesList.ItemsSource = _dupes.ToList();
        UpdateDupesButtons();
        StatusText.Text = $"{gone.Count} doublon{(gone.Count > 1 ? "s" : "")} envoyé{(gone.Count > 1 ? "s" : "")} à la corbeille · {ByteSize.Format(gone.Sum(n => n.Size))} libérés.";
    }

    // ═════════════ Vieux fichiers ═════════════

    private readonly List<OldFileRow> _old = new();
    private int _oldYears = 1;
    private long _oldMinMb = 10;
    private bool _oldDirty = true;

    private void OldAge_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } rb) return;
        if (rb.GroupName == "oldage") _oldYears = int.Parse(tag); else _oldMinMb = long.Parse(tag);
        _oldDirty = true;
        if (IsLoaded && _page == "old") RefreshOld();
    }

    private void RefreshOld()
    {
        _oldDirty = false;
        _old.Clear();
        if (_result == null) { ApplyEmptyHints(); OldList.ItemsSource = null; UpdateOldButtons(); return; }
        var found = OldFiles.Find(_result.Root, DateTime.UtcNow.AddYears(-_oldYears), _oldMinMb * 1024 * 1024, 3000);
        foreach (var f in found) _old.Add(new OldFileRow(f) { CheckChanged = UpdateOldButtons });
        OldList.ItemsSource = _old.ToList();
        OldHint.Text = found.Count == 0
            ? $"Aucun fichier de plus de {ByteSize.Format(_oldMinMb * 1024 * 1024)} non modifié depuis {_oldYears} an{(_oldYears > 1 ? "s" : "")}."
            : $"{Fmt.Count(found.Count)} fichiers non modifiés depuis plus de {_oldYears} an{(_oldYears > 1 ? "s" : "")} · {ByteSize.Format(found.Sum(f => f.Size))}. Vérifie avant de supprimer : un vieux fichier peut être important.";
        UpdateOldButtons();
    }

    private void UpdateOldButtons()
    {
        var sel = _old.Where(o => o.IsChecked).ToList();
        OldDelete.IsEnabled = sel.Count > 0;
        if (OldDelete.Content is StackPanel sp && sp.Children[1] is TextBlock t)
            t.Text = sel.Count > 0 ? $"Corbeille ({sel.Count} · {ByteSize.Format(sel.Sum(o => o.Node.Size))})" : "Envoyer à la corbeille";
    }

    private void OldDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = _old.Where(o => o.IsChecked).Select(o => o.Node).ToList();
        if (sel.Count == 0) return;
        var answer = MessageBox.Show(this, $"Envoyer {sel.Count} fichier{(sel.Count > 1 ? "s" : "")} à la corbeille ({ByteSize.Format(sel.Sum(n => n.Size))}) ?",
            "Vieux fichiers", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        var gone = RecycleNodes(sel);
        StatusText.Text = $"{gone.Count} fichier{(gone.Count > 1 ? "s" : "")} envoyé{(gone.Count > 1 ? "s" : "")} à la corbeille · {ByteSize.Format(gone.Sum(n => n.Size))} libérés.";
        RefreshOld();
    }

    // ═════════════ Nouveautés ═════════════

    private static string SnapshotDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7734FilesChecker", "snapshots");

    private async void UpdateSnapshot(FsNode root)
    {
        string rootPath = root.FullPath;
        string file = Path.Combine(SnapshotDir, Snapshot.FileNameFor(rootPath));
        var (previous, current) = await Task.Run(() =>
        {
            var prev = Snapshot.Load(file);
            var cur = Snapshot.Create(root);
            try { cur.Save(file); } catch (Exception) { }
            return (prev, cur);
        });
        if (_result?.Root != root) return;

        if (previous == null)
        {
            NewsList.ItemsSource = null;
            NewsHint.Text = $"Première analyse de {rootPath} enregistrée. À la prochaine analyse, tu verras ici les dossiers qui ont grossi ou diminué.";
            Nav("news").Badge = "";
            return;
        }
        var changes = SnapshotDiff.Compare(previous, current);
        NewsList.ItemsSource = changes.Select(c => new ChangeRow(c, rootPath)).ToList();
        long delta = current.TotalSize - previous.TotalSize;
        string sign = delta >= 0 ? "+" : "−";
        string since = Fmt.Date(previous.TakenUtc);
        NewsHint.Text = changes.Count == 0
            ? $"Depuis le {since} : {sign} {ByteSize.Format(Math.Abs(delta))} au total. Aucun dossier n'a changé de plus de 100 Mo."
            : $"Depuis le {since} : {sign} {ByteSize.Format(Math.Abs(delta))} au total · {changes.Count} dossier{(changes.Count > 1 ? "s ont" : " a")} changé de plus de 100 Mo. Double-clic pour l'afficher.";
        Nav("news").Badge = changes.Count > 0 && _page != "news" ? changes.Count.ToString() : "";
    }
}

/// <summary>Fil d'Ariane au-dessus de la carte : chaque dossier parent est cliquable.</summary>
public partial class MainWindow
{
    private void UpdateBreadcrumb(FsNode? dir)
    {
        Breadcrumb.Children.Clear();
        TextBlock Label(string text, bool bold) => new()
        {
            Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260,
        };
        if (dir == null || _result == null || !FsTree.IsInside(dir, _result.Root))
        {
            Breadcrumb.Children.Add(Label("Carte du disque", true));
            return;
        }

        var chain = new List<FsNode>();
        for (var n = dir; n != null; n = n.Parent)
        {
            chain.Add(n);
            if (n == _result.Root) break;
        }
        chain.Reverse();
        bool trimmed = chain.Count > 6;
        if (trimmed) chain = chain.Take(1).Concat(chain.Skip(chain.Count - 4)).ToList();

        for (int i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (i > 0)
            {
                Breadcrumb.Children.Add(new TextBlock
                {
                    Text = "", FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 9,
                    Foreground = (System.Windows.Media.Brush)FindResource("FaintBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0),
                });
                if (trimmed && i == 1)
                {
                    Breadcrumb.Children.Add(Label("…", false));
                    Breadcrumb.Children.Add(new TextBlock
                    {
                        Text = "", FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 9,
                        Foreground = (System.Windows.Media.Brush)FindResource("FaintBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0),
                    });
                }
            }
            string text = i == 0 ? (node.IsVirtual ? node.Name : node.FullPath.TrimEnd('\\')) : node.Name;
            bool last = i == chain.Count - 1;
            if (last)
            {
                var t = Label(text, true);
                t.Margin = new Thickness(5, 0, 0, 0);
                Breadcrumb.Children.Add(t);
            }
            else
            {
                var b = new Button { Style = (Style)FindResource("CrumbButton"), Content = Label(text, false), ToolTip = node.FullPath };
                b.Click += (_, _) => RevealInTree(node, zoomMap: true);
                Breadcrumb.Children.Add(b);
            }
        }
    }
}

/// <summary>Surbrillance d'un type de fichier sur la carte.</summary>
public partial class MainWindow
{
    private void ExtList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string? ext = (ExtList.SelectedItem as ExtensionRow)?.Stat.Extension;
        Map.HighlightExtension = ext;
        ClearHighlight.Visibility = ext != null ? Visibility.Visible : Visibility.Collapsed;
        if (ext != null && ExtList.SelectedItem is ExtensionRow row)
        {
            ClearHighlightText.Text = $"{row.Name} · {row.SizeText} · tout afficher";
            StatusText.Text = $"Carte : seuls les fichiers {row.Name} sont en couleur ({row.CountText} fichiers, {row.SizeText}).";
        }
        Map.Render();
    }

    private void ClearHighlight_Click(object sender, RoutedEventArgs e) => ExtList.SelectedItem = null;
}

/// <summary>Aperçu (miniature de l'Explorateur) dans les pages Doublons et Vieux fichiers.</summary>
public partial class MainWindow
{
    private int _previewVersion;

    private string? PreviewPath(string prefix) => prefix switch
    {
        "Dupes" => (DupesList.SelectedItem as DupeRow)?.File?.FullPath,
        "Old" => (OldList.SelectedItem as OldFileRow)?.Node.FullPath,
        _ => (CapsList.SelectedItem as CaptureRow)?.File.FullName,
    };

    private async void PreviewList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string prefix = sender == DupesList ? "Dupes" : sender == OldList ? "Old" : "Caps";
        var (image, empty, name, info) = prefix switch
        {
            "Dupes" => (DupesPreviewImage, DupesPreviewEmpty, DupesPreviewName, DupesPreviewInfo),
            "Old" => (OldPreviewImage, OldPreviewEmpty, OldPreviewName, OldPreviewInfo),
            _ => (CapsPreviewImage, CapsPreviewEmpty, CapsPreviewName, CapsPreviewInfo),
        };
        string? path = PreviewPath(prefix);
        int version = ++_previewVersion;
        image.Source = null;
        if (path == null)
        {
            empty.Text = "Sélectionne un fichier";
            empty.Visibility = Visibility.Visible;
            name.Text = info.Text = "";
            return;
        }
        var fi = new FileInfo(path);
        name.Text = fi.Name;
        info.Text = fi.Exists ? $"{ByteSize.Format(fi.Length)} · modifié le {Fmt.Date(fi.LastWriteTimeUtc)}\n{fi.DirectoryName}" : "";
        empty.Text = "Chargement…";
        empty.Visibility = Visibility.Visible;
        var thumb = await SystemTools.ThumbnailAsync(path, 400);
        if (version != _previewVersion) return;
        image.Source = thumb;
        empty.Text = "Pas d'aperçu pour ce fichier";
        empty.Visibility = thumb == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PreviewOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string p } && PreviewPath(p) is { } n) Safe(() => NativeMethods.Open(n));
    }

    private void PreviewReveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string p } && PreviewPath(p) is { } n) Safe(() => NativeMethods.ShowInExplorer(n));
    }
}
