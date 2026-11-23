using Files7734;
using Xunit;

namespace Files7734.Tests;

public class CsvAndToolsTests : IDisposable
{
    // Les guillemets sont interdits dans les noms de fichiers Windows.
    private static readonly string OddName = OperatingSystem.IsWindows() ? "x;'bizarre' ,é.bin" : "x;\"bizarre\".bin";

    private readonly string _dir = Directory.CreateTempSubdirectory("7734-csv-").FullName;

    public void Dispose()
    {
        ByteSize.Unit = SizeUnit.Auto;
        Directory.Delete(_dir, true);
    }

    private FsNode Sample()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "a", "b"));
        File.WriteAllBytes(Path.Combine(_dir, "a", "b", OddName), new byte[3000]);
        File.WriteAllBytes(Path.Combine(_dir, "a", "y.txt"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_dir, "z.iso"), new byte[50000]);
        return DirectoryWalkScanner.Scan(_dir);
    }

    [Fact]
    public void ExportPuisImportRedonneLeMemeArbre()
    {
        var root = Sample();
        var sw = new StringWriter();
        CsvExport.Write(root, sw);
        var back = CsvExport.Read(new StringReader(sw.ToString()));

        Assert.Equal(root.Size, back.Size);
        Assert.Equal(root.FileCount, back.FileCount);
        Assert.Equal(root.FolderCount, back.FolderCount);
        var odd = Analysis.EnumerateFiles(back).Single(f => f.Name.StartsWith("x;"));
        Assert.Equal(OddName, odd.Name);
        Assert.Equal(Path.Combine(_dir, "a", "b", OddName), odd.FullPath);
    }

    [Fact]
    public void RefuseUnCsvEtranger()
    {
        Assert.Throws<InvalidDataException>(() => CsvExport.Read(new StringReader("a,b,c\n1,2,3")));
    }

    [Fact]
    public void GroupeDeDossiersGardeLesCheminsComplets()
    {
        Sample();
        var a = DirectoryWalkScanner.Scan(Path.Combine(_dir, "a"));
        var z = DirectoryWalkScanner.Scan(Path.Combine(_dir, "a", "b"));
        var group = FsTree.CreateGroup("Sélection", new[] { a, z });
        Assert.Equal(3010 + 3000, group.Size);
        Assert.Equal(Path.Combine(_dir, "a", "y.txt"), group.Children!.First(c => c.Size == 3010).Children!.Single(c => c.Name == "y.txt").FullPath);
    }

    [Fact]
    public void EspaceLibreNeModifiePasLArbre()
    {
        var root = Sample();
        var view = FsTree.CreateFreeSpaceView(root, 1_000_000, out var free);
        Assert.Null(root.Parent);
        Assert.Equal(root.Size + 1_000_000, view.Size);
        Assert.Equal(free, view.Children![0]);
    }

    [Theory]
    [InlineData(SizeUnit.Bytes, 1536, "1 536 o")]
    [InlineData(SizeUnit.KB, 1536, "1,5 Ko")]
    [InlineData(SizeUnit.GB, 1073741824L * 3, "3,00 Go")]
    public void UnitesForcees(SizeUnit unit, long bytes, string expected)
    {
        ByteSize.Unit = unit;
        Assert.Equal(expected, ByteSize.Format(bytes).Replace(' ', ' ').Replace(' ', ' '));
    }
}

public class SearchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("7734-search-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void JokersEtTexte()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Films"));
        File.WriteAllBytes(Path.Combine(_dir, "Films", "a.MP4"), new byte[500]);
        File.WriteAllBytes(Path.Combine(_dir, "b.mp4"), new byte[900]);
        File.WriteAllBytes(Path.Combine(_dir, "film.txt"), new byte[10]);
        var root = DirectoryWalkScanner.Scan(_dir);

        Assert.Equal(new[] { "b.mp4", "a.MP4" }, Search.Find(root, "*.mp4").Select(n => n.Name));
        Assert.Equal(new[] { "Films", "film.txt" }, Search.Find(root, "film").Select(n => n.Name));
        Assert.Empty(Search.Find(root, "  "));
    }
}
