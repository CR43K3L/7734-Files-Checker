using System.IO.Compression;
using Files7734;
using Xunit;

namespace Files7734.Tests;

public class FiveMTests
{
    [Theory]
    [InlineData("https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp_q-r/view?usp=sharing", "1AbCdEfGhIjKlMnOp_q-r")]
    [InlineData("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOp", "1AbCdEfGhIjKlMnOp")]
    [InlineData("https://drive.google.com/uc?export=download&id=1AbCdEfGhIjKlMnOp", "1AbCdEfGhIjKlMnOp")]
    public void ReadsDriveIds(string url, string id)
    {
        Assert.Equal(id, FiveMPacks.DriveId(url));
        Assert.Contains("id=" + id, FiveMPacks.DownloadUrl(url));
    }

    [Fact]
    public void NonDriveLinksAreKept() => Assert.Equal("https://exemple.fr/p.zip", FiveMPacks.DownloadUrl("https://exemple.fr/p.zip"));

    [Fact]
    public void ReadsConfirmForm()
    {
        string html = "<form id=\"download-form\" action=\"https://drive.usercontent.google.com/download\" method=\"get\">"
                    + "<input type=\"hidden\" name=\"id\" value=\"ABC\"><input type=\"hidden\" name=\"confirm\" value=\"t\"><input type=\"hidden\" name=\"uuid\" value=\"u-1\"></form>";
        Assert.Equal("https://drive.usercontent.google.com/download?id=ABC&confirm=t&uuid=u-1", FiveMPacks.ConfirmUrlFromHtml(html));
    }

    [Fact]
    public void ReadsGamePathFromCitizenIni() =>
        Assert.Equal(@"D:\Jeux\Grand Theft Auto V", FiveMPacks.GamePathFromCitizenIni("[Game]\r\nIVPath=D:\\Jeux\\Grand Theft Auto V\\\r\nUpdateChannel=production\r\n"));

    [Fact]
    public void LayoutStripsWrappingFolder()
    {
        var plan = FiveMPacks.Layout(new[] { "Mon Pack/", "Mon Pack/mods/a.rpf", "Mon Pack/Plugins/b.dll", "Mon Pack/GTA/enbseries.ini", "Mon Pack/GTA/enbseries/x.fx", "Mon Pack/lisez-moi.txt" }, out var ignored);
        Assert.Equal(4, plan.Count);
        Assert.Contains(plan, p => p.Target == PackTarget.Mods && p.Relative == "a.rpf");
        Assert.Contains(plan, p => p.Target == PackTarget.Plugins);
        Assert.Contains(plan, p => p.Target == PackTarget.Game && p.Relative == Path.Combine("enbseries", "x.fx"));
        Assert.Equal(new[] { "Mon Pack/lisez-moi.txt" }, ignored);
    }

    [Fact]
    public void LayoutRejectsTraversal()
    {
        var plan = FiveMPacks.Layout(new[] { "mods/../../evil.dll", "mods/ok.rpf" }, out var ignored);
        Assert.Single(plan);
        Assert.Single(ignored);
    }

    [Fact]
    public void InstallThenUninstallRestoresOriginals()
    {
        string dir = Path.Combine(Path.GetTempPath(), "7734-fivem-" + Guid.NewGuid().ToString("N"));
        try
        {
            string app = Path.Combine(dir, "FiveM.app"), game = Path.Combine(dir, "GTA"), backup = Path.Combine(dir, "backup");
            Directory.CreateDirectory(Path.Combine(app, "mods"));
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "original.dat"), "origine");
            File.WriteAllText(Path.Combine(app, "mods", "autre.rpf"), "autre");

