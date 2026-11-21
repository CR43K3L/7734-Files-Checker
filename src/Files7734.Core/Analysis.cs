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

public static class ByteSize
{
    private static readonly string[] Units = { "o", "Ko", "Mo", "Go", "To", "Po" };

    /// <summary>Formate une taille en octets à la française, ex. "12,4 Go".</summary>
    public static string Format(long bytes)
    {
        double v = bytes;
        int u = 0;
        while (Math.Abs(v) >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        return u == 0 ? $"{bytes} o" : v.ToString(v >= 100 ? "0" : "0.0", fr) + " " + Units[u];
    }
}
