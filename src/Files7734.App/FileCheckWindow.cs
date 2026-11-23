using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Files7734.App;

/// <summary>Fenêtre « Ce fichier est-il sûr ? » : indices locaux + liens vers VirusTotal et une recherche web.</summary>
internal sealed class FileCheckWindow : Window
{
    private readonly string _path;
    private readonly StackPanel _findings = new();
    private readonly TextBlock _verdictTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _verdictIcon = new() { FontSize = 30, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _verdictSub = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
    private readonly TextBox _hash = new() { IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button _vt;
    private string? _sha;

    public FileCheckWindow(Window owner, string path)
    {
        _path = path;
        Owner = owner;
        Title = "Ce fichier est-il sûr ?";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = owner.FontFamily;
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "CardBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SourceInitialized += (_, _) => WindowFrame.Apply(this);

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _verdictIcon.FontFamily = (FontFamily)owner.FindResource("IconFont");
        _verdictIcon.Text = "";
        DockPanel.SetDock(_verdictIcon, Dock.Left);
        head.Children.Add(_verdictIcon);
        var headText = new StackPanel();
        _verdictTitle.Text = "Vérification en cours…";
        headText.Children.Add(_verdictTitle);
        _verdictSub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        headText.Children.Add(_verdictSub);
        head.Children.Add(headText);
        root.Children.Add(head);

        // Identité du fichier
        var info = new FileInfo(path);
        root.Children.Add(new TextBlock { Text = info.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(Muted(info.DirectoryName ?? ""));
        string details = $"{ByteSize.Format(info.Exists ? info.Length : 0)} · modifié le {Fmt.Date(info.LastWriteTimeUtc)} · créé le {Fmt.Date(info.CreationTimeUtc)}";
        root.Children.Add(Muted(details));
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            var parts = new[]
            {
                ("Description", v.FileDescription), ("Éditeur", v.CompanyName), ("Produit", v.ProductName), ("Version", v.FileVersion),
            }.Where(p => !string.IsNullOrWhiteSpace(p.Item2)).Select(p => $"{p.Item1} : {p.Item2!.Trim()}").ToList();
            if (parts.Count > 0) root.Children.Add(Muted(string.Join("\n", parts), new Thickness(0, 8, 0, 0)));
        }
        catch (Exception) { }

        root.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 14, 0, 12), Background = (Brush)owner.FindResource("HairlineBrush") });
        root.Children.Add(new TextBlock { Text = "Ce que l'app a remarqué", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(_findings);

        root.Children.Add(new TextBlock { Text = "Empreinte SHA-256", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
        _hash.Text = "calcul…";
        _hash.SetResourceReference(ForegroundProperty, "MutedBrush");
        root.Children.Add(_hash);

        var note = Muted("Ce n'est pas un antivirus. Pour un avis fiable, VirusTotal fait analyser ce fichier par plus de 70 antivirus. " +
                         "Seule l'empreinte est envoyée, jamais le fichier. Si VirusTotal ne connaît pas encore le fichier, tu pourras l'y déposer toi-même.",
                         new Thickness(0, 12, 0, 16));
        root.Children.Add(note);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _vt = new Button { Content = "Vérifier sur VirusTotal", Style = (Style)owner.FindResource("AccentButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        _vt.Click += (_, _) => { if (_sha != null) Open(FileSafety.VirusTotalUrl(_sha)); };
        var web = new Button { Content = "Rechercher sur Internet", Style = (Style)owner.FindResource("PlainButton"), Margin = new Thickness(8, 0, 0, 0) };
        web.Click += (_, _) => Open(FileSafety.WebSearchUrl(info.Name));
        var explorer = new Button { Content = "Afficher dans l'Explorateur", Style = (Style)owner.FindResource("PlainButton"), Margin = new Thickness(8, 0, 0, 0) };
        explorer.Click += (_, _) => NativeMethods.ShowInExplorer(path);
        var close = new Button { Content = "Fermer", IsCancel = true, Style = (Style)owner.FindResource("PlainButton"), Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(explorer);
        buttons.Children.Add(web);
        buttons.Children.Add(_vt);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        Content = root;
        Loaded += async (_, _) => await RunChecks();
    }

    private TextBlock Muted(string text, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = margin ?? new Thickness(0, 2, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
    }

    private async Task RunChecks()
    {
        string path = _path;
        var hashTask = Task.Run(() => { try { return SystemTools.Sha256(path); } catch (Exception) { return null; } });
        SignatureInfo? sig = FileSafety.IsExecutable(path) ? await Task.Run(() => SystemTools.CheckSignature(path)) : null;

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new Dictionary<string, string>
        {
            ["windows"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["temp"] = Path.GetTempPath(),
            ["appdata"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["localappdata"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["downloads"] = Path.Combine(profile, "Downloads"),
        };
        var (level, findings) = FileSafety.Evaluate(path, sig, folders);

        (string icon, uint color, string title, string sub) = level switch
        {
            RiskLevel.Danger => ("", 0xFFFF453Au, "Ce fichier est suspect",
                "Ne l'ouvre pas. Vérifie-le sur VirusTotal et, en cas de doute, fais une analyse complète avec ton antivirus."),
            RiskLevel.Warning => ("", 0xFFFF9F0Au, "À vérifier",
                "Rien de grave en soi, mais quelques points méritent une vérification sur VirusTotal."),
            RiskLevel.Good => ("", 0xFF32D74Bu, "Aucun signe suspect",
                "Le fichier vient d'un éditeur identifié. Tu peux quand même le vérifier sur VirusTotal."),
            _ => ("", 0xFF0A84FFu, "Risque faible",
                "Ce type de fichier ne lance pas de programme. Il peut être vérifié sur VirusTotal si tu as un doute."),
        };
        _verdictIcon.Text = icon;
        _verdictIcon.Foreground = ExtensionColors.BrushOf(color);
        _verdictTitle.Text = title;
        _verdictSub.Text = sub;

        _findings.Children.Clear();
        foreach (var f in findings)
        {
            uint c = f.Level switch { RiskLevel.Danger => 0xFFFF453A, RiskLevel.Warning => 0xFFFF9F0A, RiskLevel.Good => 0xFF32D74B, _ => 0xFF8E8E93 };
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = ExtensionColors.BrushOf(c), Margin = new Thickness(2, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = f.Text, TextWrapping = TextWrapping.Wrap });
            _findings.Children.Add(row);
        }

        _sha = await hashTask;
        _hash.Text = _sha?.ToLowerInvariant() ?? "Impossible de lire le fichier (il est peut-être utilisé ou protégé).";
        _vt.IsEnabled = _sha != null;
    }
}
