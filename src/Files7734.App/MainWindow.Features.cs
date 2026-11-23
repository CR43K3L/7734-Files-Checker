using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Files7734.App;

/// <summary>Menus Fichier / Options, raccourcis clavier, résumé du disque et recherche.</summary>
public partial class MainWindow
{
    private AppSettings _settings = new();
    private readonly Stack<FsNode> _childStack = new();
    private readonly Dictionary<(Key key, ModifierKeys mods), Action> _shortcuts = new();
    private readonly Dictionary<string, MenuItem> _optionItems = new();
    private bool _loaded;

    private void InitFeatures()
    {
        _settings = AppSettings.Load();
        ByteSize.Unit = _settings.Unit;
        BuildMenus();
        ApplyViewOptions();
        PreviewKeyDown += OnShortcutKey;
        Loaded += (_, _) => _loaded = true;
        TopList.SelectionChanged += (_, _) => { if (TopList.SelectedItem is FileRow f) Map.Selected = f.Node; };
        SearchList.SelectionChanged += (_, _) => { if (SearchList.SelectedItem is FileRow f) Map.Selected = f.Node; };
    }

    // ───────────── Construction des menus ─────────────

    private void BuildMenus()
    {
        MainMenu.Items.Clear();
        var file = TopMenu("_Fichier");
        Item(file, "Analyser", "F5", Rescan);
        Item(file, "Sélectionner un dossier…", "F3", () => FolderButton_Click(this, new RoutedEventArgs()));
        Item(file, "Sélectionner plusieurs dossiers…", "Alt+F3", SelectMultiple);
        Item(file, "Ouvrir un fichier CSV…", "Shift+F3", OpenCsv);
        Item(file, "Nouvelle fenêtre", "Ctrl+N", () => Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }));
        Sep(file);
        Item(file, "Réinitialiser l'arborescence", "F4", ResetTree);
        Item(file, "Tout développer", "Shift+F4", ExpandAll);
        Item(file, "Sélectionner le parent", "F6", SelectParent);
        Item(file, "Resélectionner l'enfant", "F7", SelectChild);
        Item(file, "Zoomer sur la carte", "F10", ZoomIn);
        Item(file, "Dézoomer la carte", "F11", ZoomOut);
        Item(file, "Enregistrer la carte en PNG…", "Ctrl+S", SaveMapPng);
        Sep(file);
        Item(file, "Ouvrir dans l'Explorateur", "Ctrl+E", () => WithNode(n => NativeMethods.ShowInExplorer(n.FullPath)));
        Item(file, "Invite de commandes ici", "Ctrl+P", OpenCommandPrompt);
        Item(file, "Copier le chemin", "Ctrl+Alt+C", () => WithNode(n => Clipboard.SetText(n.FullPath)));
        Sep(file);
        Item(file, "Copier", "Ctrl+C", () => WithNode(n => CopyToClipboard(n, cut: false)));
        Item(file, "Couper", "Ctrl+X", () => WithNode(n => CopyToClipboard(n, cut: true)));
        Item(file, "Envoyer à la corbeille", "Del", () => WithNode(n => Delete(n)));
        Item(file, "Supprimer définitivement", "Shift+Del", () => WithNode(n => Delete(n, permanent: true)));
        Item(file, "Renommer…", "F2", () => WithNode(Rename));
        Sep(file);
        Item(file, "Exporter en CSV…", "Ctrl+Alt+E", ExportCsv);
        Item(file, "Copier les infos (nom, taille…)", "Ctrl+Shift+C", () => WithNode(CopyInfo));
        Item(file, "Exporter les types de fichiers en CSV…", "Ctrl+Alt+F", ExportTypes);
        Sep(file);
        Item(file, "Quitter", "Alt+F4", () => { _quitting = true; Close(); }, registerShortcut: false);

        var opt = TopMenu("_Options");
        Check(opt, "theme:System", "Thème : comme Windows", null, () => SetTheme(ThemeMode.System));
        Check(opt, "theme:Light", "Thème : clair", null, () => SetTheme(ThemeMode.Light));
        Check(opt, "theme:Dark", "Thème : sombre", "Ctrl+T", () => SetTheme(_settings.Theme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark));
        Sep(opt);
        Check(opt, "unit:Auto", "Afficher les tailles automatiquement", null, () => SetUnit(SizeUnit.Auto));
        Check(opt, "unit:Bytes", "Afficher en octets", null, () => SetUnit(SizeUnit.Bytes));
        Check(opt, "unit:KB", "Afficher en Ko", null, () => SetUnit(SizeUnit.KB));
        Check(opt, "unit:MB", "Afficher en Mo", null, () => SetUnit(SizeUnit.MB));
        Check(opt, "unit:GB", "Afficher en Go", null, () => SetUnit(SizeUnit.GB));
        Check(opt, "unit:TB", "Afficher en To", null, () => SetUnit(SizeUnit.TB));
        Sep(opt);
        Item(opt, "Filtrer / rechercher…", "Ctrl+Shift+F", () => { SearchBox.Focus(); SearchBox.SelectAll(); });
        Sep(opt);
        Check(opt, "explorer", "Ajouter au clic droit de l'Explorateur", null, ToggleExplorer);
        Check(opt, "admin", "Exécuter en administrateur", null, RunAsAdmin);
        Check(opt, "autoscan", "Analyser dès qu'un disque est sélectionné", null, () => Toggle(s => s.AutoScanOnSelect = !s.AutoScanOnSelect));
        Sep(opt);
        Check(opt, "header", "Afficher le résumé du disque", "Ctrl+H", () => Toggle(s => s.ShowHeader = !s.ShowHeader));
        Check(opt, "types", "Afficher les types de fichiers", "F8", () => Toggle(s => s.ShowFileTypes = !s.ShowFileTypes));
        Check(opt, "map", "Afficher la carte", "F9", () => Toggle(s => s.ShowMap = !s.ShowMap));
        Check(opt, "allocated", "Carte : taille sur le disque", null, () => Toggle(s => s.MapUseAllocated = !s.MapUseAllocated));
        Check(opt, "free", "Carte : afficher l'espace libre", "Ctrl+Shift+S", () => Toggle(s => s.MapShowFreeSpace = !s.MapShowFreeSpace));
        Check(opt, "names", "Carte : afficher les noms", "Ctrl+Shift+N", () => Toggle(s => s.MapShowNames = !s.MapShowNames));
        Sep(opt);
        Check(opt, "monitor", "Surveiller l'espace disque en arrière-plan", null, ToggleMonitor);
        Check(opt, "startup", "Démarrer avec Windows (dans la zone de notification)", null, ToggleStartup);
        Sep(opt);
        Item(opt, "Rechercher les mises à jour", null, () => _ = CheckForUpdates(manual: true));
        Item(opt, "Page GitHub du projet", null, () => NativeMethods.Open($"https://github.com/{GitHubRepo}"));
        Sep(opt);
        Item(opt, "Réinitialiser les réglages", null, ResetSettings);
    }

    private MenuItem TopMenu(string header)
    {
        var m = new MenuItem { Header = header };
        MainMenu.Items.Add(m);
        return m;
    }

    private static void Sep(MenuItem parent) => parent.Items.Add(new Separator());

    private void Item(MenuItem parent, string header, string? gesture, Action action, bool registerShortcut = true)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => Safe(action);
        parent.Items.Add(item);
        if (gesture != null && registerShortcut) _shortcuts[ParseGesture(gesture)] = action;
    }

    private void Check(MenuItem parent, string key, string header, string? gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => Safe(action);
        parent.Items.Add(item);
        _optionItems[key] = item;
        if (gesture != null) _shortcuts[ParseGesture(gesture)] = action;
    }

    private static (Key, ModifierKeys) ParseGesture(string g)
    {
        var mods = ModifierKeys.None;
        var parts = g.Split('+');
        foreach (var p in parts[..^1])
        {
            mods |= p switch
            {
                "Ctrl" => ModifierKeys.Control,
                "Alt" => ModifierKeys.Alt,
                "Shift" => ModifierKeys.Shift,
                _ => ModifierKeys.None,
            };
        }
        string k = parts[^1];
        var key = k == "Del" ? Key.Delete : Enum.Parse<Key>(k);
        return (key, mods);
    }

    private void OnShortcutKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return; // on laisse le champ de recherche gérer ses touches
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_shortcuts.TryGetValue((key, Keyboard.Modifiers), out var action))
        {
            e.Handled = true;
            Safe(action);
        }
    }

    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // ───────────── Élément sélectionné ─────────────

    /// <summary>L'élément sur lequel portent les actions : celui sélectionné dans la liste active.</summary>
    private FsNode? SelectedNode
    {
        get
        {
            if (TopList.IsKeyboardFocusWithin && TopList.SelectedItem is FileRow t) return t.Node;
            if (SearchList.IsKeyboardFocusWithin && SearchList.SelectedItem is FileRow s) return s.Node;
            return (TreeList.SelectedItem as TreeRow)?.Node;
        }
    }

    private void WithNode(Action<FsNode> action)
    {
        var n = SelectedNode;
        if (n != null && !n.IsVirtual) action(n);
    }

    // ───────────── Fichier ─────────────

    private void Rescan()
    {
        if (_progressTimer.IsEnabled) return;
        if (_lastScan != null) _lastScan();
        else ScanButton_Click(this, new RoutedEventArgs());
    }

    private void SelectMultiple()
    {
        var dlg = new OpenFolderDialog { Title = "Choisis les dossiers à analyser", Multiselect = true };
        if (dlg.ShowDialog(this) != true || dlg.FolderNames.Length == 0) return;
        var paths = dlg.FolderNames;
        if (paths.Length == 1) { StartScan(paths[0]); return; }
        RunScan($"{paths.Length} dossiers", (status, ct) =>
        {
            var sw = Stopwatch.StartNew();
            var roots = new List<FsNode>();
            var method = ScanMethod.Mft;
            long errors = 0;
            foreach (var p in paths)
            {
                var r = DiskScanner.Scan(p, false, status, ct);
                roots.Add(r.Root);
                if (r.Method != ScanMethod.Mft) method = ScanMethod.DirectoryWalk;
                errors += r.Errors;
            }
            return new ScanResult { Root = FsTree.CreateGroup("Sélection", roots), Method = method, Elapsed = sw.Elapsed, Errors = errors };
        });
    }

    private void OpenCsv()
    {
        var dlg = new OpenFileDialog { Title = "Ouvrir une analyse exportée", Filter = "Fichiers CSV (*.csv)|*.csv" };
        if (dlg.ShowDialog(this) != true) return;
        string file = dlg.FileName;
        RunScan(Path.GetFileName(file), (status, ct) =>
        {
            var sw = Stopwatch.StartNew();
            using var reader = new StreamReader(file, Encoding.UTF8);
            var root = CsvExport.Read(reader);
            return new ScanResult { Root = root, Method = ScanMethod.DirectoryWalk, Elapsed = sw.Elapsed, FallbackReason = "Chargé depuis un fichier CSV." };
        });
    }

    private void ResetTree()
    {
        if (_rows.Count == 0) return;
        var root = _rows[0];
        root.IsExpanded = false;
        root.IsExpanded = true;
        TreeList.SelectedItem = root;
        TreeList.ScrollIntoView(root);
        _childStack.Clear();
    }

    private void ExpandAll()
    {
        if (TreeList.SelectedItem is not TreeRow row || !row.HasChildren) return;
        long count = row.Node.FileCount + row.Node.FolderCount;
        if (count > 200_000)
        {
            MessageBox.Show(this, $"Ce dossier contient {Fmt.Count(count)} éléments : c'est trop pour tout afficher d'un coup. Choisis un sous-dossier.",
                "Tout développer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.IsExpanded) row.IsExpanded = false;

        var list = new List<TreeRow>();
        var stack = new Stack<(FsNode node, int depth)>();
        for (int i = row.Node.Children!.Count - 1; i >= 0; i--) stack.Push((row.Node.Children[i], row.Depth + 1));
        while (stack.Count > 0)
        {
            var (n, d) = stack.Pop();
            var r = new TreeRow(n, d, OnRowToggled);
            list.Add(r);
            if (r.HasChildren)
            {
                r.SetExpandedSilently(true);
                for (int i = n.Children!.Count - 1; i >= 0; i--) stack.Push((n.Children[i], d + 1));
            }
        }
        row.SetExpandedSilently(true);
        _rows.InsertRange(_rows.IndexOf(row) + 1, list);
    }

    private void SelectParent()
    {
        var n = SelectedNode;
        if (n?.Parent == null || _result == null || !FsTree.IsInside(n.Parent, _result.Root)) return;
        _childStack.Push(n);
        RevealInTree(n.Parent, zoomMap: false);
    }

    private void SelectChild()
    {
        if (_childStack.Count > 0) RevealInTree(_childStack.Pop(), zoomMap: false);
    }

    private void ZoomIn()
    {
        var n = SelectedNode;
        if (n == null) return;
        var dir = n.IsDirectory ? n : n.Parent;
        if (dir != null) RevealInTree(dir, zoomMap: true);
    }

    private void ZoomOut() => UpButton_Click(this, new RoutedEventArgs());

    private void SaveMapPng()
    {
        if (Map.Root == null) return;
        var dlg = new SaveFileDialog { Title = "Enregistrer la carte", Filter = "Image PNG (*.png)|*.png", FileName = "carte-7734.png" };
        if (dlg.ShowDialog(this) != true) return;
        Map.SaveAsPng(dlg.FileName);
        StatusText.Text = $"Carte enregistrée dans {dlg.FileName}";
    }

    private void OpenCommandPrompt()
    {
        var n = SelectedNode;
        if (n == null) return;
        string dir = n.IsDirectory && !n.IsVirtual ? n.FullPath : n.Parent?.FullPath ?? n.FullPath;
        Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = dir, UseShellExecute = true });
    }

    private void CopyToClipboard(FsNode node, bool cut)
    {
        var files = new StringCollection { node.FullPath };
        var data = new DataObject();
        data.SetFileDropList(files);
        // 2 = déplacer (couper), 5 = copier : l'Explorateur saura quoi faire au moment de coller.
        data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { (byte)(cut ? 2 : 5), 0, 0, 0 }));
        Clipboard.SetDataObject(data, true);
        StatusText.Text = cut ? $"« {node.Name} » coupé : colle-le dans l'Explorateur pour le déplacer." : $"« {node.Name} » copié.";
    }

    private void CopyInfo(FsNode n)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Chemin\t{n.FullPath}");
        sb.AppendLine($"Taille\t{ByteSize.Format(n.Size)} ({Fmt.Count(n.Size)} octets)");
        sb.AppendLine($"Sur le disque\t{ByteSize.Format(n.Allocated)}");
        if (n.IsDirectory) sb.AppendLine($"Contenu\t{Fmt.Count(n.FileCount)} fichiers, {Fmt.Count(n.FolderCount)} dossiers");
        if (n.LastModifiedUtc != default) sb.AppendLine($"Modifié\t{Fmt.Date(n.LastModifiedUtc)}");
        Clipboard.SetText(sb.ToString());
        StatusText.Text = "Informations copiées.";
    }

    private void Rename(FsNode node)
    {
        if (node.Parent == null || node.IsVirtual) return;
        string? newName = RenameDialog.Ask(this, node.Name);
        if (newName == null || newName == node.Name) return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "Ce nom contient des caractères interdits (\\ / : * ? \" < > |).", "Renommer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string oldPath = node.FullPath;
        string newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
        if (node.IsDirectory) Directory.Move(oldPath, newPath); else File.Move(oldPath, newPath);
        FsTree.Rename(node, newName);
        foreach (var r in _rows) if (r.Node == node) r.Refresh();
        StatusText.Text = $"Renommé en « {newName} ».";
    }

    private void ExportCsv()
    {
        var n = SelectedNode is { IsDirectory: true } d ? d : _result?.Root;
        if (n == null) return;
        var dlg = new SaveFileDialog { Title = "Exporter en CSV", Filter = "Fichier CSV (*.csv)|*.csv", FileName = "analyse-7734.csv" };
        if (dlg.ShowDialog(this) != true) return;
        using (var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true)))
            CsvExport.Write(n, w);
        StatusText.Text = $"Analyse de {n.FullPath} exportée dans {dlg.FileName}";
    }

    private void ExportTypes()
    {
        if (_result == null) return;
        var dlg = new SaveFileDialog { Title = "Exporter les types de fichiers", Filter = "Fichier CSV (*.csv)|*.csv", FileName = "types-7734.csv" };
        if (dlg.ShowDialog(this) != true) return;
        using (var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true)))
            CsvExport.WriteExtensions(Analysis.ByExtension(_result.Root), _result.Root.Size, w);
        StatusText.Text = $"Types de fichiers exportés dans {dlg.FileName}";
    }

    // ───────────── Options ─────────────

    private void SetUnit(SizeUnit unit)
    {
        _settings.Unit = unit;
        ByteSize.Unit = unit;
        _settings.Save();
        RefreshAllTexts();
        ApplyViewOptions();
    }

    private void SetTheme(ThemeMode mode)
    {
        _settings.Theme = mode;
        _settings.Save();
        Theme.Apply(mode);
        RefreshAllTexts();
        ApplyViewOptions();
    }

    private void Toggle(Action<AppSettings> change)
    {
        change(_settings);
        _settings.Save();
        ApplyViewOptions();
    }

    private void ToggleExplorer()
    {
        if (ExplorerIntegration.IsEnabled())
        {
            ExplorerIntegration.Disable();
            StatusText.Text = "« Analyser avec 7734 Files Checker » retiré du clic droit de l'Explorateur.";
        }
        else
        {
            ExplorerIntegration.Enable();
            StatusText.Text = "« Analyser avec 7734 Files Checker » ajouté au clic droit des dossiers et des disques.";
        }
        ApplyViewOptions();
    }

    private void RunAsAdmin()
    {
        if (_elevated)
        {
            ApplyViewOptions();
            return;
        }
        if (NativeMethods.RestartElevated()) Application.Current.Shutdown();
        else ApplyViewOptions();
    }

    private void ResetSettings()
    {
        var answer = MessageBox.Show(this, "Remettre tous les réglages par défaut ?", "Réinitialiser", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        AppSettings.Delete();
        _settings = new AppSettings();
        ByteSize.Unit = _settings.Unit;
        Theme.Apply(_settings.Theme);
        RefreshAllTexts();
        ApplyViewOptions();
    }

    /// <summary>Applique les réglages à l'interface et met à jour les coches des menus.</summary>
    private void ApplyViewOptions()
    {
        foreach (var (key, item) in _optionItems)
        {
            item.IsChecked = key switch
            {
                "theme:System" => _settings.Theme == ThemeMode.System,
                "theme:Light" => _settings.Theme == ThemeMode.Light,
                "theme:Dark" => _settings.Theme == ThemeMode.Dark,
                "unit:Auto" => _settings.Unit == SizeUnit.Auto,
                "unit:Bytes" => _settings.Unit == SizeUnit.Bytes,
                "unit:KB" => _settings.Unit == SizeUnit.KB,
                "unit:MB" => _settings.Unit == SizeUnit.MB,
                "unit:GB" => _settings.Unit == SizeUnit.GB,
                "unit:TB" => _settings.Unit == SizeUnit.TB,
                "explorer" => SafeIsExplorerEnabled(),
                "admin" => _elevated,
                "autoscan" => _settings.AutoScanOnSelect,
                "header" => _settings.ShowHeader,
                "types" => _settings.ShowFileTypes,
                "map" => _settings.ShowMap,
                "allocated" => _settings.MapUseAllocated,
                "free" => _settings.MapShowFreeSpace,
                "names" => _settings.MapShowNames,
                "monitor" => _settings.MonitorDisks,
                "startup" => _settings.StartWithWindows,
                _ => false,
            };
        }

        DriveStats.Visibility = _settings.ShowHeader && _driveFree != null ? Visibility.Visible : Visibility.Collapsed;

        bool types = _settings.ShowFileTypes;
        SideCard.Visibility = SplitV.Visibility = types ? Visibility.Visible : Visibility.Collapsed;
        ColSide.MinWidth = types ? 260 : 0;
        ColSide.Width = types ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        ColSplit.Width = new GridLength(types ? 16 : 0);

        bool map = _settings.ShowMap;
        MapCard.Visibility = SplitH.Visibility = map ? Visibility.Visible : Visibility.Collapsed;
        RowMap.MinHeight = map ? 120 : 0;
        RowMap.Height = map ? new GridLength(4, GridUnitType.Star) : new GridLength(0);
        RowSplit.Height = new GridLength(map ? 16 : 0);

        Map.UseAllocated = _settings.MapUseAllocated;
        Map.ShowNames = _settings.MapShowNames;
        Map.FreeSpaceBytes = _settings.MapShowFreeSpace && _result != null && Map.Root == _result.Root ? _driveFree : null;
        Map.Render();
    }

    private static bool SafeIsExplorerEnabled()
    {
        try { return ExplorerIntegration.IsEnabled(); } catch (Exception) { return false; }
    }

    private void RefreshAllTexts()
    {
        foreach (var r in _rows) r.Refresh();
        if (_result != null)
        {
            var root = _result.Root;
            ExtList.ItemsSource = Analysis.ByExtension(root).Select(x => new ExtensionRow(x, root.Size)).ToList();
            TopList.ItemsSource = Analysis.LargestFiles(root, 1000).Select(f => new FileRow(f)).ToList();
            if (SearchList.ItemsSource is List<FileRow> found) SearchList.ItemsSource = found.Select(f => new FileRow(f.Node)).ToList();
            UpdateDriveStats(root);
            if (Map.Root != null) { MapTitle.Text = ByteSize.Format(Map.Root.Size); UpdateBreadcrumb(Map.Root); }
            RefreshPageTexts();
        }
        LoadDrives();
    }

    // ───────────── Résumé du disque ─────────────

    /// <summary>Total / utilisé / libre / réservé, quand on a analysé un disque entier.</summary>
    private void UpdateDriveStats(FsNode root)
    {
        _driveFree = null;
        try
        {
            string path = root.FullPath;
            string? driveRoot = root.IsVirtual ? null : Path.GetPathRoot(path);
            if (driveRoot != null && string.Equals(driveRoot.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                var d = new DriveInfo(driveRoot);
                long total = d.TotalSize, free = d.TotalFreeSpace, used = total - free;
                long reserved = Math.Max(0, used - root.Allocated);
                StatTotal.Text = ByteSize.Format(total);
                StatUsed.Text = ByteSize.Format(used);
                StatUsedPct.Text = $"  {Fmt.Percent(100.0 * used / total)}";
                StatFree.Text = ByteSize.Format(free);
                StatFreePct.Text = $"  {Fmt.Percent(100.0 * free / total)}";
                StatReserved.Text = ByteSize.Format(reserved);
                _driveFree = free;
            }
        }
        catch (Exception) { }
        ApplyViewOptions();
    }

    // ───────────── Recherche ─────────────

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            SearchTab.Visibility = Visibility.Collapsed;
            SearchList.ItemsSource = null;
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter || _result == null) return;
        e.Handled = true;
        string pattern = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(pattern)) return;

        var found = Search.Find(_result.Root, pattern);
        SearchList.ItemsSource = found.Select(n => new FileRow(n)).ToList();
        long total = found.Sum(n => n.IsDirectory ? 0 : n.Size);
        SearchTab.Header = $"Recherche ({Fmt.Count(found.Count)})";
        SearchTab.Visibility = Visibility.Visible;
        SearchTab.IsSelected = true;
        if (!_settings.ShowFileTypes) Toggle(s => s.ShowFileTypes = true);
        StatusText.Text = found.Count == 0
            ? $"Aucun résultat pour « {pattern} »."
            : $"{Fmt.Count(found.Count)} résultats pour « {pattern} » · {ByteSize.Format(total)} de fichiers";
    }
}

/// <summary>Petite fenêtre pour saisir un nouveau nom.</summary>
internal static class RenameDialog
{
    public static string? Ask(Window owner, string current)
    {
        var box = new TextBox { Text = current, Margin = new Thickness(0, 10, 0, 16), Padding = new Thickness(6, 5, 6, 5), FontSize = 13 };
        var ok = new Button { Content = "Renommer", IsDefault = true, Style = (Style)owner.FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, Style = (Style)owner.FindResource("PlainButton") };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Nouveau nom :", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "Renommer",
            Owner = owner,
            Content = panel,
            SizeToContent = SizeToContent.Height,
            Width = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontFamily = owner.FontFamily,
        };
        win.SetResourceReference(Control.BackgroundProperty, "CardBrush");
        win.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(Control.BackgroundProperty, "InputBrush");
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        win.SourceInitialized += (_, _) => WindowFrame.Apply(win);
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) =>
        {
            box.Focus();
            int dot = current.LastIndexOf('.');
            if (dot > 0) box.Select(0, dot); else box.SelectAll();
        };
        return win.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
