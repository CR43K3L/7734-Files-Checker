using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Files7734;

/// <summary>Un pack proposé dans l'app (liste publiée sur GitHub : packs/packs.json).</summary>
public sealed class PackInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    /// <summary>« Graphique », « Son »…</summary>
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Lien de partage Google Drive (ou lien direct).</summary>
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string? Image { get; set; }
}

public sealed class PackCatalog
{
    public List<PackInfo> Packs { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static PackCatalog Parse(string json)
    {
        var c = JsonSerializer.Deserialize<PackCatalog>(json, Options) ?? new PackCatalog();
        c.Packs = c.Packs.Where(p => p.Id.Length > 0 && p.Name.Length > 0 && p.Url.Length > 0).ToList();
        return c;
    }
}

/// <summary>Où vont les fichiers d'un pack.</summary>
public enum PackTarget { Mods, Plugins, Citizen, Game, App }

public sealed record PackTargets(string FiveMApp, string? Game)
{
    public string? Root(PackTarget t) => t switch
    {
        PackTarget.Mods => Path.Combine(FiveMApp, "mods"),
        PackTarget.Plugins => Path.Combine(FiveMApp, "plugins"),
        PackTarget.Citizen => Path.Combine(FiveMApp, "citizen"),
        PackTarget.App => FiveMApp,
        _ => Game,
    };
}

/// <summary>Ce qu'a posé un pack : de quoi le retirer proprement, même des mois plus tard.</summary>
public sealed class PackRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime InstalledUtc { get; set; }
    /// <summary>Fichiers copiés par le pack (chemins complets).</summary>
    public List<string> Files { get; set; } = new();
    /// <summary>Dossiers créés par le pack (supprimés s'ils sont vides à la désinstallation).</summary>
    public List<string> CreatedDirs { get; set; } = new();
    /// <summary>Fichiers d'origine remplacés par le pack, mis de côté puis remis en place à la désinstallation.</summary>
    public List<Backup> Backups { get; set; } = new();
    /// <summary>Date de la version de QuantV installée par l'app (ex. « 2026-11-22 »), si elle en a installé une.</summary>
    public string? QuantV { get; set; }
    public sealed record Backup(string Original, string Saved);

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static PackRecord? Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<PackRecord>(File.ReadAllText(file)) : null; }
        catch (Exception) { return null; }
    }
}

public sealed record PackProgress(string Step, double Fraction);

public static class FiveMPacks
{
    // ───────────── Liens Google Drive ─────────────

    /// <summary>Identifiant du fichier dans un lien de partage Google Drive (null si ce n'est pas un lien Drive).</summary>
    public static string? DriveId(string url)
    {
        var m = Regex.Match(url, @"drive\.google\.com/file/d/([A-Za-z0-9_-]{10,})");
        if (!m.Success) m = Regex.Match(url, @"(?:drive|docs)\.google\.com/.*[?&]id=([A-Za-z0-9_-]{10,})");
        if (!m.Success) m = Regex.Match(url, @"drive\.usercontent\.google\.com/.*[?&]id=([A-Za-z0-9_-]{10,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Lien de téléchargement direct (sans la page « impossible d'analyser ce fichier » des gros fichiers).</summary>
    public static string DownloadUrl(string url) =>
        DriveId(url) is string id ? $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t" : url;

    /// <summary>
    /// Pour un gros fichier, Drive peut renvoyer une page HTML avec un formulaire de confirmation : on en tire le vrai lien.
    /// </summary>
    public static string? ConfirmUrlFromHtml(string html)
    {
        var action = Regex.Match(html, @"<form[^>]+id=""download-form""[^>]+action=""([^""]+)""");
        if (!action.Success) action = Regex.Match(html, @"<form[^>]+action=""([^""]*download[^""]*)""");
        if (!action.Success) return null;
        var inputs = Regex.Matches(html, @"<input[^>]+type=""hidden""[^>]+name=""([^""]+)""[^>]+value=""([^""]*)""");
        var q = string.Join("&", inputs.Select(i => Uri.EscapeDataString(i.Groups[1].Value) + "=" + Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(i.Groups[2].Value))));
        string a = System.Net.WebUtility.HtmlDecode(action.Groups[1].Value);
        return q.Length == 0 ? a : a + (a.Contains('?') ? "&" : "?") + q;
    }

    // ───────────── Dossiers ─────────────

    /// <summary>Dossier de GTA V indiqué à FiveM (ligne « IVPath= » de CitizenFX.ini).</summary>
    public static string? GamePathFromCitizenIni(string ini)
    {
        foreach (var raw in ini.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("IVPath=", StringComparison.OrdinalIgnoreCase))
            {
                string p = line[7..].Trim().Trim('"');
                return p.Length > 0 ? p.TrimEnd('\\', '/') : null;
            }
        }
        return null;
    }

