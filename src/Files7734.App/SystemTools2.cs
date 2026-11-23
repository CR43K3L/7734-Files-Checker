using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Files7734.App;

/// <summary>Programmes installés et « qui bloque ce fichier ? » (Gestionnaire de redémarrage de Windows).</summary>
public static class SystemTools2
{
    public sealed record InstalledApp(string Name, string? Publisher, string? Version, string? InstallLocation, string UninstallCommand,
        long EstimatedSize, DateTime? InstallDate, string RegistryKey);

    public static List<InstalledApp> InstalledApps()
    {
        var list = new List<InstalledApp>();
        var roots = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, path) in roots)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var e = key.OpenSubKey(sub);
                        if (e?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                        if (e.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (e.GetValue("ParentKeyName") != null || e.GetValue("ReleaseType") is string) continue; // mises à jour
                        if (e.GetValue("UninstallString") is not string un || string.IsNullOrWhiteSpace(un)) continue;
                        if (!seen.Add(name.Trim() + "|" + (e.GetValue("DisplayVersion") as string))) continue;
                        long size = e.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0;
                        DateTime? date = null;
                        if (e.GetValue("InstallDate") is string d && d.Length == 8 &&
                            DateTime.TryParseExact(d, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt)) date = dt;
                        string? loc = (e.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        list.Add(new InstalledApp(name.Trim(), (e.GetValue("Publisher") as string)?.Trim(), e.GetValue("DisplayVersion") as string,
                            string.IsNullOrWhiteSpace(loc) ? null : loc, un, size, date, hive.Name + "\\" + path + "\\" + sub));
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
        return list;
    }

    public static bool StillInstalled(InstalledApp app)
    {
        try
        {
            string k = app.RegistryKey;
            var hive = k.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ? Registry.CurrentUser : Registry.LocalMachine;
            string sub = k[(k.IndexOf('\\') + 1)..];
            using var key = hive.OpenSubKey(sub);
            return key != null;
        }
        catch (Exception) { return true; }
    }

    /// <summary>Lance la désinstallation et attend la fin du programme lancé.</summary>
    public static async Task RunUninstaller(string command)
    {
        string cmd = command.Trim();
        // « MsiExec.exe /I{…} » ouvre la réparation : on demande la désinstallation.
        if (cmd.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase)) cmd = System.Text.RegularExpressions.Regex.Replace(cmd, @"/I\{", "/X{", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        string exe, args;
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            exe = end > 0 ? cmd[1..end] : cmd.Trim('"');
            args = end > 0 ? cmd[(end + 1)..].Trim() : "";
        }
        else
        {
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exe = i > 0 ? cmd[..(i + 4)] : cmd;
            args = i > 0 ? cmd[(i + 4)..].Trim() : "";
        }
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        if (p != null) await p.WaitForExitAsync();
    }

    /// <summary>Dossiers où les programmes laissent souvent des restes.</summary>
    public static List<string> LeftoverCandidates()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        var result = new List<string>();
        foreach (var r in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(r))
                {
                    result.Add(d);
                    // Un niveau de plus pour le rangement « Éditeur\Application ».
                    try { result.AddRange(Directory.EnumerateDirectories(d)); } catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
        return result;
    }

    // ───────────── Qui bloque ce fichier ? ─────────────

    public sealed record Locker(int Pid, string Name, string AppName, bool IsService);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint handle, int flags, string key);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint handle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint handle, uint nFiles, string[] files, uint nApps, [In] RM_UNIQUE_PROCESS[]? apps, uint nServices, string[]? services);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[]? info, ref uint reasons);

    /// <summary>Programmes qui gardent ouverts ces fichiers (pour un dossier : ses premiers fichiers).</summary>
    public static List<Locker> WhoLocks(string path)
    {
        var files = new List<string>();
        if (File.Exists(path)) files.Add(path);
        else if (Directory.Exists(path))
        {
            try
            {
                files.AddRange(new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Take(3000).Select(f => f.FullName));
            }
            catch (Exception) { }
        }
        var result = new List<Locker>();
        if (files.Count == 0) return result;
        if (RmStartSession(out uint h, 0, Guid.NewGuid().ToString()) != 0) return result;
        try
        {
            foreach (var chunk in files.Chunk(500))
                if (RmRegisterResources(h, (uint)chunk.Length, chunk, 0, null, 0, null) != 0) return result;
            uint needed = 0, count = 0, reasons = 0;
            int rc = RmGetList(h, out needed, ref count, null, ref reasons);
            if (rc == 234 && needed > 0) // ERROR_MORE_DATA
            {
                var info = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(h, out needed, ref count, info, ref reasons) == 0)
                {
                    foreach (var i in info.Take((int)count))
                    {
                        string name;
                        try { name = Process.GetProcessById(i.Process.dwProcessId).ProcessName + ".exe"; } catch (Exception) { name = "?"; }
                        result.Add(new Locker(i.Process.dwProcessId, name, string.IsNullOrWhiteSpace(i.strAppName) ? name : i.strAppName,
                            i.ApplicationType == 3 || !string.IsNullOrEmpty(i.strServiceShortName)));
                    }
                }
            }
        }
        finally { RmEndSession(h); }
        return result.DistinctBy(l => l.Pid).ToList();
    }
}
