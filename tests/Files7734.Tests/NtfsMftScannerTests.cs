using System.IO.Compression;
using Files7734;
using Xunit;

namespace Files7734.Tests;

/// <summary>
/// Teste la lecture MFT sur une petite image NTFS réelle (créée avec mkntfs puis remplie) :
/// Vidéos/clip.mp4 (1 500 000 o), Jeux/data.pak (400 000 o), Jeux/Sauvegardes/save.txt + 50 fichiers .sav,
/// petit.bin (600 o, stocké dans la MFT) avec un lien dur lien.bin, creux.vhd (fichier creux de 100 Mo).
/// </summary>
public class NtfsMftScannerTests : IDisposable
{
    private readonly string _image;

    public NtfsMftScannerTests()
    {
        _image = Path.Combine(Path.GetTempPath(), $"7734-{Guid.NewGuid():N}.img");
        using var gz = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "petit-ntfs.img.gz")), CompressionMode.Decompress);
        using var outFile = File.Create(_image);
        gz.CopyTo(outFile);
    }

    public void Dispose() => File.Delete(_image);

    private FsNode Scan()
    {
        using var vol = new ImageFileVolumeSource(_image);
        return NtfsMftScanner.Scan(vol, "X:\\");
    }

    private static FsNode Child(FsNode n, string name) => n.Children!.Single(c => c.Name == name);

    [Fact]
    public void ReconstruitLArborescence()
    {
        var root = Scan();
        Assert.Equal("X:\\", root.Name);
        Assert.Equal(1_500_000, Child(Child(root, "Vidéos"), "clip.mp4").Size);
        var jeux = Child(root, "Jeux");
        Assert.Equal(400_000, Child(jeux, "data.pak").Size);
        var saves = Child(jeux, "Sauvegardes");
        Assert.Equal(51, saves.FileCount);
        Assert.Equal(52, jeux.FileCount);
        Assert.Equal(1, jeux.FolderCount);
    }

    [Fact]
    public void GereFichiersCreuxEtResidents()
    {
        var root = Scan();
        var sparse = Child(root, "creux.vhd");
        Assert.Equal(100L * 1024 * 1024, sparse.Size);
        Assert.True(sparse.Allocated < 1024 * 1024, "un fichier creux n'occupe presque rien sur le disque");

        var resident = Child(root, "petit.bin");
        Assert.Equal(600, resident.Size);
        Assert.Equal(0, resident.Allocated);
    }

    [Fact]
    public void CompteUnLienDurUneSeuleFois()
    {
        var root = Scan();
        int count = root.Children!.Count(c => c.Name is "petit.bin" or "lien.bin");
        Assert.Equal(1, count);
    }

    [Fact]
    public void TotauxCoherents()
    {
        var root = Scan();
        long sum = Analysis.EnumerateFiles(root).Sum(f => f.Size);
        Assert.Equal(sum, root.Size);
        Assert.Equal(root.Size, root.Children!.Sum(c => c.Size));
        // Les enfants sont triés du plus gros au plus petit.
        Assert.Equal(root.Children!.OrderByDescending(c => c.Size).Select(c => c.Name), root.Children!.Select(c => c.Name));
    }

    [Fact]
    public void RefuseUnVolumeNonNtfs()
    {
        File.WriteAllBytes(_image, new byte[8192]);
        Assert.Throws<InvalidDataException>(Scan);
    }
}
