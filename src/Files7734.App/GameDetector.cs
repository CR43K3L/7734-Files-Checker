using System.IO;
using Microsoft.Win32;

namespace Files7734.App;

/// <summary>Trouve les jeux installés en lisant les fichiers et le registre des lanceurs.</summary>
internal static class GameDetector
{
    public static List<GameInfo> Detect()
    {
        var games = new List<GameInfo>();
        void Safe(Action a) { try { a(); } catch (Exception) { } }
        Safe(() => Steam(games));
        Safe(() => Epic(games));
        Safe(() => Riot(games));
        Safe(() => Gog(games));
        Safe(() => Ubisoft(games));
        Safe(() => FromUninstallEntries(games));
        Safe(() => Xbox(games));

        // Un même dossier peut être trouvé deux fois (ex. lanceur + registre) : on garde le premier.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameInfo>();
        foreach (var g in games)
        {
            string path = g.InstallPath.TrimEnd('\\', '/');
            if (path.Length < 4 || !Directory.Exists(path) || !seen.Add(path)) continue;
            bool insideOther = result.Any(r => path.StartsWith(r.InstallPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            if (insideOther) continue;
            result.Add(g with { InstallPath = path, UninstallUri = g.UninstallUri ?? FindUninstaller(path) });
        }
        return result;
    }

    // ───────────── Steam ─────────────

    /// <summary>Dossiers des bibliothèques Steam (ex. « D:\SteamLibrary »).</summary>
    public static List<string> SteamLibraryPaths()
    {
        try
        {
            string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                         ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (string.IsNullOrEmpty(steam)) return new();
            steam = steam.Replace('/', '\\');
            var libs = new List<string> { steam };
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) libs.AddRange(GameParsers.SteamLibraries(File.ReadAllText(vdf)));
            return libs.Where(l => Directory.Exists(Path.Combine(l, "steamapps"))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception) { return new(); }
    }

    private static void Steam(List<GameInfo> games)
    {
        string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                     ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrEmpty(steam)) return;
        steam = steam.Replace('/', '\\');

        var libraries = new List<string> { steam };
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(GameParsers.SteamLibraries(File.ReadAllText(vdf)));

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var m = GameParsers.SteamManifest(File.ReadAllText(acf));
                    if (m == null || m.Value.AppId == "228980") continue; // composants redistribuables de Steam
                    games.Add(new GameInfo(m.Value.Name, "Steam", Path.Combine(apps, "common", m.Value.InstallDir),
                        m.Value.AppId.Length > 0 ? "steam://uninstall/" + m.Value.AppId : null));
                }
                catch (IOException) { }
            }
        }
    }

    // ───────────── Epic Games ─────────────

    private static void Epic(List<GameInfo> games)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(dir)) return;
        foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                var m = GameParsers.EpicManifest(File.ReadAllText(item));
                if (m != null) games.Add(new GameInfo(m.Value.Name, "Epic Games", m.Value.Path.Replace('/', '\\'), "com.epicgames.launcher://apps"));
            }
            catch (IOException) { }
        }
    }

    // ───────────── Riot ─────────────

    private static void Riot(List<GameInfo> games)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\Metadata");
        if (!Directory.Exists(dir)) return;
        foreach (var product in Directory.EnumerateDirectories(dir))
        {
            string id = Path.GetFileName(product);
            if (id.StartsWith("Riot Client", StringComparison.OrdinalIgnoreCase)) continue;
            string yaml = Path.Combine(product, id + ".product_settings.yaml");
            if (!File.Exists(yaml)) continue;
            string? path = GameParsers.RiotInstallPath(File.ReadAllText(yaml));
            if (path != null) games.Add(new GameInfo(GameParsers.RiotProductName(id), "Riot", path));
        }
    }

    // ───────────── GOG ─────────────

    private static void Gog(List<GameInfo> games)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (key == null) return;
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            if (g?.GetValue("gameName") is string name && g.GetValue("path") is string path)
                games.Add(new GameInfo(name, "GOG", path));
        }
    }

    // ───────────── Ubisoft Connect ─────────────

    private static void Ubisoft(List<GameInfo> games)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (key == null) return;
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            if (g?.GetValue("InstallDir") is not string path || string.IsNullOrWhiteSpace(path)) continue;
            path = path.Replace('/', '\\').TrimEnd('\\');
            games.Add(new GameInfo(Path.GetFileName(path), "Ubisoft", path, "uplay://uninstall/" + id));
        }
    }

    // ───────────── Battle.net, EA, Rockstar (programmes installés de Windows) ─────────────

    private static readonly (string Publisher, string Launcher, string[] NotGames)[] Publishers =
    {
        ("Blizzard", "Battle.net", new[] { "Battle.net" }),
        ("Electronic Arts", "EA", new[] { "EA app", "Origin", "EA Desktop" }),
        ("Rockstar Games", "Rockstar", new[] { "Rockstar Games Launcher", "Rockstar Games Social Club" }),
    };

    private static void FromUninstallEntries(List<GameInfo> games)
    {
        foreach (var e in UninstallEntries())
        {
            foreach (var (publisher, launcher, notGames) in Publishers)
            {
                if (e.Publisher?.Contains(publisher, StringComparison.OrdinalIgnoreCase) != true) continue;
                if (notGames.Any(n => e.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase))) continue;
                if (string.IsNullOrWhiteSpace(e.InstallLocation)) continue;
                games.Add(new GameInfo(e.Name, launcher, e.InstallLocation.Trim('"'), e.Uninstall));
            }
        }
    }

    // ───────────── Xbox (Game Pass) ─────────────

    private static void Xbox(List<GameInfo> games)
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                string dir = Path.Combine(d.RootDirectory.FullName, "XboxGames");
                if (!Directory.Exists(dir)) continue;
                foreach (var g in Directory.EnumerateDirectories(dir))
                {
                    string name = Path.GetFileName(g);
                    if (name.Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                    games.Add(new GameInfo(name, "Xbox", g, "ms-settings:appsfeatures"));
                }
            }
            catch (Exception) { }
        }
    }

    // ───────────── Registre « Programmes et fonctionnalités » ─────────────

    private sealed record UninstallEntry(string Name, string? Publisher, string? InstallLocation, string? Uninstall);

    private static List<UninstallEntry>? _entries;

    private static List<UninstallEntry> UninstallEntries()
    {
        if (_entries != null) return _entries;
        var list = new List<UninstallEntry>();
        var roots = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in roots)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var e = key.OpenSubKey(sub);
                    if (e?.GetValue("DisplayName") is not string name) continue;
                    list.Add(new UninstallEntry(name, e.GetValue("Publisher") as string, e.GetValue("InstallLocation") as string,
                        e.GetValue("UninstallString") as string));
                }
            }
            catch (Exception) { }
        }
        return _entries = list;
    }

    /// <summary>Commande de désinstallation déclarée à Windows pour ce dossier, si elle existe.</summary>
    private static string? FindUninstaller(string installPath)
    {
        string p = installPath.TrimEnd('\\');
        return UninstallEntries().FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Uninstall) && e.InstallLocation != null
            && string.Equals(e.InstallLocation.Trim('"').TrimEnd('\\'), p, StringComparison.OrdinalIgnoreCase))?.Uninstall;
    }
}
