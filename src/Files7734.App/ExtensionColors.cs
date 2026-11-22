using System.Windows.Media;

namespace Files7734.App;

/// <summary>Attribue une couleur à chaque extension : les plus volumineuses reçoivent les couleurs les plus distinctes.</summary>
public static class ExtensionColors
{
    // Couleurs vives et douces, faciles à distinguer sur fond clair.
    private static readonly uint[] Palette =
    {
        0xFF0A7CFF, 0xFFFF9500, 0xFF34C759, 0xFFFF2D55, 0xFFAF52DE, 0xFF5AC8FA, 0xFFFFCC00, 0xFF5856D6,
        0xFFFF3B30, 0xFF00C7BE, 0xFFA2845E, 0xFF30B0C7, 0xFFFF6482, 0xFF8E8CD8, 0xFF66D47E, 0xFFFFB340,
    };

    private static readonly uint[] Muted =
    {
        0xFFAEAEB2, 0xFFB4B0BD, 0xFFADB8B2, 0xFFBDB4AD, 0xFFADB4BD, 0xFFBDADB8,
    };

    public const uint FolderColor = 0xFF8E8E93;

    private static volatile Dictionary<string, uint> _map = new();
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = new();

    public static void Assign(IEnumerable<string> extensionsBySizeDesc)
    {
        var map = new Dictionary<string, uint>(StringComparer.Ordinal);
        int i = 0;
        foreach (var ext in extensionsBySizeDesc)
        {
            if (i >= Palette.Length) break;
            map[ext] = Palette[i++];
        }
        _map = map;
    }

    /// <summary>Couleur ARGB d'une extension. Utilisable depuis n'importe quel thread.</summary>
    public static uint ColorFor(string extension)
    {
        if (_map.TryGetValue(extension, out var c)) return c;
        int h = 0;
        foreach (char ch in extension) h = h * 31 + ch;
        return Muted[(h & 0x7FFFFFFF) % Muted.Length];
    }

    public static uint ColorOf(FsNode node) => node.IsDirectory ? FolderColor : ColorFor(node.Extension);

    public static SolidColorBrush BrushFor(string extension) => BrushOf(ColorFor(extension));

    public static SolidColorBrush BrushOf(uint argb)
    {
        lock (Brushes)
        {
            if (!Brushes.TryGetValue(argb, out var b))
            {
                b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
                b.Freeze();
                Brushes[argb] = b;
            }
            return b;
        }
    }
}
