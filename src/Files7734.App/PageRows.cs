using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Files7734.App;

/// <summary>Une entrée du menu « Outils » de la barre latérale.</summary>
public sealed class NavItem : INotifyPropertyChanged
{
    private string _badge = "";
    public NavItem(string key, string glyph, string label) { Key = key; Glyph = glyph; Label = label; }
    public string Key { get; }
    public string Glyph { get; }
    public string Label { get; }
    public string Badge
    {
        get => _badge;
        set { _badge = value; Changed(nameof(Badge)); Changed(nameof(BadgeVisibility)); }
    }
    public Visibility BadgeVisibility => _badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Ligne avec une case à cocher.</summary>
public abstract class CheckRow : INotifyPropertyChanged
{
    private bool _checked;
    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Changed(nameof(IsChecked));
            CheckChanged?.Invoke();
        }
    }
    public virtual bool CanCheck => true;
    public Action? CheckChanged { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    public void RefreshAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class GameRow : INotifyPropertyChanged
{
    private static readonly Dictionary<string, uint> Colors = new()
    {
        ["Steam"] = 0xFF1B9AF7, ["Epic Games"] = 0xFF8E8E93, ["Riot"] = 0xFFFF4655, ["GOG"] = 0xFFAF52DE,
        ["Ubisoft"] = 0xFF0A84FF, ["Battle.net"] = 0xFF00AEFF, ["EA"] = 0xFFFF3B30, ["Rockstar"] = 0xFFFFCC00, ["Xbox"] = 0xFF34C759,
    };

    public GameRow(GameInfo game) => Game = game;
    public GameInfo Game { get; }
    public string Name => Game.Name;
    public string Launcher => Game.Launcher;
    public string InstallPath => Game.InstallPath;
    public Brush LauncherBrush => ExtensionColors.BrushOf(Colors.TryGetValue(Game.Launcher, out var c) ? c : 0xFF8E8E93);
    public long? Size { get; private set; }
    public double Percent { get; private set; }
    public string SizeText => Size is long s ? ByteSize.Format(s) : "…";
    public string PercentText => Size == null ? "" : Fmt.Percent(Percent);
    public double BarWidth => Math.Max(0, Math.Min(56, 56 * Percent / 100));

    public void SetSize(long size) { Size = size; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); }
    public void SetPercent(double p) { Percent = p; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CleanRow : CheckRow
{
    private CleanupMeasure? _measure;
    private string _state = "";
    private bool _error;
    public CleanRow(CleanupCategory c) => Category = c;
    public CleanupCategory Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public CleanupMeasure? Measure { get => _measure; set { _measure = value; RefreshAll(); } }
    public string SizeText => _measure == null ? "…" : ByteSize.Format(_measure.Bytes);
    public string FilesText => _measure == null || Category.IsSpecial ? "" : Fmt.Count(_measure.Files) + " fichiers";
    public override bool CanCheck => _measure is { Bytes: > 0 } || (_measure != null && _measure.Files > 0);
    public string StateText => _state;
    public Brush StateBrush => (Brush)Application.Current.Resources[_error ? "WarningTextBrush" : "SuccessBrush"];
    public void SetState(string text, bool error = false) { _state = text; _error = error; RefreshAll(); }
}

/// <summary>Ligne de la page Doublons : en-tête de groupe ou fichier.</summary>
public sealed class DupeRow : CheckRow
{
    public DupeRow(DuplicateGroup group, FsNode? file) { Group = group; File = file; }
    public DuplicateGroup Group { get; }
    public FsNode? File { get; }
    public bool IsHeader => File == null;
    public string Name => IsHeader ? $"{Group.Files.Count} copies de « {Group.Files[0].Name} »  ·  {ByteSize.Format(Group.Wasted)} récupérables" : File!.Name;
    public FontWeight NameWeight => IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
    public Thickness Indent => IsHeader ? new Thickness(0) : new Thickness(14, 0, 0, 0);
    public string SizeText => ByteSize.Format(Group.FileSize);
    public string Folder => IsHeader ? "" : File!.Parent?.FullPath ?? "";
    public string ModifiedText => IsHeader ? "" : Fmt.Date(File!.LastModifiedUtc);
    public Visibility CheckVisibility => IsHeader ? Visibility.Hidden : Visibility.Visible;
    public override bool CanCheck => !IsHeader;
}

public sealed class OldFileRow : CheckRow
{
    public OldFileRow(FsNode node) => Node = node;
    public FsNode Node { get; }
    public string Name => Node.Name;
    public string SizeText => ByteSize.Format(Node.Size);
    public string Folder => Node.Parent?.FullPath ?? "";
    public Brush Swatch => ExtensionColors.BrushFor(Node.Extension);
    public string ModifiedText => Fmt.Date(Node.LastModifiedUtc);
    public string AgeText
    {
        get
        {
            double years = (DateTime.UtcNow - Node.LastModifiedUtc).TotalDays / 365.25;
            return years >= 1 ? $"{Math.Floor(years):0} an{(years >= 2 ? "s" : "")}" : $"{(DateTime.UtcNow - Node.LastModifiedUtc).TotalDays / 30:0} mois";
        }
    }
}

public sealed class ChangeRow
{
    public ChangeRow(FolderChange c, string root) { Change = c; FullPath = System.IO.Path.Combine(root, c.RelativePath); }
    public FolderChange Change { get; }
    public string FullPath { get; }
    public string Path => Change.RelativePath;
    public string DeltaText => (Change.Delta >= 0 ? "+ " : "− ") + ByteSize.Format(Math.Abs(Change.Delta));
    public string BeforeText => Change.Kind == ChangeKind.New ? "" : ByteSize.Format(Change.Before);
    public string AfterText => Change.Kind == ChangeKind.Removed ? "" : ByteSize.Format(Change.After);
    public string Glyph => Change.Delta >= 0 ? "" : "";
    public Brush KindBrush => Change.Delta >= 0 ? ExtensionColors.BrushOf(0xFFFF9F0A) : (Brush)Application.Current.Resources["SuccessBrush"];
    public string KindText => Change.Kind switch
    {
        ChangeKind.New => "Nouveau dossier",
        ChangeKind.Removed => "Supprimé",
        ChangeKind.Grown => "A grossi",
        _ => "A diminué",
    };
}
