namespace Files7734;

public sealed record ExtensionStat(string Extension, long FileCount, long Size, long Allocated);

/// <summary>Statistiques calculées sur un arbre déjà scanné.</summary>
public static class Analysis
{
    public static IEnumerable<FsNode> EnumerateFiles(FsNode root)
    {
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!n.IsDirectory) { yield return n; continue; }
            if (n.Children == null) continue;
            foreach (var c in n.Children) stack.Push(c);
        }
    }

    /// <summary>Les <paramref name="count"/> plus gros fichiers, du plus gros au plus petit.</summary>
    public static List<FsNode> LargestFiles(FsNode root, int count)
    {
        var heap = new PriorityQueue<FsNode, long>();
        foreach (var f in EnumerateFiles(root))
        {
            if (heap.Count < count) heap.Enqueue(f, f.Size);
            else if (f.Size > heap.Peek().Size) heap.EnqueueDequeue(f, f.Size);
        }
        var list = new List<FsNode>(heap.Count);
        while (heap.Count > 0) list.Add(heap.Dequeue());
        list.Reverse();
        return list;
    }

    /// <summary>Taille totale par extension, de la plus grosse à la plus petite.</summary>
    public static List<ExtensionStat> ByExtension(FsNode root)
    {
        var map = new Dictionary<string, (long count, long size, long alloc)>(StringComparer.Ordinal);
        foreach (var f in EnumerateFiles(root))
        {
            string ext = f.Extension;
            map.TryGetValue(ext, out var s);
            map[ext] = (s.count + 1, s.size + f.Size, s.alloc + f.Allocated);
        }
        return map.Select(kv => new ExtensionStat(kv.Key, kv.Value.count, kv.Value.size, kv.Value.alloc))
                  .OrderByDescending(s => s.Size)
                  .ToList();
    }
}

public enum SizeUnit { Auto, Bytes, KB, MB, GB, TB }

public static class ByteSize
{
    private static readonly string[] Units = { "o", "Ko", "Mo", "Go", "To", "Po" };
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Unité d'affichage choisie dans les options (Auto = la plus lisible).</summary>
    public static SizeUnit Unit { get; set; } = SizeUnit.Auto;

    /// <summary>Formate une taille en octets à la française, ex. "12,4 Go".</summary>
    public static string Format(long bytes)
    {
        switch (Unit)
        {
            case SizeUnit.Bytes: return bytes.ToString("N0", Fr) + " o";
            case SizeUnit.KB: return Fixed(bytes, 1);
            case SizeUnit.MB: return Fixed(bytes, 2);
            case SizeUnit.GB: return Fixed(bytes, 3);
            case SizeUnit.TB: return Fixed(bytes, 4);
        }
        double v = bytes;
        int u = 0;
        while (Math.Abs(v) >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} o" : v.ToString(v >= 100 ? "0" : "0.0", Fr) + " " + Units[u];
    }

    private static string Fixed(long bytes, int power)
    {
        double v = bytes / Math.Pow(1024, power);
        return v.ToString(power >= 3 ? "N2" : "N1", Fr) + " " + Units[power];
    }
}

public static class Search
{
    /// <summary>
    /// Cherche les fichiers et dossiers dont le nom correspond au motif (ex. « *.mp4 », « photo »).
    /// Renvoie les <paramref name="max"/> plus gros résultats.
    /// </summary>
    public static List<FsNode> Find(FsNode root, string pattern, int max = 10000)
    {
        pattern = pattern.Trim();
        if (pattern.Length == 0) return new();
        Func<string, bool> match;
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            match = regex.IsMatch;
        }
        else
        {
            match = n => n.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var heap = new PriorityQueue<FsNode, long>();
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n != root && match(n.Name))
            {
                if (heap.Count < max) heap.Enqueue(n, n.Size);
                else if (n.Size > heap.Peek().Size) heap.EnqueueDequeue(n, n.Size);
            }
            if (n.Children != null) foreach (var c in n.Children) stack.Push(c);
        }
        var list = new List<FsNode>(heap.Count);
        while (heap.Count > 0) list.Add(heap.Dequeue());
        list.Reverse();
        return list;
    }
}
