namespace Files7734;

public enum ScanMethod
{
    /// <summary>Lecture directe de la table NTFS (MFT). Très rapide, demande les droits admin.</summary>
    Mft,
    /// <summary>Parcours classique dossier par dossier.</summary>
    DirectoryWalk,
}

/// <summary>État d'avancement d'un scan, lisible depuis un autre thread.</summary>
public sealed class ScanStatus
{
    private long _items;
    private long _bytes;
    private long _errors;

    public long ItemsFound => Interlocked.Read(ref _items);
    public long BytesFound => Interlocked.Read(ref _bytes);
    public long Errors => Interlocked.Read(ref _errors);

    /// <summary>Avancement entre 0 et 1, ou null si inconnu (scan classique).</summary>
    public double? Fraction { get; internal set; }

    public string Phase { get; internal set; } = "";

    internal void AddItem(long bytes)
    {
        Interlocked.Increment(ref _items);
        if (bytes != 0) Interlocked.Add(ref _bytes, bytes);
    }

    internal void AddItems(long count, long bytes)
    {
        Interlocked.Add(ref _items, count);
        Interlocked.Add(ref _bytes, bytes);
    }

    internal void AddError() => Interlocked.Increment(ref _errors);
}

public sealed class ScanResult
{
    public required FsNode Root { get; init; }
    public required ScanMethod Method { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public long Errors { get; init; }

    /// <summary>Raison pour laquelle la lecture MFT n'a pas été utilisée, le cas échéant.</summary>
    public string? FallbackReason { get; init; }
}
