using Files7734;
using Xunit;

namespace Files7734.Tests;

public class DirectoryWalkScannerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("7734-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private void Write(string rel, int size)
    {
        string p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    [Fact]
    public void CalculeTaillesEtStatistiques()
    {
        Write("a/gros.iso", 50_000);
        Write("a/b/moyen.ISO", 20_000);
        Write("c/petit.txt", 10);
        Write("sansext", 5);

        var status = new ScanStatus();
        var root = DirectoryWalkScanner.Scan(_dir, 4096, status);

        Assert.Equal(70_015, root.Size);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(3, root.FolderCount);
        Assert.Equal("a", root.Children![0].Name);
        Assert.Equal(4096 * 13 + 4096 * 5 + 4096 + 4096, root.Allocated);

        var top = Analysis.LargestFiles(root, 2);
        Assert.Equal(new[] { "gros.iso", "moyen.ISO" }, top.Select(f => f.Name));
        Assert.Equal(Path.Combine(_dir, "a", "gros.iso"), top[0].FullPath);

        var ext = Analysis.ByExtension(root);
        Assert.Equal(".iso", ext[0].Extension);
        Assert.Equal(2, ext[0].FileCount);
        Assert.Contains(ext, e => e.Extension == "" && e.Size == 5);
    }

    [Fact]
    public void DiskScannerRetrouveUnSousDossier()
    {
        Write("x/y/z.bin", 100);
        var res = DiskScanner.Scan(Path.Combine(_dir, "x"));
        Assert.Equal(100, res.Root.Size);
        // Sous Windows en administrateur, la lecture directe de la table NTFS est utilisée : les deux sont valables.
        Assert.Contains(res.Method, new[] { ScanMethod.DirectoryWalk, ScanMethod.Mft });
    }

    [Theory]
    [InlineData(0, "0 o")]
    [InlineData(1536, "1,5 Ko")]
    [InlineData(5L * 1024 * 1024 * 1024, "5,0 Go")]
    public void FormateLesTailles(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes).Replace('\u00a0', ' '));
}
