using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Files7734.App;

/// <summary>
/// Sous Windows 11, active le fond translucide « Mica » derrière la barre latérale et les coins arrondis.
/// Sur les versions plus anciennes, la fenêtre garde un fond gris clair opaque.
/// </summary>
internal static class GlassEffect
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2; // « Mica » : fond teinté par le fond d'écran
    private const int DWMWCP_ROUND = 2;

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int off = 0, round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref off, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            // Le flou système n'existe qu'à partir de Windows 11 22H2 (build 22621).
            if (Environment.OSVersion.Version.Build < 22621) return;

            int backdrop = DWMSBT_MAINWINDOW;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0) return;

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;

            // Barre latérale translucide ; la zone principale garde son propre fond clair.
            window.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xF2, 0xF2, 0xF5));
        }
        catch (Exception)
        {
            // Effet purement visuel : en cas de souci on garde le fond opaque.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
