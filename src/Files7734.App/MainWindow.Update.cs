using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;

namespace Files7734.App;

/// <summary>Mise à jour automatique depuis les « Releases » GitHub du projet.</summary>
public partial class MainWindow
{
    public const string GitHubRepo = "CR43K3L/7734-Files-Checker";
    private UpdateInfo? _update;

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    private void InitUpdates()
    {
        Title = $"7734 Files Checker {CurrentVersion.ToString(3)}";
        // Vérification discrète quelques secondes après l'ouverture, au plus toutes les 6 heures.
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += async (_, _) =>
        {
            t.Stop();
            if (DateTime.UtcNow - _settings.LastUpdateCheckUtc > TimeSpan.FromHours(6)) await CheckForUpdates(manual: false);
            else if (_settings.KnownLatestTag is string tag && UpdateInfo.TryParseVersion(tag, out var v) && v > CurrentVersion) await CheckForUpdates(manual: false);
        };
        t.Start();
    }

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"7734FilesChecker/{CurrentVersion.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private async Task CheckForUpdates(bool manual)
    {
        try
        {
            using var http = NewClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            string json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            var info = UpdateInfo.Parse(json);
            _settings.KnownLatestTag = info?.Tag;
            _settings.Save();
            if (info != null && info.IsNewerThan(CurrentVersion))
            {
                _update = info;
                UpdateBannerText.Text = $"La version {info.Version.ToString(3)} est disponible.";
                UpdateBanner.Visibility = Visibility.Visible;
                if (manual) UpdateNow_Click(this, new RoutedEventArgs());
            }
            else if (manual)
            {
                MessageBox.Show(this, $"Tu as la dernière version ({CurrentVersion.ToString(3)}).", "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (manual)
                MessageBox.Show(this, "Impossible de vérifier les mises à jour : " + ex.Message, "Mises à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_update?.PageUrl is { Length: > 0 } url) Safe(() => NativeMethods.Open(url));
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        var info = _update;
        if (info == null) return;
        string notes = info.Notes.Length > 700 ? info.Notes[..700] + "…" : info.Notes;
        if (!Ask($"Installer la version {info.Version.ToString(3)} ? (tu as la {CurrentVersion.ToString(3)})\n\n{notes}\n\nL'app va se fermer puis se relancer toute seule.")) return;

        string exe = Environment.ProcessPath!;
        string dir = Path.GetDirectoryName(exe)!;
        string newFile = Path.Combine(dir, "7734FilesChecker.update.exe");
        UpdateNowButton.IsEnabled = false;
        try
        {
            using var http = NewClient();
            using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(newFile))
            {
                var buffer = new byte[1 << 20];
                long done = 0;
                int n;
                while ((n = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n));
                    done += n;
                    UpdateBannerText.Text = $"Téléchargement… {(info.Size > 0 ? 100 * done / info.Size : 0)} %";
                }
            }
            var fi = new FileInfo(newFile);
            if (info.Size > 0 && fi.Length != info.Size) throw new IOException("le fichier téléchargé est incomplet.");
            if (info.Sha256 != null)
            {
                await using var fs = File.OpenRead(newFile);
                string sha = Convert.ToHexString(await SHA256.HashDataAsync(fs));
                if (!sha.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("le fichier téléchargé est abîmé (empreinte différente).");
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(newFile); } catch (Exception) { }
            UpdateBannerText.Text = $"La version {info.Version.ToString(3)} est disponible.";
            UpdateNowButton.IsEnabled = true;
            MessageBox.Show(this, "La mise à jour a échoué : " + ex.Message + "\n\nTu peux la télécharger à la main sur GitHub.", "Mises à jour",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Un petit script remplace l'exe une fois l'app fermée, puis la relance.
        int pid = Environment.ProcessId;
        string bat = Path.Combine(Path.GetTempPath(), "7734-update.bat");
        File.WriteAllText(bat,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"move /y \"{newFile}\" \"{exe}\" >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "(goto) 2>nul & del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        _quitting = true;
        Application.Current.Shutdown();
    }
}