            string zipPath = Path.Combine(dir, "pack.zip");
            using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                void Add(string name, string text) { using var w = new StreamWriter(z.CreateEntry(name).Open()); w.Write(text); }
                Add("Pack/mods/graph.rpf", "pack");
                Add("Pack/plugins/reshade/x.fx", "fx");
                Add("Pack/GTA/original.dat", "remplacé");
                Add("Pack/GTA/enbseries.ini", "enb");
            }

            var record = FiveMPacks.Install(zipPath, new PackInfo { Id = "p", Name = "P", Version = "1.2" }, new PackTargets(app, game), backup);
            Assert.Equal(4, record.Files.Count);
            Assert.Equal("remplacé", File.ReadAllText(Path.Combine(game, "original.dat")));
            Assert.True(File.Exists(Path.Combine(app, "plugins", "reshade", "x.fx")));

            string recFile = Path.Combine(dir, "installed.json");
            record.Save(recFile);
            var loaded = PackRecord.Load(recFile)!;
            Assert.Empty(FiveMPacks.Uninstall(loaded));

            Assert.Equal("origine", File.ReadAllText(Path.Combine(game, "original.dat")));
            Assert.False(File.Exists(Path.Combine(game, "enbseries.ini")));
            Assert.False(File.Exists(Path.Combine(app, "mods", "graph.rpf")));
            Assert.True(File.Exists(Path.Combine(app, "mods", "autre.rpf")));
            Assert.False(Directory.Exists(Path.Combine(app, "plugins")));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact]
    public void FindsAndMovesManualPack()
    {
        string dir = Path.Combine(Path.GetTempPath(), "7734-fivem-" + Guid.NewGuid().ToString("N"));
        try
        {
            string app = Path.Combine(dir, "FiveM.app"), game = Path.Combine(dir, "GTA");
            Directory.CreateDirectory(Path.Combine(app, "mods"));
            Directory.CreateDirectory(Path.Combine(game, "enbseries"));
            File.WriteAllText(Path.Combine(app, "mods", "vieux.rpf"), "x");
            File.WriteAllText(Path.Combine(game, "d3d11.dll"), "x");
            File.WriteAllText(Path.Combine(game, "GTA5.exe"), "x");
            var t = new PackTargets(app, game);
            var items = FiveMPacks.ManualPackItems(t);
            Assert.Equal(3, items.Count);
            Assert.Empty(FiveMPacks.MoveAside(items, t, Path.Combine(dir, "ancien")));
            Assert.True(File.Exists(Path.Combine(dir, "ancien", "mods", "vieux.rpf")));
            Assert.True(File.Exists(Path.Combine(dir, "ancien", "GTA", "d3d11.dll")));
            Assert.True(Directory.Exists(Path.Combine(dir, "ancien", "GTA", "enbseries")));
            Assert.True(File.Exists(Path.Combine(game, "GTA5.exe")));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact]
    public void CatalogParsesAndCompareVersions()
    {
        var c = PackCatalog.Parse("{ \"packs\": [ { \"id\": \"a\", \"name\": \"A\", \"version\": \"2.1\", \"url\": \"https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/view\" }, { \"id\": \"\", \"name\": \"x\", \"url\": \"u\" }, ] }");
        Assert.Single(c.Packs);
        Assert.True(FiveMPacks.IsNewer("2.1", "2.0"));
        Assert.True(FiveMPacks.IsNewer("v3", "2.9"));
        Assert.False(FiveMPacks.IsNewer("1.0", "1.0"));
    }
}

public class GtaSettingsTests
{
    private const string Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Settings>\n  <version value=\"27\" />\n  <graphics>\n    <TextureQuality value=\"2\" />\n    <LodScale value=\"0.800000\" />\n    <MSAA value=\"0\" />\n  </graphics>\n  <video>\n    <VSync value=\"1\" />\n  </video>\n</Settings>";

    [Fact]
    public void ReadsAndWritesValues()
    {
        var s = GtaSettings.Parse(Xml);
        Assert.Equal("2", s.Get("TextureQuality"));
        s.Set("TextureQuality", "0");
        s.Set("GrassQuality", "1");
        var again = GtaSettings.Parse(s.ToString());
        Assert.Equal("0", again.Get("TextureQuality"));
        Assert.Equal("1", again.Get("GrassQuality"));
        Assert.Contains("VSync", s.ToString());
    }

    [Fact]
    public void PresetsCoverKnownOptionsAndMatch()
    {
        foreach (var p in GtaSettings.Presets)
        {
            foreach (var (k, v) in p.Values)
            {
                var o = GtaSettings.Options.Single(x => x.Key == k);
                Assert.Contains(o.Choices, c => c.Value == v);
            }
            var s = GtaSettings.Parse(Xml);
            s.ApplyPreset(p.Values);
            Assert.Equal(p.Name, s.CurrentPreset());
        }
    }

    [Fact]
    public void MatchesClosestNumber() =>
        Assert.Equal("1.000000", GtaSettings.MatchChoice(GtaSettings.Options.Single(o => o.Key == "LodScale"), "0.800000"));
}
