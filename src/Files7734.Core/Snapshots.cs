using System.IO.Compression;
using System.Text.Json;

namespace Files7734;

/// <summary>Résumé d'une analyse (taille des dossiers) gardé pour la comparer à la suivante.</summary>
public sealed class Snapshot
{
    public string RootPath { get; set; } = "";
    public DateTime TakenUtc { get; set; }
    public long TotalSize { get; set; }
    /// <summary>Taille de chaque dossier assez gros, par chemin relatif à la racine.</summary>
    public Dictionary<string, long> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="minSize">Les dossiers plus petits ne sont pas retenus (fichier léger).</param>
    public static Snapshot Create(FsNode root, long minSize = 50L * 1024 * 1024, DateTime? now = null)
    {
        var s = new Snapshot { RootPath = root.FullPath, TakenUtc = now ?? DateTime.UtcNow, TotalSize = root.Size };
        var stack = new Stack<(FsNode node, string rel)>();
        stack.Push((root, ""));
        while (stack.Count > 0)
        {
            var (n, rel) = stack.Pop();
            if (rel.Length > 0) s.Folders[rel] = n.Size;
            if (n.Children == null) continue;
            foreach (var c in n.Children)
                if (c.IsDirectory && c.Size >= minSize) stack.Push((c, rel.Length == 0 ? c.Name : rel + "\\" + c.Name));
        }
        return s;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var fs = File.Create(file);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        JsonSerializer.Serialize(gz, this);
    }

    public static Snapshot? Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var fs = File.OpenRead(file);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            var s = JsonSerializer.Deserialize<Snapshot>(gz);
            if (s != null) s.Folders = new Dictionary<string, long>(s.Folders, StringComparer.OrdinalIgnoreCase);
            return s;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Nom de fichier de sauvegarde pour un chemin analysé (ex. « C » pour C:\).</summary>
    public static string FileNameFor(string rootPath)
    {
        var chars = rootPath.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Trim('_') + ".snapshot.gz";
    }
}

public enum ChangeKind { Grown, Shrunk, New, Removed }

public sealed record FolderChange(string RelativePath, long Before, long After, ChangeKind Kind)
{
    public long Delta => After - Before;
}

public static class SnapshotDiff
{
    /// <summary>
    /// Dossiers dont la taille a changé d'au moins <paramref name="minDelta"/>, en ne gardant que les plus précis :
    /// si un sous-dossier explique l'essentiel du changement de son parent, seul le sous-dossier est listé.
    /// </summary>
    public static List<FolderChange> Compare(Snapshot before, Snapshot after, long minDelta = 100L * 1024 * 1024)
    {
        var all = new Dictionary<string, FolderChange>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, newSize) in after.Folders)
        {
            before.Folders.TryGetValue(path, out long oldSize);
            // Absent de l'analyse précédente : nouveau dossier (ou dossier qui était encore petit).
            var kind = !before.Folders.ContainsKey(path) ? ChangeKind.New
                     : newSize >= oldSize ? ChangeKind.Grown : ChangeKind.Shrunk;
            if (Math.Abs(newSize - oldSize) >= minDelta) all[path] = new FolderChange(path, oldSize, newSize, kind);
        }
        foreach (var (path, oldSize) in before.Folders)
        {
            if (after.Folders.ContainsKey(path)) continue;
            // Disparu de la liste : supprimé, ou devenu trop petit pour être retenu.
            if (oldSize >= minDelta) all[path] = new FolderChange(path, oldSize, 0, ChangeKind.Removed);
        }

        // On retire un dossier si un de ses enfants explique au moins 80 % de son changement.
        var result = new List<FolderChange>();
        foreach (var c in all.Values)
        {
            bool explainedByChild = all.Values.Any(o => o != c
                && o.RelativePath.StartsWith(c.RelativePath + "\\", StringComparison.OrdinalIgnoreCase)
                && Math.Sign(o.Delta) == Math.Sign(c.Delta)
                && Math.Abs(o.Delta) >= 0.8 * Math.Abs(c.Delta));
            if (!explainedByChild) result.Add(c);
        }
        return result.OrderByDescending(c => Math.Abs(c.Delta)).ToList();
    }
}
