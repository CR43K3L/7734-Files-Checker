using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Files7734.App;

/// <summary>
/// Sous Windows 11, active le flou natif derrière la fenêtre (effet « verre ») et les coins arrondis.
/// Sur les versions plus anciennes, la fenêtre garde son dégradé opaque.
/// </summary>
internal static class GlassEffect
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // flou « acrylique »
    private const int DWMWCP_ROUND = 2;

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1, round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            // Le flou système n'existe qu'à partir de Windows 11 22H2 (build 22621).
            if (Environment.OSVersion.Version.Build < 22621) return;

            int backdrop = DWMSBT_TRANSIENTWINDOW;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0) return;

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;

            // Teinte sombre semi-transparente : le bureau flouté apparaît à travers.
            window.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0xB8, 0x12, 0x0E, 0x24), 0),
                    new(Color.FromArgb(0xA6, 0x1B, 0x12, 0x36), 0.5),
                    new(Color.FromArgb(0xB8, 0x0B, 0x1A, 0x2C), 1),
                },
                new Point(0, 0), new Point(1, 1));
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
