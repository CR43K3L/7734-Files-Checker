using Files7734;
using Xunit;

namespace Files7734.Tests;

public class OrganizerTests
{
    [Fact]
    public void PlansMovesCreatesAndRemovesThenUndoes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "7734-org-" + Guid.NewGuid().ToString("N"));
        try
        {
            string desktop = Path.Combine(dir, "Bureau"), start = Path.Combine(dir, "Demarrer"), removed = Path.Combine(dir, "retires");
            Directory.CreateDirectory(Path.Combine(desktop, "Vieux"));
            Directory.CreateDirectory(start);
            File.WriteAllText(Path.Combine(desktop, "Discord.lnk"), "discord");
            File.WriteAllText(Path.Combine(desktop, "Vieux", "Chrome.lnk"), "chrome");
            File.WriteAllText(Path.Combine(desktop, "Pub.lnk"), "pub");
            File.WriteAllText(Path.Combine(start, "Spotify.lnk"), "spotify");

            var items = new[]
            {
                new ShortcutItem { Key = "discord", Name = "Discord", OnDesktop = Path.Combine(desktop, "Discord.lnk"), CurrentFolder = "" },
                new ShortcutItem { Key = "chrome", Name = "Chrome", OnDesktop = Path.Combine(desktop, "Vieux", "Chrome.lnk"), CurrentFolder = "Vieux" },
                new ShortcutItem { Key = "pub", Name = "Pub", OnDesktop = Path.Combine(desktop, "Pub.lnk"), CurrentFolder = "" },
                new ShortcutItem { Key = "spotify", Name = "Spotify", Source = Path.Combine(start, "Spotify.lnk") },
                new ShortcutItem { Key = "steam://rungameid/271590", Name = "GTA V: Legacy", IsGame = true, UrlContent = DesktopOrganizer.UrlShortcut("steam://rungameid/271590", null) },
                new ShortcutItem { Key = "laisse", Name = "Laissé", Source = Path.Combine(start, "x.lnk") },
            };
            var wanted = new Dictionary<string, string?>
            {
                ["discord"] = "APP", ["chrome"] = "APP", ["pub"] = null, ["spotify"] = "APP", ["steam://rungameid/271590"] = "JEUX",
            };
            var ops = DesktopOrganizer.Plan(items, wanted, desktop, removed);
            Assert.Equal(5, ops.Count);

            var journal = DesktopOrganizer.Apply(ops, out var failed);
            Assert.Empty(failed);
            Assert.True(File.Exists(Path.Combine(desktop, "APP", "Discord.lnk")));
            Assert.True(File.Exists(Path.Combine(desktop, "APP", "Chrome.lnk")));
            Assert.True(File.Exists(Path.Combine(desktop, "APP", "Spotify.lnk")));
            Assert.True(File.Exists(Path.Combine(desktop, "JEUX", "GTA V Legacy.url")));
            Assert.True(File.Exists(Path.Combine(start, "Spotify.lnk")));
            Assert.False(File.Exists(Path.Combine(desktop, "Pub.lnk")));
            Assert.True(File.Exists(Path.Combine(removed, "Pub.lnk")));

            string jf = Path.Combine(dir, "journal.json");
            journal.Save(jf);
            Assert.Equal(0, DesktopOrganizer.Undo(OrganizeJournal.Load(jf)!));
            Assert.True(File.Exists(Path.Combine(desktop, "Discord.lnk")));
            Assert.True(File.Exists(Path.Combine(desktop, "Vieux", "Chrome.lnk")));
            Assert.True(File.Exists(Path.Combine(desktop, "Pub.lnk")));
            Assert.False(Directory.Exists(Path.Combine(desktop, "APP")));
            Assert.False(Directory.Exists(Path.Combine(desktop, "JEUX")));
            Assert.True(File.Exists(Path.Combine(start, "Spotify.lnk")));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact]
    public void AvoidsNameClashes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "7734-org-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "APP"));
            File.WriteAllText(Path.Combine(dir, "APP", "Jeu.lnk"), "x");
            var items = new[] { new ShortcutItem { Key = "a", Name = "Jeu", Source = "s.lnk" }, new ShortcutItem { Key = "b", Name = "Jeu", Source = "t.lnk" } };
            var ops = DesktopOrganizer.Plan(items, new Dictionary<string, string?> { ["a"] = "APP", ["b"] = "APP" }, dir, dir);
            Assert.Equal(new[] { "Jeu (2).lnk", "Jeu (3).lnk" }, ops.Select(o => Path.GetFileName(o.To)));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
    }

    [Fact]
    public void Helpers()
    {
        Assert.Equal("GTA V Legacy", DesktopOrganizer.SafeName("GTA V: Legacy"));
        Assert.Equal("steam://rungameid/1", DesktopOrganizer.UrlOf("[InternetShortcut]\r\nURL=steam://rungameid/1\r\n"));
        Assert.True(DesktopOrganizer.IsJunkShortcut("Uninstall Discord"));
        Assert.False(DesktopOrganizer.IsJunkShortcut("Discord"));
    }
}
