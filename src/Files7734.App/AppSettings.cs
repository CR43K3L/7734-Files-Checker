using System.IO;
using System.Text.Json;

namespace Files7734.App;

/// <summary>Préférences de l'utilisateur, enregistrées dans %AppData%\7734FilesChecker\settings.json.</summary>
public sealed class AppSettings
{
    public SizeUnit Unit { get; set; } = SizeUnit.Auto;
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public bool AutoScanOnSelect { get; set; }
    public bool ShowHeader { get; set; } = true;
    public bool ShowFileTypes { get; set; } = true;
    public bool ShowMap { get; set; } = true;
    public bool MapUseAllocated { get; set; }
    public bool MapShowFreeSpace { get; set; }
    public bool MapShowNames { get; set; } = true;
    /// <summary>Alerte quand un disque est presque plein (l'app reste dans la zone de notification).</summary>
    public bool MonitorDisks { get; set; }
    public bool StartWithWindows { get; set; }
    public bool TrayHintShown { get; set; }
    public DateTime LastUpdateCheckUtc { get; set; }
    public string? KnownLatestTag { get; set; }
    public string? SkippedUpdateTag { get; set; }
    public string? LastRunVersion { get; set; }
    /// <summary>Analyse du disque système chaque semaine, avec une bulle « ce qui a grossi ».</summary>
    public bool WeeklyReport { get; set; } = true;
    /// <summary>Dossier de GTA V choisi à la main (sinon lu dans CitizenFX.ini).</summary>
    public string? GtaPath { get; set; }
    /// <summary>Le joueur a confirmé avoir retiré son ancien pack installé à la main (tuto du premier pack).</summary>
    public bool FiveMOldPackCleared { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7734FilesChecker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch (Exception) { }
    }
}
