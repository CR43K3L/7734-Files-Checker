using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Files7734.App;

/// <summary>Catégorie FiveM : installer les packs (graphiques, sons) et réparer FiveM (cache, plantages).</summary>
public partial class MainWindow
{
    private static string CatalogUrl => $"https://raw.githubusercontent.com/{GitHubRepo}/main/packs/packs.json";
    private static string FiveMData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7734FilesChecker", "fivem");
    private static string RecordFile => Path.Combine(FiveMData, "installed.json");
    private static string CatalogCache => Path.Combine(FiveMData, "packs.json");
    private static string FiveMApp => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app");

    private PackCatalog? _catalog;
    private string? _catalogError;
    private bool _packBusy;

    private void FiveMRefresh_Click(object sender, RoutedEventArgs e) { _catalog = null; LoadFiveM(); }
    private void FiveMTuto_Click(object sender, RoutedEventArgs e) => ShowPackTutorial(firstInstall: false, onDone: null);

    // ───────────── Dossiers ─────────────

    private static bool FiveMInstalled => Directory.Exists(FiveMApp);

    /// <summary>Dossier de GTA V (Legacy) : choisi à la main, sinon celui donné à FiveM, sinon celui trouvé chez les lanceurs.</summary>
    private string? GtaDir()
    {
        if (_settings.GtaPath is string p && Directory.Exists(p)) return p;
        try
        {
            string ini = Path.Combine(FiveMApp, "CitizenFX.ini");
            if (File.Exists(ini) && FiveMPacks.GamePathFromCitizenIni(File.ReadAllText(ini)) is string g && Directory.Exists(g)) return g;
        }
        catch (Exception) { }
        try
        {
            var game = GameDetector.Detect().FirstOrDefault(x =>
                (x.Name.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("GTAV", StringComparison.OrdinalIgnoreCase))
                && !x.Name.Contains("Enhanced", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(x.InstallPath, "GTA5.exe")));
            if (game != null) return game.InstallPath;
        }
        catch (Exception) { }
        return null;
    }

    private PackTargets Targets() => new(FiveMApp, GtaDir());

    private static List<Process> FiveMProcesses() =>
        Process.GetProcesses().Where(p => { try { return p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } }).ToList();

    /// <summary>FiveM doit être fermé avant de toucher à ses fichiers. Renvoie true s'il est fermé.</summary>
    private bool EnsureFiveMClosed()
    {
        var running = FiveMProcesses();
        if (running.Count == 0) return true;
        if (!Ask("FiveM est ouvert. Il faut le fermer avant de continuer.\n\nFermer FiveM maintenant ?")) return false;
        foreach (var p in running) { try { p.Kill(true); } catch (Exception) { } }
        Thread.Sleep(1500);
        return FiveMProcesses().Count == 0;
    }

    // ───────────── Page « Mes packs » ─────────────

