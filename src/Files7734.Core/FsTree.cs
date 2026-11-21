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
}
