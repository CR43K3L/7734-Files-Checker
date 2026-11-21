namespace Files7734;

/// <summary>Un rectangle de la treemap. <see cref="IsLeaf"/> = rectangle dessiné en couleur (fichier ou dossier trop petit pour être détaillé).</summary>
public readonly record struct TreemapRect(FsNode Node, float X, float Y, float W, float H, int Depth, bool IsLeaf)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + W && y < Y + H;
}

/// <summary>Calcule une treemap "squarifiée" : des rectangles aussi carrés que possible, proportionnels à la taille.</summary>
public static class TreemapLayout
{
    /// <param name="minDirSize">En dessous de cette largeur/hauteur (en pixels), un dossier n'est plus subdivisé.</param>
    /// <returns>Les rectangles en ordre "parent avant enfants" : le dernier qui contient un point est le plus profond.</returns>
    public static List<TreemapRect> Layout(FsNode root, double width, double height, double minDirSize = 4)
    {
        var result = new List<TreemapRect>();
        if (width < 1 || height < 1 || root.Size <= 0) return result;

        var stack = new Stack<(FsNode node, double x, double y, double w, double h, int depth)>();
        stack.Push((root, 0, 0, width, height, 0));
        var items = new List<(FsNode node, double area)>();
        var placed = new List<(FsNode node, double x, double y, double w, double h)>();

        while (stack.Count > 0)
        {
            var (node, x, y, w, h, depth) = stack.Pop();
            bool subdivide = node.IsDirectory && node.Children is { Count: > 0 } && w >= minDirSize && h >= minDirSize && node.Size > 0;
            result.Add(new TreemapRect(node, (float)x, (float)y, (float)w, (float)h, depth, !subdivide));
            if (!subdivide) continue;

            // Une marge d'un pixel laisse apparaître la limite du dossier.
            if (w > 8 && h > 8 && depth > 0) { x += 1; y += 1; w -= 2; h -= 2; }

            double scale = w * h / node.Size;
            items.Clear();
            foreach (var c in node.Children!)
            {
                if (c.Size <= 0) continue;
                items.Add((c, c.Size * scale));
            }
            items.Sort(static (a, b) => b.area.CompareTo(a.area));

            placed.Clear();
            Squarify(items, x, y, w, h, placed);
            // Empilés à l'envers pour être traités dans l'ordre (du plus gros au plus petit).
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                var p = placed[i];
                if (p.w * p.h < 0.25) continue; // invisible
                stack.Push((p.node, p.x, p.y, p.w, p.h, depth + 1));
            }
        }
        return result;
    }

    private static void Squarify(List<(FsNode node, double area)> items, double x, double y, double w, double h,
        List<(FsNode node, double x, double y, double w, double h)> output)
    {
        int n = items.Count, i = 0;
        while (i < n && w > 0 && h > 0)
        {
            double side = Math.Min(w, h);
            double rowSum = 0, best = double.MaxValue;
            int j = i;
            while (j < n)
            {
                double s = rowSum + items[j].area;
                double worst = Worst(items[i].area, items[j].area, s, side);
                if (j > i && worst > best) break;
                best = worst;
                rowSum = s;
                j++;
            }
            if (rowSum <= 0) break;

            if (w >= h)
            {
                double colW = Math.Min(w, rowSum / h), cy = y;
                for (int k = i; k < j; k++)
                {
                    double ih = items[k].area / colW;
                    output.Add((items[k].node, x, cy, colW, ih));
                    cy += ih;
                }
                x += colW; w -= colW;
            }
            else
            {
                double rowH = Math.Min(h, rowSum / w), cx = x;
                for (int k = i; k < j; k++)
                {
                    double iw = items[k].area / rowH;
                    output.Add((items[k].node, cx, y, iw, rowH));
                    cx += iw;
                }
                y += rowH; h -= rowH;
            }
            i = j;
        }
    }

    private static double Worst(double maxArea, double minArea, double sum, double side)
    {
        double s2 = sum * sum, side2 = side * side;
        return Math.Max(side2 * maxArea / s2, s2 / (side2 * minArea));
    }
}
