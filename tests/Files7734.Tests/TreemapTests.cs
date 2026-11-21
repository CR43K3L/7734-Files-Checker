using Files7734;
using Xunit;

namespace Files7734.Tests;

public class TreemapTests
{
    private static FsNode Build()
    {
        var dir = Directory.CreateTempSubdirectory("7734-tm-").FullName;
        try
        {
            var sizes = new[] { 6000, 6000, 4000, 3000, 2000, 2000, 1000 };
            for (int i = 0; i < sizes.Length; i++) File.WriteAllBytes(Path.Combine(dir, $"f{i}.bin"), new byte[sizes[i]]);
            Directory.CreateDirectory(Path.Combine(dir, "sous"));
            File.WriteAllBytes(Path.Combine(dir, "sous", "x.bin"), new byte[4000]);
            return DirectoryWalkScanner.Scan(dir);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AiresProportionnellesEtSansDebordement()
    {
        var root = Build();
        var rects = TreemapLayout.Layout(root, 600, 400);
        Assert.Equal(root, rects[0].Node);
        var level1 = rects.Where(r => r.Depth == 1).ToList();
        Assert.Equal(root.Children!.Count, level1.Count);
        double total = 600 * 400;
        foreach (var r in level1)
        {
            Assert.InRange(r.W * r.H, total * r.Node.Size / root.Size - 1, total * r.Node.Size / root.Size + 1);
            Assert.True(r.X >= -0.01 && r.Y >= -0.01 && r.X + r.W <= 600.01 && r.Y + r.H <= 400.01);
        }
    }

    [Fact]
    public void LeDernierRectangleContenantUnPointEstLePlusProfond()
    {
        var root = Build();
        var rects = TreemapLayout.Layout(root, 600, 400);
        var sous = rects.Single(r => r.Node.Name == "sous");
        Assert.False(sous.IsLeaf);
        var hit = rects.Last(r => r.Contains(sous.X + sous.W / 2, sous.Y + sous.H / 2));
        Assert.Equal("x.bin", hit.Node.Name);
    }

    [Fact]
    public void SuppressionMetAJourLesTotaux()
    {
        var root = Build();
        var sous = root.Children!.Single(c => c.Name == "sous");
        long before = root.Size;
        FsTree.Remove(sous.Children![0]);
        Assert.Equal(0, sous.Size);
        Assert.Equal(before - 4000, root.Size);
        Assert.Equal(7, root.FileCount);
    }
}
