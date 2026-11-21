using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Files7734;

/// <summary>Point d'entrée : choisit automatiquement la méthode de scan la plus rapide possible.</summary>
public static class DiskScanner
{
    public static ScanResult Scan(string path, bool forceDirectoryWalk = false, ScanStatus? status = null, CancellationToken ct = default)
    {
        status ??= new ScanStatus();
        var sw = Stopwatch.StartNew();
        string full = Path.GetFullPath(path);
        string? fallbackReason = null;

        if (forceDirectoryWalk)
        {
            fallbackReason = "Scan classique demandé.";
        }
        else if (!OperatingSystem.IsWindows())
        {
            fallbackReason = "La lecture MFT n'est disponible que sous Windows.";
        }
        else
        {
            string root = Path.GetPathRoot(full) ?? full;
            string? format = null;
            try { format = new DriveInfo(root).DriveFormat; } catch { }

            if (root.Length < 2 || root[1] != ':')
                fallbackReason = "Chemin réseau : lecture MFT impossible.";
            else if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase))
                fallbackReason = $"Le disque est en {format ?? "format inconnu"}, pas en NTFS.";
            else
            {
                try
                {
                    FsNode tree;
                    using (var vol = WindowsVolumeSource.Open(root[0]))
                        tree = NtfsMftScanner.Scan(vol, root.ToUpperInvariant(), status, ct);

                    var node = FindByPath(tree, root, full);
                    if (node != null)
                    {
                        return new ScanResult { Root = node, Method = ScanMethod.Mft, Elapsed = sw.Elapsed, Errors = status.Errors };
                    }
                    fallbackReason = "Dossier introuvable dans la table NTFS.";
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException)
                {
                    fallbackReason = "Pas de droits administrateur : scan classique (plus lent).";
                }
                catch (Exception ex)
                {
                    fallbackReason = "Lecture MFT impossible (" + ex.Message + ").";
                }
            }
        }

        var walkRoot = DirectoryWalkScanner.Scan(full, GetClusterSize(full), status, ct);
        return new ScanResult
        {
            Root = walkRoot,
            Method = ScanMethod.DirectoryWalk,
            Elapsed = sw.Elapsed,
            Errors = status.Errors,
            FallbackReason = fallbackReason,
        };
    }

    /// <summary>Retrouve un dossier dans l'arbre à partir de son chemin complet.</summary>
    public static FsNode? FindByPath(FsNode tree, string rootPath, string fullPath)
    {
        string rel = fullPath.Length > rootPath.Length ? fullPath[rootPath.Length..] : "";
        var node = tree;
        foreach (var part in rel.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Children?.FirstOrDefault(c => c.IsDirectory && string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (node == null) return null;
        }
        return node;
    }

    private static long GetClusterSize(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            string root = Path.GetPathRoot(path) ?? path;
            if (GetDiskFreeSpaceW(root, out uint spc, out uint bps, out _, out _)) return (long)spc * bps;
        }
        return 4096;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
