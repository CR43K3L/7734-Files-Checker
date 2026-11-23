namespace Files7734;

public sealed class MoveProgress
{
    public long CopiedBytes { get; internal set; }
    public long TotalBytes { get; internal set; }
    public string CurrentFile { get; internal set; } = "";
}

public enum MoveResult { Done, DoneButOldCopyLeft, SourceInUse, Failed, Cancelled }

/// <summary>
/// Déplace un dossier (ex. un jeu) vers un autre disque puis laisse à l'ancien emplacement un lien vers le nouveau,
/// pour que le lanceur le retrouve. En cas d'échec, tout est remis comme avant.
/// </summary>
public static class FolderMover
{
    /// <param name="makeLink">Crée le lien (jonction) : (emplacement du lien, cible). Renvoie true si réussi.</param>
    public static (MoveResult Result, string? Error, string Destination) Move(string source, string destinationParent,
        Func<string, string, bool> makeLink, MoveProgress? progress = null, CancellationToken ct = default)
    {
        progress ??= new MoveProgress();
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        string dest = Path.Combine(Path.GetFullPath(destinationParent), Path.GetFileName(source));
        if (Directory.Exists(dest) || File.Exists(dest)) return (MoveResult.Failed, $"Le dossier {dest} existe déjà.", dest);
        if ((dest + Path.DirectorySeparatorChar).StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return (MoveResult.Failed, "La destination est à l'intérieur du dossier à déplacer.", dest);

        // 1) On renomme l'original : impossible si un fichier est ouvert (jeu ou lanceur en cours d'exécution).
        string parked = source + ".7734-deplacement";
        try { Directory.Move(source, parked); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (MoveResult.SourceInUse, e.Message, dest);
        }

        void Rollback()
        {
            try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch (Exception) { }
            try { if (Directory.Exists(source)) Directory.Delete(source); } catch (Exception) { }
            try { Directory.Move(parked, source); } catch (Exception) { }
        }

        try
        {
            // 2) Copie, 3) vérification
            Copy(parked, dest, progress, ct);
            if (!SameContent(parked, dest)) { Rollback(); return (MoveResult.Failed, "La copie ne correspond pas à l'original.", dest); }
            // 4) Lien à l'ancien emplacement
            if (!makeLink(source, dest)) { Rollback(); return (MoveResult.Failed, "Impossible de créer le lien vers le nouveau dossier.", dest); }
        }
        catch (OperationCanceledException)
        {
            Rollback();
            return (MoveResult.Cancelled, null, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Rollback();
            return (MoveResult.Failed, e.Message, dest);
        }

        // 5) Suppression de l'ancienne copie
        try
        {
            Directory.Delete(parked, true);
            return (MoveResult.Done, null, dest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (MoveResult.DoneButOldCopyLeft, parked, dest);
        }
    }

    public static void Copy(string source, string dest, MoveProgress progress, CancellationToken ct)
    {
        var files = new DirectoryInfo(source).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).ToList();
        progress.TotalBytes = files.Sum(f => f.Length);
        progress.CopiedBytes = 0;
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));

        var buffer = new byte[4 << 20];
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(source, f.FullName);
            progress.CurrentFile = rel;
            string target = Path.Combine(dest, rel);
            using (var input = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1))
            {
                output.SetLength(f.Length);
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, n);
                    progress.CopiedBytes += n;
                }
            }
            File.SetLastWriteTimeUtc(target, f.LastWriteTimeUtc);
            File.SetAttributes(target, f.Attributes & ~FileAttributes.ReadOnly | (f.Attributes & FileAttributes.ReadOnly));
        }
    }

    /// <summary>Même liste de fichiers avec les mêmes tailles.</summary>
    public static bool SameContent(string a, string b)
    {
        Dictionary<string, long> Map(string root) => new DirectoryInfo(root)
            .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .ToDictionary(f => Path.GetRelativePath(root, f.FullName), f => f.Length, StringComparer.OrdinalIgnoreCase);
        var ma = Map(a);
        var mb = Map(b);
        return ma.Count == mb.Count && ma.All(kv => mb.TryGetValue(kv.Key, out var l) && l == kv.Value);
    }
}
