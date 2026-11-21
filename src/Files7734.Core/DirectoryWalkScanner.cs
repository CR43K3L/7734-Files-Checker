using System.IO.Enumeration;

namespace Files7734;

/// <summary>
/// Scan classique : parcourt les dossiers un par un. Fonctionne partout (tout système de fichiers,
/// sans droits admin) mais beaucoup plus lent que la lecture de la MFT.
/// </summary>
public static class DirectoryWalkScanner
{
    private readonly record struct Entry(string Name, bool IsDirectory, long Length, DateTime LastWriteUtc, FileAttributes Attributes);

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
    };

    /// <param name="clusterSize">Taille de cluster utilisée pour estimer l'espace occupé.</param>
    public static FsNode Scan(string rootPath, long clusterSize = 4096, ScanStatus? status = null, CancellationToken ct = default)
    {
        status ??= new ScanStatus();
        status.Phase = "Parcours des dossiers";
        status.Fraction = null;
        var root = new FsNode(rootPath, true);
        try { root.LastModifiedUtc = Directory.GetLastWriteTimeUtc(rootPath); } catch { }
        ScanDirectory(root, rootPath, 0, Math.Max(1, clusterSize), status, ct);
        TreeBuilder.Finalize(root);
        status.Fraction = 1;
        return root;
    }

    private static void ScanDirectory(FsNode node, string path, int depth, long cluster, ScanStatus status, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var subdirs = new List<(FsNode node, string path)>();
        var children = new List<FsNode>();
        try
        {
            var items = new FileSystemEnumerable<Entry>(path,
                static (ref FileSystemEntry e) => new Entry(e.FileName.ToString(), e.IsDirectory, e.IsDirectory ? 0 : e.Length,
                    e.LastWriteTimeUtc.UtcDateTime, e.Attributes),
                Options);
            foreach (var e in items)
            {
                var child = new FsNode(e.Name, e.IsDirectory) { LastModifiedUtc = e.LastWriteUtc };
                if (!e.IsDirectory)
                {
                    child.Size = e.Length;
                    child.Allocated = e.Length == 0 ? 0 : (e.Length + cluster - 1) / cluster * cluster;
                }
                child.Parent = node;
                children.Add(child);
                status.AddItem(child.Size);
                // On ne suit pas les jonctions / liens symboliques : évite les doublons et les boucles.
                if (e.IsDirectory && (e.Attributes & FileAttributes.ReparsePoint) == 0)
                    subdirs.Add((child, PathUtil.Combine(path, e.Name)));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            status.AddError();
        }

        if (children.Count > 0) node.Children = children;

        if (depth < 3 && subdirs.Count > 1)
        {
            Parallel.ForEach(subdirs, new ParallelOptions { CancellationToken = ct },
                s => ScanDirectory(s.node, s.path, depth + 1, cluster, status, ct));
        }
        else
        {
            foreach (var s in subdirs) ScanDirectory(s.node, s.path, depth + 1, cluster, status, ct);
        }
    }
}
