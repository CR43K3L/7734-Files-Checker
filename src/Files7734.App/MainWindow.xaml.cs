using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Files7734.App;

public partial class MainWindow : Window
{
    private readonly BulkObservableCollection<TreeRow> _rows = new();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly bool _elevated;
    private ScanResult? _result;
    private ScanStatus? _status;
    private CancellationTokenSource? _cts;
    private Stopwatch _scanClock = new();
    private bool _revealing;

    private sealed record DriveItem(string Root, string Label)
    {
        public override string ToString() => Label;
    }

    public MainWindow()
    {
        InitializeComponent();
        TreeList.ItemsSource = _rows;
        _elevated = NativeMethods.IsElevated();
        AdminBanner.Visibility = _elevated ? Visibility.Collapsed : Visibility.Visible;

        _progressTimer.Tick += (_, _) => UpdateProgress();
        Map.NodeClicked += node => RevealInTree(node, zoomMap: false);
        Map.NodeDoubleClicked += node =>
        {
            var dir = node.IsDirectory ? node : node.Parent;
            if (dir != null) RevealInTree(dir, zoomMap: true);
        };
        Map.NodeRightClicked += node =>
        {
            RevealInTree(node, zoomMap: false);
            var menu = BuildMenu(node, new ContextMenu());
            menu.PlacementTarget = Map;
            menu.IsOpen = true;
        };

        LoadDrives();

        // Un chemin passé en argument (ex. glisser un dossier sur l'exe) est analysé directement.
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && Directory.Exists(args[1]))
            Loaded += (_, _) => StartScan(args[1]);
    }

    // ───────────── Disques ─────────────

    private void LoadDrives()
    {
        var items = new List<DriveItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                long used = d.TotalSize - d.TotalFreeSpace;
                string name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Disque local" : d.VolumeLabel;
                items.Add(new DriveItem(d.RootDirectory.FullName,
                    $"{d.Name.TrimEnd('\\')}  {name} — {ByteSize.Format(used)} / {ByteSize.Format(d.TotalSize)} ({d.DriveFormat})"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        DriveBox.ItemsSource = items;
        DriveBox.SelectedIndex = Math.Max(0, items.FindIndex(i => i.Root.StartsWith("C", StringComparison.OrdinalIgnoreCase)));
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (DriveBox.SelectedItem is DriveItem d) StartScan(d.Root);
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choisis le dossier à analyser" };
        if (dlg.ShowDialog(this) == true) StartScan(dlg.FolderName);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (NativeMethods.RestartElevated()) Application.Current.Shutdown();
    }

    // ───────────── Scan ─────────────

    private async void StartScan(string path)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _status = new ScanStatus();
        var status = _status;

        // Libère l'ancien résultat avant d'en charger un nouveau.
        _result = null;
        _rows.ReplaceAll(Array.Empty<TreeRow>());
        ExtList.ItemsSource = null;
        TopList.ItemsSource = null;
        Map.Root = null;
        Map.Selected = null;
        MapTitle.Text = "Carte du disque";
        UpButton.IsEnabled = false;

        SetBusy(true);
        _scanClock = Stopwatch.StartNew();
        StatusText.Text = $"Analyse de {path}…";

        try
        {
            var result = await Task.Run(() => DiskScanner.Scan(path, false, status, ct), ct);
            ProgressText.Text = "Calcul des statistiques…";
            var root = result.Root;
            var (exts, top) = await Task.Run(() => (Analysis.ByExtension(root), Analysis.LargestFiles(root, 1000)), ct);

            _result = result;
            ExtensionColors.Assign(exts.Select(x => x.Extension));
            ExtList.ItemsSource = exts.Select(x => new ExtensionRow(x, root.Size)).ToList();
            TopList.ItemsSource = top.Select(f => new FileRow(f)).ToList();

            var rootRow = new TreeRow(root, 0, OnRowToggled);
            _rows.ReplaceAll(new[] { rootRow });
            rootRow.IsExpanded = true;
            TreeList.SelectedItem = rootRow;
            SetMapRoot(root);

            string method = result.Method == ScanMethod.Mft ? "lecture directe de la table NTFS" : "scan classique";
            string errors = result.Errors > 0 ? $" · {Fmt.Count(result.Errors)} dossiers inaccessibles ignorés" : "";
            StatusText.Text = $"{root.FullPath} analysé en {result.Elapsed.TotalSeconds.ToString("0.0", Fmt.Fr)} s ({method}) · " +
                              $"{ByteSize.Format(root.Size)} · {Fmt.Count(root.FileCount)} fichiers · {Fmt.Count(root.FolderCount)} dossiers{errors}";

            if (result.Method != ScanMethod.Mft && result.FallbackReason != null && _elevated)
            {
                AdminBannerText.Text = result.FallbackReason;
                AdminBanner.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan annulé.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Erreur pendant le scan : " + ex.Message;
        }
        finally
        {
            if (ct == _cts?.Token) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = FolderButton.IsEnabled = DriveBox.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) _progressTimer.Start();
        else
        {
            _progressTimer.Stop();
            ProgressText.Text = "";
        }
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var s = _status;
        if (s == null || !_progressTimer.IsEnabled) return;
        Progress.IsIndeterminate = s.Fraction == null;
        if (s.Fraction is double f) Progress.Value = f;
        string pct = s.Fraction is double ff ? $" {(ff * 100).ToString("0", Fmt.Fr)} %" : "";
        ProgressText.Text = $"{s.Phase}{pct} · {Fmt.Count(s.ItemsFound)} éléments · {_scanClock.Elapsed.TotalSeconds.ToString("0", Fmt.Fr)} s";
    }

    // ───────────── Arborescence ─────────────

    private void OnRowToggled(TreeRow row)
    {
        int index = _rows.IndexOf(row);
        if (index < 0) return;
        if (row.IsExpanded)
        {
            var children = row.Node.Children!.Select(c => new TreeRow(c, row.Depth + 1, OnRowToggled)).ToList();
            _rows.InsertRange(index + 1, children);
        }
        else
        {
            int end = index + 1;
            while (end < _rows.Count && _rows[end].Depth > row.Depth) end++;
            _rows.RemoveRange(index + 1, end - index - 1);
        }
    }

    private void TreeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_revealing || TreeList.SelectedItem is not TreeRow row) return;
        // Un dossier sélectionné dans la liste s'affiche dans la carte ; un fichier y est encadré.
        if (row.Node.IsDirectory) SetMapRoot(row.Node);
        else if (Map.Root == null || !FsTree.IsInside(row.Node, Map.Root)) SetMapRoot(row.Node.Parent ?? row.Node);
        Map.Selected = row.Node;
    }

    private void TreeList_KeyDown(object sender, KeyEventArgs e)
    {
        if (TreeList.SelectedItem is not TreeRow row) return;
        switch (e.Key)
        {
            case Key.Right when row.HasChildren && !row.IsExpanded:
                row.IsExpanded = true; e.Handled = true; break;
            case Key.Left when row.IsExpanded:
                row.IsExpanded = false; e.Handled = true; break;
            case Key.Left when row.Node.Parent != null:
                RevealInTree(row.Node.Parent, zoomMap: true); e.Handled = true; break;
            case Key.Delete:
                Delete(row.Node); e.Handled = true; break;
        }
    }

    private void TreeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TreeList.SelectedItem is TreeRow row && row.HasChildren) row.IsExpanded = !row.IsExpanded;
    }

    private void TopList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TopList.SelectedItem is FileRow f) RevealInTree(f.Node, zoomMap: true);
    }

    /// <summary>Déplie l'arborescence jusqu'à l'élément, le sélectionne et le fait défiler à l'écran.</summary>
    private void RevealInTree(FsNode node, bool zoomMap)
    {
        if (_rows.Count == 0) return;
        var rootNode = _rows[0].Node;
        if (!FsTree.IsInside(node, rootNode)) return;

        var chain = new List<FsNode>();
        for (var n = node; n != null && n != rootNode; n = n.Parent) chain.Add(n);
        chain.Reverse();

        _revealing = true;
        try
        {
            TreeRow current = _rows[0];
            foreach (var step in chain)
            {
                if (!current.IsExpanded) current.IsExpanded = true;
                int start = _rows.IndexOf(current);
                TreeRow? found = null;
                for (int i = start + 1; i < _rows.Count && _rows[i].Depth > current.Depth; i++)
                {
                    if (_rows[i].Depth == current.Depth + 1 && _rows[i].Node == step) { found = _rows[i]; break; }
                }
                if (found == null) break;
                current = found;
            }
            TreeList.SelectedItem = current;
            TreeList.ScrollIntoView(current);
        }
        finally
        {
            _revealing = false;
        }

        if (zoomMap && node.IsDirectory) SetMapRoot(node);
        else if (zoomMap && node.Parent != null && (Map.Root == null || !FsTree.IsInside(node, Map.Root))) SetMapRoot(node.Parent);
        Map.Selected = node;
    }

    // ───────────── Carte ─────────────

    private void SetMapRoot(FsNode dir)
    {
        Map.Root = dir;
        MapTitle.Text = $"{dir.FullPath}  ·  {ByteSize.Format(dir.Size)}";
        UpButton.IsEnabled = _result != null && dir != _result.Root && dir.Parent != null;
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = Map.Root?.Parent;
        if (parent != null && _result != null && FsTree.IsInside(parent, _result.Root)) RevealInTree(parent, zoomMap: true);
    }

    // ───────────── Actions (clic droit) ─────────────

    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var list = (ListView)sender;
        FsNode? node = list.SelectedItem switch
        {
            TreeRow r => r.Node,
            FileRow f => f.Node,
            _ => null,
        };
        if (node == null) { e.Handled = true; return; }
        BuildMenu(node, list.ContextMenu ??= new ContextMenu());
    }

    private ContextMenu BuildMenu(FsNode node, ContextMenu menu)
    {
        menu.Items.Clear();
        string path = node.FullPath;

        void Add(string glyph, string text, Action action, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = text,
                IsEnabled = enabled,
                Icon = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont") },
            };
            item.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            menu.Items.Add(item);
        }

        Add("", node.IsDirectory ? "Ouvrir le dossier" : "Ouvrir", () => NativeMethods.Open(path));
        Add("", "Afficher dans l'Explorateur", () => NativeMethods.ShowInExplorer(path));
        Add("", "Copier le chemin", () => Clipboard.SetText(path));
        if (node.IsDirectory) Add("", "Analyser ce dossier seul", () => StartScan(path));
        menu.Items.Add(new Separator());
        Add("", "Envoyer à la corbeille…", () => Delete(node), enabled: node.Parent != null);
        return menu;
    }

    private void Delete(FsNode node)
    {
        if (node.Parent == null || _result == null) return;
        string path = node.FullPath;
        string what = node.IsDirectory
            ? $"le dossier « {node.Name} » ({ByteSize.Format(node.Size)}, {Fmt.Count(node.FileCount)} fichiers)"
            : $"le fichier « {node.Name} » ({ByteSize.Format(node.Size)})";
        var answer = MessageBox.Show(this, $"Envoyer {what} à la corbeille ?\n\n{path}", "Envoyer à la corbeille",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        bool ok = NativeMethods.SendToRecycleBin(path, hwnd);
        bool gone = !File.Exists(path) && !Directory.Exists(path);
        if (!ok && !gone)
        {
            MessageBox.Show(this, "La suppression n'a pas pu être faite (fichier utilisé, droits insuffisants ou opération annulée).",
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parent = node.Parent;
        RemoveRows(node);
        FsTree.Remove(node);
        foreach (var r in _rows) r.Refresh();

        var root = _result.Root;
        var exts = Analysis.ByExtension(root);
        ExtList.ItemsSource = exts.Select(x => new ExtensionRow(x, root.Size)).ToList();
        TopList.ItemsSource = Analysis.LargestFiles(root, 1000).Select(f => new FileRow(f)).ToList();

        var mapRoot = Map.Root;
        if (mapRoot == null || !FsTree.IsInside(mapRoot, root)) SetMapRoot(parent);
        else SetMapRoot(mapRoot);
        Map.Render();
        RevealInTree(parent, zoomMap: false);
        StatusText.Text = $"« {node.Name} » envoyé à la corbeille · {ByteSize.Format(node.Size)} libérés.";
    }

    private void RemoveRows(FsNode node)
    {
        int index = -1;
        for (int i = 0; i < _rows.Count; i++) if (_rows[i].Node == node) { index = i; break; }
        if (index < 0) return;
        int end = index + 1;
        while (end < _rows.Count && _rows[end].Depth > _rows[index].Depth) end++;
        _rows.RemoveRange(index, end - index);
    }
}
