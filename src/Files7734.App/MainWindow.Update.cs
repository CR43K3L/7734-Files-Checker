using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Files7734.App;

/// <summary>Mise à jour automatique depuis les « Releases » GitHub du projet.</summary>
public partial class MainWindow
{
    public const string GitHubRepo = "CR43K3L/7734-Files-Checker";
    private UpdateInfo? _update;
    private bool _updatePopupShown;

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    private static string CurrentTag => "v" + CurrentVersion.ToString(3);

    private void InitUpdates()
    {
        Title = $"7734 Files Checker {CurrentVersion.ToString(3)}";
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += async (_, _) =>
        {
            t.Stop();
            // Première ouverture après une mise à jour : on montre ce qui a changé.
            string? last = _settings.LastRunVersion;
            _settings.LastRunVersion = CurrentVersion.ToString(3);
            _settings.Save();
            if (last != null && last != CurrentVersion.ToString(3) && !StartHidden) await ShowWhatsNew(welcome: true);
            await CheckForUpdates(manual: false);
        };
        t.Start();
    }

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"7734FilesChecker/{CurrentVersion.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private async Task CheckForUpdates(bool manual)
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(15));
            string json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settings.Save();
            var info = UpdateInfo.Parse(json);
            if (info != null && info.IsNewerThan(CurrentVersion))
            {
                _update = info;
                UpdateBannerText.Text = $"La version {info.Version.ToString(3)} est disponible.";
                UpdateBanner.Visibility = Visibility.Visible;
                bool skipped = _settings.SkippedUpdateTag == info.Tag;
                if (manual || (!_updatePopupShown && !skipped && IsVisible))
                {
                    _updatePopupShown = true;
                    ShowUpdateWindow(info);
                }
            }
            else if (manual)
            {
                MessageBox.Show(this, $"Tu as la dernière version ({CurrentVersion.ToString(3)}).", "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (manual) MessageBox.Show(this, "Impossible de vérifier les mises à jour : " + ex.Message, "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Les boutons du bandeau de la barre latérale.
    private void UpdateNow_Click(object sender, RoutedEventArgs e) { if (_update != null) ShowUpdateWindow(_update); }
    private void UpdateNotes_Click(object sender, RoutedEventArgs e) { if (_update != null) ShowUpdateWindow(_update); }

    // ───────────── Fenêtre « Nouvelle version disponible » ─────────────

    private void ShowUpdateWindow(UpdateInfo info)
    {
        var win = NewDialog("Mise à jour disponible", 560);
        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };

        root.Children.Add(Header("", $"La version {info.Version.ToString(3)} est disponible",
            $"Tu as la version {CurrentVersion.ToString(3)}. La mise à jour prend quelques secondes et garde tous tes réglages."));

        root.Children.Add(new TextBlock { Text = "Nouveautés", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 16, 0, 6) });
        root.Children.Add(NotesBox(info.Notes));

        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        root.Children.Add(status);
        root.Children.Add(bar);

        var skip = new CheckBox { Content = "Ne plus me proposer cette version", FontSize = 12, Margin = new Thickness(0, 14, 0, 0), IsChecked = _settings.SkippedUpdateTag == info.Tag };
        root.Children.Add(skip);

        var update = new Button { Content = "Mettre à jour maintenant", IsDefault = true, Style = (Style)FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0) };
        var later = new Button { Content = "Plus tard", IsCancel = true, Style = (Style)FindResource("PlainButton"), Margin = new Thickness(8, 0, 0, 0) };
        var github = new Button { Content = "Voir sur GitHub", Style = (Style)FindResource("PlainButton") };
        github.Click += (_, _) => Safe(() => NativeMethods.Open(info.PageUrl.Length > 0 ? info.PageUrl : $"https://github.com/{GitHubRepo}/releases"));
        later.Click += (_, _) => win.Close();
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(later);
        right.Children.Add(update);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { github } });
        root.Children.Add(buttons);

        bool downloading = false;
        win.Closing += (_, e) =>
        {
            if (downloading) { e.Cancel = true; return; }
            _settings.SkippedUpdateTag = skip.IsChecked == true ? info.Tag : (_settings.SkippedUpdateTag == info.Tag ? null : _settings.SkippedUpdateTag);
            _settings.Save();
        };
        update.Click += async (_, _) =>
        {
            downloading = true;
            update.IsEnabled = later.IsEnabled = skip.IsEnabled = false;
            status.Visibility = bar.Visibility = Visibility.Visible;
            status.Text = "Téléchargement de la mise à jour…";
            string? error = await DownloadAndInstall(info, p =>
            {
                bar.Value = p;
                status.Text = $"Téléchargement… {(p * 100):0} %";
            });
            downloading = false;
            if (error == null) return; // l'app se ferme et se relance
            status.Text = "La mise à jour a échoué : " + error + " Tu peux la télécharger à la main avec « Voir sur GitHub ».";
            bar.Visibility = Visibility.Collapsed;
            update.IsEnabled = later.IsEnabled = skip.IsEnabled = true;
        };

        win.Content = root;
        win.ShowDialog();
    }

    /// <summary>Télécharge, vérifie et remplace l'exe. Renvoie un message d'erreur, ou null si l'app redémarre.</summary>
    private async Task<string?> DownloadAndInstall(UpdateInfo info, Action<double> progress)
    {
        string exe = Environment.ProcessPath!;
        string dir = Path.GetDirectoryName(exe)!;
        string newFile = Path.Combine(dir, "7734FilesChecker.update.exe");
        try
        {
            using var http = NewClient(TimeSpan.FromMinutes(15));
            using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.Size;
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(newFile))
            {
                var buffer = new byte[1 << 20];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n));
                    done += n;
                    if (total > 0) progress((double)done / total);
                }
            }
            if (info.Size > 0 && new FileInfo(newFile).Length != info.Size) throw new IOException("le fichier téléchargé est incomplet.");
            if (info.Sha256 != null)
            {
                await using var fs = File.OpenRead(newFile);
                string sha = Convert.ToHexString(await SHA256.HashDataAsync(fs));
                if (!sha.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("le fichier téléchargé est abîmé.");
            }
        }
        catch (UnauthorizedAccessException)
        {
            return "l'app n'a pas le droit d'écrire dans son dossier. Relance-la en administrateur.";
        }
        catch (Exception ex)
        {
            try { File.Delete(newFile); } catch (Exception) { }
            return ex.Message;
        }

        // Un petit script remplace l'exe une fois l'app fermée, puis la relance.
        int pid = Environment.ProcessId;
        string bat = Path.Combine(Path.GetTempPath(), "7734-update.bat");
        File.WriteAllText(bat,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"move /y \"{newFile}\" \"{exe}\" >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "(goto) 2>nul & del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        _quitting = true;
        Application.Current.Shutdown();
        return null;
    }

    // ───────────── Fenêtre « Nouveautés de cette version » ─────────────

    private async Task ShowWhatsNew(bool welcome)
    {
        string notes;
        string page = $"https://github.com/{GitHubRepo}/releases";
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(15));
            string json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/tags/{CurrentTag}");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            notes = doc.RootElement.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            if (doc.RootElement.TryGetProperty("html_url", out var h)) page = h.GetString() ?? page;
        }
        catch (Exception)
        {
            if (welcome) return; // hors ligne : on ne dérange pas
            notes = "Impossible de charger les nouveautés (pas de connexion ?). Elles sont aussi sur la page GitHub du projet.";
        }

        var win = NewDialog("Nouveautés", 560);
        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        root.Children.Add(Header("", welcome ? $"Bienvenue dans la version {CurrentVersion.ToString(3)} !" : $"Nouveautés de la version {CurrentVersion.ToString(3)}",
            welcome ? "La mise à jour s'est bien passée. Voici ce qui a changé." : "Ce qui a changé dans ta version."));
        root.Children.Add(new Border { Height = 14 });
        root.Children.Add(NotesBox(notes));
        var ok = new Button { Content = "Super !", IsDefault = true, IsCancel = true, Style = (Style)FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => win.Close();
        var gh = new Button { Content = "Toutes les versions", Style = (Style)FindResource("PlainButton") };
        gh.Click += (_, _) => Safe(() => NativeMethods.Open(page));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(gh);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        win.Content = root;
        win.ShowDialog();
    }

    // ───────────── Éléments communs ─────────────

    private Window NewDialog(string title, double width)
    {
        var win = new Window
        {
            Title = title, Owner = IsVisible ? this : null, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = !IsVisible, FontFamily = FontFamily, FontSize = 13, Icon = Icon,
        };
        win.SetResourceReference(BackgroundProperty, "CardBrush");
        win.SetResourceReference(ForegroundProperty, "TextBrush");
        win.SourceInitialized += (_, _) => WindowFrame.Apply(win);
        return win;
    }

    private UIElement Header(string glyph, string title, string subtitle)
    {
        var head = new DockPanel();
        var icon = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        icon.Child = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var sub = new TextBlock { Text = subtitle, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(sub);
        head.Children.Add(text);
        return head;
    }

    /// <summary>Affiche les notes de version (Markdown simple : titres, listes, gras, citations).</summary>
    private UIElement NotesBox(string markdown)
    {
        var panel = new StackPanel();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Trim().Length == 0) { panel.Children.Add(new Border { Height = 6 }); continue; }
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            string text = line.TrimStart();
            if (text.StartsWith("#"))
            {
                text = text.TrimStart('#').Trim();
                tb.FontWeight = FontWeights.SemiBold;
                tb.FontSize = 14;
                tb.Margin = new Thickness(0, 6, 0, 2);
            }
            else if (text.StartsWith("- ") || text.StartsWith("* "))
            {
                text = text[2..];
                tb.Margin = new Thickness(line.Length - line.TrimStart().Length > 1 ? 30 : 12, 1, 0, 1);
                tb.Inlines.Add(new Run("•  ") { Foreground = (Brush)FindResource("AccentBrush") });
            }
            else if (text.StartsWith(">"))
            {
                text = text.TrimStart('>').Trim();
                tb.FontStyle = FontStyles.Italic;
                tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }
            // **gras**
            var parts = text.Split("**");
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) tb.Inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 ? FontWeights.SemiBold : tb.FontWeight });
            panel.Children.Add(tb);
        }
        var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "ContentBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        box.Child = new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        return box;
    }
}
