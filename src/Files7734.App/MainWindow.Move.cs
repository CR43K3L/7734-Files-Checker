using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Files7734.App;

/// <summary>Déplacement d'un jeu vers un autre disque (copie + lien à l'ancien emplacement).</summary>
public partial class MainWindow
{
    private async void GameMove_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } g) return;
        string src = g.InstallPath;
        if (g.Launcher == "Xbox")
        {
            MessageBox.Show(this, "Les jeux Xbox sont protégés par Windows : déplace-les depuis l'application Xbox (Paramètres > Gestion des jeux).",
                "Déplacer un jeu", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SystemTools.IsJunction(src))
        {
            MessageBox.Show(this, "Ce jeu a déjà été déplacé : son ancien dossier n'est plus qu'un lien vers le nouveau.", "Déplacer un jeu",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFolderDialog { Title = $"Où mettre « {g.Name} » ? (choisis un dossier sur un autre disque)" };
        if (dlg.ShowDialog(this) != true) return;
        await MoveGame(g, dlg.FolderName, ask: true);
    }

    /// <summary>Déplace un jeu dans <paramref name="targetParent"/>. Renvoie l'espace libéré (0 si échec).</summary>
    private async Task<long> MoveGame(GameRow g, string targetParent, bool ask)
    {
        string src = g.InstallPath;
        string srcRoot = Path.GetPathRoot(src) ?? "", dstRoot = Path.GetPathRoot(targetParent) ?? "";
        if (string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, $"Ce dossier est sur le même disque ({srcRoot.TrimEnd('\\')}) : ça ne libérerait aucune place. Choisis un autre disque.",
                "Déplacer un jeu", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }
        long size = g.Size ?? 0;
        try
        {
            long free = new DriveInfo(dstRoot).AvailableFreeSpace;
            if (size > 0 && free < size + 1024L * 1024 * 1024)
            {
                MessageBox.Show(this, $"Pas assez de place sur {dstRoot.TrimEnd('\\')} : il faut {ByteSize.Format(size)} et il reste {ByteSize.Format(free)}.",
                    "Déplacer un jeu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 0;
            }
        }
        catch (Exception) { }

        string dest = Path.Combine(targetParent, Path.GetFileName(src));
        if (ask && MessageBox.Show(this,
            $"Déplacer « {g.Name} » ({g.SizeText}) vers :\n{dest}\n\n" +
            $"Ferme d'abord le jeu et {g.Launcher}.\n\n" +
            $"Le jeu est copié, vérifié, puis supprimé de {srcRoot.TrimEnd('\\')}. Un lien est laissé à l'ancien emplacement : " +
            $"{g.Launcher} continuera de le trouver, sans rien réinstaller. En cas de problème, tout est remis comme avant.",
            "Déplacer un jeu", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return 0;
        Directory.CreateDirectory(targetParent);

        var progress = new MoveProgress();
        var cts = new CancellationTokenSource();
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 12, 0, 8) };
        var text = new TextBlock { Text = "Préparation…", TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var cancel = new Button { Content = "Annuler", Style = (Style)FindResource("PlainButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        panel.Children.Add(new TextBlock { Text = $"Déplacement de « {g.Name} »", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(text);
        panel.Children.Add(bar);
        panel.Children.Add(detail);
        panel.Children.Add(cancel);
        var win = new Window
        {
            Title = "Déplacer un jeu", Owner = this, Content = panel, Width = 460, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, FontFamily = FontFamily,
        };
        win.SetResourceReference(BackgroundProperty, "CardBrush");
        win.SetResourceReference(ForegroundProperty, "TextBrush");
        win.SourceInitialized += (_, _) => WindowFrame.Apply(win);
        bool finished = false;
        win.Closing += (_, ev) => { if (!finished) { ev.Cancel = true; cts.Cancel(); } };
        cancel.Click += (_, _) => { cts.Cancel(); cancel.IsEnabled = false; text.Text = "Annulation, remise en place…"; };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (progress.TotalBytes <= 0) return;
            bar.Value = (double)progress.CopiedBytes / progress.TotalBytes;
            if (!cts.IsCancellationRequested) text.Text = $"Copie : {ByteSize.Format(progress.CopiedBytes)} sur {ByteSize.Format(progress.TotalBytes)}";
            detail.Text = progress.CurrentFile;
        };
        timer.Start();
        win.Show();
        IsEnabled = false;

        (MoveResult result, string? error, string destination) outcome;
        try
        {
            outcome = await Task.Run(() => FolderMover.Move(src, targetParent, SystemTools.CreateJunction, progress, cts.Token));
        }
        finally
        {
            timer.Stop();
            finished = true;
            win.Close();
            IsEnabled = true;
        }

        switch (outcome.result)
        {
            case MoveResult.Done:
                StatusText.Text = $"« {g.Name} » déplacé vers {outcome.destination} · {ByteSize.Format(size)} libérés sur {srcRoot.TrimEnd('\\')}.";
                if (ask) MessageBox.Show(this, $"« {g.Name} » est maintenant dans :\n{outcome.destination}\n\n{ByteSize.Format(size)} libérés sur {srcRoot.TrimEnd('\\')}. Lance le jeu depuis {g.Launcher} comme d'habitude.",
                    "Déplacer un jeu", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case MoveResult.DoneButOldCopyLeft:
                MessageBox.Show(this, $"Le jeu est déplacé et fonctionne, mais l'ancienne copie n'a pas pu être supprimée entièrement :\n{outcome.error}\n\nTu peux supprimer ce dossier toi-même.",
                    "Déplacer un jeu", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MoveResult.SourceInUse:
                MessageBox.Show(this, $"Impossible de déplacer « {g.Name} » : un de ses fichiers est utilisé. Ferme le jeu et {g.Launcher} (y compris près de l'horloge), puis réessaie.",
                    "Déplacer un jeu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 0;
            case MoveResult.Cancelled:
                StatusText.Text = "Déplacement annulé : le jeu est resté à sa place.";
                return 0;
            default:
                MessageBox.Show(this, $"Le déplacement a échoué et le jeu a été remis à sa place.\n\n{outcome.error}", "Déplacer un jeu",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return 0;
        }
        LoadDrives();
        if (_result != null && FindInResult(src) is { } node)
        {
            // L'ancien dossier n'est plus qu'un lien : il ne compte plus sur ce disque.
            AfterRemoval(new[] { node });
        }
        LoadGames();
        return size;
    }
}
