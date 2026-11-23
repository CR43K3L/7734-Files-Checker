using Xunit;

namespace Files7734.Tests;

public class FileSafetyTests
{
    private static readonly Dictionary<string, string> Folders = new()
    {
        ["windows"] = @"C:\Windows", ["temp"] = @"C:\Users\a\AppData\Local\Temp", ["appdata"] = @"C:\Users\a\AppData\Roaming",
        ["localappdata"] = @"C:\Users\a\AppData\Local", ["downloads"] = @"C:\Users\a\Downloads",
    };

    [Fact]
    public void FakeSystemFileOutsideWindowsIsDanger()
    {
        var (level, f) = FileSafety.Evaluate(@"C:\Users\a\AppData\Roaming\svchost.exe", new SignatureInfo(false, false, null), Folders);
        Assert.Equal(RiskLevel.Danger, level);
        Assert.Contains(f, x => x.Text.Contains("nom d'un programme de Windows"));
    }

    [Fact]
    public void SignedMicrosoftFileInWindowsIsGood()
    {
        var (level, _) = FileSafety.Evaluate(@"C:\Windows\System32\svchost.exe", new SignatureInfo(true, true, "Microsoft Windows"), Folders);
        Assert.Equal(RiskLevel.Good, level);
    }

    [Fact]
    public void DoubleExtensionIsDanger()
    {
        var (level, _) = FileSafety.Evaluate(@"C:\Users\a\Downloads\facture.pdf.exe", new SignatureInfo(false, false, null), Folders);
        Assert.Equal(RiskLevel.Danger, level);
    }

    [Fact]
    public void UnsignedInTempIsWarningAndVideoIsInfo()
    {
        Assert.Equal(RiskLevel.Warning, FileSafety.Evaluate(@"C:\Users\a\AppData\Local\Temp\x\setup.exe", new SignatureInfo(false, false, null), Folders).Overall);
        Assert.Equal(RiskLevel.Info, FileSafety.Evaluate(@"D:\Films\film.mkv", null, Folders).Overall);
        Assert.StartsWith("https://www.virustotal.com/gui/file/ab", FileSafety.VirusTotalUrl("AB12"));
    }
}

public class FolderMoverTests
{
    [Fact]
    public void MovesFolderAndLeavesLink()
    {
        string root = Path.Combine(Path.GetTempPath(), "mover-" + Guid.NewGuid().ToString("N"));
        string src = Path.Combine(root, "a", "Jeu");
        string dstParent = Path.Combine(root, "b");
        Directory.CreateDirectory(Path.Combine(src, "data"));
        Directory.CreateDirectory(dstParent);
        File.WriteAllBytes(Path.Combine(src, "game.bin"), new byte[300_000]);
        File.WriteAllText(Path.Combine(src, "data", "x.txt"), "bonjour");
        try
        {
            var (result, _, dest) = FolderMover.Move(src, dstParent, (link, target) =>
            {
                if (OperatingSystem.IsWindows())
                {
                    // Jonction : pas besoin des droits administrateur (contrairement aux liens symboliques).
                    using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
                Directory.CreateSymbolicLink(link, target);
                return true;
            });
            Assert.Equal(MoveResult.Done, result);
            Assert.Equal("bonjour", File.ReadAllText(Path.Combine(dest, "data", "x.txt")));
            Assert.Equal("bonjour", File.ReadAllText(Path.Combine(src, "data", "x.txt"))); // via le lien
            Assert.False(Directory.Exists(src + ".7734-deplacement"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedLinkRestoresOriginal()
    {
        string root = Path.Combine(Path.GetTempPath(), "mover-" + Guid.NewGuid().ToString("N"));
        string src = Path.Combine(root, "a", "Jeu");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(Path.Combine(root, "b"));
        File.WriteAllText(Path.Combine(src, "f.txt"), "x");
        try
        {
            var (result, _, dest) = FolderMover.Move(src, Path.Combine(root, "b"), (_, _) => false);
            Assert.Equal(MoveResult.Failed, result);
            Assert.True(File.Exists(Path.Combine(src, "f.txt")));
            Assert.False(Directory.Exists(dest));
        }
        finally { Directory.Delete(root, true); }
    }
}
