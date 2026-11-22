namespace Files7734;

public static class FsTree
{
    /// <summary>Retire un élément de l'arbre (après suppression) et met à jour les totaux de tous ses parents.</summary>
    public static void Remove(FsNode node)
    {
        var parent = node.Parent;
        if (parent == null) return;
        parent.Children?.Remove(node);
        long files = node.IsDirectory ? node.FileCount : 1;
        long folders = node.IsDirectory ? node.FolderCount + 1 : 0;
        for (var a = parent; a != null; a = a.Parent)
        {
            a.Size -= node.Size;
            a.Allocated -= node.Allocated;
            a.FileCount -= files;
            a.FolderCount -= folders;
        }
        node.Parent = null;
    }

    /// <summary>Vrai si <paramref name="node"/> est <paramref name="ancestor"/> ou se trouve à l'intérieur.</summary>
    public static bool IsInside(FsNode node, FsNode ancestor)
    {
        for (var n = node; n != null; n = n.Parent) if (n == ancestor) return true;
        return false;
    }

    /// <summary>Change le nom d'un élément après un renommage sur le disque.</summary>
    public static void Rename(FsNode node, string newName) => node.Name = newName;

    /// <summary>
    /// Regroupe plusieurs dossiers analysés séparément sous une racine artificielle.
    /// Chaque dossier garde son chemin complet comme nom.
    /// </summary>
    public static FsNode CreateGroup(string name, IEnumerable<FsNode> roots)
    {
        var group = new FsNode(name, true) { IsVirtual = true };
        foreach (var r in roots)
        {
            r.Name = r.FullPath;
            r.Parent = null;
            group.AddChild(r);
        }
        TreeBuilder.Finalize(group);
        return group;
    }

    /// <summary>
    /// Vue pour la carte : le dossier plus un bloc « Espace libre ».
    /// Ne modifie pas l'arbre (le dossier ne change pas de parent).
    /// </summary>
    public static FsNode CreateFreeSpaceView(FsNode root, long freeBytes, out FsNode freeNode)
    {
        freeNode = new FsNode("Espace libre", false) { IsVirtual = true, Size = freeBytes, Allocated = freeBytes };
        var view = new FsNode(root.Name, true)
        {
            IsVirtual = true,
            Children = new List<FsNode> { root, freeNode },
            Size = root.Size + freeBytes,
            Allocated = root.Allocated + freeBytes,
        };
        view.Children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        return view;
    }
}
