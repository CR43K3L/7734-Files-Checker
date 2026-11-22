using Files7734;
using Xunit;

namespace Files7734.Tests;

public class GameParserTests
{
    [Fact]
    public void LitLesBibliothequesSteam()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "apps" { "730" "123" }
                }
                "1"
                {
                    "path"      "D:\\SteamLibrary"
                }
            }
            """;
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameParsers.SteamLibraries(vdf));
    }

    [Fact]
    public void LitUnManifesteSteam()
    {
        const string acf = """
            "AppState"
            {
                "appid"     "271590"
                "name"      "Grand Theft Auto V"
                "installdir"        "Grand Theft Auto V"
            }
            """;
        var m = GameParsers.SteamManifest(acf);
        Assert.Equal(("Grand Theft Auto V", "271590", "Grand Theft Auto V"), m);
    }

    [Fact]
    public void LitEpicEtRiot()
    {
        var epic = GameParsers.EpicManifest("""{ "DisplayName": "Fortnite", "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite" }""");
        Assert.Equal(("Fortnite", @"C:\Program Files\Epic Games\Fortnite"), epic);
        Assert.Null(GameParsers.EpicManifest("pas du json"));

        Assert.Equal(@"C:\Riot Games\VALORANT\live", GameParsers.RiotInstallPath("product_install_full_path: \"C:/Riot Games/VALORANT/live\"\nother: 1"));
        Assert.Equal("League of Legends", GameParsers.RiotProductName("league_of_legends.live"));
    }
}

public class CleanerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("7734-clean-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void MesureEtVideLeContenuSansToucherAuxDossiersCibles()
    {
        foreach (var profile in new[] { "Default", "Profile 1" })
        {
            var cache = Path.Combine(_dir, "User Data", profile, "Cache", "sub");
            Directory.CreateDirectory(cache);
            File.WriteAllBytes(Path.Combine(cache, "a.bin"), new byte[1000]);
        }
        File.WriteAllBytes(Path.Combine(_dir, "User Data", "Default", "Bookmarks"), new byte[50]); // ne doit pas être touché

        var cat = new CleanupCategory("t", "Test", "", Path.Combine(_dir, "User Data", "*", "Cache"));
        Assert.Equal(2, Cleaner.ExpandTarget(cat.Targets[0]).Count);
        Assert.Equal(new CleanupMeasure(2000, 2), Cleaner.Measure(cat));

        var outcome = Cleaner.Clean(cat);
        Assert.Equal(2000, outcome.FreedBytes);
        Assert.True(Directory.Exists(Path.Combine(_dir, "User Data", "Default", "Cache")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "User Data", "Default", "Cache", "sub")));
        Assert.True(File.Exists(Path.Combine(_dir, "User Data", "Default", "Bookmarks")));
        Assert.Equal(new CleanupMeasure(0, 0), Cleaner.Measure(cat));
    }

    [Fact]
    public void JokerSurUnNomDeFichier()
    {
        File.WriteAllBytes(Path.Combine(_dir, "thumbcache_32.db"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_dir, "autre.db"), new byte[10]);
        var cat = new CleanupCategory("t", "Test", "", Path.Combine(_dir, "thumbcache_*.db"));
        Assert.Equal(new CleanupMeasure(10, 1), Cleaner.Measure(cat));
    }

    [Fact]
    public void CategoriesWindowsNonVides() => Assert.True(Cleaner.WindowsCategories().Count >= 8);
}

public class DuplicateAndOldFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("7734-dup-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void TrouveLesFichiersIdentiques()
    {
        var content = new byte[200_000];
        new Random(1).NextBytes(content);
        Directory.CreateDirectory(Path.Combine(_dir, "copie"));
        File.WriteAllBytes(Path.Combine(_dir, "a.bin"), content);
        File.WriteAllBytes(Path.Combine(_dir, "copie", "a (2).bin"), content);
        var different = (byte[])content.Clone();
        different[^1] ^= 1; // même taille, même début, fin différente
        File.WriteAllBytes(Path.Combine(_dir, "b.bin"), different);

        var root = DirectoryWalkScanner.Scan(_dir);
        var groups = DuplicateFinder.Find(root, minSize: 1000);
        var g = Assert.Single(groups);
        Assert.Equal(2, g.Files.Count);
        Assert.Equal(200_000, g.Wasted);
        Assert.DoesNotContain(g.Files, f => f.Name == "b.bin");
    }

    [Fact]
    public void VieuxFichiers()
    {
        string oldFile = Path.Combine(_dir, "vieux.iso"), newFile = Path.Combine(_dir, "recent.iso");
        File.WriteAllBytes(oldFile, new byte[3000]);
        File.WriteAllBytes(newFile, new byte[3000]);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddYears(-3));
        var root = DirectoryWalkScanner.Scan(_dir);
        var old = OldFiles.Find(root, DateTime.UtcNow.AddYears(-1), minSize: 1000);
        Assert.Equal("vieux.iso", Assert.Single(old).Name);
    }
}

public class SnapshotTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("7734-snap-").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private void Write(string rel, int size)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    [Fact]
    public void DetecteCeQuiAGrossiEtGardeLeDossierLePlusPrecis()
    {
        Write(@"Jeux/CoD/data.pak", 1000);
        Write(@"Docs/a.txt", 5000);
        var before = Snapshot.Create(DirectoryWalkScanner.Scan(_dir), minSize: 1);

        Write(@"Jeux/CoD/update.pak", 9000);  // +9000 dans Jeux\CoD
        File.Delete(Path.Combine(_dir, "Docs", "a.txt"));
        Write(@"Nouveau/film.mkv", 4000);
        var after = Snapshot.Create(DirectoryWalkScanner.Scan(_dir), minSize: 1);

        var changes = SnapshotDiff.Compare(before, after, minDelta: 1000);
        Assert.Equal("Jeux" + "\\" + "CoD", changes[0].RelativePath);
        Assert.Equal(9000, changes[0].Delta);
        Assert.DoesNotContain(changes, c => c.RelativePath == "Jeux"); // expliqué par Jeux\CoD
        Assert.Contains(changes, c => c.RelativePath == "Nouveau" && c.Kind == ChangeKind.New);
        Assert.Contains(changes, c => c.RelativePath == "Docs" && c.Delta == -5000);
    }

    [Fact]
    public void SauvegardeEtRelecture()
    {
        Write("x/y.bin", 100);
        var s = Snapshot.Create(DirectoryWalkScanner.Scan(_dir), minSize: 1);
        string file = Path.Combine(_dir, "snap.gz");
        s.Save(file);
        var back = Snapshot.Load(file)!;
        Assert.Equal(s.TotalSize, back.TotalSize);
        Assert.Equal(100, back.Folders["x"]);
        Assert.Equal(100, back.Folders["X"]); // insensible à la casse
    }
}
