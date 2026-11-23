namespace Files7734;

public enum RiskLevel { Info, Good, Warning, Danger }

public sealed record RiskFinding(RiskLevel Level, string Text);

/// <summary>Résultat de la vérification de la signature numérique d'un fichier (faite par Windows).</summary>
public sealed record SignatureInfo(bool IsSigned, bool IsValid, string? Signer);

/// <summary>
/// Indices simples pour juger si un fichier inconnu est suspect. Ce n'est pas un antivirus :
/// on regarde le type, l'emplacement, le nom et la signature, puis on renvoie vers une vraie analyse (VirusTotal).
/// </summary>
public static class FileSafety
{
    private static readonly HashSet<string> Executables = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".scr", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp",
        ".sys", ".hta", ".pif", ".cpl", ".jar", ".lnk", ".reg", ".appx", ".msix", ".ocx", ".drv",
    };

    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".mp3", ".mp4",
        ".avi", ".mkv", ".zip", ".rar", ".7z", ".rtf", ".csv", ".odt",
    };

    /// <summary>Noms de programmes de Windows souvent imités par les malwares.</summary>
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "csrss.exe", "lsass.exe", "winlogon.exe", "services.exe", "smss.exe", "explorer.exe", "conhost.exe",
        "rundll32.exe", "dllhost.exe", "taskhostw.exe", "spoolsv.exe", "wininit.exe", "dwm.exe", "ctfmon.exe", "sihost.exe",
        "runtimebroker.exe", "searchindexer.exe", "regsvr32.exe", "taskmgr.exe", "lsm.exe", "audiodg.exe", "fontdrvhost.exe",
    };

    public static bool IsExecutable(string name) => Executables.Contains(Path.GetExtension(name));

    /// <param name="folders">Dossiers connus : clés « windows », « temp », « appdata », « localappdata », « downloads », « programfiles ».</param>
    public static (RiskLevel Overall, List<RiskFinding> Findings) Evaluate(string fullPath, SignatureInfo? signature, IReadOnlyDictionary<string, string> folders)
    {
        var f = new List<RiskFinding>();
        string name = fullPath[(fullPath.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
        string ext = Path.GetExtension(name);
        bool exe = IsExecutable(name);
        string? Rel(string key)
        {
            if (!folders.TryGetValue(key, out var dir) || string.IsNullOrEmpty(dir)) return null;
            string d = dir.TrimEnd('\\', '/');
            bool inside = fullPath.Length > d.Length && fullPath.StartsWith(d, StringComparison.OrdinalIgnoreCase) && fullPath[d.Length] is '\\' or '/';
            return inside ? fullPath[(d.Length + 1)..] : null;
        }
        bool In(string key) => Rel(key) != null;

        if (name.Contains('‮'))
            f.Add(new(RiskLevel.Danger, "Le nom contient un caractère invisible qui inverse l'affichage du texte : une ruse classique pour cacher un « .exe »."));

        // « facture.pdf.exe » : faux document.
        string inner = Path.GetExtension(Path.GetFileNameWithoutExtension(name));
        if (exe && inner.Length > 0 && Documents.Contains(inner))
            f.Add(new(RiskLevel.Danger, $"Double extension « {inner}{ext} » : le fichier se fait passer pour un {inner.TrimStart('.').ToUpperInvariant()} mais c'est un programme."));

        if (SystemNames.Contains(name) && !In("windows"))
            f.Add(new(RiskLevel.Danger, $"Il porte le nom d'un programme de Windows ({name}) mais il n'est pas dans le dossier Windows. C'est typique d'un malware."));

        if (!exe)
        {
            f.Add(new(RiskLevel.Info, $"Les fichiers {(ext.Length > 0 ? ext : "sans extension")} ne lancent pas de programme par eux-mêmes : le risque est faible."));
        }
        else
        {
            if (signature is { IsSigned: true, IsValid: true })
                f.Add(new(RiskLevel.Good, $"Signé numériquement par « {signature.Signer ?? "éditeur inconnu"} », et la signature est valide : le fichier n'a pas été modifié depuis."));
            else if (signature is { IsSigned: true, IsValid: false })
                f.Add(new(RiskLevel.Danger, "Il a une signature numérique, mais elle est invalide ou le fichier a été modifié après la signature."));
            else if (signature != null)
                f.Add(new(RiskLevel.Warning, "Pas de signature numérique : on ne peut pas savoir qui a créé ce programme. Beaucoup de petits logiciels sont dans ce cas, mais c'est aussi le cas de la plupart des malwares."));

            if (In("temp"))
                f.Add(new(RiskLevel.Warning, "Programme rangé dans un dossier temporaire : les installateurs le font, mais les malwares aussi."));
            else if (In("downloads"))
                f.Add(new(RiskLevel.Info, "Fichier téléchargé : vérifie que tu sais d'où il vient."));
            else if (In("appdata") || In("localappdata"))
            {
                string rel = Rel("appdata") ?? Rel("localappdata")!;
                if (!rel.Contains('\\') && !rel.Contains('/'))
                    f.Add(new(RiskLevel.Warning, "Programme posé directement dans AppData, sans dossier d'application : emplacement apprécié des malwares."));
            }
            else if (In("windows") && signature is { IsSigned: true, IsValid: true } && signature.Signer?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true)
                f.Add(new(RiskLevel.Good, "Fichier de Windows signé par Microsoft."));
        }

        RiskLevel overall = f.Any(x => x.Level == RiskLevel.Danger) ? RiskLevel.Danger
                          : f.Any(x => x.Level == RiskLevel.Warning) ? RiskLevel.Warning
                          : f.Any(x => x.Level == RiskLevel.Good) ? RiskLevel.Good
                          : RiskLevel.Info;
        return (overall, f);
    }

    /// <summary>Adresse VirusTotal du rapport d'un fichier : seule l'empreinte (SHA-256) est envoyée, jamais le fichier.</summary>
    public static string VirusTotalUrl(string sha256) => "https://www.virustotal.com/gui/file/" + sha256.ToLowerInvariant();

    /// <summary>Recherche web du nom du fichier (sans le chemin, qui peut contenir ton nom d'utilisateur).</summary>
    public static string WebSearchUrl(string fileName) =>
        "https://duckduckgo.com/?q=" + Uri.EscapeDataString($"\"{fileName}\" fichier c'est quoi virus");
}
