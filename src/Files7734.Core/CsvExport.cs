using System.Globalization;
using System.Text;

namespace Files7734;

/// <summary>Export / import d'une analyse au format CSV (séparateur « ; », lisible dans Excel).</summary>
public static class CsvExport
{
    private const string Header = "Chemin;Taille;Taille sur le disque;Modifié;Fichiers;Dossiers;Type";

    public static void Write(FsNode root, TextWriter w)
    {
        w.WriteLine(Header);
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            w.Write(Quote(n.FullPath)); w.Write(';');
            w.Write(n.Size.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(n.Allocated.ToString(CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(n.LastModifiedUtc == default ? "" : n.LastModifiedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); w.Write(';');
            w.Write(n.IsDirectory ? n.FileCount.ToString(CultureInfo.InvariantCulture) : ""); w.Write(';');
            w.Write(n.IsDirectory ? n.FolderCount.ToString(CultureInfo.InvariantCulture) : ""); w.Write(';');
            w.WriteLine(n.IsDirectory ? "Dossier" : "Fichier");
            if (n.Children == null) continue;
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }

    public static void WriteExtensions(IEnumerable<ExtensionStat> stats, long total, TextWriter w)
    {
        w.WriteLine("Extension;Taille;Taille sur le disque;Pourcentage;Fichiers");
        foreach (var s in stats)
        {
            double pct = total > 0 ? 100.0 * s.Size / total : 0;
            w.WriteLine(string.Join(';',
                Quote(s.Extension.Length == 0 ? "(sans extension)" : s.Extension),
                s.Size.ToString(CultureInfo.InvariantCulture),
                s.Allocated.ToString(CultureInfo.InvariantCulture),
                pct.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR")),
                s.FileCount.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Recharge une analyse exportée. La première ligne de données est la racine.</summary>
    public static FsNode Read(TextReader r)
    {
        string? line = r.ReadLine();
        if (line == null || !line.StartsWith("Chemin;", StringComparison.Ordinal))
            throw new InvalidDataException("Ce fichier n'est pas un export CSV de 7734 Files Checker.");

        FsNode? root = null;
        var dirs = new Dictionary<string, FsNode>(StringComparer.OrdinalIgnoreCase);
        while ((line = r.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            var f = Split(line);
            if (f.Count < 7) continue;
            string path = f[0];
            bool isDir = f[6] == "Dossier";
            long.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size);
            long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long alloc);
            DateTime.TryParseExact(f[3], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var modified);

            if (root == null)
            {
                root = new FsNode(path, true) { LastModifiedUtc = modified };
                dirs[Trim(path)] = root;
                continue;
            }

            string trimmed = Trim(path);
            int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            if (cut < 0) continue;
            string parentPath = Trim(trimmed[..(cut + 1)]);
            if (!dirs.TryGetValue(parentPath, out var parent)) continue;

            var node = new FsNode(trimmed[(cut + 1)..], isDir) { LastModifiedUtc = modified };
            if (!isDir) { node.Size = size; node.Allocated = alloc; }
            parent.AddChild(node);
            if (isDir) dirs[trimmed] = node;
        }

        if (root == null) throw new InvalidDataException("Le fichier CSV est vide.");
        TreeBuilder.Finalize(root);
        return root;
    }

    private static string Trim(string p) => p.Length > 3 ? p.TrimEnd('\\', '/') : p.TrimEnd('/');

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static List<string> Split(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ';') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
