using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Files7734.App;

/// <summary>Assistant « Faire de la place » : un plan pour libérer exactement l'espace demandé.</summary>
public partial class MainWindow
{
    private readonly List<SpaceRow> _space = new();
    private bool _spaceBusy;
    private string? _spaceDrive;

    private void PrepareSpace()
    {
        var drives = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.DriveType == DriveType.Fixed) drives.Add(d.Name.TrimEnd('\\')); } catch (Exception) { }
        }
        string? current = _spaceDrive ?? (DriveList.SelectedItem as DriveItem)?.Letter;
        _spaceDrive = drives.Contains(current ?? "") ? current : drives.FirstOrDefault();
        SpaceDrives.Children.Clear();
        foreach (var d in drives)
        {
            var rb = new RadioButton { Content = d, GroupName = "spacedrive", Style = (Style)FindResource("Segment"), IsChecked = d == _spaceDrive };
            rb.Checked += (_, _) => { _spaceDrive = d; UpdateSpaceBar(); };
            SpaceDrives.Children.Add(rb);
        }
        SpaceGoal.Focus();
        UpdateSpaceBar();
    }

    private void SpaceGoal_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SpaceFind_Click(sender, e);
    }

    private long SpaceGoalBytes =>
        double.TryParse(SpaceGoal.Text.Replace(',', '.').Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var go) && go > 0
            ? (long)(go * 1024 * 1024 * 1024) : 0;

    private long DriveFree(string letter)
    {
        try { return new DriveInfo(letter + "\\").AvailableFreeSpace; } catch (Exception) { return 0; }
    }

    private async void SpaceFind_Click(object sender, RoutedEventArgs e)
    {
        if (_spaceBusy) return;
        long goal = SpaceGoalBytes;
        if (goal <= 0) { SpaceHint.Text = "Écris combien de Go il te faut (par exemple 150)."; SpaceGoal.Focus(); return; }
        if (_spaceDrive is not string drive) return;
        string root = drive + "\\";
        long free = DriveFree(drive);
        string forWhat = SpaceFor.Text.Trim();
        string target = forWhat.Length > 0 ? $" pour {forWhat}" : "";
        if (free >= goal)
        {
            SpaceHint.Text = $"Bonne nouvelle : {drive} a déjà {ByteSize.Format(free)} de libre, c'est assez{target}.";
            _space.Clear();
            SpaceList.ItemsSource = null;
            UpdateSpaceBar();
            return;
        }

        _spaceBusy = true;
        SpaceFind.IsEnabled = false;
        SpaceHint.Text = $"Recherche de {ByteSize.Format(goal - free)} à libérer sur {drive}{target}…";
        _space.Clear();
        SpaceList.ItemsSource = null;
        try
        {
            var rows = await BuildSpacePlan(drive, goal - free);
            foreach (var r in rows) { r.CheckChanged = UpdateSpaceBar; _space.Add(r); }
            SpaceList.ItemsSource = _space.ToList();
            long possible = _space.Sum(r => r.Size);
            SpaceHint.Text = possible + free >= goal
                ? $"Il manque {ByteSize.Format(goal - free)} sur {drive}{target}. Voici {_space.Count} propositions, de la plus sûre à la plus engageante : coche-les jusqu'à remplir la barre."
                : $"Il manque {ByteSize.Format(goal - free)} sur {drive}{target}, mais l'app n'a trouvé que {ByteSize.Format(possible)} à libérer sans risque. " +
                  "Regarde aussi les pages Vieux fichiers et Applications.";
        }
        finally
        {
            _spaceBusy = false;
            SpaceFind.IsEnabled = true;
            UpdateSpaceBar();
        }
    }

    private async Task<List<SpaceRow>> BuildSpacePlan(string drive, long needed)
    {
        var rows = new List<SpaceRow>();
        string root = drive + "\\";
        bool onSystem = string.Equals(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), root, StringComparison.OrdinalIgnoreCase);
        bool OnDrive(string p) => p.StartsWith(root, StringComparison.OrdinalIgnoreCase);

        // 1) Nettoyage sûr (caches, temporaires, corbeille)
        if (onSystem)
        {
            foreach (var cat in Cleaner.WindowsCategories().Where(c => c.Id != "winold"))
            {
                var m = await Task.Run(() => cat.IsSpecial ? new CleanupMeasure(NativeMethods.RecycleBinSize(), 0) : Cleaner.Measure(cat));
                if (m.Bytes < 50L * 1024 * 1024) continue;
                var c = cat;
                rows.Add(new SpaceRow(c.Name, c.Description, m.Bytes, async () =>
                {
                    if (c.IsSpecial) return NativeMethods.EmptyRecycleBin(Hwnd) ? (m.Bytes, "Corbeille vidée") : (0, "Échec");
                    var o = await Task.Run(() => Cleaner.Clean(c));
                    return (o.FreedBytes, $"{ByteSize.Format(o.FreedBytes)} libérés");
                }));
            }
        }

        // 2) Restes de jeux désinstallés
        var games = _games?.Select(g => g.Game).ToList() ?? await Task.Run(GameDetector.Detect);
        var ghosts = await Task.Run(() => FindGhosts(games).Where(g => OnDrive(g.Path)).Select(g => (g.Path, Size: GameCleanup.FolderSize(g.Path))).Where(g => g.Size > 0).ToList());
        if (ghosts.Count > 0)
            rows.Add(new SpaceRow($"Restes de {ghosts.Count} jeu{(ghosts.Count > 1 ? "x" : "")} désinstallé{(ghosts.Count > 1 ? "s" : "")}",
                "Dossiers laissés par des jeux qui ne sont plus installés : " + string.Join(", ", ghosts.Take(4).Select(g => Path.GetFileName(g.Path))) + (ghosts.Count > 4 ? "…" : ""),
                ghosts.Sum(g => g.Size), () => DeleteMany(ghosts.Select(g => g.Path))));

        // 3) Installateurs déjà utilisés dans Téléchargements
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (OnDrive(downloads))
        {
            var setupExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".iso", ".zip", ".rar", ".7z" };
            var limit = DateTime.UtcNow.AddDays(-30);
            var setups = await Task.Run(() => CollectFiles(new[] { downloads }, f => setupExt.Contains(f.Extension) && f.LastWriteTimeUtc < limit));
            if (setups.Sum(f => f.Length) > 50L * 1024 * 1024)
                rows.Add(new SpaceRow($"{setups.Count} installateurs et archives dans Téléchargements",
                    "Fichiers .exe, .msi, .zip, .iso… téléchargés il y a plus d'un mois, en général déjà installés ou décompressés.",
                    setups.Sum(f => f.Length), () => DeleteMany(setups.Select(f => f.FullName))));
        }

        // 4) Vieux clips et captures
        var (shots, clips, _) = CaptureFolders();
        var oldMedia = await Task.Run(() =>
        {
            var lim = DateTime.UtcNow.AddMonths(-6);
            return CollectFiles(shots.Concat(clips), f => (VideoExt.Contains(f.Extension) || ImageExt.Contains(f.Extension)) && f.LastWriteTimeUtc < lim && OnDrive(f.FullName));
        });
        if (oldMedia.Sum(f => f.Length) > 100L * 1024 * 1024)
            rows.Add(new SpaceRow($"{oldMedia.Count} clips et captures de plus de 6 mois",
                "Enregistrements de jeux et captures d'écran anciens. Vérifie-les d'abord dans la page Captures et clips si tu veux en garder.",
                oldMedia.Sum(f => f.Length), () => DeleteMany(oldMedia.Select(f => f.FullName))));

        // 5) Doublons déjà trouvés
        var dupCopies = _dupes.Where(d => d.File != null && d.Group.Files.IndexOf(d.File) > 0 && OnDrive(d.File.FullPath)).Select(d => d.File!).Distinct().ToList();
        if (dupCopies.Count > 0)
            rows.Add(new SpaceRow($"{dupCopies.Count} copies en double", "Fichiers identiques trouvés dans la page Doublons. Le plus ancien exemplaire de chaque fichier est gardé.",
                dupCopies.Sum(f => f.Size), () => DeleteMany(dupCopies.Select(f => f.FullPath))));

        // 6) Hibernation réduite
        if (onSystem && _elevated)
        {
            long hiber = SystemTools.FileSize(Path.Combine(root, "hiberfil.sys"));
            if (hiber > 1024L * 1024 * 1024)
                rows.Add(new SpaceRow("Réduire le fichier d'hibernation", "Garde le démarrage rapide de Windows mais divise le fichier à peu près par deux.",
                    hiber / 2, async () =>
                    {
                        long before = DriveFree(drive);
                        int code = await Task.Run(() => SystemTools.RunHidden("powercfg.exe", "/h /type reduced"));
                        return code == 0 ? (Math.Max(0, DriveFree(drive) - before), "Fait") : (0, "Windows a refusé");
                    }));
        }

        // 7) Déplacer des jeux vers un autre disque
        var other = DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.Equals(root, StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } })
                             .OrderByDescending(d => d.AvailableFreeSpace).FirstOrDefault();
        if (other != null && _games != null)
        {
            long room = other.AvailableFreeSpace - 10L * 1024 * 1024 * 1024;
            foreach (var g in _games.Where(g => OnDrive(g.InstallPath) && g.Launcher != "Xbox" && g.Size is > 1024L * 1024 * 1024 && !SystemTools.IsJunction(g.InstallPath))
                                    .OrderByDescending(g => g.Size).Take(6))
            {
                if (g.Size > room) continue;
                room -= g.Size!.Value;
                string dest = Path.Combine(other.RootDirectory.FullName, "Jeux");
                var game = g;
                rows.Add(new SpaceRow($"Déplacer {g.Name} vers {other.Name.TrimEnd('\\')}",
                    $"Le jeu reste jouable depuis {g.Launcher}, sans rien réinstaller. Il sera copié dans {dest}.",
                    g.Size.Value, async () =>
                    {
                        long freed = await MoveGame(game, dest, ask: false);
                        return (freed, freed > 0 ? "Déplacé" : "Non déplacé");
                    }));
            }
        }
        return rows;
    }

    private async Task<(long, string)> DeleteMany(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var sizes = list.ToDictionary(p => p, p => File.Exists(p) ? new FileInfo(p).Length : GameCleanup.FolderSize(p), StringComparer.OrdinalIgnoreCase);
        var gone = await Task.Run(() => list.Where(DeletePath).ToList());
        RemovePathsFromResult(gone);
        long freed = gone.Sum(p => sizes[p]);
        return (freed, gone.Count == list.Count ? $"{ByteSize.Format(freed)} libérés" : $"{ByteSize.Format(freed)} libérés ({list.Count - gone.Count} fichiers utilisés ignorés)");
    }

    private void UpdateSpaceBar()
    {
        long goal = SpaceGoalBytes;
        string? drive = _spaceDrive;
        long free = drive != null ? DriveFree(drive) : 0;
        long sel = _space.Where(r => r.IsChecked && !r.Done).Sum(r => r.Size);
        double width = Math.Max(0, (SpaceBar.Parent as FrameworkElement)?.ActualWidth ?? 0);
        if (goal <= 0 || width <= 0)
        {
            SpaceFree.Width = SpaceBar.Width = 0;
            SpaceGoalText.Text = "";
        }
        else
        {
            double fFree = Math.Min(1, (double)free / goal), fSel = Math.Min(1 - fFree, (double)sel / goal);
            SpaceFree.Width = width * fFree;
            SpaceBar.Margin = new Thickness(width * fFree, 0, 0, 0);
            SpaceBar.Width = width * fSel;
            long total = free + sel;
            SpaceGoalText.Text = total >= goal ? $"✓ {ByteSize.Format(total)} / {ByteSize.Format(goal)}" : $"{ByteSize.Format(total)} / {ByteSize.Format(goal)}";
        }
        SpaceApply.IsEnabled = !_spaceBusy && sel > 0;
        if (SpaceApply.Content is StackPanel sp && sp.Children[1] is TextBlock t)
            t.Text = sel > 0 ? $"Libérer {ByteSize.Format(sel)}" : "Libérer la sélection";
    }

    private async void SpaceApply_Click(object sender, RoutedEventArgs e)
    {
        var sel = _space.Where(r => r.IsChecked && !r.Done).ToList();
        if (sel.Count == 0 || _spaceBusy) return;
        string list = string.Join("\n", sel.Select(r => $"  • {r.Title} ({r.SizeText})"));
        if (!Ask($"Faire tout ça ?\n\n{list}\n\nLes fichiers supprimés ne passent pas par la corbeille (elle ne libérerait pas la place). Les jeux déplacés restent jouables.")) return;
        _spaceBusy = true;
        SpaceFind.IsEnabled = false;
        long total = 0;
        try
        {
            foreach (var r in sel)
            {
                r.SetState("En cours…");
                try
                {
                    var (freed, msg) = await r.Apply();
                    total += freed;
                    r.IsChecked = false;
                    r.SetState(msg, error: freed == 0, done: freed > 0);
                }
                catch (Exception ex) { r.SetState("Erreur : " + ex.Message, error: true); }
            }
        }
        finally
        {
            _spaceBusy = false;
            SpaceFind.IsEnabled = true;
            LoadDrives();
            UpdateSpaceBar();
        }
        long goal = SpaceGoalBytes;
        string? drive = _spaceDrive;
        long free = drive != null ? DriveFree(drive) : 0;
        SpaceStatus.Text = $"{ByteSize.Format(total)} libérés. {drive} a maintenant {ByteSize.Format(free)} de libre" +
                           (goal > 0 && free >= goal ? " : objectif atteint 🎉" : ".");
    }
}
