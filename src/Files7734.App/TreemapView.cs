using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Files7734.App;

/// <summary>
/// Carte en rectangles : chaque fichier est un rectangle dont l'aire est proportionnelle à sa taille,
/// coloré selon son extension. Dessinée pixel par pixel en arrière-plan pour rester fluide.
/// </summary>
public sealed class TreemapView : Grid
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Rectangle _selection = new() { Stroke = Brushes.White, StrokeThickness = 2, Visibility = Visibility.Collapsed };
    private readonly Rectangle _selectionShadow = new() { Stroke = Brushes.Black, StrokeThickness = 4, Opacity = 0.55, Visibility = Visibility.Collapsed };
    private readonly Rectangle _hover = new() { Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), StrokeThickness = 1, Visibility = Visibility.Collapsed };
    private readonly Border _tip;
    private readonly TextBlock _tipTitle = new() { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _tipText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD0, 0xE4)), FontSize = 11.5 };
    private readonly TextBlock _empty = new()
    {
        Text = "Lance un scan pour voir la carte de ton disque",
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0x9E)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 14,
    };
    private readonly DispatcherTimer _resizeTimer;

    private List<TreemapRect> _rects = new();
    private double _scaleX = 1, _scaleY = 1;
    private int _version;
    private FsNode? _root;
    private FsNode? _selected;

    public TreemapView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1A, 0x22));
        ClipToBounds = true;
        Children.Add(_image);
        Children.Add(_empty);
        Children.Add(_overlay);
        _overlay.Children.Add(_hover);
        _overlay.Children.Add(_selectionShadow);
        _overlay.Children.Add(_selection);

        var tipPanel = new StackPanel { MaxWidth = 520 };
        tipPanel.Children.Add(_tipTitle);
        tipPanel.Children.Add(_tipText);
        _tip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 0x17, 0x15, 0x1F)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Child = tipPanel,
            Visibility = Visibility.Collapsed,
        };
        _overlay.Children.Add(_tip);

        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); Render(); };
        SizeChanged += (_, _) => { _resizeTimer.Stop(); _resizeTimer.Start(); };
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { _hover.Visibility = Visibility.Collapsed; _tip.Visibility = Visibility.Collapsed; };
        MouseLeftButtonDown += OnMouseDown;
        MouseRightButtonUp += OnRightClick;
        Cursor = Cursors.Hand;
    }

    /// <summary>Clic sur un élément.</summary>
    public event Action<FsNode>? NodeClicked;
    /// <summary>Double-clic : demande de zoom sur un dossier.</summary>
    public event Action<FsNode>? NodeDoubleClicked;
    /// <summary>Clic droit : menu contextuel.</summary>
    public event Action<FsNode>? NodeRightClicked;

    public FsNode? Root
    {
        get => _root;
        set
        {
            if (_root == value) return;
            _root = value;
            Render();
        }
    }

    public FsNode? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateSelection();
        }
    }

    /// <summary>Redessine la carte (après un scan, une suppression…).</summary>
    public async void Render()
    {
        int version = ++_version;
        var root = _root;
        _empty.Visibility = root == null ? Visibility.Visible : Visibility.Collapsed;
        if (root == null || ActualWidth < 4 || ActualHeight < 4)
        {
            _image.Source = null;
            _rects = new();
            UpdateSelection();
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        int pw = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        int ph = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));

        var (pixels, rects) = await Task.Run(() =>
        {
            var r = TreemapLayout.Layout(root, pw, ph);
            return (Paint(r, pw, ph), r);
        });
        if (version != _version) return; // un rendu plus récent a été demandé entre-temps

        var bmp = new WriteableBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, pw * 4, 0);
        bmp.Freeze();
        _image.Source = bmp;
        _rects = rects;
        _scaleX = dpi.DpiScaleX;
        _scaleY = dpi.DpiScaleY;
        _hover.Visibility = Visibility.Collapsed;
        UpdateSelection();
    }

    private static int[] Paint(List<TreemapRect> rects, int w, int h)
    {
        var px = new int[w * h];
        Array.Fill(px, unchecked((int)0xFF1B1A22));
        foreach (var r in rects)
        {
            int x0 = Math.Max(0, (int)r.X), y0 = Math.Max(0, (int)r.Y);
            int x1 = Math.Min(w, (int)(r.X + r.W)), y1 = Math.Min(h, (int)(r.Y + r.H));
            if (x1 <= x0 || y1 <= y0) continue;

            if (!r.IsLeaf)
            {
                // Fond d'un dossier : visible à travers les marges entre ses enfants.
                int shade = Math.Max(0x16, 0x34 - r.Depth * 4);
                int c = unchecked((int)(0xFF000000 | (uint)(shade << 16) | (uint)((shade - 2) << 8) | (uint)(shade + 10)));
                for (int y = y0; y < y1; y++) Array.Fill(px, c, y * w + x0, x1 - x0);
                continue;
            }

            // Un pixel d'espace à droite et en bas sépare les rectangles voisins.
            if (x1 - x0 >= 3) x1--;
            if (y1 - y0 >= 3) y1--;
            uint baseColor = r.Node.IsDirectory ? ExtensionColors.FolderColor : ExtensionColors.ColorFor(r.Node.Extension);
            int br = (int)((baseColor >> 16) & 0xFF), bg = (int)((baseColor >> 8) & 0xFF), bb = (int)(baseColor & 0xFF);
            double rw = Math.Max(1, x1 - x0), rh = Math.Max(1, y1 - y0);

            for (int y = y0; y < y1; y++)
            {
                double ty = (y - y0) / rh;
                int row = y * w;
                for (int x = x0; x < x1; x++)
                {
                    double tx = (x - x0) / rw;
                    // Léger relief : plus clair en haut à gauche, plus sombre en bas à droite.
                    double f = 1.22 - 0.38 * ty - 0.16 * tx;
                    int cr = Math.Min(255, (int)(br * f)), cg = Math.Min(255, (int)(bg * f)), cb = Math.Min(255, (int)(bb * f));
                    px[row + x] = unchecked((int)(0xFF000000 | (uint)(cr << 16) | (uint)(cg << 8) | (uint)cb));
                }
            }
        }
        return px;
    }

    private FsNode? HitTest(Point p)
    {
        double x = p.X * _scaleX, y = p.Y * _scaleY;
        for (int i = _rects.Count - 1; i >= 0; i--)
            if (_rects[i].Contains(x, y)) return _rects[i].Node;
        return null;
    }

    private bool TryGetRect(FsNode node, out Rect rect)
    {
        foreach (var r in _rects)
        {
            if (r.Node == node)
            {
                rect = new Rect(r.X / _scaleX, r.Y / _scaleY, r.W / _scaleX, r.H / _scaleY);
                return true;
            }
        }
        rect = default;
        return false;
    }

    private void UpdateSelection()
    {
        // Si l'élément sélectionné est trop petit pour être dessiné, on encadre son parent visible le plus proche.
        for (var n = _selected; n != null; n = n.Parent)
        {
            if (n == _root && n != _selected) break;
            if (TryGetRect(n, out var rect))
            {
                Place(_selection, rect);
                Place(_selectionShadow, rect);
                return;
            }
        }
        _selection.Visibility = _selectionShadow.Visibility = Visibility.Collapsed;
    }

    private static void Place(Rectangle shape, Rect r)
    {
        shape.Width = Math.Max(2, r.Width);
        shape.Height = Math.Max(2, r.Height);
        Canvas.SetLeft(shape, r.X);
        Canvas.SetTop(shape, r.Y);
        shape.Visibility = Visibility.Visible;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var node = HitTest(p);
        if (node == null || !TryGetRect(node, out var rect))
        {
            _hover.Visibility = Visibility.Collapsed;
            _tip.Visibility = Visibility.Collapsed;
            return;
        }
        Place(_hover, rect);

        _tipTitle.Text = node.Name;
        string detail = node.IsDirectory ? $"Dossier · {Fmt.Count(node.FileCount)} fichiers" : (node.Extension.Length > 0 ? $"Fichier {node.Extension}" : "Fichier");
        string pct = _root is { Size: > 0 } ? " · " + Fmt.Percent(100.0 * node.Size / _root.Size) : "";
        _tipText.Text = $"{ByteSize.Format(node.Size)}{pct}\n{detail}\n{node.Parent?.FullPath}";
        _tip.Visibility = Visibility.Visible;
        _tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tx = p.X + 16, ty = p.Y + 18;
        if (tx + _tip.DesiredSize.Width > ActualWidth) tx = Math.Max(0, p.X - _tip.DesiredSize.Width - 8);
        if (ty + _tip.DesiredSize.Height > ActualHeight) ty = Math.Max(0, p.Y - _tip.DesiredSize.Height - 8);
        Canvas.SetLeft(_tip, tx);
        Canvas.SetTop(_tip, ty);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var node = HitTest(e.GetPosition(this));
        if (node == null) return;
        if (e.ClickCount == 2) NodeDoubleClicked?.Invoke(node);
        else NodeClicked?.Invoke(node);
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var node = HitTest(e.GetPosition(this));
        if (node != null) NodeRightClicked?.Invoke(node);
    }
}
