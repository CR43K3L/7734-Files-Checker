using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Files7734.App;

internal static class Fmt
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public static string Count(long n) => n.ToString("N0", Fr);
    public static string Percent(double p) => p.ToString("0.0", Fr) + " %";
    public static string Date(DateTime utc) => utc == default ? "" : utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr);
}

/// <summary>Une ligne de l'arborescence (liste "aplatie" pour rester fluide avec des millions de fichiers).</summary>
public sealed class TreeRow : INotifyPropertyChanged
{
    private static readonly Brush FolderBrush = ExtensionColors.BrushOf(0xFF1E9BFF);
    private readonly Action<TreeRow> _onToggle;
    private bool _expanded;

    public TreeRow(FsNode node, int depth, Action<TreeRow> onToggle)
    {
        Node = node;
        Depth = depth;
        _onToggle = onToggle;
    }

    public FsNode Node { get; }
    public int Depth { get; }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value || !HasChildren) return;
            _expanded = value;
            Changed(nameof(IsExpanded));
            _onToggle(this);
        }
    }

    public bool HasChildren => Node.IsDirectory && Node.Children is { Count: > 0 };
    public Visibility ExpanderVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public string Icon => Node.IsDirectory ? "" : "";
    public Brush IconBrush => Node.IsDirectory ? FolderBrush : ExtensionColors.BrushFor(Node.Extension);
    public string Name => Node.Name;
    public string SizeText => ByteSize.Format(Node.Size);
    public string AllocatedText => ByteSize.Format(Node.Allocated);

    public double Percent => Depth == 0 || Node.Parent is not { Size: > 0 } p ? 100 : 100.0 * Node.Size / p.Size;
    public string PercentText => Fmt.Percent(Percent);
    public double PercentBarWidth => Math.Max(0, Math.Min(56, 56 * Percent / 100));

    public string FilesText => Node.IsDirectory ? Fmt.Count(Node.FileCount) : "";
    public string FoldersText => Node.IsDirectory ? Fmt.Count(Node.FolderCount) : "";
    public string ModifiedText => Fmt.Date(Node.LastModifiedUtc);

    /// <summary>Met à jour l'affichage (après une suppression par exemple).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    internal void SetExpandedSilently(bool value)
    {
        _expanded = value;
        Changed(nameof(IsExpanded));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ExtensionRow
{
    public ExtensionRow(ExtensionStat stat, long total)
    {
        Stat = stat;
        Percent = total > 0 ? 100.0 * stat.Size / total : 0;
    }

    public ExtensionStat Stat { get; }
    public double Percent { get; }
    public string Name => Stat.Extension.Length == 0 ? "(sans extension)" : Stat.Extension;
    public Brush Swatch => ExtensionColors.BrushFor(Stat.Extension);
    public string SizeText => ByteSize.Format(Stat.Size);
    public string PercentText => Fmt.Percent(Percent);
    public double PercentBarWidth => Math.Max(0, Math.Min(56, 56 * Percent / 100));
    public string CountText => Fmt.Count(Stat.FileCount);
}

public sealed class FileRow
{
    public FileRow(FsNode node) => Node = node;
    public FsNode Node { get; }
    public string Name => Node.Name;
    public string SizeText => ByteSize.Format(Node.Size);
    public string Folder => Node.Parent?.FullPath ?? "";
    public Brush Swatch => ExtensionColors.BrushFor(Node.Extension);
    public string ModifiedText => Fmt.Date(Node.LastModifiedUtc);
}
