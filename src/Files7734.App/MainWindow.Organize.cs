using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Files7734.App;

/// <summary>Page « Ranger le bureau » : les raccourcis des programmes et des jeux rangés dans des dossiers au choix.</summary>
public partial class MainWindow
{
    private bool _organizeLoaded;
    private List<OrganizeRow> _organize = new();
    private Dictionary<string, string> _organizeIcons = new(StringComparer.OrdinalIgnoreCase);
    private string _organizeFilter = "all";
    private FileSystemWatcher? _desktopWatcher;

    private static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string PublicDesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private static string OrganizeData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7734FilesChecker", "bureau");
    private static string JournalFile => Path.Combine(OrganizeData, "dernier-rangement.json");

    // ───────────── Lecture des raccourcis ─────────────

    private sealed record Lnk(string Target, string Args, string? Icon);

    /// <summary>Cible d'un raccourci .lnk (composant Shell de Windows, à appeler depuis un thread STA).</summary>
    private static Lnk? ReadLnk(dynamic shell, string path)
    {
        try
        {
            var sc = shell.CreateShortcut(path);
            string target = sc.TargetPath ?? "";
            if (target.Length == 0) return null;
            string icon = sc.IconLocation ?? "";
            int comma = icon.LastIndexOf(',');
            if (comma > 0) icon = icon[..comma];
            icon = Environment.ExpandEnvironmentVariables(icon);
            return new Lnk(target, sc.Arguments ?? "", icon.Length > 0 && File.Exists(icon) ? icon : null);
        }
        catch (Exception) { return null; }
    }

    private static string KeyOf(string target, string args) => (target + " " + args).Trim().ToLowerInvariant();

    private static bool IsGameUrl(string url) =>
        url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("uplay://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("goggalaxy://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tous les programmes et jeux : raccourcis du bureau, du menu Démarrer, et jeux Steam détectés.</summary>
    private static (List<ShortcutItem> Items, Dictionary<string, string> Icons) CollectShortcuts()
    {
        var items = new Dictionary<string, ShortcutItem>(StringComparer.OrdinalIgnoreCase);
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<GameInfo> games;
        try { games = GameDetector.Detect(); } catch (Exception) { games = new(); }
        var gameDirs = games.Select(g => g.InstallPath.TrimEnd('\\') + "\\").ToList();
        bool InGame(string target) => gameDirs.Any(d => target.StartsWith(d, StringComparison.OrdinalIgnoreCase))
                                      || target.Contains(@"\FiveM\", StringComparison.OrdinalIgnoreCase);

        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) return (new(), icons);
        dynamic shell = Activator.CreateInstance(type)!;

        ShortcutItem? Read(string file, string? onDesktop, string? folder)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(file);
            if (ext == ".lnk")
            {
                var l = ReadLnk(shell, file);
                if (l == null) return null;
                if (onDesktop == null && (!l.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(l.Target) || DesktopOrganizer.IsJunkShortcut(name))) return null;
                string key = KeyOf(l.Target, l.Args);
                icons.TryAdd(key, l.Icon ?? l.Target);
                return new ShortcutItem { Key = key, Name = name, IsGame = InGame(l.Target), Source = file, OnDesktop = onDesktop, CurrentFolder = folder };
            }
            if (ext == ".url")
            {
                string? url;
                try { url = DesktopOrganizer.UrlOf(File.ReadAllText(file)); } catch (Exception) { return null; }
                if (url == null || (onDesktop == null && !IsGameUrl(url))) return null;
                try
                {
                    var iconLine = File.ReadAllLines(file).FirstOrDefault(x => x.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase));
                    if (iconLine != null && File.Exists(iconLine[9..])) icons.TryAdd(url.ToLowerInvariant(), iconLine[9..]);
                }
                catch (Exception) { }
                return new ShortcutItem { Key = url.ToLowerInvariant(), Name = name, IsGame = IsGameUrl(url), Source = file, OnDesktop = onDesktop, CurrentFolder = folder };
            }
            return null;
        }

