using System.Text.RegularExpressions;

namespace Files7734;

/// <summary>Retrouve les dossiers oubliés par un programme désinstallé (AppData, ProgramData…).</summary>
public static class AppLeftovers
{
    // Noms de dossiers trop génériques pour être attribués à un seul programme.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "google", "common", "commonfiles", "programs", "temp", "cache", "packages", "apps", "app",
        "software", "games", "mozilla", "intel", "nvidia", "amd", "adobe", "apple", "users", "data", "logs", "crashdumps",
        "microsoftcorporation", "oracle", "java", "python", "steam", "epicgames", "riotgames", "ubisoft", "electronicarts",
    };

    /// <summary>Nom simplifié : minuscules, sans version, sans « (x64) », sans espaces ni ponctuation.</summary>
    public static string Normalize(string name)
    {
        string s = name.ToLowerInvariant();
        s = Regex.Replace(s, @"\((x64|x86|64-bit|32-bit|64 bits|32 bits)\)", "");
        s = Regex.Replace(s, @"\b(x64|x86|64-bit|32-bit)\b", "");
        s = Regex.Replace(s, @"\bv?\d+(\.\d+)+\b.*$", ""); // « Truc 2.4.1 » → « truc »
        s = Regex.Replace(s, @"[^a-z0-9]", "");
        return s;
    }

    /// <summary>
    /// Parmi <paramref name="folders"/> (dossiers candidats, ex. sous-dossiers d'AppData), ceux qui appartiennent à l'application
    /// et à aucune autre application encore installée.
    /// </summary>
    public static List<string> Match(string appName, string? publisher, IEnumerable<string> folders, IEnumerable<string> otherInstalledApps)
    {
        string app = Normalize(appName);
        if (app.Length < 3) return new();
        string pub = publisher == null ? "" : Normalize(Regex.Replace(publisher, @"\b(inc|llc|ltd|gmbh|corp|corporation|co|sa|sas)\b\.?", "", RegexOptions.IgnoreCase));
        var others = otherInstalledApps.Select(Normalize).Where(o => o.Length >= 3 && o != app).ToList();

        var result = new List<string>();
        foreach (var folder in folders)
        {
            string name = Normalize(LastSegment(folder));
            string parent = Normalize(LastSegment(ParentOf(folder)));
            if (name.Length < 3 || Generic.Contains(name)) continue;
            bool mine = name == app
                        || (name.Length >= 5 && app.StartsWith(name) && (double)name.Length / app.Length >= 0.6)
                        || (app.Length >= 5 && name.StartsWith(app) && (double)app.Length / name.Length >= 0.6);
            // Rangement « Éditeur\Application » : le dossier de l'éditeur seul n'est jamais proposé.
            if (!mine && pub.Length >= 3 && parent == pub && (name == app || app.Contains(name) && name.Length >= 4)) mine = true;
            if (!mine) continue;
            if (others.Any(o => o == name || (o.StartsWith(name) && name.Length >= 5))) continue; // partagé avec un autre programme
            result.Add(folder);
        }
        return result;
    }

    private static string LastSegment(string path)
    {
        string p = path.TrimEnd('\\', '/');
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 ? p[(i + 1)..] : p;
    }

    private static string ParentOf(string path)
    {
        string p = path.TrimEnd('\\', '/');
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i > 0 ? p[..i] : "";
    }
}

/// <summary>Fichiers jetables d'un jeu : caches, rapports de plantage, journaux. Les sauvegardes ne sont jamais proposées.</summary>
public static class GameCleanup
{
    public sealed record Item(string Path, long Size, string Kind, bool IsDirectory);

    private static readonly string[] CrashFolders = { "crashes", "crashreports", "crashdumps", "crashreportclient", "dumps" };
    private static readonly string[] LogFolders = { "logs", "log" };

    /// <param name="extraFolders">Dossiers entiers jetables trouvés ailleurs (ex. cache de shaders de Steam) avec leur description.</param>
    public static List<Item> Find(string installDir, IEnumerable<(string Path, string Kind)>? extraFolders = null, CancellationToken ct = default)
    {
        var items = new List<Item>();
        foreach (var (path, kind) in extraFolders ?? Enumerable.Empty<(string, string)>())
        {
            if (!Directory.Exists(path)) continue;
            long size = FolderSize(path);
            if (size > 0) items.Add(new Item(path, size, kind, true));
        }
        if (!Directory.Exists(installDir)) return items;

        var opts = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var stack = new Stack<string>();
        stack.Push(installDir);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            IEnumerable<string> subdirs, files;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", opts).ToList();
                files = Directory.EnumerateFiles(dir, "*", opts).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            foreach (var sub in subdirs)
            {
                string n = System.IO.Path.GetFileName(sub).ToLowerInvariant();
                if (n.Contains("save") && n != "saved") continue; // jamais les sauvegardes (« Saved » d'Unreal contient aussi Crashes et Logs)
                if (CrashFolders.Contains(n))
                {
                    long s = FolderSize(sub);
                    if (s > 0) items.Add(new Item(sub, s, "Rapports de plantage", true));
                }
                else if (LogFolders.Contains(n))
                {
                    // Dans un dossier de journaux, seuls les .log sont proposés.
                    foreach (var f in SafeFiles(sub, "*.log")) items.Add(new Item(f.FullName, f.Length, "Journal", false));
                }
                else stack.Push(sub);
            }
            foreach (var f in files)
            {
                string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".dmp" or ".mdmp")
                {
                    try { items.Add(new Item(f, new FileInfo(f).Length, "Vidage mémoire (plantage)", false)); } catch (IOException) { }
                }
            }
        }
        return items.OrderByDescending(i => i.Size).ToList();
    }

    private static IEnumerable<FileInfo> SafeFiles(string dir, string pattern)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles(pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Enumerable.Empty<FileInfo>(); }
    }

    public static long FolderSize(string dir) => SafeFiles(dir, "*").Sum(f => { try { return f.Length; } catch (IOException) { return 0L; } });

    /// <summary>
    /// Dossiers de jeux sans jeu installé (restes d'une désinstallation) : sous-dossiers de <paramref name="gamesRoot"/>
    /// qui ne correspondent à aucun dossier installé.
    /// </summary>
    public static List<string> GhostFolders(string gamesRoot, IEnumerable<string> installedFolders, IEnumerable<string>? ignoreNames = null)
    {
        if (!Directory.Exists(gamesRoot)) return new();
        var installed = new HashSet<string>(installedFolders.Select(p => p.TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);
        var ignore = new HashSet<string>(ignoreNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(gamesRoot))
            {
                if (ignore.Contains(System.IO.Path.GetFileName(d))) continue;
                if (installed.Contains(d.TrimEnd('\\', '/'))) continue;
                // Un jeu installé dans un sous-dossier de celui-ci : ce n'est pas un reste.
                if (installed.Any(i => i.StartsWith(d.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                    || i.StartsWith(d.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(d);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result;
    }
}