    /// <summary>Dossiers du cache de FiveM qu'on peut vider sans risque (FiveM les recrée). game-storage est gardé : il évite de tout retélécharger.</summary>
    /// <summary>Dossier, dans celui du jeu, où sont gardés les fichiers d'origine remplacés par un pack.</summary>
    public const string GameBackupFolder = "_7734_originaux";

    public static readonly string[] CacheFolders = { @"data\cache", @"data\server-cache", @"data\server-cache-priv", @"data\nui-storage" };

    /// <summary>Fichiers et dossiers typiques d'un ENB ou d'un ReShade dans le dossier du jeu.</summary>
    public static readonly string[] GraphicsModNames =
    {
        "d3d11.dll", "d3dcompiler_46e.dll", "dxgi.dll", "enbseries.ini", "enblocal.ini", "enbseries", "enbcache",
        "enbadaptation.fx", "enbbloom.fx", "enbdepthoffield.fx", "enbeffect.fx", "enbeffectprepass.fx", "enbeffectpostpass.fx",
        "enblens.fx", "enbpalette.bmp", "enbsunsprite.fx", "enbunderwater.fx", "enbraindrops", "enbspectrum.bmp",
        "reshade.ini", "reshade.log", "reshade-shaders", "reshadepreset.ini", "dxgi.log", "d3d11.log",
    };

    // ───────────── Contenu d'un pack ─────────────