    private async void LoadFiveM()
    {
        FiveMCards.Children.Clear();
        var targets = Targets();
        var record = PackRecord.Load(RecordFile);

        // État de l'installation
        string game = targets.Game ?? "introuvable";
        FiveMCards.Children.Add(InfoCard(FiveMInstalled ? "FiveM est installé" : "FiveM n'est pas installé", "",
            (FiveMInstalled ? $"Dossier de FiveM : {FiveMApp}" : "Installe FiveM depuis fivem.net puis lance-le une fois.") + $"\nDossier de GTA V : {game}",
            Pill(FiveMInstalled && targets.Game != null ? "Prêt" : "À régler", FiveMInstalled && targets.Game != null ? 0xFF32D74Bu : 0xFFFF9F0Au),
            ("Choisir le dossier de GTA V…", ChooseGtaDir, true, targets.Game == null),
            ("Ouvrir FiveM.app", () => NativeMethods.Open(FiveMApp), FiveMInstalled, false)));

        if (record != null)
            FiveMCards.Children.Add(InfoCard($"Installé : {record.Name}", "",
                $"Version {record.Version}, installé le {record.InstalledUtc.ToLocalTime():d MMMM yyyy}. {record.Files.Count} fichier(s) posés par l'app : elle sait les retirer proprement.",
                Pill("Installé", 0xFF0A84FFu),
                ("Retirer ce pack", () => _ = UninstallPack(), !_packBusy, false)));

        FiveMCards.Children.Add(InfoCard("QuantV", "",
            (record?.QuantV is string qv ? $"Version du {qv} installée par l'app. " : "") +
            "Quand QuantV affiche « mise à jour disponible » en jeu : télécharge la mise à jour FiveM sur ton compte QuantV, puis glisse le .zip sur cette page (ou clique sur Importer). L'app remplace les anciens fichiers toute seule.",
            Pill("Mise à jour", 0xFFAF52DEu),
            ("Importer une mise à jour…", PickQuantVZip, FiveMInstalled && !_packBusy, false)));

        if (_catalog == null)
        {
            FiveMHint.Text = "Chargement des packs…";
            (_catalog, _catalogError) = await FetchCatalog();
            if (_page != "fivem") return;
            LoadFiveM();
            return;
        }

        FiveMHint.Text = _catalog.Packs.Count == 0
            ? (_catalogError ?? "Aucun pack n'est disponible pour le moment.")
            : "Clique sur Installer : l'app télécharge le pack, retire l'ancien, met chaque fichier à sa place et vide le cache de FiveM.";

        foreach (var pack in _catalog.Packs)
        {
            bool installed = record?.Id == pack.Id;
            bool update = installed && FiveMPacks.IsNewer(pack.Version, record!.Version);
            string action = update ? "Mettre à jour" : installed ? "Réinstaller" : "Installer";
            UIElement badge = update ? Pill("Nouvelle version", 0xFFFF9F0Au) : installed ? Pill("Installé", 0xFF32D74Bu) : Pill(pack.Kind.Length > 0 ? pack.Kind : "Pack", 0xFF8E8E93u);
            string size = pack.Size > 0 ? " · " + ByteSize.Format(pack.Size) : "";
            var p = pack;
            FiveMCards.Children.Add(InfoCard(pack.Name, "", $"{pack.Description}\nVersion {pack.Version}{size}".Trim(), badge,
                (action, () => _ = InstallPack(p), FiveMInstalled && !_packBusy, !installed || update)));
        }
        Nav("fivem").Badge = record != null && _catalog.Packs.Any(p => p.Id == record.Id && FiveMPacks.IsNewer(p.Version, record.Version)) ? "1" : "";
    }

