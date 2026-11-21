using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Files7734;

/// <summary>Accès en lecture brute à un volume (disque Windows ou image de test).</summary>
public interface IVolumeSource : IDisposable
{
    /// <summary>Remplit entièrement <paramref name="buffer"/> à partir de <paramref name="offset"/>.</summary>
    void ReadExactly(long offset, Span<byte> buffer);
}

/// <summary>Lit une image de volume NTFS stockée dans un fichier (utilisé pour les tests).</summary>
public sealed class ImageFileVolumeSource : IVolumeSource
{
    private readonly FileStream _fs;

    public ImageFileVolumeSource(string path)
    {
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.RandomAccess);
    }

    public void ReadExactly(long offset, Span<byte> buffer)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = RandomAccess.Read(_fs.SafeFileHandle, buffer[done..], offset + done);
            if (n <= 0) { buffer[done..].Clear(); return; }
            done += n;
        }
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>Ouvre un volume Windows (ex. \\.\C:) en lecture brute. Demande les droits administrateur.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVolumeSource : IVolumeSource
{
    private const int Align = 4096; // couvre les secteurs de 512 et 4096 octets
    private readonly SafeFileHandle _handle;
    private byte[] _tmp = Array.Empty<byte>();

    private WindowsVolumeSource(SafeFileHandle handle) => _handle = handle;

    /// <param name="driveLetter">Lettre de lecteur, ex. 'C'.</param>
    public static WindowsVolumeSource Open(char driveLetter)
    {
        string path = $@"\\.\{char.ToUpperInvariant(driveLetter)}:";
        var h = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            if (err == 5) throw new UnauthorizedAccessException("Accès refusé au volume : l'application doit être lancée en administrateur.");
            throw new Win32Exception(err, $"Impossible d'ouvrir le volume {path}");
        }
        return new WindowsVolumeSource(h);
    }

    public void ReadExactly(long offset, Span<byte> buffer)
    {
        if (offset % Align == 0 && buffer.Length % Align == 0)
        {
            ReadRaw(offset, buffer);
            return;
        }

        long start = offset - (offset % Align);
        long end = offset + buffer.Length;
        if (end % Align != 0) end += Align - (end % Align);
        int len = checked((int)(end - start));
        if (_tmp.Length < len) _tmp = new byte[len];
        ReadRaw(start, _tmp.AsSpan(0, len));
        _tmp.AsSpan((int)(offset - start), buffer.Length).CopyTo(buffer);
    }

    private unsafe void ReadRaw(long offset, Span<byte> buffer)
    {
        if (!SetFilePointerEx(_handle, offset, out _, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        int done = 0;
        fixed (byte* p = buffer)
        {
            while (done < buffer.Length)
            {
                if (!ReadFile(_handle, p + done, buffer.Length - done, out int read, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (read == 0) { buffer[done..].Clear(); return; }
                done += read;
            }
        }
    }

    public void Dispose() => _handle.Dispose();

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPos, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadFile(SafeFileHandle h, byte* buffer, int toRead, out int read, IntPtr overlapped);
}