        // 1. Ce qui est déjà sur le bureau (à la racine et dans les dossiers du bureau).
        foreach (var desk in new[] { DesktopDir, PublicDesktopDir }.Where(Directory.Exists))
        {
            foreach (var f in SafeEnum(() => Directory.EnumerateFiles(desk)))
                if (Read(f, f, "") is ShortcutItem it) items.TryAdd(it.Key, it);
            foreach (var d in SafeEnum(() => Directory.EnumerateDirectories(desk)))
                foreach (var f in SafeEnum(() => Directory.EnumerateFiles(d)))
                    if (Read(f, f, Path.GetFileName(d)) is ShortcutItem it) items.TryAdd(it.Key, it);
        }

        // 2. Tous les programmes du menu Démarrer.
        foreach (var start in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) }.Where(Directory.Exists))
            foreach (var f in SafeEnum(() => Directory.EnumerateFiles(start, "*.*", SearchOption.AllDirectories)))
                if (Read(f, null, null) is ShortcutItem it) items.TryAdd(it.Key, it);

        // 3. Jeux Steam qui n'ont pas de raccourci.
        foreach (var g in games.Where(g => g.UninstallUri?.StartsWith("steam://uninstall/") == true))
        {
            string url = "steam://rungameid/" + g.UninstallUri!["steam://uninstall/".Length..];
            if (items.ContainsKey(url)) continue;
            string? exe = null;
            try { exe = new DirectoryInfo(g.InstallPath).EnumerateFiles("*.exe").OrderByDescending(f => f.Length).FirstOrDefault()?.FullName; } catch (Exception) { }
            if (exe != null) icons.TryAdd(url, exe);
            items[url] = new ShortcutItem { Key = url, Name = g.Name, IsGame = true, UrlContent = DesktopOrganizer.UrlShortcut(url, exe) };
        }
        return (items.Values.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), icons);
    }

    private static IEnumerable<string> SafeEnum(Func<IEnumerable<string>> f)
    {
        try { return f().ToList(); } catch (Exception) { return Enumerable.Empty<string>(); }
    }

    private static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var t = new Thread(() => { try { tcs.SetResult(work()); } catch (Exception e) { tcs.SetException(e); } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    // ───────────── Page ─────────────

    private async void LoadOrganizer()
    {
        _organizeLoaded = true;
        OrganizeHint.Text = "Recherche des programmes et des jeux…";
        OrganizeApply.IsEnabled = false;
        OrganizeAuto.IsChecked = _settings.AutoSortDesktop;
        var (items, icons) = await RunSta(CollectShortcuts);
        _organizeIcons = icons;
        // Les dossiers du bureau qui contiennent déjà des raccourcis deviennent des dossiers de rangement.
        foreach (var f in items.Select(i => i.CurrentFolder).Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!_settings.DesktopFolders.Contains(f!, StringComparer.OrdinalIgnoreCase)) _settings.DesktopFolders.Add(f!);
        _organize = items.Select(i => new OrganizeRow(i, i.CurrentFolder, i.IsGame ? GamesFolder() : AppsFolder()) { Edited = UpdateOrganizeState }).ToList();
        BuildOrganizeFolders();
        BuildOrganizeFilter();
        ApplyOrganizeFilter();
        UpdateOrganizeState();
        _ = LoadOrganizeIcons();
    }

    private async Task LoadOrganizeIcons()
    {
        foreach (var row in _organize.ToList())
        {
            if (row.Icon != null || !_organizeIcons.TryGetValue(row.Item.Key, out var path)) continue;
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    using var ic = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (ic == null) return;
                    var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    row.Icon = src;
                }
                catch (Exception) { }
            }, DispatcherPriority.Background);
        }
    }

    private void UpdateOrganizeState()
    {
        int changed = _organize.Count(r => r.IsChanged);
        int onDesk = _organize.Count(r => r.Item.CurrentFolder != null);
        int loose = _organize.Count(r => r.Item.CurrentFolder == "");
        OrganizeApply.IsEnabled = changed > 0;
        OrganizeApply.Content = changed > 0 ? $"Ranger ({changed})" : "Ranger";
        OrganizeUndo.IsEnabled = File.Exists(JournalFile);
        OrganizeHint.Text = $"{_organize.Count} programmes et jeux trouvés, {onDesk} sur ton bureau dont {loose} en vrac. "
                          + "Coche ce que tu veux sur le bureau et choisis son dossier, puis clique sur Ranger. Seuls les raccourcis bougent : les programmes restent installés.";
        if (_organizeFilter == "changed") ApplyOrganizeFilter();
    }

    private void BuildOrganizeFilter()
    {
        OrganizeFilter.Children.Clear();
        foreach (var (key, label) in new[] { ("all", "Tout"), ("apps", "Applications"), ("games", "Jeux"), ("desk", "Sur le bureau"), ("changed", "Modifiés") })
        {
            var rb = new RadioButton { Content = label, GroupName = "orgfilter", Style = (Style)FindResource("Segment"), IsChecked = key == _organizeFilter };
            rb.Checked += (_, _) => { _organizeFilter = key; ApplyOrganizeFilter(); };
            OrganizeFilter.Children.Add(rb);
        }
    }

    private void ApplyOrganizeFilter()
    {
        OrganizeList.ItemsSource = _organizeFilter switch
        {
            "apps" => _organize.Where(r => !r.Item.IsGame).ToList(),
            "games" => _organize.Where(r => r.Item.IsGame).ToList(),
            "desk" => _organize.Where(r => r.OnDesktop).ToList(),
            "changed" => _organize.Where(r => r.IsChanged).ToList(),
            _ => _organize,
        };
    }

    private void BuildOrganizeFolders()
    {
        OrganizeFolders.Children.Clear();
        var label = new TextBlock { Text = "Mes dossiers :", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6), FontWeight = FontWeights.SemiBold };
        OrganizeFolders.Children.Add(label);
        foreach (var f in _settings.DesktopFolders.ToList())
        {
            var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 6, 4), Margin = new Thickness(0, 0, 8, 6), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BackgroundProperty, "ContentBrush");
            chip.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            row.Children.Add(icon);
            int count = _organize.Count(r => r.OnDesktop && string.Equals(r.Folder, f, StringComparison.OrdinalIgnoreCase));
            row.Children.Add(new TextBlock { Text = count > 0 ? $"{f} ({count})" : f, VerticalAlignment = VerticalAlignment.Center });
            var del = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 9, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource("PlainButton"), ToolTip = "Retirer ce dossier (ses raccourcis restent sur le bureau, en vrac)" };
            string folder = f;
            del.Click += (_, _) =>
            {
                _settings.DesktopFolders.RemoveAll(x => x.Equals(folder, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                foreach (var r in _organize.Where(r => string.Equals(r.Folder, folder, StringComparison.OrdinalIgnoreCase))) r.Folder = "";
                BuildOrganizeFolders();
            };
            row.Children.Add(del);
            chip.Child = row;
            OrganizeFolders.Children.Add(chip);
        }
        var add = new Button { Content = "+ Nouveau dossier", Style = (Style)FindResource("PlainButton"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 6) };
        add.Click += (_, _) => NewOrganizeFolder(null);
        OrganizeFolders.Children.Add(add);
        var auto = new Button { Content = "Tout ranger pour moi", Style = (Style)FindResource("PlainButton"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 6),
            ToolTip = "Propose : applications dans le premier dossier, jeux dans le dossier des jeux" };
        auto.Click += (_, _) => SuggestOrganize();
        OrganizeFolders.Children.Add(auto);
    }

    private string? NewOrganizeFolder(OrganizeRow? forRow)
    {
        string? name = RenameDialog.Ask(this, "", "Nouveau dossier", "Nom du dossier sur le bureau :", "Créer");
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = DesktopOrganizer.SafeName(name);
        if (!_settings.DesktopFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) _settings.DesktopFolders.Add(name);
        _settings.Save();
        if (forRow != null) forRow.Folder = name;
        BuildOrganizeFolders();
        return name;
    }

    private string GamesFolder() => _settings.DesktopFolders.FirstOrDefault(f => f.Contains("jeu", StringComparison.OrdinalIgnoreCase) || f.Contains("game", StringComparison.OrdinalIgnoreCase))
                                    ?? _settings.DesktopFolders.Skip(1).FirstOrDefault() ?? AppsFolder();
    private string AppsFolder() => _settings.DesktopFolders.FirstOrDefault(f => f.Contains("app", StringComparison.OrdinalIgnoreCase))
                                   ?? _settings.DesktopFolders.FirstOrDefault() ?? "";

    /// <summary>Range ce qui est en vrac sur le bureau : applications dans le dossier des applications, jeux dans celui des jeux.</summary>
    private void SuggestOrganize()
    {
        int n = 0;
        foreach (var r in _organize.Where(r => r.OnDesktop && r.Folder.Length == 0))
        {
            r.Folder = r.Item.IsGame ? GamesFolder() : AppsFolder();
            n++;
        }
        StatusText.Text = n > 0 ? $"{n} raccourci(s) en vrac placés dans tes dossiers. Vérifie puis clique sur Ranger." : "Rien n'est en vrac sur le bureau.";
        BuildOrganizeFolders();
    }

    private void OrganizeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: OrganizeRow row } b) return;
        var menu = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(string header, Action a, bool check = false)
        {
            var mi = new MenuItem { Header = header, IsChecked = check };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        foreach (var f in _settings.DesktopFolders)
        {
            string folder = f;
            Add(f, () => { row.Folder = folder; BuildOrganizeFolders(); }, string.Equals(row.Folder, f, StringComparison.OrdinalIgnoreCase));
        }
        Add("En vrac sur le bureau", () => { row.Folder = ""; BuildOrganizeFolders(); }, row.Folder.Length == 0);
        menu.Items.Add(new Separator());
        Add("Nouveau dossier…", () => NewOrganizeFolder(row));
        menu.IsOpen = true;
    }

    private void OrganizeApply_Click(object sender, RoutedEventArgs e)
    {
        var changed = _organize.Where(r => r.IsChanged).ToList();
        if (changed.Count == 0) return;
        int add = changed.Count(r => r.Item.CurrentFolder == null), remove = changed.Count(r => r.Wanted == null), move = changed.Count - add - remove;
        var parts = new List<string>();
        if (move > 0) parts.Add($"{move} raccourci(s) rangé(s)");
        if (add > 0) parts.Add($"{add} ajouté(s) au bureau");
        if (remove > 0) parts.Add($"{remove} retiré(s) du bureau (mis de côté, pas supprimés)");
        if (!Ask("Ranger le bureau ?\n\n" + string.Join("\n", parts.Select(p => "•  " + p)) + "\n\nTu pourras tout remettre comme avant avec « Annuler le dernier rangement ».")) return;

        var wanted = changed.ToDictionary(r => r.Item.Key, r => r.Wanted, StringComparer.OrdinalIgnoreCase);
        var ops = DesktopOrganizer.Plan(changed.Select(r => r.Item), wanted, DesktopDir, Path.Combine(OrganizeData, "raccourcis-retires"));
        var journal = DesktopOrganizer.Apply(ops, out var failed);
        if (journal.Done.Count > 0) journal.Save(JournalFile);

        foreach (var r in changed.Where(r => r.Wanted != null)) _settings.DesktopRules[r.Item.Key] = r.Wanted!;
        foreach (var r in changed.Where(r => r.Wanted == null)) _settings.DesktopRules.Remove(r.Item.Key);
        _settings.Save();

        StatusText.Text = $"Bureau rangé : {journal.Done.Count} changement(s).";
        if (failed.Count > 0)
        {
            bool publicDesk = failed.Any(f => f.From.StartsWith(PublicDesktopDir, StringComparison.OrdinalIgnoreCase));
            MessageBox.Show(this, $"{failed.Count} raccourci(s) n'ont pas pu être déplacés." + (publicDesk && !_elevated
                    ? "\n\nCertains raccourcis sont sur le bureau de tous les comptes : relance l'app en administrateur pour les ranger."
                    : "\n\nUn fichier est peut-être ouvert ou protégé."),
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        LoadOrganizer();
    }

    private void OrganizeUndo_Click(object sender, RoutedEventArgs e)
    {
        var journal = OrganizeJournal.Load(JournalFile);
        if (journal == null) return;
        if (!Ask($"Remettre le bureau comme avant le rangement du {journal.WhenUtc.ToLocalTime():d MMMM à HH:mm} ?")) return;
        int failed = DesktopOrganizer.Undo(journal);
        try { File.Delete(JournalFile); } catch (Exception) { }
        StatusText.Text = failed == 0 ? "Bureau remis comme avant." : $"Bureau remis comme avant, sauf {failed} raccourci(s) qui avaient été déplacés ou supprimés entre-temps.";
        LoadOrganizer();
    }

    // ───────────── Rangement automatique des nouveaux raccourcis ─────────────

    private void OrganizeAuto_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoSortDesktop = OrganizeAuto.IsChecked == true;
        _settings.Save();
        ApplyDesktopWatcher();
        StatusText.Text = _settings.AutoSortDesktop
            ? "Les nouveaux raccourcis du bureau seront rangés tout seuls tant que l'app est ouverte (ou dans la zone de notification)."
            : "Rangement automatique désactivé.";
    }

    private void InitOrganizer() => ApplyDesktopWatcher();

    private void ApplyDesktopWatcher()
    {
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        if (!_settings.AutoSortDesktop || !Directory.Exists(DesktopDir)) return;
        try
        {
            _desktopWatcher = new FileSystemWatcher(DesktopDir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
            _desktopWatcher.Created += (_, e) => OnNewDesktopFile(e.FullPath);
            _desktopWatcher.Renamed += (_, e) => OnNewDesktopFile(e.FullPath);
        }
        catch (Exception) { _desktopWatcher = null; }
    }

    private void OnNewDesktopFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".lnk" or ".url")) return;
        // On laisse l'installateur finir d'écrire le raccourci.
        Task.Delay(2000).ContinueWith(_ => RunSta(() =>
        {
            if (!File.Exists(path)) return (string?)null;
            string key;
            bool game;
            if (ext == ".lnk")
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                dynamic shell = Activator.CreateInstance(type)!;
                var l = ReadLnk(shell, path);
                if (l == null) return null;
                key = KeyOf(l.Target, l.Args);
                game = l.Target.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase) || l.Target.Contains(@"\FiveM\", StringComparison.OrdinalIgnoreCase)
                       || l.Target.Contains("Games", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                string? url = DesktopOrganizer.UrlOf(File.ReadAllText(path));
                if (url == null) return null;
                key = url.ToLowerInvariant();
                game = IsGameUrl(url);
            }
            return key + "\n" + (game ? "1" : "0");
        })).Unwrap().ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result is not string info) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var parts = info.Split('\n');
                string key = parts[0];
                bool game = parts[1] == "1";
                string folder = _settings.DesktopRules.TryGetValue(key, out var rule) ? rule : game ? GamesFolder() : AppsFolder();
                if (folder.Length == 0 || !File.Exists(path)) return;
                string dir = Path.Combine(DesktopDir, DesktopOrganizer.SafeName(folder));
                try
                {
                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, Path.GetFileName(path));
                    for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(path)} ({i}){Path.GetExtension(path)}");
                    File.Move(path, target);
                    _settings.DesktopRules[key] = folder;
                    _settings.Save();
                    StatusText.Text = $"Nouveau raccourci « {Path.GetFileNameWithoutExtension(path)} » rangé dans {folder}.";
                    _tray?.ShowBalloonTip(3000, "Raccourci rangé", $"« {Path.GetFileNameWithoutExtension(path)} » est dans le dossier {folder} du bureau.", System.Windows.Forms.ToolTipIcon.Info);
                    if (_page == "organize") LoadOrganizer();
                    else _organizeLoaded = false;
                }
                catch (Exception) { }
            }));
        });
    }
}
