using System.Text.Json;
using System.Text.RegularExpressions;

namespace Files7734;

/// <summary>Un jeu installé, trouvé via son lanceur.</summary>
public sealed record GameInfo(string Name, string Launcher, string InstallPath, string? UninstallUri = null);

/// <summary>Lecture des fichiers de configuration des lanceurs de jeux (sans dépendre de Windows : testable).</summary>
public static class GameParsers
{
    /// <summary>
    /// Lit un fichier VDF de Steam (format clé/valeur entre guillemets avec accolades)
    /// et renvoie toutes les paires (chemin.de.cle, valeur).
    /// </summary>
    public static List<(string Path, string Value)> ParseVdf(string text)
    {
        var result = new List<(string, string)>();
        var stack = new List<string>();
        string? pendingKey = null;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"')
            {
                var sb = new System.Text.StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { i++; sb.Append(text[i] == 'n' ? '\n' : text[i]); }
                    else sb.Append(text[i]);
                    i++;
                }
                i++;
                string token = sb.ToString();
                if (pendingKey == null) pendingKey = token;
                else
                {
                    result.Add((string.Join(".", stack.Append(pendingKey)), token));
                    pendingKey = null;
                }
            }
            else if (c == '{')
            {
                stack.Add(pendingKey ?? "");
                pendingKey = null;
                i++;
            }
            else if (c == '}')
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                pendingKey = null;
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else i++;
        }
        return result;
    }

    /// <summary>Dossiers des bibliothèques Steam à partir de libraryfolders.vdf.</summary>
    public static List<string> SteamLibraries(string libraryFoldersVdf)
    {
        return ParseVdf(libraryFoldersVdf)
            .Where(p => p.Path.EndsWith(".path", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value.Replace(@"\\", @"\"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Nom, identifiant et dossier d'un jeu Steam à partir de son appmanifest_*.acf.</summary>
    public static (string Name, string AppId, string InstallDir)? SteamManifest(string acf)
    {
        var kv = ParseVdf(acf).ToDictionary(p => p.Path, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (!kv.TryGetValue("AppState.name", out var name) || !kv.TryGetValue("AppState.installdir", out var dir)) return null;
        kv.TryGetValue("AppState.appid", out var appId);
        return (name, appId ?? "", dir);
    }

    /// <summary>Jeu Epic Games à partir d'un fichier .item (JSON) du dossier Manifests.</summary>
    public static (string Name, string Path)? EpicManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("DisplayName", out var name) || !root.TryGetProperty("InstallLocation", out var loc)) return null;
            string? n = name.GetString(), l = loc.GetString();
            if (string.IsNullOrWhiteSpace(n) || string.IsNullOrWhiteSpace(l)) return null;
            return (n!, l!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Dossier d'installation d'un jeu Riot à partir de son *.product_settings.yaml.</summary>
    public static string? RiotInstallPath(string yaml)
    {
        var m = Regex.Match(yaml, "product_install_full_path:\\s*\"?([^\"\\r\\n]+)\"?");
        return m.Success ? m.Groups[1].Value.Trim().Replace('/', '\\') : null;
    }

    /// <summary>Nom lisible d'un produit Riot (« valorant.live » → « VALORANT »).</summary>
    public static string RiotProductName(string productId) => productId.Split('.')[0].ToLowerInvariant() switch
    {
        "valorant" => "VALORANT",
        "league_of_legends" => "League of Legends",
        "bacon" => "Legends of Runeterra",
        "lion" => "2XKO",
        _ => productId.Split('.')[0],
    };
}
