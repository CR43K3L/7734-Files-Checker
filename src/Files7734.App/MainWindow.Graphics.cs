using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Files7734.App;

/// <summary>Page FiveM « Réglages graphiques » : profils en un clic et réglages un par un (gta5_settings.xml).</summary>
public partial class MainWindow
{
    private const string GfxBackupSuffix = ".7734-original";

    /// <summary>Fichiers de réglages de GTA lus par FiveM (le sien en priorité, et celui du jeu s'il existe).</summary>
    private static List<string> GfxFiles()
    {
        var list = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "gta5_settings.xml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V", "settings.xml"),
        };
        return list.Where(File.Exists).ToList();
    }

    private void LoadGfx()
    {
        GfxCards.Children.Clear();
        var files = GfxFiles();
        GfxRestore.IsEnabled = files.Any(f => File.Exists(f + GfxBackupSuffix));
        if (files.Count == 0)
        {
            GfxHint.Text = "Fichier de réglages introuvable. Lance FiveM une fois (jusqu'au menu), puis reviens ici.";
            return;
        }
        GtaSettings settings;
        try { settings = GtaSettings.Parse(File.ReadAllText(files[0])); }
        catch (Exception ex)
        {
            GfxHint.Text = "Le fichier de réglages est illisible : " + ex.Message;
            return;
        }

        bool running = FiveMProcesses().Count > 0;
        GfxHint.Text = running
            ? "⚠ FiveM est ouvert : ferme-le avant de changer les réglages, sinon il remettra les anciens en quittant."
            : "Choisis un profil ou règle chaque option. C'est appliqué tout de suite, au prochain lancement de FiveM.";

        // Profils
        string? current = settings.CurrentPreset();
        var presets = new UniformGrid3();
        foreach (var (name, desc, values) in GtaSettings.Presets)
        {
            var v = values;
            presets.Add(InfoCard(name, "", desc, name == current ? Pill("Actuel", 0xFF32D74Bu) : null,
                (name == current ? "Appliqué" : "Appliquer", () => SaveGfx(s => s.ApplyPreset(v), $"Profil « {name} » appliqué."), name != current, name != current)));
        }
        GfxCards.Children.Add(presets.Panel);

        var title = new TextBlock { Text = "Réglage par réglage", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(2, 8, 0, 8) };
        GfxCards.Children.Add(title);

        foreach (var opt in GtaSettings.Options)
        {
            string? value = GtaSettings.MatchChoice(opt, settings.Get(opt.Key));
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var host = new Border { Style = (Style)FindResource("SegmentHost"), VerticalAlignment = VerticalAlignment.Center };
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (val, label) in opt.Choices)
            {
                var rb = new RadioButton { Content = label, GroupName = "gfx-" + opt.Key, Style = (Style)FindResource("Segment"), IsChecked = val == value };
                string key = opt.Key, chosen = val, optLabel = opt.Label;
                rb.Checked += (_, _) =>
                {
                    if (!rb.IsLoaded) return;
                    SaveGfx(s =>
                    {
                        s.Set(key, chosen);
                        if (key == "MSAA") s.Set("MSAAFragments", chosen);
                    }, $"{optLabel} : {label}.", reload: false);
                };
                seg.Children.Add(rb);
            }
            host.Child = seg;
            DockPanel.SetDock(host, Dock.Right);
            row.Children.Add(host);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 12, 0) };
            text.Children.Add(new TextBlock { Text = opt.Label, FontWeight = FontWeights.SemiBold });
            if (opt.Help.Length > 0)
            {
                var help = new TextBlock { Text = opt.Help, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
                help.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                text.Children.Add(help);
            }
            row.Children.Add(text);
            GfxCards.Children.Add(row);
        }
    }

    /// <summary>Applique une modification à chaque fichier de réglages (après une sauvegarde de l'original, une seule fois).</summary>
    private void SaveGfx(Action<GtaSettings> change, string done, bool reload = true)
    {
        if (FiveMProcesses().Count > 0 && !EnsureFiveMClosed()) { LoadGfx(); return; }
        int ok = 0;
        string? error = null;
        foreach (var f in GfxFiles())
        {
            try
            {
                if (!File.Exists(f + GfxBackupSuffix)) File.Copy(f, f + GfxBackupSuffix);
                var s = GtaSettings.Parse(File.ReadAllText(f));
                change(s);
                var attrs = File.GetAttributes(f);
                if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, attrs & ~FileAttributes.ReadOnly);
                s.Save(f);
                ok++;
            }
            catch (Exception ex) { error = ex.Message; }
        }
        StatusText.Text = ok > 0 ? done + " Relance FiveM pour voir le changement." : "Impossible d'enregistrer les réglages : " + error;
        GfxRestore.IsEnabled = true;
        if (reload) LoadGfx();
        else if (_page == "fivemgfx") RefreshGfxPresets();
    }

    /// <summary>Met à jour le badge « Actuel » des profils sans reconstruire les réglages (pour ne pas perdre le défilement).</summary>
    private void RefreshGfxPresets()
    {
        if (GfxCards.Children.Count == 0 || GfxFiles().FirstOrDefault() is not string f) return;
        try
        {
            var s = GtaSettings.Parse(File.ReadAllText(f));
            string? current = s.CurrentPreset();
            var grid = new UniformGrid3();
            foreach (var (name, desc, values) in GtaSettings.Presets)
            {
                var v = values;
                grid.Add(InfoCard(name, "", desc, name == current ? Pill("Actuel", 0xFF32D74Bu) : null,
                    (name == current ? "Appliqué" : "Appliquer", () => SaveGfx(x => x.ApplyPreset(v), $"Profil « {name} » appliqué."), name != current, name != current)));
            }
            GfxCards.Children.RemoveAt(0);
            GfxCards.Children.Insert(0, grid.Panel);
        }
        catch (Exception) { }
    }

    private void GfxRestore_Click(object sender, RoutedEventArgs e)
    {
        if (!Ask("Remettre les réglages graphiques que tu avais avant d'utiliser l'app ?")) return;
        if (!EnsureFiveMClosed()) return;
        int n = 0;
        foreach (var f in GfxFiles())
        {
            try
            {
                if (!File.Exists(f + GfxBackupSuffix)) continue;
                File.Copy(f + GfxBackupSuffix, f, overwrite: true);
                n++;
            }
            catch (Exception) { }
        }
        StatusText.Text = n > 0 ? "Anciens réglages graphiques remis en place." : "Aucune sauvegarde à remettre.";
        LoadGfx();
    }

    /// <summary>Trois cartes côte à côte.</summary>
    private sealed class UniformGrid3
    {
        public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new() { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
        public void Add(Border card)
        {
            card.Margin = new Thickness(5, 0, 5, 10);
            Panel.Children.Add(card);
        }
    }
}
