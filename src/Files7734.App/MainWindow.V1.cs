using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Files7734.App;

/// <summary>Applications, « qui bloque ? », captures et clips, jeux fantômes, nettoyage par jeu.</summary>
public partial class MainWindow
{
    /// <summary>Retrouve un fichier ou un dossier dans l'arbre analysé.</summary>
    private FsNode? FindAnyInResult(string path)
    {
        var dir = FindInResult(path);
        if (dir != null) return dir;
        string? parent = Path.GetDirectoryName(path);
        if (parent == null) return null;
        var p = FindInResult(parent);
        string name = Path.GetFileName(path);
        return p?.Children?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Après une suppression faite hors de l'arbre (chemins), met l'analyse à jour.</summary>
    private void RemovePathsFromResult(IEnumerable<string> paths)
    {
        if (_result == null) return;
        var nodes = paths.Select(FindAnyInResult).Where(n => n != null && n.Parent != null).Cast<FsNode>().Distinct().ToList();
        if (nodes.Count > 0) AfterRemoval(nodes);
    }

    private static bool DeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
                    if (f.IsReadOnly) f.IsReadOnly = false;
                Directory.Delete(path, true);
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception) { }
        return !Directory.Exists(path) && !File.Exists(path);
    }

    // ═════════════ Applications ═════════════

    private bool _appsLoaded;
    private List<AppRow> _apps = new();
    private bool _uninstalling;

    private void AppsRefresh_Click(object sender, RoutedEventArgs e) => LoadApps();

    private async void LoadApps()
    {
        _appsLoaded = true;
        AppsRefresh.IsEnabled = false;
        AppsHint.Text = "Lecture des programmes installés…";
        var apps = await Task.Run(SystemTools2.InstalledApps);
        _apps = apps.Select(a => new AppRow(a, a.InstallLocation != null ? FindInResult(a.InstallLocation)?.Size : null))
                    .OrderByDescending(a => a.Size).ThenBy(a => a.Name).ToList();
        AppsRefresh.IsEnabled = true;
        ApplyAppsFilter();
        long total = _apps.Sum(a => a.Size);
        AppsHint.Text = $"{_apps.Count} applications · {ByteSize.Format(total)} au total. Après une désinstallation, l'app cherche les restes oubliés dans AppData.";
    }

    private void AppsFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyAppsFilter();

    private void ApplyAppsFilter()
    {
        string f = AppsFilter.Text.Trim();
        AppsList.ItemsSource = f.Length == 0 ? _apps
            : _apps.Where(a => a.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase) || a.Publisher.Contains(f, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void AppFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppRow { App.InstallLocation: { } loc }) Safe(() => NativeMethods.Open(loc));
    }

    private async void AppUninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRow row || _uninstalling) return;
        var app = row.App;
        if (!Ask($"Désinstaller « {app.Name} »{(row.Size > 0 ? $" ({row.SizeText})" : "")} ?\n\nLe programme de désinstallation de l'application va s'ouvrir. Ensuite, l'app cherchera les dossiers oubliés.")) return;
        _uninstalling = true;
        AppsHint.Text = $"Désinstallation de {app.Name} en cours… suis les étapes dans sa fenêtre.";
        try
        {
            try { await SystemTools2.RunUninstaller(app.UninstallCommand); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Impossible de lancer la désinstallation : " + ex.Message, "Applications", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Certains désinstalleurs rendent la main tout de suite et continuent dans une autre fenêtre.
            if (SystemTools2.StillInstalled(app))
            {
                MessageBox.Show(this, $"Quand la désinstallation de « {app.Name} » est terminée, clique sur OK pour chercher les restes.",
                    "Applications", MessageBoxButton.OK, MessageBoxImage.Information);
                if (SystemTools2.StillInstalled(app))
                {
                    AppsHint.Text = $"« {app.Name} » est toujours installé (désinstallation annulée ?).";
                    return;
                }
            }
            await OfferLeftovers(app);
        }
        finally
        {
            _uninstalling = false;
            LoadApps();
            LoadDrives();
        }
    }

    private async Task OfferLeftovers(SystemTools2.InstalledApp app)
    {
        var others = _apps.Where(a => a.App != app).Select(a => a.Name).ToList();
        var found = await Task.Run(() =>
        {
            var candidates = SystemTools2.LeftoverCandidates();
            var matches = AppLeftovers.Match(app.Name, app.Publisher, candidates, others);
            if (app.InstallLocation != null && Directory.Exists(app.InstallLocation)) matches.Insert(0, app.InstallLocation);
            return matches.Distinct(StringComparer.OrdinalIgnoreCase).Select(m => (Path: m, Size: GameCleanup.FolderSize(m))).ToList();
        });
        if (found.Count == 0)
        {
            StatusText.Text = $"« {app.Name} » désinstallé. Aucun reste trouvé.";
            return;
        }
        var rows = found.Select(f => new GhostRow(f.Path, f.Size, f.Path.Equals(app.InstallLocation, StringComparison.OrdinalIgnoreCase) ? "Dossier d'installation" : "Données oubliées")).ToList();
        var chosen = PickDialog($"Restes de « {app.Name} »",
            "Ces dossiers semblent appartenir à l'application désinstallée. Coche ceux à supprimer : ils iront à la corbeille. En cas de doute, laisse décoché.",
            rows, "Envoyer à la corbeille");
        if (chosen.Count == 0) return;
        NativeMethods.SendToRecycleBin(chosen.Select(c => c.Path).ToList(), Hwnd);
        var gone = chosen.Where(c => !Directory.Exists(c.Path)).ToList();
        RemovePathsFromResult(gone.Select(g => g.Path));
        StatusText.Text = $"« {app.Name} » désinstallé · {gone.Count} dossier{(gone.Count > 1 ? "s" : "")} de restes envoyé{(gone.Count > 1 ? "s" : "")} à la corbeille ({ByteSize.Format(gone.Sum(g => g.Size))}).";
    }

    /// <summary>Fenêtre avec une liste à cocher (rien n'est coché d'office). Renvoie les éléments cochés et validés.</summary>
    private List<GhostRow> PickDialog(string title, string hint, List<GhostRow> rows, string action)
    {
        var list = new ListView { MaxHeight = 320, SelectionMode = SelectionMode.Single, ItemsSource = rows, Margin = new Thickness(0, 12, 0, 12) };
        var grid = new GridView();
        DataTemplate Template(string xaml) => (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + xaml + "</DataTemplate>");
        grid.Columns.Add(new GridViewColumn { Width = 40, CellTemplate = Template("<CheckBox IsChecked='{Binding IsChecked, Mode=TwoWay}' Margin='4,0,0,0'/>") });
        grid.Columns.Add(new GridViewColumn { Header = "Élément", Width = 330, CellTemplate = Template("<StackPanel><TextBlock Text='{Binding Name}' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Path}' FontSize='11' Opacity='0.65' TextTrimming='CharacterEllipsis'/></StackPanel>") });
        grid.Columns.Add(new GridViewColumn { Header = "Taille", Width = 90, CellTemplate = Template("<TextBlock Text='{Binding SizeText}' FontWeight='SemiBold' HorizontalAlignment='Right'/>") });
        grid.Columns.Add(new GridViewColumn { Header = "Type", Width = 170, CellTemplate = Template("<TextBlock Text='{Binding Reason}' Opacity='0.7' TextTrimming='CharacterEllipsis'/>") });
        list.View = grid;

        var ok = new Button { Content = action, Style = (Style)FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        var all = new Button { Content = "Tout cocher", Style = (Style)FindResource("PlainButton") };
        var cancel = new Button { Content = "Annuler", IsCancel = true, Style = (Style)FindResource("PlainButton"), Margin = new Thickness(8, 0, 0, 0) };
        var total = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        void Update()
        {
            var sel = rows.Where(r => r.IsChecked).ToList();
            ok.IsEnabled = sel.Count > 0;
            total.Text = sel.Count > 0 ? $"{sel.Count} élément{(sel.Count > 1 ? "s" : "")} · {ByteSize.Format(sel.Sum(r => r.Size))}" : "";
        }
        foreach (var r in rows) r.CheckChanged = Update;
        all.Click += (_, _) => { foreach (var r in rows) r.IsChecked = true; };

        var buttons = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(cancel);
        right.Children.Add(ok);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(all);
        total.Margin = new Thickness(12, 0, 0, 0);
        left.Children.Add(total);
        buttons.Children.Add(left);

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 16 });
        var h = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        h.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(h);
        panel.Children.Add(list);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = title, Owner = this, Content = panel, Width = 700, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = FontFamily, FontSize = 13,
        };
        win.SetResourceReference(BackgroundProperty, "CardBrush");
        win.SetResourceReference(ForegroundProperty, "TextBrush");
        win.SourceInitialized += (_, _) => WindowFrame.Apply(win);
        ok.Click += (_, _) => win.DialogResult = true;
        return win.ShowDialog() == true ? rows.Where(r => r.IsChecked).ToList() : new List<GhostRow>();
    }

    // ═════════════ Qui bloque ce fichier ? ═════════════

    private async void ShowLockers(string path)
    {
        StatusText.Text = "Recherche du programme qui utilise ce fichier…";
        var lockers = await Task.Run(() => SystemTools2.WhoLocks(path));
        StatusText.Text = "Prêt.";
        string name = Path.GetFileName(path.TrimEnd('\\'));
        if (lockers.Count == 0)
        {
            MessageBox.Show(this, $"Aucun programme n'utilise « {name} » en ce moment." +
                (_elevated ? "" : "\n\nS'il refuse quand même d'être supprimé, relance l'app en administrateur : il est peut-être protégé par Windows."),
                "Qui bloque ce fichier ?", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        panel.Children.Add(new TextBlock { Text = $"« {name} » est utilisé par :", FontWeight = FontWeights.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        var win = new Window
        {
            Title = "Qui bloque ce fichier ?", Owner = this, Content = panel, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = FontFamily, FontSize = 13,
        };
        foreach (var l in lockers)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var close = new Button { Content = l.IsService ? "Service Windows" : "Fermer", Style = (Style)FindResource("PlainButton"), IsEnabled = !l.IsService };
            var locker = l;
            close.Click += async (_, _) =>
            {
                close.IsEnabled = false;
                if (await CloseProcess(locker)) close.Content = "Fermé";
                else close.IsEnabled = true;
            };
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = l.AppName, FontWeight = FontWeights.SemiBold });
            var sub = new TextBlock { Text = $"{l.Name} · processus {l.Pid}", FontSize = 11.5 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            t.Children.Add(sub);
            row.Children.Add(t);
            panel.Children.Add(row);
        }
        var note = new TextBlock { Text = "Enregistre ton travail dans ce programme avant de le fermer.", FontSize = 12, Margin = new Thickness(0, 14, 0, 10), TextWrapping = TextWrapping.Wrap };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(note);
        var ok = new Button { Content = "Terminé", IsCancel = true, IsDefault = true, Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => win.Close();
        panel.Children.Add(ok);
        win.SetResourceReference(BackgroundProperty, "CardBrush");
        win.SetResourceReference(ForegroundProperty, "TextBrush");
        win.SourceInitialized += (_, _) => WindowFrame.Apply(win);
        win.ShowDialog();
    }

    private async Task<bool> CloseProcess(SystemTools2.Locker l)
    {
        try
        {
            using var p = Process.GetProcessById(l.Pid);
            p.CloseMainWindow();
            if (await Task.Run(() => p.WaitForExit(4000))) return true;
            if (!Ask($"« {l.AppName} » ne se ferme pas. Forcer la fermeture ? Ce qui n'est pas enregistré sera perdu.")) return false;
            p.Kill(entireProcessTree: false);
            return await Task.Run(() => p.WaitForExit(4000));
        }
        catch (ArgumentException) { return true; } // déjà fermé
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossible de fermer ce programme : " + ex.Message, "Qui bloque ce fichier ?", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ═════════════ Captures, clips et téléchargements ═════════════

    private bool _capsLoaded;
    private string _capsKind = "shots";
    private readonly Dictionary<string, List<CaptureRow>> _caps = new();

    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".jxr", ".heic" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".wmv" };

    private static List<FileInfo> CollectFiles(IEnumerable<string> dirs, Func<FileInfo, bool> keep)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<FileInfo>();
        foreach (var d in dirs.Where(Directory.Exists))
        {
            try
            {
                foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System }))
                    if (keep(f) && seen.Add(f.FullName)) list.Add(f);
            }
            catch (Exception) { }
        }
        return list.OrderByDescending(f => f.Length).Take(5000).ToList();
    }

    private static (List<string> Shots, List<string> Clips, string Downloads) CaptureFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        string? onedrive = Environment.GetEnvironmentVariable("OneDrive");
        var shots = new List<string> { Path.Combine(pictures, "Screenshots"), Path.Combine(pictures, "Captures d’écran"), Path.Combine(pictures, "Captures d'écran"), Path.Combine(videos, "Captures") };
        if (onedrive != null) shots.AddRange(new[] { Path.Combine(onedrive, "Pictures", "Screenshots"), Path.Combine(onedrive, "Images", "Captures d’écran"), Path.Combine(onedrive, "Images", "Screenshots") });
        var clips = new List<string> { videos, @"C:\Medal", Path.Combine(profile, "Medal") };
        foreach (var lib in GameDetector.SteamLibraryPaths().Take(1))
        {
            string userdata = Path.Combine(lib, "userdata");
            if (!Directory.Exists(userdata)) continue;
            try
            {
                foreach (var u in Directory.EnumerateDirectories(userdata))
                {
                    shots.Add(Path.Combine(u, "760", "remote")); // captures Steam (F12)
                    clips.Add(Path.Combine(u, "gamerecordings"));
                }
            }
            catch (Exception) { }
        }
        return (shots, clips, Path.Combine(profile, "Downloads"));
    }

    private async void LoadCaptures()
    {
        _capsLoaded = true;
        CapsHint.Text = "Recherche de tes captures, clips de jeux et téléchargements…";
        var (shots, clips, downloads) = await Task.Run(() =>
        {
            var (s, c, d) = CaptureFolders();
            return (CollectFiles(s, f => ImageExt.Contains(f.Extension) || VideoExt.Contains(f.Extension)),
                    CollectFiles(c, f => VideoExt.Contains(f.Extension)),
                    CollectFiles(new[] { d }, _ => true));
        });
        _caps["shots"] = shots.Select(f => new CaptureRow(f) { CheckChanged = UpdateCapsButtons }).ToList();
        _caps["clips"] = clips.Where(f => !shots.Any(s => s.FullName == f.FullName)).Select(f => new CaptureRow(f) { CheckChanged = UpdateCapsButtons }).ToList();
        _caps["downloads"] = downloads.Select(f => new CaptureRow(f) { CheckChanged = UpdateCapsButtons }).ToList();
        CapsShots.Content = $"Captures d'écran · {ByteSize.Format(_caps["shots"].Sum(r => r.Size))}";
        CapsClips.Content = $"Clips de jeux · {ByteSize.Format(_caps["clips"].Sum(r => r.Size))}";
        CapsDownloads.Content = $"Téléchargements · {ByteSize.Format(_caps["downloads"].Sum(r => r.Size))}";
        ShowCapsKind();
    }

    private void CapsKind_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string kind }) _capsKind = kind;
        if (_capsLoaded) ShowCapsKind();
    }

    private void ShowCapsKind()
    {
        if (!_caps.TryGetValue(_capsKind, out var rows)) return;
        CapsList.ItemsSource = rows.ToList();
        string what = _capsKind switch { "shots" => "captures d'écran", "clips" => "clips de jeux (Xbox Game Bar, NVIDIA, Medal, Steam…)", _ => "fichiers dans Téléchargements" };
        CapsHint.Text = rows.Count == 0 ? $"Aucun fichier trouvé dans tes {what}."
            : $"{Fmt.Count(rows.Count)} {what} · {ByteSize.Format(rows.Sum(r => r.Size))}. Double-clic pour ouvrir, et l'aperçu est à droite.";
        UpdateCapsButtons();
    }

    private void CapsOld_Click(object sender, RoutedEventArgs e)
    {
        if (!_caps.TryGetValue(_capsKind, out var rows)) return;
        var limit = DateTime.UtcNow.AddMonths(-6);
        foreach (var r in rows) r.IsChecked = r.File.LastWriteTimeUtc < limit;
    }

    private void UpdateCapsButtons()
    {
        var sel = _caps.TryGetValue(_capsKind, out var rows) ? rows.Where(r => r.IsChecked).ToList() : new();
        CapsDelete.IsEnabled = sel.Count > 0;
        if (CapsDelete.Content is StackPanel sp && sp.Children[1] is TextBlock t)
            t.Text = sel.Count > 0 ? $"Corbeille ({sel.Count} · {ByteSize.Format(sel.Sum(r => r.Size))})" : "Envoyer à la corbeille";
    }

    private void CapsDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!_caps.TryGetValue(_capsKind, out var rows)) return;
        var sel = rows.Where(r => r.IsChecked).ToList();
        if (sel.Count == 0 || !Ask($"Envoyer {sel.Count} fichier{(sel.Count > 1 ? "s" : "")} à la corbeille ({ByteSize.Format(sel.Sum(r => r.Size))}) ?")) return;
        NativeMethods.SendToRecycleBin(sel.Select(r => r.File.FullName).ToList(), Hwnd);
        var gone = sel.Where(r => !File.Exists(r.File.FullName)).ToList();
        rows.RemoveAll(gone.Contains);
        RemovePathsFromResult(gone.Select(g => g.File.FullName));
        StatusText.Text = $"{gone.Count} fichier{(gone.Count > 1 ? "s" : "")} envoyé{(gone.Count > 1 ? "s" : "")} à la corbeille · {ByteSize.Format(gone.Sum(g => g.Size))}.";
        ShowCapsKind();
    }

    private void CapsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CapsList.SelectedItem is CaptureRow r) Safe(() => NativeMethods.Open(r.File.FullName));
    }

    // ═════════════ Jeux : doublons, restes, nettoyage par jeu ═════════════

    private readonly List<GhostRow> _ghosts = new();

    private static void MarkDuplicateGames(List<GameRow> games)
    {
        foreach (var g in games) g.DuplicateOf = null;
        foreach (var group in games.GroupBy(g => AppLeftovers.Normalize(g.Name)).Where(gr => gr.Key.Length >= 3 && gr.Count() > 1))
            foreach (var g in group)
                g.DuplicateOf = string.Join(", ", group.Where(o => o != g).Select(o => $"{o.Launcher} ({o.InstallPath})"));
    }

    private static string? SteamAppId(GameInfo g) =>
        g.UninstallUri?.StartsWith("steam://uninstall/") == true ? g.UninstallUri["steam://uninstall/".Length..] : null;

    /// <summary>Restes de jeux désinstallés dans les dossiers des lanceurs.</summary>
    private static List<(string Path, string Reason)> FindGhosts(List<GameInfo> games)
    {
        var result = new List<(string, string)>();
        var steamIds = games.Select(SteamAppId).Where(i => i != null).ToHashSet()!;
        foreach (var lib in GameDetector.SteamLibraryPaths())
        {
            string apps = Path.Combine(lib, "steamapps");
            var installed = games.Where(g => g.Launcher == "Steam").Select(g => g.InstallPath).ToList();
            foreach (var d in GameCleanup.GhostFolders(Path.Combine(apps, "common"), installed, new[] { "Steamworks Shared", "Steam Controller Configs" }))
                result.Add((d, "Jeu Steam désinstallé"));
            foreach (var sub in new[] { ("shadercache", "Cache de shaders d'un jeu désinstallé"), ("workshop\\content", "Mods d'un jeu désinstallé") })
            {
                string dir = Path.Combine(apps, sub.Item1);
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var d in Directory.EnumerateDirectories(dir))
                        if (!steamIds.Contains(Path.GetFileName(d))) result.Add((d, sub.Item2));
                }
                catch (Exception) { }
            }
        }
        // Dossiers « Epic Games » / « GOG Games » : seuls les dossiers dont le nom désigne clairement le lanceur sont examinés.
        foreach (var (launcher, marker, ignore) in new[] { ("Epic Games", "epic", new[] { "Launcher", "DirectXRedist", "Epic Online Services" }), ("GOG", "gog", Array.Empty<string>()) })
        {
            var mine = games.Where(g => g.Launcher == launcher).Select(g => g.InstallPath).ToList();
            foreach (var parent in mine.Select(Path.GetDirectoryName).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Path.GetFileName(parent!).Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var d in GameCleanup.GhostFolders(parent!, mine, ignore)) result.Add((d, $"Jeu {launcher} désinstallé"));
            }
        }
        return result;
    }

    private async Task LoadGhosts(List<GameInfo> games)
    {
        var ghosts = await Task.Run(() => FindGhosts(games).Select(g => new GhostRow(g.Path, GameCleanup.FolderSize(g.Path), g.Reason)).Where(g => g.Size > 0).OrderByDescending(g => g.Size).ToList());
        _ghosts.Clear();
        foreach (var g in ghosts) { g.CheckChanged = UpdateGhostButton; _ghosts.Add(g); }
        GhostList.ItemsSource = _ghosts.ToList();
        GhostPanel.Visibility = _ghosts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GhostHint.Text = $"{_ghosts.Count} dossier{(_ghosts.Count > 1 ? "s" : "")} · {ByteSize.Format(_ghosts.Sum(g => g.Size))} laissés par des jeux qui ne sont plus installés. Coche ceux à supprimer.";
        UpdateGhostButton();
    }

    private void UpdateGhostButton()
    {
        var sel = _ghosts.Where(g => g.IsChecked).ToList();
        GhostDelete.IsEnabled = sel.Count > 0;
        GhostDelete.Content = sel.Count > 0 ? $"Supprimer ({ByteSize.Format(sel.Sum(g => g.Size))})" : "Supprimer la sélection";
    }

    private async void GhostDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = _ghosts.Where(g => g.IsChecked).ToList();
        if (sel.Count == 0) return;
        string list = string.Join("\n", sel.Take(8).Select(g => $"  • {g.Name} ({g.SizeText})")) + (sel.Count > 8 ? $"\n  … et {sel.Count - 8} autres" : "");
        if (!Ask($"Supprimer définitivement ces restes de jeux ?\n\n{list}\n\nIls ne passent pas par la corbeille (elle ne libérerait pas la place).")) return;
        GhostDelete.IsEnabled = false;
        var gone = await Task.Run(() => sel.Where(g => DeletePath(g.Path)).ToList());
        _ghosts.RemoveAll(gone.Contains);
        GhostList.ItemsSource = _ghosts.ToList();
        RemovePathsFromResult(gone.Select(g => g.Path));
        LoadDrives();
        StatusText.Text = $"{gone.Count} reste{(gone.Count > 1 ? "s" : "")} de jeux supprimé{(gone.Count > 1 ? "s" : "")} · {ByteSize.Format(gone.Sum(g => g.Size))} libérés.";
        GhostPanel.Visibility = _ghosts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateGhostButton();
    }

    private static List<(string Path, string Kind)> SteamExtras(GameInfo g)
    {
        var list = new List<(string, string)>();
        string? id = SteamAppId(g);
        string? apps = Path.GetDirectoryName(Path.GetDirectoryName(g.InstallPath)); // …\steamapps\common\Jeu → …\steamapps
        if (id == null || apps == null) return list;
        list.Add((Path.Combine(apps, "shadercache", id), "Cache de shaders Steam"));
        list.Add((Path.Combine(apps, "temp", id), "Fichiers temporaires de mise à jour"));
        list.Add((Path.Combine(apps, "downloading", id), "Mise à jour interrompue"));
        return list;
    }

    private async void GameClean_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } g) return;
        StatusText.Text = $"Recherche des fichiers jetables de {g.Name}…";
        var items = await Task.Run(() => GameCleanup.Find(g.InstallPath, g.Launcher == "Steam" ? SteamExtras(g.Game) : null));
        StatusText.Text = "Prêt.";
        if (items.Count == 0)
        {
            MessageBox.Show(this, $"Rien à nettoyer dans « {g.Name} » : pas de cache, de rapport de plantage ni de vieux journal.", "Nettoyer un jeu",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rows = items.Select(i => new GhostRow(i.Path, i.Size, i.Kind)).ToList();
        var chosen = PickDialog($"Nettoyer « {g.Name} »",
            "Caches, rapports de plantage et journaux : le jeu les recrée si besoin. Tes sauvegardes et tes réglages ne sont jamais proposés. La suppression est définitive.",
            rows, "Supprimer");
        if (chosen.Count == 0) return;
        var gone = await Task.Run(() => chosen.Where(c => DeletePath(c.Path)).ToList());
        RemovePathsFromResult(gone.Select(c => c.Path));
        LoadDrives();
        StatusText.Text = $"« {g.Name} » nettoyé · {ByteSize.Format(gone.Sum(c => c.Size))} libérés.";
        _ = ComputeGameSizes();
    }
}
