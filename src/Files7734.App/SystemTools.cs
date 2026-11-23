using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Files7734.App;

/// <summary>Outils Windows : signature numérique, miniatures, jonctions, santé des disques, fichiers système.</summary>
internal static class SystemTools
{
    // ───────────── Signature numérique (gère aussi les fichiers signés par catalogue de Windows) ─────────────

    public static SignatureInfo CheckSignature(string path)
    {
        string script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                        "$s=Get-AuthenticodeSignature -LiteralPath '" + path.Replace("'", "''") + "';" +
                        "$s.Status.ToString();if($s.SignerCertificate){$s.SignerCertificate.Subject}";
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string status = lines.FirstOrDefault() ?? "";
            string? signer = lines.Length > 1 ? CommonName(lines[1]) : null;
            return status switch
            {
                "Valid" => new SignatureInfo(true, true, signer),
                "NotSigned" or "Incompatible" or "" => new SignatureInfo(false, false, null),
                _ => new SignatureInfo(true, false, signer),
            };
        }
        catch (Exception)
        {
            return new SignatureInfo(false, false, null);
        }
    }

    private static string CommonName(string subject)
    {
        foreach (var part in subject.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return p[3..].Trim('"');
        }
        return subject;
    }

    public static string Sha256(string path, CancellationToken ct = default)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, n);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    // ───────────── Miniatures (les mêmes que l'Explorateur : images, vidéos, documents…) ─────────────

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    /// <summary>Miniature d'un fichier, calculée sur un thread à part. Null si indisponible.</summary>
    public static Task<BitmapSource?> ThumbnailAsync(string path, int size)
    {
        var tcs = new TaskCompletionSource<BitmapSource?>();
        var t = new Thread(() =>
        {
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                int hr = factory.GetImage(new NativeSize { cx = size, cy = size }, 0, out var hbm);
                Marshal.ReleaseComObject(factory);
                if (hr != 0 || hbm == IntPtr.Zero) { tcs.SetResult(null); return; }
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    tcs.SetResult(src);
                }
                finally { DeleteObject(hbm); }
            }
            catch (Exception) { tcs.TrySetResult(null); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    // ───────────── Jonction (lien de dossier, sans droits administrateur) ─────────────

    public static bool CreateJunction(string link, string target)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            p.WaitForExit(15000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception) { return false; }
    }

    public static bool IsJunction(string path)
    {
        try { return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception) { return false; }
    }

    // ───────────── Santé des disques ─────────────

    public sealed record DiskHealth(string Name, string Kind, string Bus, long Size, int Health, string? OperationalStatus,
        int? Temperature, int? TemperatureMax, int? Wear, long? PowerOnHours, long? ReadErrors, long? WriteErrors);

    public static List<DiskHealth> DiskHealths()
    {
        var list = new List<DiskHealth>();
        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk"));
        foreach (ManagementObject d in searcher.Get())
        {
            using (d)
            {
                string kind = Convert.ToInt32(d["MediaType"] ?? 0) switch { 3 => "Disque dur (HDD)", 4 => "SSD", 5 => "Mémoire SCM", _ => "Disque" };
                string bus = Convert.ToInt32(d["BusType"] ?? 0) switch
                {
                    17 => "NVMe", 11 => "SATA", 7 => "USB", 10 => "SAS", 8 => "RAID", 15 => "Virtuel", 12 => "SD", 14 => "Virtuel", _ => "",
                };
                if (bus == "NVMe" && kind == "Disque") kind = "SSD";
                int? temp = null, tempMax = null, wear = null;
                long? hours = null, rerr = null, werr = null;
                try
                {
                    foreach (ManagementObject r in d.GetRelated("MSFT_StorageReliabilityCounter"))
                    {
                        using (r)
                        {
                            temp = AsInt(r["Temperature"]);
                            tempMax = AsInt(r["TemperatureMax"]);
                            wear = AsInt(r["Wear"]);
                            hours = AsLong(r["PowerOnHours"]);
                            rerr = AsLong(r["ReadErrorsTotal"]);
                            werr = AsLong(r["WriteErrorsTotal"]);
                        }
                    }
                }
                catch (Exception) { } // compteurs réservés à l'administrateur
                string? op = d["OperationalStatus"] is ushort[] ops && ops.Length > 0 ? ops[0] switch
                {
                    2 => null, 0xD00D => "Prédiction de panne", 0xD012 => "Usure avancée", 3 => "Dégradé", 5 => "Prédiction de panne", _ => null,
                } : null;
                if (temp == 0) temp = null;
                if (tempMax == 0) tempMax = null;
                list.Add(new DiskHealth((d["FriendlyName"] as string ?? "Disque").Trim(), kind, bus, AsLong(d["Size"]) ?? 0,
                    Convert.ToInt32(d["HealthStatus"] ?? 5), op, temp, tempMax, wear, hours, rerr, werr));
            }
        }
        return list;
    }

    private static int? AsInt(object? o) => o == null ? null : Convert.ToInt32(o);
    private static long? AsLong(object? o) => o == null ? null : Convert.ToInt64(o);

    // ───────────── Fichiers cachés de Windows ─────────────

    public static long FileSize(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir == null) return 0;
            // FileInfo marche même sur les fichiers verrouillés (pagefile.sys…), via les infos du dossier.
            var f = new DirectoryInfo(dir).EnumerateFiles(Path.GetFileName(path), new System.IO.EnumerationOptions { AttributesToSkip = 0 }).FirstOrDefault();
            return f?.Length ?? 0;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>Espace utilisé par les points de restauration (clichés instantanés), tous disques confondus.</summary>
    public static (long Used, long Max)? RestorePointsSize()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT UsedSpace, MaxSpace FROM Win32_ShadowStorage");
            long used = 0, max = 0;
            bool any = false;
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    any = true;
                    used += Convert.ToInt64(o["UsedSpace"] ?? 0L);
                    max += Convert.ToInt64(o["MaxSpace"] ?? 0L);
                }
            }
            return any ? (used, max) : (0, 0);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Lance une commande dans une fenêtre visible (pour les opérations longues comme DISM).</summary>
    public static void RunVisible(string command)
    {
        Process.Start(new ProcessStartInfo("cmd.exe", "/k " + command) { UseShellExecute = true });
    }

    /// <summary>Lance une commande en silence et renvoie son code de sortie.</summary>
    public static int RunHidden(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit(60000);
        return p.ExitCode;
    }
}
