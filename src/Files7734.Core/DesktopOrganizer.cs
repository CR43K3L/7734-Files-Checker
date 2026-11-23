using System.Text.Json;

namespace Files7734;

/// <summary>Un programme ou un jeu qui peut avoir un raccourci sur le bureau.</summary>
public sealed class ShortcutItem
{
    /// <summary>Identité du programme (cible du raccourci en minuscules) : sert à le reconnaître partout.</summary>
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsGame { get; init; }
    /// <summary>Raccourci existant à copier (menu Démarrer, bureau…) ; null si l'app doit le créer.</summary>
    public string? Source { get; init; }
    /// <summary>Contenu d'un raccourci .url à créer quand il n'y a pas de source (ex. jeu Steam).</summary>
    public string? UrlContent { get; init; }
    /// <summary>Raccourci actuellement sur le bureau (à la racine ou dans un dossier), s'il y en a un.</summary>
    public string? OnDesktop { get; init; }
    /// <summary>Dossier actuel sur le bureau : "" à la racine, null si absent du bureau.</summary>
    public string? CurrentFolder { get; init; }
    public string Extension => System.IO.Path.GetExtension(OnDesktop ?? Source ?? "x.url");
}

public enum OrganizeOpKind { Move, Create, Remove }

/// <summary>Une opération faite sur le bureau, enregistrée pour pouvoir l'annuler.</summary>
public sealed record OrganizeOp(OrganizeOpKind Kind, string From, string To, string? Content = null);

public sealed class OrganizeJournal
{
    public DateTime WhenUtc { get; set; }
    public List<OrganizeOp> Done { get; set; } = new();
    /// <summary>Dossiers créés sur le bureau (supprimés à l'annulation s'ils sont vides).</summary>
    public List<string> CreatedFolders { get; set; } = new();

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static OrganizeJournal? Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<OrganizeJournal>(File.ReadAllText(file)) : null; }
        catch (Exception) { return null; }
    }
}

