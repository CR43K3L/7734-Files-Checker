using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Files7734.App;

public enum ThemeMode { System, Light, Dark }

/// <summary>Couleurs de l'interface en mode clair et sombre. Les styles les lisent via DynamicResource.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    /// <summary>Prévenu quand le thème change (la carte se redessine).</summary>
    public static event Action? Changed;

    private static readonly Dictionary<string, uint> Light = new()
    {
        ["TextBrush"] = 0xFF1D1D1F, ["MutedBrush"] = 0xFF6E6E73, ["FaintBrush"] = 0xFFA1A1A6,
        ["AccentBrush"] = 0xFF0A7CFF, ["AccentHoverBrush"] = 0xFF3395FF, ["AccentPressedBrush"] = 0xFF0063D1, ["AccentGradient"] = 0xFF0A7CFF,
        ["SidebarBrush"] = 0xFFE8E8ED, ["ContentBrush"] = 0xFFF5F5F7, ["CardBrush"] = 0xFFFFFFFF, ["CardBorderBrush"] = 0x14000000,
        ["RowHoverBrush"] = 0xFFF2F2F5, ["RowSelectedBrush"] = 0xFFDCEBFF, ["TrackBrush"] = 0xFFE8E8ED, ["HairlineBrush"] = 0xFFE5E5EA,
        ["GlassEdge"] = 0xFFE5E5EA, ["SidebarSelectedBrush"] = 0x1A000000, ["HoverOverlayBrush"] = 0x12000000, ["ShadowBrush"] = 0x17000000,
        ["ControlBrush"] = 0xFFFFFFFF, ["ControlHoverBrush"] = 0xFFF7F7F9, ["ControlPressedBrush"] = 0xFFECECF0, ["ControlBorderBrush"] = 0x1F000000,
        ["InputBrush"] = 0xFFE3E3E8, ["InputFocusBrush"] = 0xFFFFFFFF, ["SegmentBrush"] = 0xFFEEEEF0, ["SegmentSelectedBrush"] = 0xFFFFFFFF,
        ["PopupBrush"] = 0xFAFFFFFF, ["ScrollThumbBrush"] = 0x33000000, ["ScrollThumbHoverBrush"] = 0x59000000,
        ["SuccessBrush"] = 0xFF248A3D, ["WarningBgBrush"] = 0x1AFF9500, ["WarningTextBrush"] = 0xFFC93400,
    };

    private static readonly Dictionary<string, uint> Dark = new()
    {
        ["TextBrush"] = 0xFFF5F5F7, ["MutedBrush"] = 0xFFA1A1A6, ["FaintBrush"] = 0xFF6E6E73,
        ["AccentBrush"] = 0xFF0A84FF, ["AccentHoverBrush"] = 0xFF409CFF, ["AccentPressedBrush"] = 0xFF0071E3, ["AccentGradient"] = 0xFF0A84FF,
        ["SidebarBrush"] = 0xFF232325, ["ContentBrush"] = 0xFF1C1C1E, ["CardBrush"] = 0xFF2C2C2E, ["CardBorderBrush"] = 0x1FFFFFFF,
        ["RowHoverBrush"] = 0xFF3A3A3C, ["RowSelectedBrush"] = 0xFF1D4A80, ["TrackBrush"] = 0xFF3A3A3C, ["HairlineBrush"] = 0xFF3A3A3C,
        ["GlassEdge"] = 0xFF3A3A3C, ["SidebarSelectedBrush"] = 0x24FFFFFF, ["HoverOverlayBrush"] = 0x17FFFFFF, ["ShadowBrush"] = 0x40000000,
        ["ControlBrush"] = 0xFF3A3A3C, ["ControlHoverBrush"] = 0xFF444446, ["ControlPressedBrush"] = 0xFF505052, ["ControlBorderBrush"] = 0x26FFFFFF,
        ["InputBrush"] = 0xFF2C2C2E, ["InputFocusBrush"] = 0xFF1C1C1E, ["SegmentBrush"] = 0xFF1C1C1E, ["SegmentSelectedBrush"] = 0xFF48484A,
        ["PopupBrush"] = 0xF52C2C2E, ["ScrollThumbBrush"] = 0x40FFFFFF, ["ScrollThumbHoverBrush"] = 0x70FFFFFF,
        ["SuccessBrush"] = 0xFF32D74B, ["WarningBgBrush"] = 0x26FF9F0A, ["WarningTextBrush"] = 0xFFFF9F0A,
    };

    public static void Apply(ThemeMode mode)
    {
        IsDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark(),
        };
        var res = Application.Current.Resources;
        foreach (var (key, argb) in IsDark ? Dark : Light)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            brush.Freeze();
            res[key] = brush;
        }
        foreach (Window w in Application.Current.Windows) WindowFrame.Apply(w);
        Changed?.Invoke();
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }
}
