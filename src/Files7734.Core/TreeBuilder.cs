namespace Files7734;

internal static class TreeBuilder
{
    /// <summary>
    /// Calcule les totaux des dossiers (taille, fichiers, sous-dossiers, date)
    /// et trie les enfants par taille décroissante. Sans récursion.
    /// </summary>
    public static void Finalize(FsNode root)
    {
        var order = new List<FsNode> { root };
        for (int i = 0; i < order.Count; i++)
        {
            var c = order[i].Children;
            if (c != null) order.AddRange(c);
        }

        foreach (var n in order)
        {
            if (!n.IsDirectory) continue;
            n.Size = 0;
            n.Allocated = 0;
            n.FileCount = 0;
            n.FolderCount = 0;
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            var n = order[i];
            if (n.IsDirectory)
                n.Children?.Sort(static (x, y) => y.Size.CompareTo(x.Size));

            var p = n.Parent;
            if (p == null || i == 0) continue;
            p.Size += n.Size;
            p.Allocated += n.Allocated;
            if (n.IsDirectory)
            {
                p.FileCount += n.FileCount;
                p.FolderCount += n.FolderCount + 1;
            }
            else
            {
                p.FileCount += 1;
            }
            if (n.LastModifiedUtc > p.LastModifiedUtc) p.LastModifiedUtc = n.LastModifiedUtc;
        }
    }
}
