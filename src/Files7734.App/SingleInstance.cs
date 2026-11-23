using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Files7734.App;

/// <summary>
/// Une seule instance de l'app : si elle tourne déjà, la nouvelle lui passe ses arguments (ex. dossier à analyser
/// depuis le clic droit de l'Explorateur) puis se ferme, et la fenêtre déjà ouverte revient au premier plan.
/// </summary>
internal static class SingleInstance
{
    private static readonly string Id = "7734FilesChecker-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);
    private static Mutex? _mutex;

    /// <summary>true si cette instance est la seule (elle garde le verrou jusqu'à la fin).</summary>
    public static bool Acquire(bool waitForPrevious)
    {
        _mutex = new Mutex(false, @"Local\" + Id);
        try
        {
            // Relance (administrateur, mise à jour) : l'ancienne instance est en train de se fermer, on l'attend.
            if (_mutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(15) : TimeSpan.Zero)) return true;
        }
        catch (AbandonedMutexException) { return true; }
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>Envoie les arguments à l'instance déjà ouverte. Renvoie false si elle ne répond pas.</summary>
    public static bool SendToRunning(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(string.Join("\n", args));
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Écoute les instances suivantes et appelle <paramref name="onArgs"/> (sur un thread de fond) à chaque lancement.</summary>
    public static void Listen(Action<string[]> onArgs)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    // Accès en lecture-écriture pour l'utilisateur : une instance normale peut parler à une instance lancée en administrateur.
                    var security = new PipeSecurity();
                    var me = WindowsIdentity.GetCurrent().User;
                    if (me != null) security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
                    using var server = NamedPipeServerStreamAcl.Create(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, security);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string text = reader.ReadToEnd();
                    onArgs(text.Length == 0 ? Array.Empty<string>() : text.Split('\n'));
                }
                catch (Exception) { Thread.Sleep(500); }
            }
        }) { IsBackground = true, Name = "SingleInstance" };
        thread.Start();
    }
}
