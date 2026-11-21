using System.Diagnostics;
using System.Text;
using Files7734;

Console.OutputEncoding = Encoding.UTF8;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
Console.WriteLine("7734 Files Checker — outil de test du moteur de scan (v0.1)");
Console.WriteLine();

bool interactive = args.Length == 0;
bool forceWalk = args.Contains("--classique");
string? image = null;
string? target = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--image" && i + 1 < args.Length) image = args[++i];
    else if (!args[i].StartsWith("--")) target = args[i];
}

if (target == null && image == null)
{
    Console.Write("Quel disque ou dossier scanner ? (ex. C, D:, C:\\Users) [C] : ");
    target = Console.ReadLine()?.Trim().Trim('"');
    if (string.IsNullOrEmpty(target)) target = "C";
}
if (target != null && target.Length == 1 && char.IsLetter(target[0])) target += ":\\";
if (target != null && target.Length == 2 && target[1] == ':') target += "\\";

var status = new ScanStatus();
var sw = Stopwatch.StartNew();
ScanResult result;
using (var cts = new CancellationTokenSource())
{
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    var task = Task.Run(() =>
    {
        if (image != null)
        {
            using var vol = new ImageFileVolumeSource(image);
            var root = NtfsMftScanner.Scan(vol, "image:\\", status, cts.Token);
            return new ScanResult { Root = root, Method = ScanMethod.Mft, Elapsed = sw.Elapsed };
        }
        return DiskScanner.Scan(target!, forceWalk, status, cts.Token);
    });

    while (!task.Wait(250))
    {
        string pct = status.Fraction is double f ? $"{f:P0} " : "";
        Console.Write($"\r{status.Phase}… {pct}{status.ItemsFound:N0} éléments, {ByteSize.Format(status.BytesFound)}      ");
    }
    Console.Write("\r" + new string(' ', 90) + "\r");
    try { result = task.GetAwaiter().GetResult(); }
    catch (OperationCanceledException) { Console.WriteLine("Scan annulé."); return 1; }
    catch (Exception ex) { Console.WriteLine("Erreur : " + ex.Message); Pause(); return 1; }
}

var r = result.Root;
Console.WriteLine($"Méthode   : {(result.Method == ScanMethod.Mft ? "lecture directe de la table NTFS (MFT)" : "scan classique")}");
if (result.FallbackReason != null) Console.WriteLine($"Pourquoi  : {result.FallbackReason}");
Console.WriteLine($"Durée     : {result.Elapsed.TotalSeconds:0.00} s");
Console.WriteLine($"Dossier   : {r.FullPath}");
Console.WriteLine($"Taille    : {ByteSize.Format(r.Size)}  (sur le disque : {ByteSize.Format(r.Allocated)})");
Console.WriteLine($"Contenu   : {r.FileCount:N0} fichiers, {r.FolderCount:N0} dossiers");
if (result.Errors > 0) Console.WriteLine($"Ignorés   : {result.Errors:N0} dossiers inaccessibles");
if (OperatingSystem.IsWindows() && image == null)
{
    try
    {
        var d = new DriveInfo(Path.GetPathRoot(r.FullPath)!);
        Console.WriteLine($"Disque    : {ByteSize.Format(d.TotalSize - d.TotalFreeSpace)} utilisés sur {ByteSize.Format(d.TotalSize)}");
    }
    catch { }
}

Console.WriteLine();
Console.WriteLine("Plus gros dossiers :");
foreach (var c in (r.Children ?? new()).Where(c => c.IsDirectory).Take(15))
    Console.WriteLine($"  {ByteSize.Format(c.Size),10}  {c.FileCount,10:N0} fich.  {c.Name}");

Console.WriteLine();
Console.WriteLine("Plus gros fichiers :");
foreach (var f in Analysis.LargestFiles(r, 20))
    Console.WriteLine($"  {ByteSize.Format(f.Size),10}  {f.FullPath}");

Console.WriteLine();
Console.WriteLine("Par extension :");
foreach (var e in Analysis.ByExtension(r).Take(15))
    Console.WriteLine($"  {ByteSize.Format(e.Size),10}  {e.FileCount,10:N0} fich.  {(e.Extension.Length == 0 ? "(sans extension)" : e.Extension)}");

if (interactive) Pause();
return 0;

static void Pause()
{
    Console.WriteLine();
    Console.Write("Appuie sur Entrée pour fermer…");
    Console.ReadLine();
}