    private static readonly Dictionary<string, PackTarget> TopFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mods"] = PackTarget.Mods, ["plugins"] = PackTarget.Plugins, ["citizen"] = PackTarget.Citizen,
        ["gta"] = PackTarget.Game, ["gtav"] = PackTarget.Game, ["gta v"] = PackTarget.Game, ["gta5"] = PackTarget.Game, ["gta 5"] = PackTarget.Game,
        ["gta legacy"] = PackTarget.Game, ["gta v legacy"] = PackTarget.Game, ["grand theft auto v"] = PackTarget.Game, ["jeu"] = PackTarget.Game,
    };

    public sealed record Placement(string Entry, PackTarget Target, string Relative);

    /// <summary>
    /// Où va chaque fichier de l'archive : le premier dossier (mods, plugins, citizen ou GTA) dit la destination.
    /// Un dossier racine unique qui englobe tout (ex. « MonPack\mods\… ») est ignoré. Les autres fichiers sont listés dans <c>ignored</c>.
    /// </summary>
    public static List<Placement> Layout(IEnumerable<string> entries, out List<string> ignored)
    {
        var files = entries.Select(e => e.Replace('\\', '/')).Where(e => !e.EndsWith('/')).ToList();
        ignored = new List<string>();
        string prefix = "";
        // Dossier racine englobant (on peut en enlever plusieurs : « Pack/Pack v2/mods/… »).
        while (true)
        {
            var firsts = files.Select(f => f[prefix.Length..]).Select(f => f.Contains('/') ? f[..f.IndexOf('/')] : null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (firsts.Count == 1 && firsts[0] is string only && !TopFolders.ContainsKey(only)) prefix += only + "/";
            else break;
        }
        var result = new List<Placement>();
        foreach (var f in files)
        {
            string rest = f[prefix.Length..];
            int slash = rest.IndexOf('/');
            if (slash <= 0 || !TopFolders.TryGetValue(rest[..slash], out var target)) { ignored.Add(f); continue; }
            string rel = rest[(slash + 1)..];
            if (rel.Length == 0 || rel.Split('/').Any(s => s == ".." || s == ".")) { ignored.Add(f); continue; }
            result.Add(new Placement(f, target, rel.Replace('/', Path.DirectorySeparatorChar)));
        }
        return result;
    }

    // ───────────── Installer / désinstaller ─────────────

    /// <summary>
    /// Installe le pack : les fichiers d'origine remplacés sont mis de côté dans <paramref name="backupDir"/>.
    /// En cas d'erreur, tout ce qui a été posé est retiré avant de relancer l'exception.
    /// </summary>
    public static PackRecord Install(string zipPath, PackInfo pack, PackTargets targets, string backupDir,
        IProgress<PackProgress>? progress = null, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var byName = zip.Entries.GroupBy(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var plan = Layout(byName.Keys, out _);
        if (plan.Count == 0) throw new InvalidDataException("Le pack ne contient aucun dossier mods, plugins ou GTA.");
        if (plan.Any(p => p.Target == PackTarget.Game) && string.IsNullOrEmpty(targets.Game))
            throw new InvalidOperationException("Le dossier de GTA V est introuvable : indique-le avant d'installer ce pack.");

        var record = new PackRecord { Id = pack.Id, Name = pack.Name, Version = pack.Version, InstalledUtc = DateTime.UtcNow };
        long total = Math.Max(1, plan.Sum(p => byName[p.Entry].Length)), done = 0;
        try
        {
            foreach (var p in plan)
            {
                ct.ThrowIfCancellationRequested();
                string root = Path.GetFullPath(targets.Root(p.Target)!);
                string dest = Path.GetFullPath(Path.Combine(root, p.Relative));
                if (!dest.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

                CreateDirs(Path.GetDirectoryName(dest)!, record);
                if (File.Exists(dest))
                {
                    // Les fichiers du jeu sont mis de côté sur le même disque que le jeu (déplacement instantané, même pour des Go).
                    string saved = p.Target == PackTarget.Game
                        ? Path.Combine(targets.Game!, GameBackupFolder, pack.Id, p.Relative)
                        : Path.Combine(backupDir, p.Target.ToString(), p.Relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    if (File.Exists(saved)) File.Delete(saved);
                    File.Move(dest, saved);
                    record.Backups.Add(new PackRecord.Backup(dest, saved));
                }
                record.Files.Add(dest);
                byName[p.Entry].ExtractToFile(dest, overwrite: true);
                done += byName[p.Entry].Length;
                progress?.Report(new PackProgress(Path.GetFileName(dest), (double)done / total));
            }
        }
        catch
        {
            Uninstall(record);
            throw;
        }
        return record;
    }

    private static void CreateDirs(string dir, PackRecord record)
    {
        var missing = new Stack<string>();
        for (string? d = dir; d != null && !Directory.Exists(d); d = Path.GetDirectoryName(d)) missing.Push(d);
        while (missing.Count > 0)
        {
            string d = missing.Pop();
            Directory.CreateDirectory(d);
            record.CreatedDirs.Add(d);
        }
    }

    /// <summary>Retire un pack : supprime ses fichiers, remet les originaux, enlève les dossiers qu'il a créés s'ils sont vides. Renvoie les fichiers qui n'ont pas pu être retirés.</summary>
    public static List<string> Uninstall(PackRecord record)
    {
        var failed = new List<string>();
        foreach (var f in record.Files)
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch (Exception) { failed.Add(f); }
        }
        foreach (var b in record.Backups)
        {
            try
            {
                if (!File.Exists(b.Saved)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(b.Original)!);
                if (File.Exists(b.Original)) File.Delete(b.Original);
                File.Move(b.Saved, b.Original);
            }
            catch (Exception) { failed.Add(b.Original); }
        }
        foreach (var d in record.CreatedDirs.OrderByDescending(d => d.Length))
        {
            try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); }
            catch (Exception) { }
        }
        // Dossiers de sauvegarde vidés.
        foreach (var dir in record.Backups.Select(b => Path.GetDirectoryName(b.Saved)!).Distinct().OrderByDescending(d => d.Length))
        {
            for (string? d = dir; d != null && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any(); d = Path.GetDirectoryName(d))
            {
                try { Directory.Delete(d); } catch (Exception) { break; }
                if (Path.GetFileName(d) is GameBackupFolder or "sauvegarde-packs") break;
            }
        }
        return failed;
    }

    /// <summary>
    /// Ancien pack installé à la main : contenu des dossiers mods et plugins, et fichiers ENB/ReShade du dossier du jeu.
    /// </summary>
    public static List<string> ManualPackItems(PackTargets targets)
    {
        var items = new List<string>();
        foreach (var t in new[] { PackTarget.Mods, PackTarget.Plugins })
        {
            string? root = targets.Root(t);
            if (root == null || !Directory.Exists(root)) continue;
            try { items.AddRange(Directory.EnumerateFileSystemEntries(root)); } catch (Exception) { }
        }
        if (targets.Game is string game && Directory.Exists(game))
            foreach (var n in GraphicsModNames)
            {
                string p = Path.Combine(game, n);
                if (File.Exists(p) || Directory.Exists(p)) items.Add(p);
            }
        return items;
    }

    /// <summary>Range les éléments d'un ancien pack dans <paramref name="dest"/> (rien n'est supprimé). Renvoie ceux qui n'ont pas pu être déplacés.</summary>
    public static List<string> MoveAside(IEnumerable<string> items, PackTargets targets, string dest)
    {
        var failed = new List<string>();
        foreach (var item in items)
        {
            try
            {
                string group = item.StartsWith(targets.FiveMApp, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Path.GetDirectoryName(item)!) : "GTA";
                string target = Path.Combine(dest, group, Path.GetFileName(item));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(item)) MoveDir(item, target);
                else File.Move(item, target, overwrite: true);
            }
            catch (Exception) { failed.Add(item); }
        }
        return failed;
    }

    private static void MoveDir(string src, string dst)
    {
        try { Directory.Move(src, dst); return; }
        catch (IOException) when (!Directory.Exists(dst) && Path.GetPathRoot(src) != Path.GetPathRoot(dst)) { }
        // Autre disque : copie puis suppression.
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string t = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t, overwrite: true);
        }
        Directory.Delete(src, recursive: true);
    }

    // ───────────── Mises à jour QuantV ─────────────

    public sealed record QuantVPlan(List<Placement> Files, List<string> GpuOptions, string? Date);

    /// <summary>
    /// Contenu d'un zip QuantV FiveM : « Part1\FiveM_App_data\… » toujours, puis une seule option de « Part2 »
    /// (« NUI in-process GPU on (default) » ou « … off … »). QV_Optionals et le README sont ignorés.
    /// </summary>
    public static QuantVPlan QuantVLayout(IEnumerable<string> entries, string zipName, bool? gpuInProcess)
    {
        var files = entries.Select(e => e.Replace('\\', '/')).Where(e => !e.EndsWith('/')).ToList();
        var result = new List<Placement>();
        var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        const string data = "FiveM_App_data/";
        foreach (var f in files)
        {
            var parts = f.Split('/');
            int part = Array.FindIndex(parts, x => Regex.IsMatch(x, @"^Part\d+$", RegexOptions.IgnoreCase));
            int dataAt = f.IndexOf(data, StringComparison.OrdinalIgnoreCase);
            if (part < 0 || dataAt < 0) continue;
            string rel = f[(dataAt + data.Length)..];
            if (rel.Length == 0 || rel.Split('/').Any(x => x is ".." or ".")) continue;
            // Une option de Part2 : le dossier entre « PartN » et « FiveM_App_data ».
            string between = string.Join("/", parts.Skip(part + 1).TakeWhile(x => !x.Equals("FiveM_App_data", StringComparison.OrdinalIgnoreCase)));
            if (between.Length > 0)
            {
                options.Add(between);
                bool off = Regex.IsMatch(between, @"\bgpu\s*off\b", RegexOptions.IgnoreCase);
                bool want = gpuInProcess ?? true;
                if (off == want) continue;
            }
            result.Add(new Placement(f, PackTarget.App, rel.Replace('/', Path.DirectorySeparatorChar)));
        }
        var date = Regex.Match(zipName, @"\d{4}-\d{2}-\d{2}");
        return new QuantVPlan(result, options.ToList(), date.Success ? date.Value : null);
    }

    /// <summary>
    /// Ajoute ou remplace des fichiers et les inscrit dans <paramref name="record"/> (pour qu'ils partent avec le pack).
    /// Un fichier qui n'appartenait pas au pack est mis de côté ; en cas d'erreur, tout revient comme avant.
    /// </summary>
    public static void Merge(string zipPath, IReadOnlyList<Placement> plan, PackTargets targets, PackRecord record, string backupDir,
        IProgress<PackProgress>? progress = null)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var byName = zip.Entries.GroupBy(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var owned = new HashSet<string>(record.Files, StringComparer.OrdinalIgnoreCase);
        string tx = Path.Combine(backupDir, "en-cours-" + Guid.NewGuid().ToString("N")[..8]);
        var swapped = new List<(string Dest, string Temp)>();
        var added = new List<string>();
        var newBackups = new List<PackRecord.Backup>();
        var newDirs = new PackRecord();
        long total = Math.Max(1, plan.Sum(p => byName.TryGetValue(p.Entry, out var e) ? e.Length : 0)), done = 0;
        try
        {
            foreach (var p in plan)
            {
                if (!byName.TryGetValue(p.Entry, out var entry)) continue;
                string root = Path.GetFullPath(targets.Root(p.Target)!);
                string dest = Path.GetFullPath(Path.Combine(root, p.Relative));
                if (!dest.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                CreateDirs(Path.GetDirectoryName(dest)!, newDirs);
                if (File.Exists(dest))
                {
                    string temp = Path.Combine(owned.Contains(dest) ? tx : Path.Combine(backupDir, "QuantV"), p.Target.ToString(), p.Relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                    if (File.Exists(temp)) File.Delete(temp);
                    File.Move(dest, temp);
                    if (owned.Contains(dest)) swapped.Add((dest, temp));
                    else newBackups.Add(new PackRecord.Backup(dest, temp));
                }
                if (!owned.Contains(dest)) added.Add(dest);
                entry.ExtractToFile(dest, overwrite: true);
                done += entry.Length;
                progress?.Report(new PackProgress(Path.GetFileName(dest), (double)done / total));
            }
        }
        catch
        {
            // Retour en arrière : nouveaux fichiers retirés, anciens remis.
            Uninstall(new PackRecord { Files = added, Backups = newBackups, CreatedDirs = newDirs.CreatedDirs });
            foreach (var (dest, temp) in swapped) { try { File.Move(temp, dest, overwrite: true); } catch (Exception) { } }
            try { if (Directory.Exists(tx)) Directory.Delete(tx, true); } catch (Exception) { }
            throw;
        }
        try { if (Directory.Exists(tx)) Directory.Delete(tx, true); } catch (Exception) { }
        record.Files.AddRange(added);
        record.Backups.AddRange(newBackups);
        record.CreatedDirs.AddRange(newDirs.CreatedDirs);
    }

    public static bool IsNewer(string available, string installed)
    {
        static Version V(string s) => Version.TryParse(Regex.Match(s, @"\d+(\.\d+){0,3}").Value is { Length: > 0 } v ? (v.Contains('.') ? v : v + ".0") : "0.0", out var r) ? r : new Version(0, 0);
        return V(available) > V(installed);
    }
}
