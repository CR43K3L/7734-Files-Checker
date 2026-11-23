using Xunit;

namespace Files7734.Tests;

public class AppLeftoverTests
{
    [Fact]
    public void NormalizeRemovesVersionAndArch()
    {
        Assert.Equal("7zip", AppLeftovers.Normalize("7-Zip 23.01 (x64)"));
        Assert.Equal("discord", AppLeftovers.Normalize("Discord"));
    }

    [Fact]
    public void MatchesOwnFoldersOnly()
    {
        var folders = new[]
        {
            @"C:\Users\a\AppData\Roaming\obs-studio", @"C:\Users\a\AppData\Roaming\Microsoft", @"C:\Users\a\AppData\Local\OBS Studio",
            @"C:\Users\a\AppData\Local\Discord", @"C:\ProgramData\Overwolf",
        };
        var m = AppLeftovers.Match("OBS Studio", "OBS Project", folders, new[] { "Discord", "Overwolf" });
        Assert.Contains(@"C:\Users\a\AppData\Roaming\obs-studio", m);
        Assert.Contains(@"C:\Users\a\AppData\Local\OBS Studio", m);
        Assert.DoesNotContain(@"C:\Users\a\AppData\Roaming\Microsoft", m);
        Assert.DoesNotContain(@"C:\Users\a\AppData\Local\Discord", m);
    }

    [Fact]
    public void PublisherFolderAloneIsNeverProposed()
    {
        var m = AppLeftovers.Match("Razer Synapse", "Razer Inc.", new[] { @"C:\ProgramData\Razer", @"C:\ProgramData\Razer\Synapse" }, new[] { "Razer Cortex" });
        Assert.DoesNotContain(@"C:\ProgramData\Razer", m);
        Assert.Contains(@"C:\ProgramData\Razer\Synapse", m);
    }
}

public class GameCleanupTests
{
    [Fact]
    public void FindsCrashesAndLogsButNotSaves()
    {
        string root = Path.Combine(Path.GetTempPath(), "gc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Saved", "Crashes", "c1"));
        Directory.CreateDirectory(Path.Combine(root, "Saved", "Logs"));
        Directory.CreateDirectory(Path.Combine(root, "SaveGames"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        File.WriteAllBytes(Path.Combine(root, "Saved", "Crashes", "c1", "x.dmp"), new byte[100]);
        File.WriteAllBytes(Path.Combine(root, "Saved", "Logs", "game.log"), new byte[50]);
        File.WriteAllBytes(Path.Combine(root, "Saved", "Logs", "keep.ini"), new byte[50]);
        File.WriteAllBytes(Path.Combine(root, "SaveGames", "crash.dmp"), new byte[70]);
        File.WriteAllBytes(Path.Combine(root, "bin", "old.mdmp"), new byte[30]);
        try
        {
            var items = GameCleanup.Find(root);
            Assert.Equal(3, items.Count);
            Assert.DoesNotContain(items, i => i.Path.Contains("SaveGames"));
            Assert.DoesNotContain(items, i => i.Path.EndsWith("keep.ini"));
            Assert.Equal(180, items.Sum(i => i.Size));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void GhostFoldersAreUninstalledOnes()
    {
        string root = Path.Combine(Path.GetTempPath(), "gh-" + Guid.NewGuid().ToString("N"));
        foreach (var d in new[] { "Portal 2", "Old Game", "Steamworks Shared" }) Directory.CreateDirectory(Path.Combine(root, d));
        try
        {
            var ghosts = GameCleanup.GhostFolders(root, new[] { Path.Combine(root, "Portal 2") }, new[] { "Steamworks Shared" });
            Assert.Single(ghosts);
            Assert.EndsWith("Old Game", ghosts[0]);
        }
        finally { Directory.Delete(root, true); }
    }
}

public class UpdateInfoTests
{
    [Fact]
    public void ParsesLatestRelease()
    {
        const string json = """
            {"tag_name":"v1.2.0","draft":false,"prerelease":false,"html_url":"https://github.com/x/y/releases/tag/v1.2.0","body":"Nouveautés",
             "assets":[{"name":"autre.zip","size":1,"browser_download_url":"u0"},
                       {"name":"7734FilesChecker.exe","size":72000000,"browser_download_url":"https://github.com/x/y/releases/download/v1.2.0/7734FilesChecker.exe","digest":"sha256:abcd"}]}
            """;
        var u = UpdateInfo.Parse(json)!;
        Assert.Equal(new Version(1, 2, 0), u.Version);
        Assert.Equal("abcd", u.Sha256);
        Assert.True(u.IsNewerThan(new Version(1, 0, 0, 0)));
        Assert.False(u.IsNewerThan(new Version(1, 2, 0, 0)));
    }

    [Fact]
    public void RejectsPrereleaseAndBadTags()
    {
        Assert.Null(UpdateInfo.Parse("""{"tag_name":"v2.0.0","prerelease":true,"assets":[]}"""));
        Assert.False(UpdateInfo.TryParseVersion("latest", out _));
        Assert.True(UpdateInfo.TryParseVersion("v1.0", out var v));
        Assert.Equal(new Version(1, 0, 0), v);
    }
}

public class StartupAndHistoryTests
{
    [Fact]
    public void AdviceAndApprovedState()
    {
        Assert.Equal(StartupVerdict.CanDisable, StartupAdvice.For("Discord", @"C:\Users\a\AppData\Local\Discord\Update.exe --processStart Discord.exe").Verdict);
        Assert.Equal(StartupVerdict.Keep, StartupAdvice.For("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe").Verdict);
        Assert.Equal(StartupVerdict.Unknown, StartupAdvice.For("MonOutil", @"C:\outil.exe").Verdict);
        Assert.True(StartupAdvice.IsEnabled(null));
        Assert.True(StartupAdvice.IsEnabled(new byte[] { 2, 0, 0, 0 }));
        Assert.True(StartupAdvice.IsEnabled(new byte[] { 6, 0, 0, 0 }));
        Assert.False(StartupAdvice.IsEnabled(new byte[] { 3, 0, 0, 0 }));
        Assert.False(StartupAdvice.IsEnabled(StartupAdvice.ApprovedValue(false, DateTime.UtcNow)));
        Assert.True(StartupAdvice.IsEnabled(StartupAdvice.ApprovedValue(true, DateTime.UtcNow)));
    }

    [Fact]
    public void HistoryPredictsFullDisk()
    {
        var h = new SpaceHistory();
        var start = new DateTime(2026, 1, 1);
        const long G = 1L << 30;
        for (int d = 0; d <= 30; d++) h.Record("C:", start.AddDays(d), 500 * G, (200 - d) * G); // 1 Go de plus par jour
        h.Record("C:", start.AddDays(30).AddHours(5), 500 * G, 170 * G); // même jour : remplacé
        Assert.Equal(31, h.Drives["C:"].Count);
        Assert.Equal(G, h.DailyGrowth("C:")!.Value, 1e6);
        Assert.Equal(170, h.DaysUntilFull("C:")!.Value, 0.5);
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        h.Save(file);
        Assert.Equal(31, SpaceHistory.Load(file).Drives["c:"].Count);
        File.Delete(file);
    }
}
