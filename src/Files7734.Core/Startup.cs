using System.Text.Json;

namespace Files7734;

public enum StartupVerdict { Unknown, CanDisable, Keep }

/// <summary>Conseils pour les programmes lancés au démarrage de Windows, et lecture de leur état (comme le Gestionnaire des tâches).</summary>
public static class StartupAdvice
{
    private static readonly (string Match, string Why)[] Optional =
    {
        ("steam", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("epicgameslauncher", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("battle.net", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("riotclient", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("eadesktop", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("origin", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("ubisoftconnect", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("upc.exe", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("goggalaxy", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("rockstar", "Lanceur de jeux : il s'ouvrira quand tu lanceras un jeu."),
        ("discord", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("spotify", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("teams", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("skype", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("whatsapp", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("telegram", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("overwolf", "Superposition de jeu : inutile au démarrage."),
        ("medal", "Enregistreur de clips : tu peux le lancer avant de jouer."),
        ("opera", "Navigateur : il se lancera quand tu l'ouvriras."),
        ("msedge", "Navigateur : il se lancera quand tu l'ouvriras."),
        ("chrome", "Navigateur : il se lancera quand tu l'ouvriras."),
        ("adobe", "Mises à jour et services Adobe : inutiles au démarrage."),
        ("ccleaner", "Inutile au démarrage."),
        ("utorrent", "Tu peux l'ouvrir toi-même quand tu en as besoin."),
        ("cortana", "Inutile pour la plupart des gens."),
        ("onedrive", "Désactive-le seulement si tu n'utilises pas OneDrive pour synchroniser tes fichiers."),
    };

    private static readonly (string Match, string Why)[] Essential =
    {
        ("securityhealth", "Sécurité de Windows (antivirus) : à garder."),
        ("windowsdefender", "Sécurité de Windows (antivirus) : à garder."),
        ("realtek", "Pilote audio : à garder pour le son."),
        ("rtkaud", "Pilote audio : à garder pour le son."),
        ("nvidia", "Pilote de la carte graphique : à garder."),
        ("amdrs", "Pilote de la carte graphique : à garder."),
        ("radeon", "Pilote de la carte graphique : à garder."),
        ("igfx", "Pilote graphique Intel : à garder."),
        ("synaptics", "Pilote du pavé tactile : à garder."),
        ("etdctrl", "Pilote du pavé tactile : à garder."),
        ("logitech", "Logiciel de ta souris ou ton clavier : garde-le si tu utilises ses réglages (macros, RGB, DPI)."),
        ("lghub", "Logiciel de ta souris ou ton clavier : garde-le si tu utilises ses réglages (macros, RGB, DPI)."),
        ("razer", "Logiciel de tes périphériques : garde-le si tu utilises ses réglages (macros, RGB, DPI)."),
        ("icue", "Logiciel de tes périphériques : garde-le si tu utilises ses réglages (RGB, ventilateurs)."),
        ("steelseries", "Logiciel de tes périphériques : garde-le si tu utilises ses réglages."),
        ("avast", "Antivirus : à garder."),
        ("avgui", "Antivirus : à garder."),
        ("kaspersky", "Antivirus : à garder."),
        ("bitdefender", "Antivirus : à garder."),
        ("malwarebytes", "Antivirus : à garder."),
        ("norton", "Antivirus : à garder."),
        ("egui.exe", "Antivirus : à garder."),
    };

    public static (StartupVerdict Verdict, string Why) For(string name, string command)
    {
        string hay = (name + " " + command).ToLowerInvariant().Replace(" ", "");
        foreach (var (m, why) in Essential) if (hay.Contains(m)) return (StartupVerdict.Keep, why);
        foreach (var (m, why) in Optional) if (hay.Contains(m)) return (StartupVerdict.CanDisable, why);
        return (StartupVerdict.Unknown, "");
    }

    /// <summary>
    /// État « activé » lu dans la clé StartupApproved de Windows : le premier octet vaut 2 ou 6 si le programme est actif,
    /// 3 ou 7 s'il a été désactivé. Pas de valeur = actif.
    /// </summary>
    public static bool IsEnabled(byte[]? approved) => approved == null || approved.Length == 0 || (approved[0] & 1) == 0;

    /// <summary>Valeur à écrire dans StartupApproved (12 octets, comme le Gestionnaire des tâches).</summary>
    public static byte[] ApprovedValue(bool enabled, DateTime nowUtc)
    {
        var b = new byte[12];
        b[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(nowUtc.ToFileTimeUtc()).CopyTo(b, 4);
        return b;
    }
}

/// <summary>Historique quotidien de l'espace utilisé par disque, pour tracer une courbe et prévoir quand il sera plein.</summary>
public sealed class SpaceHistory
{
    public sealed class Point
    {
        public DateTime Day { get; set; }
        public long Total { get; set; }
        public long Free { get; set; }
        public long Used => Total - Free;
    }

    public Dictionary<string, List<Point>> Drives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime LastAutoScanUtc { get; set; }

    /// <summary>Ajoute (ou remplace) la mesure du jour pour un disque. Garde 2 ans.</summary>
    public void Record(string drive, DateTime nowUtc, long total, long free)
    {
        if (total <= 0) return;
        if (!Drives.TryGetValue(drive, out var list)) Drives[drive] = list = new List<Point>();
        var day = nowUtc.Date;
        list.RemoveAll(p => p.Day == day);
        list.Add(new Point { Day = day, Total = total, Free = free });
        list.Sort((a, b) => a.Day.CompareTo(b.Day));
        list.RemoveAll(p => p.Day < day.AddYears(-2));
    }

    /// <summary>Variation moyenne de l'espace utilisé par jour sur les <paramref name="days"/> derniers jours (null si pas assez de mesures).</summary>
    public double? DailyGrowth(string drive, int days = 30)
    {
        if (!Drives.TryGetValue(drive, out var list) || list.Count < 2) return null;
        var last = list[^1];
        var first = list.LastOrDefault(p => p.Day <= last.Day.AddDays(-days)) ?? list[0];
        double span = (last.Day - first.Day).TotalDays;
        if (span < 3) return null;
        return (last.Used - first.Used) / span;
    }

    /// <summary>Nombre de jours avant que le disque soit plein au rythme actuel (null si le disque ne se remplit pas).</summary>
    public double? DaysUntilFull(string drive, int days = 30)
    {
        var g = DailyGrowth(drive, days);
        if (g is not > 0 || !Drives.TryGetValue(drive, out var list)) return null;
        return list[^1].Free / g.Value;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this));
    }

    public static SpaceHistory Load(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                var h = JsonSerializer.Deserialize<SpaceHistory>(File.ReadAllText(file));
                if (h != null)
                {
                    h.Drives = new Dictionary<string, List<Point>>(h.Drives, StringComparer.OrdinalIgnoreCase);
                    return h;
                }
            }
        }
        catch (Exception) { }
        return new SpaceHistory();
    }
}