    private async Task<(PackCatalog, string?)> FetchCatalog()
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(15));
            string json = await http.GetStringAsync(CatalogUrl + "?t=" + DateTime.UtcNow.Ticks);
            var c = PackCatalog.Parse(json);
            try { Directory.CreateDirectory(FiveMData); File.WriteAllText(CatalogCache, json); } catch (Exception) { }
            return (c, null);
        }
        catch (Exception)
        {
            try { if (File.Exists(CatalogCache)) return (PackCatalog.Parse(File.ReadAllText(CatalogCache)), "Pas de connexion : voici la dernière liste connue."); }
            catch (Exception) { }
            return (new PackCatalog(), "Impossible de charger la liste des packs. Vérifie ta connexion puis clique sur Actualiser.");
        }
    }

    private void ChooseGtaDir()
    {
        using var dlg = new Forms.FolderBrowserDialog { Description = "Choisis le dossier de GTA V (celui qui contient GTA5.exe)", UseDescriptionForTitle = true };
        if (GtaDir() is string cur) dlg.SelectedPath = cur;
        if (dlg.ShowDialog() != Forms.DialogResult.OK) return;
        if (!File.Exists(Path.Combine(dlg.SelectedPath, "GTA5.exe")))
        {
            MessageBox.Show(this, "Ce dossier ne contient pas GTA5.exe. Choisis le dossier principal du jeu.", "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.GtaPath = dlg.SelectedPath;
        _settings.Save();
        LoadFiveM();
    }

    // ───────────── Installation ─────────────

    private async Task InstallPack(PackInfo pack)
    {
        if (_packBusy || !FiveMInstalled) return;
        var targets = Targets();
        var record = PackRecord.Load(RecordFile);

        // Premier pack installé par l'app : on fait d'abord retirer l'ancien pack posé à la main.
        if (record == null && !_settings.FiveMOldPackCleared)
        {
            bool go = false;
            ShowPackTutorial(firstInstall: true, onDone: () => go = true);
            if (!go) return;
            _settings.FiveMOldPackCleared = true;
            _settings.Save();
        }
        else if (!Ask(record == null
                     ? $"Installer « {pack.Name} » ?\n\nFiveM sera fermé s'il est ouvert, puis son cache sera vidé."
                     : $"Installer « {pack.Name} » ?\n\nL'app retire d'abord « {record.Name} » (tous ses fichiers, et remet les fichiers d'origine du jeu), puis installe le nouveau pack et vide le cache de FiveM."))
            return;
        if (!EnsureFiveMClosed()) return;

        if (!EnoughSpaceFor(pack)) return;

        _packBusy = true;
        var (win, status, bar, closeWin) = ProgressDialog($"Installation de « {pack.Name} »");
        win.Show();
        string zip = Path.Combine(Path.GetTempPath(), $"7734-pack-{pack.Id}.zip");
        try
        {
            status.Text = "Téléchargement du pack…";
            var clock = Stopwatch.StartNew();
            var samples = new Queue<(double T, long Bytes)>();
            await DownloadPack(pack, zip, (done, total) =>
            {
                bar.IsIndeterminate = total <= 0;
                if (total > 0) bar.Value = (double)done / total;
                // Vitesse moyenne sur les 3 dernières secondes, et temps restant.
                double now = clock.Elapsed.TotalSeconds;
                samples.Enqueue((now, done));
                while (samples.Count > 2 && now - samples.Peek().T > 3) samples.Dequeue();
                var first = samples.Peek();
                double speed = now - first.T > 0.3 ? (done - first.Bytes) / (now - first.T) : 0;
                string rate = speed > 0 ? $" · {ByteSize.Format((long)speed)}/s" : "";
                string left = speed > 0 && total > done ? " · " + Remaining((total - done) / speed) : "";
                status.Text = total > 0
                    ? $"Téléchargement… {ByteSize.Format(done)} sur {ByteSize.Format(total)}{rate}{left}"
                    : $"Téléchargement… {ByteSize.Format(done)}{rate}";
            });

            if (record != null)
            {
                status.Text = $"Retrait de « {record.Name} »…";
                bar.IsIndeterminate = true;
                var failed = await Task.Run(() => FiveMPacks.Uninstall(record));
                if (failed.Count > 0) throw new IOException($"{failed.Count} fichier(s) de l'ancien pack n'ont pas pu être retirés (FiveM ou GTA est peut-être encore ouvert).");
                File.Delete(RecordFile);
            }

            status.Text = "Installation des fichiers…";
            bar.IsIndeterminate = false;
            var progress = new Progress<PackProgress>(p => { bar.Value = p.Fraction; status.Text = "Installation… " + p.Step; });
            string backup = Path.Combine(FiveMData, "sauvegarde-packs", pack.Id);
            var newRecord = await Task.Run(() => FiveMPacks.Install(zip, pack, targets, backup, progress));
            newRecord.Save(RecordFile);

            status.Text = "Vidage du cache de FiveM…";
            await Task.Run(ClearFiveMCache);
            closeWin();
            MessageBox.Show(this, $"« {pack.Name} » est installé ! Lance FiveM pour en profiter.\n\nPour revenir en arrière : FiveM > Mes packs > Retirer ce pack.",
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"Pack « {pack.Name} » installé ({newRecord.Files.Count} fichiers).";
        }
        catch (Exception ex)
        {
            closeWin();
            MessageBox.Show(this, "L'installation n'a pas abouti : " + ex.Message + "\n\nRien n'a été laissé à moitié : les fichiers posés ont été retirés.",
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try { File.Delete(zip); } catch (Exception) { }
            _packBusy = false;
            if (_page == "fivem") LoadFiveM();
        }
    }

    /// <summary>Le zip téléchargé puis ses fichiers décompressés : il faut environ deux fois la taille du pack.</summary>
    private bool EnoughSpaceFor(PackInfo pack)
    {
        if (pack.Size <= 0) return true;
        try
        {
            var temp = new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!);
            var game = new DriveInfo(Path.GetPathRoot(FiveMApp)!);
            long needTemp = pack.Size + (512L << 20);
            long needGame = pack.Size + (512L << 20);
            bool same = temp.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase);
            string? problem = same
                ? (temp.AvailableFreeSpace < needTemp + needGame ? $"Il faut environ {ByteSize.Format(needTemp + needGame)} de libre sur {temp.Name.TrimEnd('\\')} (le pack téléchargé, puis ses fichiers). Il reste {ByteSize.Format(temp.AvailableFreeSpace)}." : null)
                : temp.AvailableFreeSpace < needTemp ? $"Il faut environ {ByteSize.Format(needTemp)} de libre sur {temp.Name.TrimEnd('\\')} pour le téléchargement. Il reste {ByteSize.Format(temp.AvailableFreeSpace)}."
                : game.AvailableFreeSpace < needGame ? $"Il faut environ {ByteSize.Format(needGame)} de libre sur {game.Name.TrimEnd('\\')} pour le pack. Il reste {ByteSize.Format(game.AvailableFreeSpace)}." : null;
            if (problem == null) return true;
            if (MessageBox.Show(this, $"Pas assez de place pour « {pack.Name} ».\n\n{problem}\n\nOuvrir « Faire de la place » ?", "7734 Files Checker",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ShowPage("space");
            return false;
        }
        catch (Exception) { return true; }
    }

    private static string Remaining(double seconds) => seconds switch
    {
        < 60 => $"encore {Math.Max(1, (int)seconds)} s",
        < 3600 => $"encore {(int)(seconds / 60)} min {(int)(seconds % 60):00} s",
        _ => $"encore {(int)(seconds / 3600)} h {(int)(seconds % 3600 / 60):00}",
    };

    private async Task UninstallPack()
    {
        var record = PackRecord.Load(RecordFile);
        if (record == null || _packBusy) return;
        if (!Ask($"Retirer « {record.Name} » ?\n\nTous ses fichiers seront supprimés et les fichiers d'origine de GTA V remis en place.")) return;
        if (!EnsureFiveMClosed()) return;
        _packBusy = true;
        try
        {
            var failed = await Task.Run(() => FiveMPacks.Uninstall(record));
            if (failed.Count == 0)
            {
                File.Delete(RecordFile);
                await Task.Run(ClearFiveMCache);
                StatusText.Text = $"« {record.Name} » a été retiré.";
            }
            else MessageBox.Show(this, $"{failed.Count} fichier(s) n'ont pas pu être retirés. Ferme FiveM et GTA V puis réessaie.", "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _packBusy = false; LoadFiveM(); }
    }

    private async Task DownloadPack(PackInfo pack, string file, Action<long, long> progress)
    {
        using var http = NewClient(TimeSpan.FromHours(2));
        string url = FiveMPacks.DownloadUrl(pack.Url);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType == "text/html")
            {
                // Page de confirmation de Drive (fichier trop gros pour l'antivirus de Google) : on suit son formulaire.
                string html = await response.Content.ReadAsStringAsync();
                url = FiveMPacks.ConfirmUrlFromHtml(html)
                      ?? throw new IOException("Google Drive refuse le téléchargement. Le lien doit être partagé en « Tout le monde avec le lien ».");
                continue;
            }
            long total = response.Content.Headers.ContentLength ?? pack.Size;
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(file);
            var buffer = new byte[1 << 20];
            long done = 0;
            int n;
            var last = DateTime.MinValue;
            while ((n = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n));
                done += n;
                if ((DateTime.UtcNow - last).TotalMilliseconds > 100) { progress(done, total); last = DateTime.UtcNow; }
            }
            progress(done, total);
            return;
        }
        throw new IOException("Google Drive refuse le téléchargement (trop de téléchargements aujourd'hui ?). Réessaie plus tard.");
    }

    /// <summary>Fenêtre de progression que l'utilisateur ne peut pas fermer ; <c>close</c> la ferme depuis le code.</summary>
    private (Window Win, TextBlock Status, ProgressBar Bar, Action Close) ProgressDialog(string title)
    {
        var win = NewDialog(title, 460);
        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        root.Children.Add(Header("\uE896", title, "Ne ferme pas l'app pendant l'installation."));
        var status = new TextBlock { Margin = new Thickness(0, 16, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 8, 0, 0), IsIndeterminate = true };
        root.Children.Add(status);
        root.Children.Add(bar);
        win.Content = root;
        bool allow = false;
        win.Closing += (_, e) => { if (!allow) e.Cancel = true; };
        return (win, status, bar, () => { allow = true; win.Close(); });
    }

    // ───────────── QuantV ─────────────

    private void FiveMPage_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && files[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            _ = ImportQuantV(files[0]);
    }

    private void PickQuantVZip()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Mise à jour QuantV (*.zip)|*.zip", Title = "Choisis le .zip de la mise à jour QuantV FiveM" };
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) dlg.InitialDirectory = downloads;
        if (dlg.ShowDialog(this) == true) _ = ImportQuantV(dlg.FileName);
    }

    private static string? GpuName()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            return searcher.Get().Cast<System.Management.ManagementObject>().Select(o => o["Name"] as string)
                .Where(n => n != null).OrderByDescending(n => n!.Contains("NVIDIA") || n.Contains("AMD") || n.Contains("Radeon")).FirstOrDefault();
        }
        catch (Exception) { return null; }
    }

    /// <summary>Vieilles cartes (GTX 10xx et avant, Radeon RX 500 et avant) : QuantV conseille « NUI in-process GPU off ».</summary>
    private static bool IsOldGpu(string? name) => name != null && System.Text.RegularExpressions.Regex.IsMatch(name,
        @"GTX\s*(10[0-9]{2}|9[0-9]{2}|7[0-9]{2}|6[0-9]{2})|RX\s*[45][0-9]{2}\b|R9\s|R7\s|HD\s*\d{4}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Demande l'option « NUI in-process GPU ». Renvoie null si annulé.</summary>
    private bool? AskGpuOption()
    {
        string? gpu = GpuName();
        bool old = IsOldGpu(gpu);
        var win = NewDialog("Option QuantV", 520);
        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        root.Children.Add(Header("\uE7F4", "Quelle option pour ta carte graphique ?",
            (gpu != null ? $"Ta carte : {gpu}. " : "") + (old
                ? "C'est une carte plus ancienne : QuantV conseille « GPU off »."
                : "« GPU on » est l'option conseillée par QuantV pour les cartes récentes.")));
        bool? result = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, Style = (Style)FindResource("PlainButton") };
        var off = new Button { Content = "GPU off (anciennes cartes)" + (old ? " · conseillé" : ""), Style = (Style)FindResource(old ? "AccentButton" : "PlainButton"), Margin = new Thickness(8, 0, 0, 0), IsDefault = old };
        var on = new Button { Content = "GPU on" + (old ? "" : " · conseillé"), Style = (Style)FindResource(old ? "PlainButton" : "AccentButton"), Margin = new Thickness(8, 0, 0, 0), IsDefault = !old };
        cancel.Click += (_, _) => win.Close();
        off.Click += (_, _) => { result = false; win.Close(); };
        on.Click += (_, _) => { result = true; win.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(off);
        buttons.Children.Add(on);
        root.Children.Add(buttons);
        win.Content = root;
        win.ShowDialog();
        return result;
    }

    private async Task ImportQuantV(string zipPath)
    {
        if (_packBusy) return;
        if (!FiveMInstalled) { MessageBox.Show(this, "FiveM n'est pas installé sur ce PC.", "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        List<string> entries;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            entries = zip.Entries.Select(e => e.FullName).ToList();
        }
        catch (Exception)
        {
            MessageBox.Show(this, "Ce fichier n'est pas un .zip valide (téléchargement incomplet ?).", "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var probe = FiveMPacks.QuantVLayout(entries, Path.GetFileName(zipPath), null);
        if (probe.Files.Count == 0)
        {
            MessageBox.Show(this, "Ce zip ne ressemble pas à une mise à jour QuantV pour FiveM (il doit contenir Part1\\FiveM_App_data). Vérifie que tu as pris la version FiveM.",
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        bool? gpuOn = null;
        if (probe.GpuOptions.Count > 1)
        {
            gpuOn = AskGpuOption();
            if (gpuOn == null) return;
        }
        var plan = FiveMPacks.QuantVLayout(entries, Path.GetFileName(zipPath), gpuOn);
        if (!EnsureFiveMClosed()) return;

        _packBusy = true;
        var (win, status, bar, closeWin) = ProgressDialog("Mise à jour de QuantV");
        win.Show();
        try
        {
            var record = PackRecord.Load(RecordFile) ?? new PackRecord { Id = "quantv", Name = "QuantV", Version = plan.Date ?? "", InstalledUtc = DateTime.UtcNow };
            bar.IsIndeterminate = false;
            var progress = new Progress<PackProgress>(p => { bar.Value = p.Fraction; status.Text = "Copie… " + p.Step; });
            var targets = Targets();
            await Task.Run(() => FiveMPacks.Merge(zipPath, plan.Files, targets, record, Path.Combine(FiveMData, "sauvegarde-packs", record.Id), progress));
            record.QuantV = plan.Date ?? DateTime.Now.ToString("yyyy-MM-dd");
            record.Save(RecordFile);
            status.Text = "Vidage du cache de FiveM…";
            bar.IsIndeterminate = true;
            await Task.Run(ClearFiveMCache);
            closeWin();
            MessageBox.Show(this, $"QuantV est à jour{(plan.Date != null ? $" (version du {plan.Date})" : "")} ! Le message de mise à jour ne s'affichera plus en jeu.",
                "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = $"QuantV mis à jour ({plan.Files.Count} fichiers).";
        }
        catch (Exception ex)
        {
            closeWin();
            MessageBox.Show(this, "La mise à jour n'a pas abouti : " + ex.Message + "\n\nTout a été remis comme avant.", "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _packBusy = false;
            if (_page == "fivem") LoadFiveM();
        }
    }

    // ───────────── Tuto ─────────────

    private void ShowPackTutorial(bool firstInstall, Action? onDone)
    {
        var targets = Targets();
        var win = NewDialog(firstInstall ? "Avant ton premier pack" : "Comment ça marche ?", 600);
        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        root.Children.Add(Header("", firstInstall ? "Avant ton premier pack" : "Installer un pack en un clic",
            firstInstall
                ? "Si tu as déjà installé un pack à la main, il faut le retirer d'abord, sinon les deux vont se mélanger. Ensuite, l'app s'occupe de tout."
                : "L'app pose chaque fichier au bon endroit et retient tout ce qu'elle installe."));

        string md = (firstInstall ? "" : "# Ce que fait l'app\n" +
                     "- Elle télécharge le pack et ferme FiveM s'il est ouvert.\n" +
                     "- Elle retire le pack précédent installé par l'app, fichier par fichier, et remet les fichiers d'origine du jeu.\n" +
                     "- Elle copie les fichiers dans **mods**, **plugins** et le dossier de **GTA V**, puis vide le cache de FiveM.\n" +
                     "- Tu peux revenir en arrière à tout moment avec **Retirer ce pack**.\n\n") +
                    "# Retirer un ancien pack installé à la main\n" +
                    "- Ferme FiveM.\n" +
                    "- Ouvre le dossier **mods** de FiveM et supprime ce que ton ancien pack y avait mis.\n" +
                    "- Fais pareil dans **plugins**.\n" +
                    "- Dans le dossier de **GTA V**, supprime les fichiers de l'ENB (d3d11.dll, enbseries.ini, enblocal.ini, dossier enbseries…) ou du ReShade (dxgi.dll, ReShade.ini, reshade-shaders).\n" +
                    "- Si ton ancien pack remplaçait des sons du jeu, lance une **vérification des fichiers** de GTA V dans ton lanceur (Steam, Epic ou Rockstar) : ça remet les sons d'origine.\n\n" +
                    "Tu peux aussi cliquer sur **Ranger automatiquement** : l'app met tout ça de côté dans un dossier, rien n'est supprimé.";
        var notes = NotesBox(md);
        root.Children.Add(new Border { Margin = new Thickness(0, 14, 0, 0), Child = notes });

        var found = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontWeight = FontWeights.SemiBold };
        root.Children.Add(found);

        var folders = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void FolderButton(string text, string? path)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("PlainButton"), Margin = new Thickness(0, 0, 8, 6), IsEnabled = path != null };
            b.Click += (_, _) => Safe(() => { Directory.CreateDirectory(path!); NativeMethods.Open(path!); });
            folders.Children.Add(b);
        }
        FolderButton("Ouvrir mods", targets.Root(PackTarget.Mods));
        FolderButton("Ouvrir plugins", targets.Root(PackTarget.Plugins));
        FolderButton("Ouvrir le dossier de GTA V", targets.Game);
        var tidy = new Button { Style = (Style)FindResource("PlainButton"), Margin = new Thickness(0, 0, 8, 6) };
        folders.Children.Add(tidy);
        root.Children.Add(folders);

        void Refresh()
        {
            var items = FiveMInstalled ? FiveMPacks.ManualPackItems(targets) : new List<string>();
            found.Text = items.Count == 0
                ? "✓ Aucun ancien pack détecté dans mods, plugins ou le dossier de GTA V."
                : $"{items.Count} élément(s) d'un ancien pack trouvés : " + string.Join(", ", items.Take(6).Select(Path.GetFileName)) + (items.Count > 6 ? "…" : "");
            found.Foreground = ExtensionColors.BrushOf(items.Count == 0 ? 0xFF32D74Bu : 0xFFFF9F0Au);
            tidy.Content = $"Ranger automatiquement ({items.Count})";
            tidy.IsEnabled = items.Count > 0;
        }
        tidy.Click += (_, _) =>
        {
            if (!EnsureFiveMClosed()) return;
            string dest = Path.Combine(FiveMData, "ancien-pack-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm"));
            var failed = FiveMPacks.MoveAside(FiveMPacks.ManualPackItems(targets), targets, dest);
            Refresh();
            if (failed.Count > 0)
                MessageBox.Show(win, $"{failed.Count} élément(s) n'ont pas pu être déplacés (fichier ouvert ?). Ferme FiveM et GTA V puis réessaie.", "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Warning);
            else StatusText.Text = $"Ancien pack rangé dans {dest}.";
        };
        Refresh();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        if (firstInstall)
        {
            var cancel = new Button { Content = "Annuler", IsCancel = true, Style = (Style)FindResource("PlainButton"), Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += (_, _) => win.Close();
            var ok = new Button { Content = "C'est fait, installer le pack", IsDefault = true, Style = (Style)FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (_, _) => { onDone?.Invoke(); win.Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
        }
        else
        {
            var close = new Button { Content = "Compris", IsDefault = true, IsCancel = true, Style = (Style)FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0) };
            close.Click += (_, _) => win.Close();
            buttons.Children.Add(close);
        }
        root.Children.Add(buttons);
        win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 60 };
        win.ShowDialog();
    }

    // ───────────── Réparer FiveM ─────────────

    private static void ClearFiveMCache()
    {
        foreach (var rel in FiveMPacks.CacheFolders)
        {
            string dir = Path.Combine(FiveMApp, rel);
            if (!Directory.Exists(dir)) continue;
            foreach (var e in Directory.EnumerateFileSystemEntries(dir))
            {
                try { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); } catch (Exception) { }
            }
        }
    }

    private static long FolderBytes(string dir) => Directory.Exists(dir) ? GameCleanup.FolderSize(dir) : 0;

    private async void LoadFiveMFix()
    {
        FiveMFixCards.Children.Clear();
        if (!FiveMInstalled)
        {
            FiveMFixHint.Text = "FiveM n'est pas installé sur ce PC (dossier FiveM.app introuvable).";
            return;
        }
        FiveMFixHint.Text = "Calcul des tailles…";
        var (cache, crashes, logs) = await Task.Run(() => (
            FiveMPacks.CacheFolders.Sum(r => FolderBytes(Path.Combine(FiveMApp, r))),
            FolderBytes(Path.Combine(FiveMApp, "crashes")),
            FolderBytes(Path.Combine(FiveMApp, "logs"))));
        if (_page != "fivemfix") return;
        FiveMFixHint.Text = "Les solutions qui règlent la plupart des soucis de FiveM : chargement bloqué, textures qui ne s'affichent pas, plantages.";
        FiveMFixCards.Children.Clear();

        FiveMFixCards.Children.Add(InfoCard("Vider le cache", ByteSize.Format(cache),
            "Règle la plupart des problèmes : serveur qui ne charge pas, textures bizarres, pack qui ne s'applique pas. FiveM retélécharge ce qu'il faut au prochain lancement. Le dossier game-storage est gardé pour éviter de tout retélécharger.",
            null, ("Vider le cache", () =>
            {
                if (!EnsureFiveMClosed()) return;
                ClearFiveMCache();
                StatusText.Text = "Cache de FiveM vidé. Relance FiveM.";
                LoadFiveMFix();
            }, cache > 0, true)));

        FiveMFixCards.Children.Add(InfoCard("Rapports de plantage et journaux", ByteSize.Format(crashes + logs),
            "Fichiers créés à chaque plantage ou session de jeu. Ils ne servent qu'à envoyer un rapport au support.",
            null, ("Supprimer", () =>
            {
                if (!EnsureFiveMClosed()) return;
                foreach (var d in new[] { "crashes", "logs" })
                {
                    string dir = Path.Combine(FiveMApp, d);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var e in Directory.EnumerateFileSystemEntries(dir))
                        try { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); } catch (Exception) { }
                }
                StatusText.Text = "Rapports de plantage et journaux de FiveM supprimés.";
                LoadFiveMFix();
            }, crashes + logs > 0, false)));

        var record = PackRecord.Load(RecordFile);
        FiveMFixCards.Children.Add(InfoCard("Revenir au jeu d'origine", "",
            record != null
                ? $"Retire « {record.Name} » et remet les fichiers d'origine de GTA V."
                : "Aucun pack installé par l'app. Si tu as un pack installé à la main, le tuto t'explique comment le retirer.",
            null,
            record != null ? ("Retirer le pack", () => _ = UninstallPack(), true, false) : ("Voir le tuto", () => ShowPackTutorial(false, null), true, false)));

        FiveMFixCards.Children.Add(InfoCard("Vérifier les fichiers de GTA V", "",
            "Si le jeu plante au lancement ou si des sons ont été remplacés à la main : ton lanceur répare les fichiers abîmés ou modifiés. Steam : clic droit sur GTA V > Propriétés > Fichiers installés > Vérifier. Rockstar : Paramètres > GTA V > Vérifier l'intégrité. Epic : … à côté du jeu > Gérer > Vérifier.",
            null, ("Vérifier avec Steam", () => NativeMethods.Open("steam://validate/271590"), true, false)));
    }
}
