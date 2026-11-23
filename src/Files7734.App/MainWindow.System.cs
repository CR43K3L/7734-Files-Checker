using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Files7734.App;

/// <summary>Pages « Fichiers cachés de Windows » et « Santé des disques ».</summary>
public partial class MainWindow
{
    private bool _systemLoaded, _healthLoaded;

    // ───────────── Petits éléments d'interface ─────────────

    private Border InfoCard(string title, string value, string description, UIElement? badge = null, params (string Text, Action Click, bool Enabled, bool Accent)[] actions)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "ContentBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var dock = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var a in actions)
        {
            var b = new Button { Content = a.Text, Style = (Style)FindResource(a.Accent ? "AccentButton" : "PlainButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = a.Enabled };
            var act = a.Click;
            b.Click += (_, _) => Safe(act);
            buttons.Children.Add(b);
        }
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);

        var text = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        if (badge != null) titleRow.Children.Add(badge);
        text.Children.Add(titleRow);
        if (value.Length > 0) text.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 2) });
        var desc = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0) };
        desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        text.Children.Add(desc);
        dock.Children.Add(text);
        card.Child = dock;
        return card;
    }

    private static Border Pill(string text, uint color) => new()
    {
        CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Background = ExtensionColors.BrushOf((color & 0x00FFFFFF) | 0x33000000),
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = ExtensionColors.BrushOf(color) },
    };

    private bool ConfirmAdmin()
    {
        if (_elevated) return true;
        MessageBox.Show(this, "Cette action demande les droits administrateur. Utilise « Relancer en administrateur » en bas à gauche.",
            "7734 Files Checker", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    // ═════════════ Fichiers cachés de Windows ═════════════

    private void SystemRefresh_Click(object sender, RoutedEventArgs e) => LoadSystemFiles();

    private async void LoadSystemFiles()
    {
        _systemLoaded = true;
        SystemRefresh.IsEnabled = false;
        SystemHint.Text = "Lecture…";
        string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
        var (hiber, page, swap, restore) = await Task.Run(() => (
            SystemTools.FileSize(Path.Combine(sys, "hiberfil.sys")),
            SystemTools.FileSize(Path.Combine(sys, "pagefile.sys")),
            SystemTools.FileSize(Path.Combine(sys, "swapfile.sys")),
            SystemTools.RestorePointsSize()));
        SystemRefresh.IsEnabled = true;

        string winsxs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
        var winsxsNode = FindInResult(winsxs);
        SystemCards.Children.Clear();
        string drive = sys.TrimEnd('\\');
        string adminNote = _elevated ? "" : " (droits administrateur nécessaires)";

        SystemCards.Children.Add(InfoCard("Fichier d'hibernation (hiberfil.sys)", hiber > 0 ? ByteSize.Format(hiber) : "Désactivé",
            "Sert à la mise en veille prolongée et au démarrage rapide de Windows. « Réduire » garde le démarrage rapide et divise souvent la taille par deux. " +
            "« Désactiver » libère tout, mais l'hibernation et le démarrage rapide ne seront plus disponibles." + adminNote, null,
            ("Réduire", () => { if (ConfirmAdmin() && Ask("Réduire le fichier d'hibernation ? Le démarrage rapide reste actif.")) PowerCfg("/h /type reduced"); }, hiber > 0, false),
            ("Désactiver", () => { if (ConfirmAdmin() && Ask("Désactiver l'hibernation ? La mise en veille prolongée et le démarrage rapide ne seront plus disponibles. Tu pourras la réactiver plus tard.")) PowerCfg("/h off"); }, hiber > 0, false),
            ("Réactiver", () => { if (ConfirmAdmin()) PowerCfg("/h on"); }, hiber == 0, false)));

        SystemCards.Children.Add(InfoCard("Mémoire virtuelle (pagefile.sys, swapfile.sys)", ByteSize.Format(page + swap),
            "Windows l'utilise quand la mémoire vive est pleine. Il vaut mieux le laisser géré automatiquement : le supprimer peut faire planter les jeux. " +
            "Tu peux le placer sur un autre disque depuis les réglages.", null,
            ("Ouvrir les réglages", () => NativeMethods.Open("SystemPropertiesPerformance.exe"), true, false)));

        string restoreValue = restore is { } r ? ByteSize.Format(r.Used) : "Inconnu";
        string restoreDesc = restore is { } rr && rr.Max > 0
            ? $"Copies de sécurité de Windows pour revenir en arrière après un problème. Limite actuelle : {ByteSize.Format(rr.Max)}. Les limiter à 5 % du disque garde les points récents."
            : "Copies de sécurité de Windows pour revenir en arrière après un problème." + (_elevated ? "" : " Relance en administrateur pour voir leur taille.");
        SystemCards.Children.Add(InfoCard("Points de restauration", restoreValue, restoreDesc + adminNote, null,
            ("Limiter à 5 %", () =>
            {
                if (!ConfirmAdmin() || !Ask($"Limiter les points de restauration à 5 % du disque {drive} ? Les plus anciens seront supprimés si besoin.")) return;
                int code = SystemTools.RunHidden("vssadmin.exe", $"Resize ShadowStorage /For={drive} /On={drive} /MaxSize=5%");
                StatusText.Text = code == 0 ? "Points de restauration limités à 5 % du disque." : "Windows a refusé la modification (la protection du système est peut-être désactivée).";
                LoadSystemFiles();
            }, restore is { Used: > 0 }, false),
            ("Protection du système", () => NativeMethods.Open("SystemPropertiesProtection.exe"), true, false)));

        SystemCards.Children.Add(InfoCard("Composants de Windows (WinSxS)", winsxsNode != null ? ByteSize.Format(winsxsNode.Size) : "",
            "Anciennes versions des fichiers de Windows gardées après les mises à jour. Le nettoyage officiel de Windows (DISM) retire celles qui ne servent plus. " +
            "Il prend de 5 à 20 minutes, dans une fenêtre à part." + (winsxsNode != null ? " La taille affichée compte aussi des fichiers partagés avec Windows : le gain réel est plus faible." : "") + adminNote, null,
            ("Nettoyer", () =>
            {
                if (ConfirmAdmin() && Ask("Lancer le nettoyage des composants de Windows ? Une fenêtre noire va s'ouvrir : laisse-la travailler jusqu'au bout."))
                    SystemTools.RunVisible("Dism.exe /Online /Cleanup-Image /StartComponentCleanup");
            }, true, false)));

        long total = hiber + page + swap + (restore?.Used ?? 0);
        SystemHint.Text = $"Ces fichiers prennent {ByteSize.Format(total)} sur {drive} et n'apparaissent pas dans l'Explorateur.";
    }

    private bool Ask(string question) =>
        MessageBox.Show(this, question, "7734 Files Checker", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void PowerCfg(string args)
    {
        int code = SystemTools.RunHidden("powercfg.exe", args);
        StatusText.Text = code == 0 ? "Réglage de l'hibernation appliqué." : "Windows a refusé la modification de l'hibernation.";
        LoadSystemFiles();
        LoadDrives();
    }

    // ═════════════ Santé des disques ═════════════

    private void HealthRefresh_Click(object sender, RoutedEventArgs e) => LoadHealth();

    private async void LoadHealth()
    {
        _healthLoaded = true;
        HealthRefresh.IsEnabled = false;
        HealthHint.Text = "Lecture des disques…";
        List<SystemTools.DiskHealth>? disks = null;
        string? error = null;
        try { disks = await Task.Run(SystemTools.DiskHealths); }
        catch (Exception ex) { error = ex.Message; }
        HealthRefresh.IsEnabled = true;
        HealthCards.Children.Clear();
        if (disks == null || disks.Count == 0)
        {
            HealthHint.Text = "Windows n'a pas donné d'informations sur tes disques" + (error != null ? $" ({error})." : ".");
            return;
        }

        bool anyCounters = disks.Any(d => d.Temperature != null || d.Wear != null || d.PowerOnHours != null);
        foreach (var d in disks)
        {
            var (label, color) = d.Health switch
            {
                0 when d.OperationalStatus == null => ("Bon état", 0xFF32D74Bu),
                0 => ("Attention", 0xFFFF9F0Au),
                1 => ("Attention", 0xFFFF9F0Au),
                2 => ("Défaillant", 0xFFFF453Au),
                _ => ("État inconnu", 0xFF8E8E93u),
            };
            var lines = new List<string>();
            if (d.OperationalStatus != null) lines.Add($"Windows signale : {d.OperationalStatus}.");
            if (d.Temperature is int t)
            {
                bool ssd = d.Kind == "SSD";
                string judge = t >= (ssd ? 70 : 55) ? " (chaud : vérifie la ventilation)" : t >= (ssd ? 60 : 48) ? " (un peu chaud)" : " (normal)";
                lines.Add($"Température : {t} °C{judge}" + (d.TemperatureMax is int tm && tm > 0 ? $" · limite {tm} °C" : ""));
            }
            if (d.Wear is int w && d.Kind == "SSD") lines.Add($"Usure : {w} % · durée de vie restante estimée : {Math.Max(0, 100 - w)} %");
            if (d.PowerOnHours is long h && h > 0) lines.Add($"Allumé pendant {Fmt.Count(h)} heures (≈ {(h / 24.0 / 365).ToString("0.0", Fmt.Fr)} ans en continu)");
            long errors = (d.ReadErrors ?? 0) + (d.WriteErrors ?? 0);
            if (d.ReadErrors != null || d.WriteErrors != null)
                lines.Add(errors == 0 ? "Aucune erreur de lecture ou d'écriture." : $"{Fmt.Count(errors)} erreurs de lecture/écriture : sauvegarde tes fichiers importants.");
            if (lines.Count == 0)
                lines.Add(_elevated ? "Ce disque ne donne pas de détails (température, usure)." : "Relance en administrateur pour voir la température et l'usure.");
            if (d.Health == 2) lines.Insert(0, "Ce disque risque de tomber en panne : copie tes fichiers importants ailleurs dès que possible.");

            string sub = string.Join(" · ", new[] { d.Kind, d.Bus, d.Size > 0 ? ByteSize.Format(d.Size) : "" }.Where(x => x.Length > 0));
            HealthCards.Children.Add(InfoCard(d.Name, "", sub + "\n" + string.Join("\n", lines), Pill(label, color)));
        }
        int bad = disks.Count(d => d.Health is 1 or 2 || d.OperationalStatus != null);
        HealthHint.Text = (bad == 0 ? $"{disks.Count} disque{(disks.Count > 1 ? "s" : "")}, tous en bon état." : $"{bad} disque{(bad > 1 ? "s" : "")} à surveiller.")
                          + (!anyCounters && !_elevated ? " Relance en administrateur pour plus de détails." : "");
    }
}
