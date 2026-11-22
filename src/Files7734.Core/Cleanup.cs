namespace Files7734;

/// <summary>Une catégorie de fichiers qu'on peut supprimer sans risque (caches, temporaires…).</summary>
public sealed class CleanupCategory
{
    public CleanupCategory(string id, string name, string description, params string[] targets)
    {
        Id = id;
        Name = name;
        Description = description;
        Targets = targets;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }

    /// <summary>
    /// Dossiers dont on vide le contenu (ou fichiers). Les variables d'environnement (%TEMP%…) sont acceptées,
    /// ainsi que « * » dans un nom de dossier ou de fichier (ex. « …\User Data\*\Cache »).
    /// </summary>
    public IReadOnlyList<string> Targets { get; }

    /// <summary>Catégorie traitée à part (ex. la corbeille).</summary>
    public bool IsSpecial { get; init; }
}

public sealed record CleanupMeasure(long Bytes, long Files);
public sealed record CleanupOutcome(long FreedBytes, long DeletedFiles, long SkippedFiles);

/// <summary>Mesure et vide les catégories choisies par l'utilisateur.</summary>
public static class Cleaner
{
    /// <summary>Catégories proposées sous Windows. Aucune n'est cochée par défaut.</summary>
    public static List<CleanupCategory> WindowsCategories() => new()
    {
        new("temp", "Fichiers temporaires", "Fichiers laissés par les installations et les programmes.",
            @"%TEMP%", @"%WINDIR%\Temp"),
        new("wu", "Cache de Windows Update", "Mises à jour déjà installées et téléchargées.",
            @"%WINDIR%\SoftwareDistribution\Download"),
        new("do", "Optimisation de la distribution", "Copies de mises à jour partagées avec d'autres PC.",
            @"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
        new("crash", "Rapports de plantage", "Rapports d'erreurs et vidages mémoire après un plantage.",
            @"%LOCALAPPDATA%\CrashDumps", @"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive",
            @"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue", @"%WINDIR%\Minidump", @"%WINDIR%\MEMORY.DMP"),
        new("thumbs", "Miniatures de l'Explorateur", "Aperçus d'images, recréés automatiquement.",
            @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db", @"%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db"),
        new("shaders", "Caches de shaders (cartes graphiques)", "Recréés au prochain lancement des jeux, qui peut être un peu plus lent.",
            @"%LOCALAPPDATA%\D3DSCache", @"%LOCALAPPDATA%\NVIDIA\DXCache", @"%LOCALAPPDATA%\NVIDIA\GLCache",
            @"%LOCALAPPDATA%\AMD\DxCache", @"%LOCALAPPDATA%\AMD\DxcCache", @"%LOCALAPPDATA%\AMD\VkCache",
            @"%LOCALAPPDATA%\Intel\ShaderCache"),
        new("browsers", "Caches des navigateurs", "Chrome, Edge, Brave, Opera, Firefox. Tes mots de passe et ton historique ne sont pas touchés.",
            @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Code Cache",
            @"%LOCALAPPDATA%\Google\Chrome\User Data\*\GPUCache",
            @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Cache", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Code Cache",
            @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\GPUCache",
            @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Cache", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Code Cache",
            @"%APPDATA%\Opera Software\*\Cache", @"%LOCALAPPDATA%\Opera Software\*\Cache",
            @"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2"),
        new("discord", "Cache de Discord", "Images et vidéos déjà vues dans Discord.",
            @"%APPDATA%\discord\Cache", @"%APPDATA%\discord\Code Cache", @"%APPDATA%\discord\GPUCache",
            @"%APPDATA%\discordptb\Cache", @"%APPDATA%\discordcanary\Cache"),
        new("launchers", "Caches des lanceurs de jeux", "Caches web de Steam, Epic Games, Battle.net et Riot (pas les jeux).",
            @"%PROGRAMFILES(X86)%\Steam\appcache\httpcache", @"%LOCALAPPDATA%\Steam\htmlcache",
            @"%LOCALAPPDATA%\EpicGamesLauncher\Saved\webcache*", @"%PROGRAMDATA%\Battle.net\Agent\data\cache",
            @"%LOCALAPPDATA%\Battle.net\Cache", @"%LOCALAPPDATA%\Riot Games\Riot Client\Data\Cache"),
        new("winold", "Ancienne installation de Windows", "Dossier Windows.old laissé après une mise à jour majeure. Tu ne pourras plus revenir à l'ancienne version.",
            @"%SYSTEMDRIVE%\Windows.old"),
        new("recycle", "Corbeille", "Vide la corbeille de tous les disques.") { IsSpecial = true },
    };

    /// <summary>Remplace les variables d'environnement et développe les « * ».</summary>
    public static List<string> ExpandTarget(string target)
    {
        string expanded = Environment.ExpandEnvironmentVariables(target);
        if (expanded.Contains('%')) return new(); // variable inconnue sur ce PC
        if (!expanded.Contains('*')) return File.Exists(expanded) || Directory.Exists(expanded) ? new() { expanded } : new();

        string root = Path.GetPathRoot(expanded) ?? "";
        if (root.Length == 0) return new();
        var parts = expanded[root.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string> { root };

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0) continue;
            bool last = i == parts.Length - 1;
            var next = new List<string>();
            foreach (var dir in current)
            {
                if (!Directory.Exists(dir)) continue;
                if (part.Contains('*'))
                {
                    try
                    {
                        next.AddRange(Directory.EnumerateDirectories(dir, part));
                        if (last) next.AddRange(Directory.EnumerateFiles(dir, part));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                else
                {
                    string p = Path.Combine(dir, part);
                    if (Directory.Exists(p) || (last && File.Exists(p))) next.Add(p);
                }
            }
            current = next;
        }
        return current;
    }

    /// <summary>Taille de tout ce qui serait supprimé.</summary>
    public static CleanupMeasure Measure(CleanupCategory category, CancellationToken ct = default)
    {
        long bytes = 0, files = 0;
        foreach (var target in category.Targets)
        foreach (var path in ExpandTarget(target))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                try { bytes += new FileInfo(path).Length; files++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                continue;
            }
            foreach (var f in SafeFiles(path))
            {
                try { bytes += f.Length; files++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return new CleanupMeasure(bytes, files);
    }

    /// <summary>Supprime le contenu (les dossiers cibles eux-mêmes sont conservés). Les fichiers en cours d'utilisation sont ignorés.</summary>
    public static CleanupOutcome Clean(CleanupCategory category, CancellationToken ct = default)
    {
        long freed = 0, deleted = 0, skipped = 0;
        foreach (var target in category.Targets)
        foreach (var path in ExpandTarget(target))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                TryDelete(new FileInfo(path), ref freed, ref deleted, ref skipped);
                continue;
            }
            foreach (var f in SafeFiles(path).ToList())
            {
                ct.ThrowIfCancellationRequested();
                TryDelete(f, ref freed, ref deleted, ref skipped);
            }
            // Supprime les sous-dossiers devenus vides (du plus profond au moins profond).
            try
            {
                var dirs = new DirectoryInfo(path).EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Select(d => d.FullName).OrderByDescending(d => d.Length).ToList();
                foreach (var d in dirs)
                {
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                if (category.Id == "winold" && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new CleanupOutcome(freed, deleted, skipped);
    }

    private static void TryDelete(FileInfo f, ref long freed, ref long deleted, ref long skipped)
    {
        try
        {
            long len = f.Length;
            if (f.IsReadOnly) f.IsReadOnly = false;
            f.Delete();
            freed += len;
            deleted++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            skipped++;
        }
    }

    private static IEnumerable<FileInfo> SafeFiles(string dir)
    {
        if (!Directory.Exists(dir)) return Enumerable.Empty<FileInfo>();
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, // ne suit jamais un lien vers un autre dossier
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<FileInfo>();
        }
    }
}
