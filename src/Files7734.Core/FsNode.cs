namespace Files7734;

/// <summary>Un fichier ou un dossier de l'arbre scanné.</summary>
public sealed class FsNode
{
    public FsNode(string name, bool isDirectory)
    {
        Name = name;
        IsDirectory = isDirectory;
    }

    public string Name { get; internal set; }
    public bool IsDirectory { get; }

    /// <summary>Nœud artificiel (regroupement de plusieurs dossiers, espace libre…) : n'existe pas sur le disque.</summary>
    public bool IsVirtual { get; internal set; }
    public FsNode? Parent { get; internal set; }
    public List<FsNode>? Children { get; internal set; }

    /// <summary>Taille logique en octets (total du contenu pour un dossier).</summary>
    public long Size { get; internal set; }

    /// <summary>Espace réellement occupé sur le disque, en octets.</summary>
    public long Allocated { get; internal set; }

    /// <summary>Nombre de fichiers contenus (récursif). Vaut 0 pour un fichier.</summary>
    public long FileCount { get; internal set; }

    /// <summary>Nombre de sous-dossiers contenus (récursif).</summary>
    public long FolderCount { get; internal set; }

    /// <summary>Dernière modification (UTC). Pour un dossier, la plus récente de son contenu.</summary>
    public DateTime LastModifiedUtc { get; internal set; }

    public string Extension
    {
        get
        {
            if (IsDirectory) return string.Empty;
            int dot = Name.LastIndexOf('.');
            return dot <= 0 || dot == Name.Length - 1 ? string.Empty : Name[dot..].ToLowerInvariant();
        }
    }

    public string FullPath
    {
        get
        {
            var parts = new Stack<string>();
            for (var n = this; n != null && !n.IsVirtual; n = n.Parent) parts.Push(n.Name);
            if (parts.Count == 0) return Name;
            string path = parts.Pop();
            while (parts.Count > 0) path = PathUtil.Combine(path, parts.Pop());
            return path;
        }
    }

    internal void AddChild(FsNode child)
    {
        child.Parent = this;
        (Children ??= new List<FsNode>()).Add(child);
    }

    public override string ToString() => FullPath;
}

internal static class PathUtil
{
    public static string Combine(string a, string b)
    {
        if (a.Length == 0) return b;
        char last = a[^1];
        return last is '\\' or '/' ? a + b : a + Path.DirectorySeparatorChar + b;
    }
}
