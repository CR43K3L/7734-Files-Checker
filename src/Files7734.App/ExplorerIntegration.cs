using Microsoft.Win32;

namespace Files7734.App;

/// <summary>
/// Ajoute « Analyser avec 7734 Files Checker » au clic droit de l'Explorateur sur les dossiers et les disques.
/// Écrit uniquement dans le registre de l'utilisateur (pas besoin des droits admin).
/// </summary>
internal static class ExplorerIntegration
{
    private const string KeyName = "7734FilesChecker";
    private static readonly string[] Bases =
    {
        @"Software\Classes\Directory\shell",
        @"Software\Classes\Drive\shell",
        @"Software\Classes\Directory\Background\shell",
    };

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{Bases[0]}\{KeyName}");
        return k != null;
    }

    public static void Enable()
    {
        string exe = Environment.ProcessPath!;
        foreach (var b in Bases)
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{b}\{KeyName}");
            key.SetValue("", "Analyser avec 7734 Files Checker");
            key.SetValue("Icon", $"\"{exe}\",0");
            using var cmd = key.CreateSubKey("command");
            string arg = b.EndsWith(@"Background\shell") ? "%V" : "%1";
            cmd.SetValue("", $"\"{exe}\" \"{arg}\"");
        }
    }

    public static void Disable()
    {
        foreach (var b in Bases)
            Registry.CurrentUser.DeleteSubKeyTree($@"{b}\{KeyName}", throwOnMissingSubKey: false);
    }
}
