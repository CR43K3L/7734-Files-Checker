using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Files7734.App;

/// <summary>Fenêtre « Réglages » rangée par catégories (remplace les menus Fichier et Options).</summary>
public partial class MainWindow
{
    private Action? _settingsRefresh;
    private Window? _settingsWindow;

    private static readonly (string Category, string Glyph, string Description)[] SettingsCategories =
    {
        ("Apparence", "", "Thème et unité des tailles."),
        ("Analyse", "", "Ce qui s'affiche pendant et après une analyse."),
        ("Surveillance", "", "Alertes quand un disque se remplit, résumé de la semaine."),
        ("Windows", "", "Intégration à l'Explorateur et droits administrateur."),
        ("Fichiers et export", "", "Analyser plusieurs dossiers, ouvrir ou enregistrer une analyse."),
        ("Raccourcis clavier", "", "Toutes les touches utiles."),
        ("À propos", "", "Version, mises à jour et réinitialisation."),
    };

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettingsWindow();

    private void ShowSettingsWindow(string? category = null)
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        var win = NewDialog("Réglages", 780);
        win.SizeToContent = SizeToContent.Manual;
        win.Height = 580;
        win.ResizeMode = ResizeMode.CanResize;
        win.MinWidth = 640;
        win.MinHeight = 420;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Colonne des catégories
        var side = new DockPanel { Margin = new Thickness(0) };
        side.SetResourceReference(Panel.BackgroundProperty, "SidebarBrush");
        var title = new TextBlock { Text = "Réglages", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(18, 18, 0, 14) };
        DockPanel.SetDock(title, Dock.Top);
        side.Children.Add(title);
        var list = new ListBox { Style = (Style)FindResource("PlainListBox"), ItemContainerStyle = (Style)FindResource("NavItem"), Margin = new Thickness(8, 0, 8, 12) };
        foreach (var (cat, glyph, _) in SettingsCategories)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = cat };
            var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15, Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = cat, VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(row);
        }
        side.Children.Add(list);
        grid.Children.Add(side);

        // Contenu
        var content = new StackPanel { Margin = new Thickness(26, 20, 26, 20) };
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        grid.Children.Add(scroll);

        string current = category ?? SettingsCategories[0].Category;
        void Show()
        {
            content.Children.Clear();
            var info = SettingsCategories.First(c => c.Category == current);
            content.Children.Add(new TextBlock { Text = info.Category, FontSize = 20, FontWeight = FontWeights.SemiBold });
            var d = new TextBlock { Text = info.Description, Margin = new Thickness(0, 3, 0, 18), TextWrapping = TextWrapping.Wrap };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            content.Children.Add(d);
            BuildSettingsCategory(current, content);
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is StackPanel { Tag: string c }) { current = c; Show(); }
        };
        list.SelectedIndex = Array.FindIndex(SettingsCategories, c => c.Category == current);

        _settingsRefresh = () => { if (current != "Raccourcis clavier") Show(); };
        win.Closed += (_, _) => { _settingsRefresh = null; _settingsWindow = null; };
        win.Content = grid;
        _settingsWindow = win;
        win.Show();
    }

    private UIElement SettingsGroup(string title, UIElement body)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "ContentBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var s = new StackPanel();
        if (title.Length > 0) s.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        s.Children.Add(body);
        card.Child = s;
        return card;
    }

    private UIElement Segments(IEnumerable<AppCommand> commands)
    {
        var host = new Border { Style = (Style)FindResource("SegmentHost"), HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        string group = "set-" + Guid.NewGuid().ToString("N");
        foreach (var c in commands)
        {
            var rb = new RadioButton { Content = c.Header, GroupName = group, Style = (Style)FindResource("Segment"), IsChecked = OptionState(c.Key!) };
            var cmd = c;
            rb.Click += (_, _) => { if (!OptionState(cmd.Key!)) Safe(cmd.Action); };
            panel.Children.Add(rb);
        }
        host.Child = panel;
        return host;
    }

    private static string GestureText(string g) => g.Replace("OemComma", ",").Replace("Del", "Suppr").Replace("Shift", "Maj");

    private void BuildSettingsCategory(string category, StackPanel content)
    {
        var cmds = _commands.Where(c => c.Category == category).ToList();
        switch (category)
        {
            case "Apparence":
                content.Children.Add(SettingsGroup("Thème", Segments(cmds.Where(c => c.Key!.StartsWith("theme:")))));
                content.Children.Add(SettingsGroup("Unité des tailles", Segments(cmds.Where(c => c.Key!.StartsWith("unit:")))));
                var tip = new TextBlock { Text = "Astuce : Ctrl+T passe du thème sombre au thème clair.", FontSize = 12 };
                tip.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                content.Children.Add(tip);
                return;

            case "Raccourcis clavier":
                var table = new Grid();
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                int r = 0;
                foreach (var c in _commands.Where(c => c.Gesture != null).OrderBy(c => c.Category.Length == 0 ? 0 : 1))
                {
                    table.RowDefinitions.Add(new RowDefinition());
                    var name = new TextBlock { Text = c.Key != null && c.Category == "Apparence" ? "Thème clair / sombre" : c.Header, Margin = new Thickness(0, 3, 12, 3) };
                    var key = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Right,
                        Child = new TextBlock { Text = GestureText(c.Gesture!), FontSize = 12, FontFamily = new FontFamily("Consolas, Segoe UI") } };
                    key.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
                    Grid.SetRow(name, r); Grid.SetRow(key, r); Grid.SetColumn(key, 1);
                    table.Children.Add(name); table.Children.Add(key);
                    r++;
                }
                content.Children.Add(SettingsGroup("", table));
                return;

            case "À propos":
                var head = new StackPanel { Orientation = Orientation.Horizontal };
                head.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/logo.png")), Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0) });
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = "7734 Files Checker", FontSize = 16, FontWeight = FontWeights.SemiBold });
                var v = new TextBlock { Text = $"Version {CurrentVersion.ToString(3)}" };
                v.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                t.Children.Add(v);
                head.Children.Add(t);
                content.Children.Add(SettingsGroup("", head));
                break;
        }

        var checks = cmds.Where(c => c.Key != null).ToList();
        if (checks.Count > 0)
        {
            var box = new StackPanel();
            foreach (var c in checks)
            {
                var cb = new CheckBox { IsChecked = OptionState(c.Key!), Margin = new Thickness(0, 4, 0, 4) };
                var label = new DockPanel();
                if (c.Gesture != null)
                {
                    var g = new TextBlock { Text = GestureText(c.Gesture), FontSize = 11.5, Margin = new Thickness(12, 0, 0, 0) };
                    g.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    DockPanel.SetDock(g, Dock.Right);
                    label.Children.Add(g);
                }
                label.Children.Add(new TextBlock { Text = c.Header, TextWrapping = TextWrapping.Wrap });
                cb.Content = label;
                var cmd = c;
                cb.Click += (_, _) => Safe(cmd.Action); // l'action relit l'état et rafraîchit la fenêtre
                box.Children.Add(cb);
            }
            content.Children.Add(SettingsGroup("", box));
        }

        var actions = cmds.Where(c => c.Key == null).ToList();
        if (actions.Count > 0)
        {
            var box = new StackPanel();
            foreach (var c in actions)
            {
                var b = new Button { Style = (Style)FindResource("PlainButton"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
                var row = new DockPanel { MinWidth = 380 };
                if (c.Gesture != null)
                {
                    var g = new TextBlock { Text = GestureText(c.Gesture), FontSize = 11.5, Margin = new Thickness(16, 0, 0, 0) };
                    g.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    DockPanel.SetDock(g, Dock.Right);
                    row.Children.Add(g);
                }
                row.Children.Add(new TextBlock { Text = c.Header });
                b.Content = row;
                var cmd = c;
                b.Click += (_, _) => Safe(cmd.Action);
                box.Children.Add(b);
            }
            content.Children.Add(SettingsGroup("", box));
        }
    }
}
