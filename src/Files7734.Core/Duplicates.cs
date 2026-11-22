using System.Security.Cryptography;

namespace Files7734;

/// <summary>Un groupe de fichiers au contenu identique.</summary>
public sealed record DuplicateGroup(long FileSize, List<FsNode> Files)
{
    /// <summary>Espace récupérable en ne gardant qu'un seul exemplaire.</summary>
    public long Wasted => FileSize * (Files.Count - 1);
}

public sealed class DuplicateProgress
{
    public string Phase { get; internal set; } = "";
    public long Done { get; internal set; }
    public long Total { get; internal set; }
}

/// <summary>
/// Trouve les fichiers identiques : même taille, puis même début de fichier, puis même contenu complet (SHA-256).
/// Ne lit que les fichiers qui ont au moins un « jumeau » de même taille.
/// </summary>
public static class DuplicateFinder
{
    private const int PartialBytes = 64 * 1024;

    /// <param name="pathOf">Chemin réel d'un nœud (par défaut <see cref="FsNode.FullPath"/>).</param>
    public static List<DuplicateGroup> Find(FsNode root, long minSize = 1024 * 1024, DuplicateProgress? progress = null,
        CancellationToken ct = default, Func<FsNode, string>? pathOf = null)
    {
        progress ??= new DuplicateProgress();
        pathOf ??= n => n.FullPath;

        progress.Phase = "Comparaison des tailles";
        var bySize = new Dictionary<long, List<FsNode>>();
        foreach (var f in Analysis.EnumerateFiles(root))
        {
            if (f.Size < minSize || f.IsVirtual) continue;
            if (!bySize.TryGetValue(f.Size, out var list)) bySize[f.Size] = list = new List<FsNode>(2);
            list.Add(f);
        }
        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        progress.Total = candidates.Sum(kv => (long)kv.Value.Count);
        progress.Done = 0;

        var groups = new List<DuplicateGroup>();
        progress.Phase = "Lecture des fichiers";
        foreach (var (size, files) in candidates.OrderByDescending(kv => kv.Key))
        {
            ct.ThrowIfCancellationRequested();
            // 1) début du fichier
            var byPartial = GroupBy(files, f => Hash(pathOf(f), PartialBytes, ct));
            foreach (var partialGroup in byPartial)
            {
                if (partialGroup.Count < 2) continue;
                // 2) contenu complet (inutile si le fichier tient entièrement dans le début déjà lu)
                var full = size <= PartialBytes ? new List<List<FsNode>> { partialGroup } : GroupBy(partialGroup, f => Hash(pathOf(f), long.MaxValue, ct));
                foreach (var g in full)
                    if (g.Count > 1) groups.Add(new DuplicateGroup(size, g.OrderBy(n => n.LastModifiedUtc).ToList()));
            }
            progress.Done += files.Count;
        }
        return groups.OrderByDescending(g => g.Wasted).ToList();
    }

    private static List<List<FsNode>> GroupBy(List<FsNode> files, Func<FsNode, string?> key)
    {
        var map = new Dictionary<string, List<FsNode>>();
        foreach (var f in files)
        {
            string? k = key(f);
            if (k == null) continue; // fichier illisible : ignoré
            if (!map.TryGetValue(k, out var l)) map[k] = l = new List<FsNode>();
            l.Add(f);
        }
        return map.Values.ToList();
    }

    private static string? Hash(string path, long maxBytes, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            long remaining = maxBytes;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                int n = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (n <= 0) break;
                sha.AppendData(buffer, 0, n);
                remaining -= n;
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
