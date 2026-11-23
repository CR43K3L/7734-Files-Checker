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

    /// <summary>Renseigné quand le même jeu est installé ailleurs (autre lanceur ou autre disque).</summary>
    public string? DuplicateOf { get; set; }
    public Visibility DuplicateVisibility => DuplicateOf != null ? Visibility.Visible : Visibility.Collapsed;
    public string? DuplicateTip => DuplicateOf == null ? null : "Aussi installé ici : " + DuplicateOf;

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

public sealed class AppRow
{
    public AppRow(SystemTools2.InstalledApp app, long? measured) { App = app; Size = measured ?? app.EstimatedSize; }
    public SystemTools2.InstalledApp App { get; }
    public long Size { get; }
    public string Name => App.Name;
    public string Publisher => App.Publisher ?? "";
    public string Version => App.Version ?? "";
    public string SizeText => Size > 0 ? ByteSize.Format(Size) : "";
    public string DateText => App.InstallDate?.ToString("dd/MM/yyyy", Fmt.Fr) ?? "";
    public bool HasFolder => App.InstallLocation != null && System.IO.Directory.Exists(App.InstallLocation);
}

public sealed class CaptureRow : CheckRow
{
    public CaptureRow(System.IO.FileInfo file) => File = file;
    public System.IO.FileInfo File { get; }
    public string Name => File.Name;
    public long Size => File.Length;
    public string SizeText => ByteSize.Format(File.Length);
    public string Folder => File.DirectoryName ?? "";
    public string ModifiedText => Fmt.Date(File.LastWriteTimeUtc);
    public Brush Swatch => ExtensionColors.BrushFor(File.Extension.ToLowerInvariant());
}

public sealed class GhostRow : CheckRow
{
    public GhostRow(string path, long size, string reason) { Path = path; Size = size; Reason = reason; }
    public string Path { get; }
    public long Size { get; }
    public string Reason { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public string SizeText => ByteSize.Format(Size);
}

/// <summary>Une proposition de l'assistant « Faire de la place ».</summary>
public sealed class SpaceRow : CheckRow
{
    private string _state = "";
    private bool _error;
    public SpaceRow(string title, string detail, long size, Func<Task<(long Freed, string Message)>> apply)
    { Title = title; Detail = detail; Size = size; Apply = apply; }
    public string Title { get; }
    public string Detail { get; }
    public long Size { get; }
    public Func<Task<(long Freed, string Message)>> Apply { get; }
    public bool Done { get; private set; }
    public string SizeText => ByteSize.Format(Size);
    public override bool CanCheck => !Done && Size > 0;
    public string StateText => _state;
    public Brush StateBrush => (Brush)Application.Current.Resources[_error ? "WarningTextBrush" : "SuccessBrush"];
    public void SetState(string text, bool error = false, bool done = false) { _state = text; _error = error; Done |= done; RefreshAll(); }
}

public sealed class StartupRow : CheckRow
{
    public StartupRow(string name, string command, string location, string? publisher, bool needsAdmin, Func<string, bool, bool> setEnabled)
    {
        Name = name; Command = command; Location = location; Publisher = publisher ?? ""; NeedsAdmin = needsAdmin; _set = setEnabled;
        (Verdict, Why) = StartupAdvice.For(name, command);
    }
    private readonly Func<string, bool, bool> _set;
    public string Name { get; }
    public string Command { get; }
    public string Location { get; }
    public string Publisher { get; }
    public bool NeedsAdmin { get; }
    public bool Writable { get; set; } = true;
    public StartupVerdict Verdict { get; }
    public string Why { get; }
    public override bool CanCheck => Writable;
    public string AdviceText => Verdict switch
    {
        StartupVerdict.Keep => Why,
        StartupVerdict.CanDisable => "Peut être désactivé. " + Why,
        _ => "Inconnu : laisse-le si tu ne sais pas ce que c'est.",
    };
    public Brush AdviceBrush => ExtensionColors.BrushOf(Verdict switch { StartupVerdict.Keep => 0xFF32D74Bu, StartupVerdict.CanDisable => 0xFFFF9F0Au, _ => 0xFF8E8E93u });
    /// <summary>Applique le changement ; renvoie false si Windows a refusé.</summary>
    public bool Apply(bool enabled) => _set(Name, enabled);
}

/// <summary>Un programme ou un jeu dans la page « Ranger le bureau ».</summary>
public sealed class OrganizeRow : INotifyPropertyChanged
{
    /// <param name="defaultFolder">Dossier proposé si le joueur ajoute ce programme au bureau.</param>
    public OrganizeRow(ShortcutItem item, string? wanted, string defaultFolder)
    {
        Item = item;
        _onDesktop = wanted != null;
        _folder = wanted ?? defaultFolder;
    }
    public ShortcutItem Item { get; }
    public string Name => Item.Name;
    public string KindText => Item.IsGame ? "Jeu" : "Application";
    public string NowText => Item.CurrentFolder == null ? "Pas sur le bureau" : Item.CurrentFolder.Length == 0 ? "Sur le bureau, en vrac" : $"Sur le bureau, dans {Item.CurrentFolder}";
    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set { _icon = value; Changed(nameof(Icon)); } }

    private bool _onDesktop;
    public bool OnDesktop { get => _onDesktop; set { _onDesktop = value; Changed(nameof(OnDesktop)); Changed(nameof(FolderText)); Edited?.Invoke(); } }
    private string _folder;
    public string Folder { get => _folder; set { _folder = value; Changed(nameof(FolderText)); Edited?.Invoke(); } }
    public string FolderText => !_onDesktop ? "—" : _folder.Length == 0 ? "En vrac sur le bureau" : _folder;

    /// <summary>État voulu : dossier ("" = en vrac) ou null (pas sur le bureau).</summary>
    public string? Wanted => _onDesktop ? _folder : null;
    public bool IsChanged => Wanted != Item.CurrentFolder;

    public Action? Edited { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
