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
    private readonly Rectangle _selectionShadow = new() { Stroke = Brushes.White, StrokeThickness = 5, Opacity = 0.9, Visibility = Visibility.Collapsed };
    private readonly Rectangle _hover = new() { Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
    private readonly Border _tip;
    private readonly TextBlock _tipTitle = new() { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F)) };
    private readonly TextBlock _tipText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x73)), FontSize = 11.5 };
    private readonly TextBlock _empty = new()
    {
        Text = "Lance un scan pour voir la carte de ton disque",
        Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xA6)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 14,
    };
    private readonly DispatcherTimer _resizeTimer;

    private readonly LabelLayer _labels = new() { IsHitTestVisible = false };
    private List<TreemapRect> _rects = new();
    private FsNode? _freeNode;
    private double _scaleX = 1, _scaleY = 1;
    private int _version;
    private FsNode? _root;
    private FsNode? _selected;
    private (TreemapRect? From, FsNode? ZoomOutOf)? _pendingZoom;
    private readonly ScaleTransform _zoomScale = new();
    private readonly TranslateTransform _zoomMove = new();

    public TreemapView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));
        ClipToBounds = true;
        var zoom = new TransformGroup();
        zoom.Children.Add(_zoomScale);
        zoom.Children.Add(_zoomMove);
        _image.RenderTransform = zoom;
        Children.Add(_image);
        Children.Add(_labels);
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
            Background = new SolidColorBrush(Color.FromArgb(250, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.18 },
            Padding = new Thickness(10, 7, 10, 7),
            Child = tipPanel,
            Visibility = Visibility.Collapsed,
        };
        _overlay.Children.Add(_tip);

        ApplyThemeColors();
        Theme.Changed += () => { ApplyThemeColors(); Render(); };

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
            var old = _root;
            _pendingZoom = null;
            if (old != null && value != null && _rects.Count > 0)
            {
                if (FsTree.IsInside(value, old))
                {
                    // Zoom avant : le dossier choisi grandit depuis sa place actuelle.
                    for (var n = value; n != null && n != old; n = n.Parent)
                    {
                        int i = _rects.FindIndex(r => r.Node == n);
                        if (i >= 0) { _pendingZoom = (_rects[i], null); break; }
                    }
                }
                else if (FsTree.IsInside(old, value))
                {
                    _pendingZoom = (null, old); // zoom arrière : l'ancien dossier rétrécit vers sa place
                }
            }
            _root = value;
            Render();
        }
    }

    /// <summary>La carte représente l'espace réellement occupé sur le disque plutôt que la taille des fichiers.</summary>
    public bool UseAllocated { get; set; }

    /// <summary>Affiche les noms sur les rectangles assez grands.</summary>
    public bool ShowNames { get; set; } = true;

    /// <summary>Si renseigné, un bloc « Espace libre » de cette taille est ajouté à la carte.</summary>
    public long? FreeSpaceBytes { get; set; }

    public FsNode? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateSelection();
        }
    }

    private void ApplyThemeColors()
    {
        bool dark = Theme.IsDark;
        Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF5, 0xF5, 0xF7));
        _tip.Background = new SolidColorBrush(dark ? Color.FromArgb(250, 0x2C, 0x2C, 0x2E) : Color.FromArgb(250, 0xFF, 0xFF, 0xFF));
        _tip.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0, 0, 0));
        _tipTitle.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        _tipText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xA1, 0xA1, 0xA6) : Color.FromRgb(0x6E, 0x6E, 0x73));
        _empty.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0x6E, 0x6E, 0x73) : Color.FromRgb(0xA1, 0xA1, 0xA6));
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
            _labels.Update(null, 1, 1, 1);
            UpdateSelection();
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        int pw = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        int ph = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));

        bool useAllocated = UseAllocated;
        bool dark = Theme.IsDark;
        FsNode? freeNode = null;
        var layoutRoot = FreeSpaceBytes is long free && free > 0 ? FsTree.CreateFreeSpaceView(root, free, out freeNode) : root;
        var (pixels, rects) = await Task.Run(() =>
        {
            var r = TreemapLayout.Layout(layoutRoot, pw, ph, useAllocated: useAllocated);
            return (Paint(r, pw, ph, dark), r);
        });
        if (version != _version) return; // un rendu plus récent a été demandé entre-temps

        var bmp = new WriteableBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, pw, ph), pixels, pw * 4, 0);
        bmp.Freeze();
        _image.Source = bmp;
        _rects = rects;
        _freeNode = freeNode;
        _scaleX = dpi.DpiScaleX;
        _scaleY = dpi.DpiScaleY;
        _labels.Update(ShowNames ? rects : null, _scaleX, _scaleY, dpi.PixelsPerDip);
        _hover.Visibility = Visibility.Collapsed;
        UpdateSelection();
        var zoom = _pendingZoom;
        _pendingZoom = null;
        if (zoom != null) AnimateZoom(zoom.Value.From, zoom.Value.ZoomOutOf);
    }

    /// <summary>Petite animation de zoom (≈ 0,3 s) entre l'ancien et le nouveau dossier affiché.</summary>
    private void AnimateZoom(TreemapRect? from, FsNode? zoomOutOf)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4 || !SystemParameters.ClientAreaAnimation) return;
        double sx, sy, tx, ty;
        if (from is TreemapRect f)
        {
            double rx = f.X / _scaleX, ry = f.Y / _scaleY, rw = Math.Max(1, f.W / _scaleX), rh = Math.Max(1, f.H / _scaleY);
            sx = rw / w; sy = rh / h; tx = rx; ty = ry;
        }
        else
        {
            int i = _rects.FindIndex(x => x.Node == zoomOutOf);
            if (i < 0) return;
            var r = _rects[i];
            double rx = r.X / _scaleX, ry = r.Y / _scaleY, rw = Math.Max(1, r.W / _scaleX), rh = Math.Max(1, r.H / _scaleY);
            sx = w / rw; sy = h / rh; tx = -rx * sx; ty = -ry * sy;
        }
        if (Math.Abs(sx - 1) < 0.01 && Math.Abs(sy - 1) < 0.01) return;

        var duration = new Duration(TimeSpan.FromMilliseconds(300));
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        System.Windows.Media.Animation.DoubleAnimation Anim(double fromValue, double to) =>
            new(fromValue, to, duration) { EasingFunction = ease };
        _zoomScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(sx, 1));
        _zoomScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(sy, 1));
        _zoomMove.BeginAnimation(TranslateTransform.XProperty, Anim(tx, 0));
        _zoomMove.BeginAnimation(TranslateTransform.YProperty, Anim(ty, 0));
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)))
        {
            BeginTime = TimeSpan.FromMilliseconds(200),
        };
        _labels.Opacity = 0;
        _labels.BeginAnimation(OpacityProperty, fade);
        _overlay.BeginAnimation(OpacityProperty, fade);
    }

    private static int[] Paint(List<TreemapRect> rects, int w, int h, bool dark)
    {
        var px = new int[w * h];
        // fond transparent : le verre reste visible entre les rectangles
        foreach (var r in rects)
        {
            int x0 = Math.Max(0, (int)r.X), y0 = Math.Max(0, (int)r.Y);
            int x1 = Math.Min(w, (int)(r.X + r.W)), y1 = Math.Min(h, (int)(r.Y + r.H));
            if (x1 <= x0 || y1 <= y0) continue;

            if (!r.IsLeaf)
            {
                // Fond d'un dossier : visible à travers les marges entre ses enfants.
                int shade = dark ? Math.Max(0x22, 0x40 - r.Depth * 5) : Math.Max(0xC4, 0xE6 - r.Depth * 6);
                int c = unchecked((int)(0xFF000000 | (uint)(shade << 16) | (uint)(shade << 8) | (uint)Math.Min(255, shade + 6)));
                for (int y = y0; y < y1; y++) Array.Fill(px, c, y * w + x0, x1 - x0);
                continue;
            }

            // Un pixel d'espace à droite et en bas sépare les rectangles voisins.
            if (x1 - x0 >= 3) x1--;
            if (y1 - y0 >= 3) y1--;
            if (r.Node.IsVirtual)
            {
                // Espace libre : hachures grises discrètes.
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                        px[y * w + x] = ((x + y) / 6 % 2 == 0)
                            ? unchecked((int)(dark ? 0xFF303033 : 0xFFEDEDF0))
                            : unchecked((int)(dark ? 0xFF28282A : 0xFFE2E2E7));
                continue;
            }
            uint baseColor = ExtensionColors.ColorOf(r.Node);
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
                    double f = 1.10 - 0.16 * ty - 0.06 * tx;
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
        {
            if (!_rects[i].Contains(x, y)) continue;
            var n = _rects[i].Node;
            return n.IsVirtual && n != _freeNode ? null : n;
        }
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
            if (n == _root) break; // la racine occupe toute la carte : inutile de l'encadrer
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
        if (node == _freeNode)
        {
            _tipTitle.Text = "Espace libre";
            _tipText.Text = ByteSize.Format(node.Size);
            _tip.Visibility = Visibility.Visible;
            Canvas.SetLeft(_tip, Math.Min(p.X + 16, Math.Max(0, ActualWidth - 160)));
            Canvas.SetTop(_tip, Math.Min(p.Y + 18, Math.Max(0, ActualHeight - 60)));
            return;
        }

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
        if (node == null || node.IsVirtual) return;
        if (e.ClickCount == 2) NodeDoubleClicked?.Invoke(node);
        else NodeClicked?.Invoke(node);
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        var node = HitTest(e.GetPosition(this));
        if (node != null && !node.IsVirtual) NodeRightClicked?.Invoke(node);
    }

    /// <summary>Enregistre la carte (avec les noms) dans une image PNG.</summary>
    public void SaveAsPng(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int pw = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        int ph = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_image.Source != null) dc.DrawImage(_image.Source, new Rect(0, 0, ActualWidth, ActualHeight));
        }
        rtb.Render(visual);
        rtb.Render(_labels);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>Dessine les noms des plus grands rectangles par-dessus la carte.</summary>
    private sealed class LabelLayer : FrameworkElement
    {
        private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private List<(Rect rect, string text, bool dark)> _items = new();
        private double _ppd = 1;

        public void Update(List<TreemapRect>? rects, double sx, double sy, double pixelsPerDip)
        {
            _ppd = pixelsPerDip;
            _items = new();
            if (rects != null)
            {
                var big = rects.Where(r => r.IsLeaf && !r.Node.IsVirtual && r.W / sx >= 58 && r.H / sy >= 17)
                               .OrderByDescending(r => r.W * r.H).Take(350);
                foreach (var r in big)
                {
                    uint c = ExtensionColors.ColorOf(r.Node);
                    double lum = 0.299 * ((c >> 16) & 0xFF) + 0.587 * ((c >> 8) & 0xFF) + 0.114 * (c & 0xFF);
                    _items.Add((new Rect(r.X / sx, r.Y / sy, r.W / sx, r.H / sy), r.Node.Name, lum > 170));
                }
                var free = rects.FirstOrDefault(r => r.Node.IsVirtual && r.Depth == 1);
                if (free.Node != null && free.W / sx >= 80 && free.H / sy >= 17)
                    _items.Add((new Rect(free.X / sx, free.Y / sy, free.W / sx, free.H / sy), "Espace libre · " + ByteSize.Format(free.Node.Size), !Theme.IsDark));
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var white = Brushes.White;
            var dark = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F));
            var shadow = new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0));
            foreach (var (rect, text, isDark) in _items)
            {
                var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, isDark ? dark : white, _ppd)
                {
                    MaxTextWidth = Math.Max(1, rect.Width - 10),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                dc.PushClip(new RectangleGeometry(rect));
                if (!isDark)
                {
                    var sft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, shadow, _ppd)
                    { MaxTextWidth = ft.MaxTextWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                    dc.DrawText(sft, new Point(rect.X + 5, rect.Y + 3.8));
                }
                dc.DrawText(ft, new Point(rect.X + 5, rect.Y + 3));
                dc.Pop();
            }
        }
    }
}
