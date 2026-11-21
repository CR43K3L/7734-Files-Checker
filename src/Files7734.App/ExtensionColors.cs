using System.Windows.Media;

namespace Files7734.App;

/// <summary>Attribue une couleur à chaque extension : les plus volumineuses reçoivent les couleurs les plus distinctes.</summary>
public static class ExtensionColors
{
    private static readonly uint[] Palette =
    {
        0xFF7C3AED, 0xFFF28E2B, 0xFF4E79A7, 0xFF59A14F, 0xFFE15759, 0xFFEDC948, 0xFF76B7B2, 0xFFB07AA1,
        0xFFFF9DA7, 0xFF9C755F, 0xFF2BB5D8, 0xFFD37295, 0xFF8CD17D, 0xFFB6992D, 0xFF499894, 0xFFE377C2,
    };

    private static readonly uint[] Muted =
    {
        0xFF8A8FA3, 0xFF9C94AE, 0xFF8FA39A, 0xFFA39A8F, 0xFF8F9AA3, 0xFFA38F9C,
    };

    public const uint FolderColor = 0xFF5B6274;

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