public static class DesktopOrganizer
{
    private static readonly char[] Bad = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).Distinct().ToArray();

    /// <summary>Nom de fichier ou de dossier valide sous Windows.</summary>
    public static string SafeName(string name)
    {
        var s = new string(name.Select(c => Bad.Contains(c) || c < 32 ? ' ' : c).ToArray()).Trim().TrimEnd('.');
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length == 0 ? "Raccourci" : s;
    }

    /// <summary>
    /// Opérations pour arriver à l'état voulu : <paramref name="wanted"/> donne, pour chaque programme, le dossier
    /// ("" = à la racine du bureau) ou null pour ne pas l'avoir sur le bureau. Les programmes absents du dictionnaire ne bougent pas.
    /// </summary>
    public static List<OrganizeOp> Plan(IEnumerable<ShortcutItem> items, IReadOnlyDictionary<string, string?> wanted, string desktop, string removedDir)
    {
        var ops = new List<OrganizeOp>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!wanted.TryGetValue(item.Key, out var folder)) continue;
            if (folder == item.CurrentFolder) continue;
            if (folder == null)
            {
                if (item.OnDesktop != null)
                    ops.Add(new OrganizeOp(OrganizeOpKind.Remove, item.OnDesktop, Unique(Path.Combine(removedDir, Path.GetFileName(item.OnDesktop)), taken)));
                continue;
            }
            string dir = folder.Length == 0 ? desktop : Path.Combine(desktop, SafeName(folder));
            string target = Unique(Path.Combine(dir, SafeName(item.Name) + item.Extension), taken);
            if (item.OnDesktop != null) ops.Add(new OrganizeOp(OrganizeOpKind.Move, item.OnDesktop, target));
            else if (item.Source != null) ops.Add(new OrganizeOp(OrganizeOpKind.Create, item.Source, target));
            else if (item.UrlContent != null) ops.Add(new OrganizeOp(OrganizeOpKind.Create, "", target, item.UrlContent));
        }
        return ops;
    }

    private static string Unique(string path, HashSet<string> taken)
    {
        string dir = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        string p = path;
        for (int i = 2; File.Exists(p) || !taken.Add(p); i++) p = Path.Combine(dir, $"{name} ({i}){ext}");
        return p;
    }

    /// <summary>Applique les opérations une par une ; celles qui échouent sont renvoyées dans <paramref name="failed"/>.</summary>
    public static OrganizeJournal Apply(IEnumerable<OrganizeOp> ops, out List<OrganizeOp> failed)
    {
        var journal = new OrganizeJournal { WhenUtc = DateTime.UtcNow };
        failed = new List<OrganizeOp>();
        foreach (var op in ops)
        {
            try
            {
                string dir = Path.GetDirectoryName(op.To)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    journal.CreatedFolders.Add(dir);
                }
                switch (op.Kind)
                {
                    case OrganizeOpKind.Create when op.Content != null:
                        File.WriteAllText(op.To, op.Content);
                        break;
                    case OrganizeOpKind.Create:
                        File.Copy(op.From, op.To);
                        break;
                    default:
                        File.Move(op.From, op.To);
                        break;
                }
                journal.Done.Add(op);
            }
            catch (Exception) { failed.Add(op); }
        }
        // Dossiers du bureau vidés par le rangement (ex. ancien dossier « Jeux »).
        foreach (var dir in journal.Done.Where(o => o.Kind != OrganizeOpKind.Create).Select(o => Path.GetDirectoryName(o.From)!).Distinct())
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any() && journal.Done.Any(o => Path.GetDirectoryName(o.From) == dir)
                    && !journal.CreatedFolders.Contains(dir) && IsDesktopSubfolder(dir, journal))
                    Directory.Delete(dir);
            }
            catch (Exception) { }
        }
        return journal;
    }

    private static bool IsDesktopSubfolder(string dir, OrganizeJournal j) =>
        j.Done.Any(o => o.Kind == OrganizeOpKind.Move && Path.GetDirectoryName(Path.GetDirectoryName(o.To)!) == Path.GetDirectoryName(dir));

    /// <summary>Remet le bureau comme avant un rangement. Renvoie le nombre d'opérations qui n'ont pas pu être annulées.</summary>
    public static int Undo(OrganizeJournal journal)
    {
        int failed = 0;
        foreach (var op in Enumerable.Reverse(journal.Done))
        {
            try
            {
                if (op.Kind == OrganizeOpKind.Create) { if (File.Exists(op.To)) File.Delete(op.To); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(op.From)!);
                    File.Move(op.To, op.From);
                }
            }
            catch (Exception) { failed++; }
        }
        foreach (var d in journal.CreatedFolders.OrderByDescending(d => d.Length))
        {
            try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (Exception) { }
        }
        return failed;
    }

    /// <summary>Contenu d'un raccourci Internet (.url), ex. pour lancer un jeu Steam.</summary>
    public static string UrlShortcut(string url, string? icon) =>
        "[InternetShortcut]\r\nURL=" + url + "\r\n" + (icon != null ? "IconFile=" + icon + "\r\nIconIndex=0\r\n" : "");

    /// <summary>Lit l'adresse d'un fichier .url.</summary>
    public static string? UrlOf(string urlFileContent) =>
        urlFileContent.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..];

    /// <summary>Raccourcis du menu Démarrer qui ne sont pas des programmes (désinstalleurs, aides, sites web…).</summary>
    public static bool IsJunkShortcut(string name)
    {
        string n = name.ToLowerInvariant();
        string[] junk = { "uninstall", "désinstall", "desinstall", "readme", "lisez", "read me", "help", "aide", "manual", "manuel", "documentation", "website", "site web",
                          "support", "release notes", "license", "licence", "changelog", "what's new", "faq", "uninst", "repair", "réparer" };
        return junk.Any(n.Contains);
    }
}
