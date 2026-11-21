using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Files7734;

/// <summary>
/// Lit directement la table des fichiers NTFS (Master File Table) d'un volume
/// et reconstruit l'arborescence complète avec les tailles, sans ouvrir un seul dossier.
/// </summary>
public static class NtfsMftScanner
{
    private const uint AttrStandardInformation = 0x10;
    private const uint AttrAttributeList = 0x20;
    private const uint AttrFileName = 0x30;
    private const uint AttrData = 0x80;
    private const uint AttrEnd = 0xFFFFFFFF;
    private const int RootRecord = 5;
    private const int ChunkSize = 4 * 1024 * 1024;

    private readonly record struct Extent(long Vcn, long Lcn, long Clusters);

    private sealed class Geometry
    {
        public int BytesPerSector;
        public long ClusterSize;
        public int RecordSize;
        public long MftLcn;
    }

    /// <summary>Scanne tout le volume et renvoie la racine de l'arbre.</summary>
    /// <param name="rootName">Nom affiché pour la racine, ex. "C:\".</param>
    public static FsNode Scan(IVolumeSource volume, string rootName, ScanStatus? status = null, CancellationToken ct = default)
    {
        status ??= new ScanStatus();
        status.Phase = "Lecture de la table NTFS";
        var geo = ReadBootSector(volume);
        var (extents, mftBytes) = ReadMftLayout(volume, geo);

        long totalRecords = mftBytes / geo.RecordSize;
        if (totalRecords > int.MaxValue) throw new InvalidDataException("Table MFT trop grande.");
        int count = (int)totalRecords;

        var names = new string?[count];
        var nameRank = new byte[count];
        var parents = new int[count];
        var sizes = new long[count];
        var allocs = new long[count];
        var modified = new long[count];
        var flags = new byte[count]; // 1 = utilisé, 2 = dossier

        int recordsPerChunk = Math.Max(1, ChunkSize / geo.RecordSize);
        int chunkBytes = recordsPerChunk * geo.RecordSize;
        var bufA = new byte[chunkBytes];
        var bufB = new byte[chunkBytes];

        long ReadChunk(byte[] buf, int firstRecord)
        {
            long vOff = (long)firstRecord * geo.RecordSize;
            int len = (int)Math.Min(chunkBytes, mftBytes - vOff);
            ReadVirtual(volume, geo, extents, vOff, buf.AsSpan(0, len));
            return len;
        }

        int next = 0;
        var pending = Task.Run(() => ReadChunk(bufA, 0));
        var current = bufA;
        while (next < count)
        {
            ct.ThrowIfCancellationRequested();
            int len = (int)pending.GetAwaiter().GetResult();
            int first = next;
            int recs = len / geo.RecordSize;
            next += recs;

            var other = current == bufA ? bufB : bufA;
            if (next < count)
            {
                int start = next;
                pending = Task.Run(() => ReadChunk(other, start));
            }

            for (int r = 0; r < recs; r++)
            {
                var rec = current.AsSpan(r * geo.RecordSize, geo.RecordSize);
                ParseRecord(rec, first + r, count, names, nameRank, parents, sizes, allocs, modified, flags);
            }
            current = other;
            status.Fraction = 0.9 * next / count;
            if (recs == 0) break;
        }

        status.Phase = "Construction de l'arborescence";
        var root = BuildTree(rootName, count, names, parents, sizes, allocs, modified, flags, status);
        status.Fraction = 1;
        return root;
    }

    private static Geometry ReadBootSector(IVolumeSource volume)
    {
        var boot = new byte[4096];
        volume.ReadExactly(0, boot);
        if (Encoding.ASCII.GetString(boot, 3, 8) != "NTFS    ")
            throw new InvalidDataException("Ce volume n'est pas formaté en NTFS.");

        int bps = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0B));
        int spcRaw = boot[0x0D];
        long spc = spcRaw <= 0x80 ? spcRaw : 1L << (256 - spcRaw);
        long cluster = bps * spc;
        long mftLcn = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(0x30));
        sbyte cpr = (sbyte)boot[0x40];
        int recordSize = cpr > 0 ? (int)(cpr * cluster) : 1 << -cpr;

        if (bps < 256 || cluster <= 0 || recordSize < 256 || recordSize > 65536 || mftLcn <= 0)
            throw new InvalidDataException("Secteur de démarrage NTFS invalide.");

        return new Geometry { BytesPerSector = bps, ClusterSize = cluster, RecordSize = recordSize, MftLcn = mftLcn };
    }

    /// <summary>Lit l'enregistrement 0 ($MFT) pour connaître l'emplacement de toute la table sur le disque.</summary>
    private static (List<Extent> extents, long mftBytes) ReadMftLayout(IVolumeSource volume, Geometry geo)
    {
        var rec = new byte[geo.RecordSize];
        volume.ReadExactly(geo.MftLcn * geo.ClusterSize, rec);
        if (!ApplyFixup(rec)) throw new InvalidDataException("Enregistrement $MFT illisible.");

        var extents = new List<Extent>();
        long mftBytes = 0;
        byte[]? attrList = null;

        ForEachAttribute(rec, (span, off, type, nonRes, nameLen) =>
        {
            if (type == AttrData && nameLen == 0 && nonRes)
            {
                long startVcn = I64(span, off + 0x10);
                if (startVcn == 0) mftBytes = I64(span, off + 0x30);
                extents.AddRange(DecodeRuns(span, off, startVcn));
            }
            else if (type == AttrAttributeList)
            {
                attrList = ReadAttributeValue(volume, geo, span, off, nonRes);
            }
        });

        if (attrList != null)
        {
            // $MFT très fragmentée : une partie de ses extents est dans d'autres enregistrements.
            var extra = new HashSet<long>();
            for (int p = 0; p + 0x1A <= attrList.Length;)
            {
                uint type = U32(attrList, p);
                int len = U16(attrList, p + 4);
                if (len == 0) break;
                byte nameLen = attrList[p + 6];
                long recNo = I64(attrList, p + 0x10) & 0xFFFFFFFFFFFF;
                if (type == AttrData && nameLen == 0 && recNo != 0) extra.Add(recNo);
                p += len;
            }

            var ext = new byte[geo.RecordSize];
            foreach (long recNo in extra)
            {
                extents.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));
                ReadVirtual(volume, geo, extents, recNo * geo.RecordSize, ext);
                if (!ApplyFixup(ext)) continue;
                ForEachAttribute(ext, (span, off, type, nonRes, nameLen) =>
                {
                    if (type == AttrData && nameLen == 0 && nonRes)
                    {
                        long startVcn = I64(span, off + 0x10);
                        if (extents.Any(e => e.Vcn == startVcn)) return;
                        if (startVcn == 0) mftBytes = I64(span, off + 0x30);
                        extents.AddRange(DecodeRuns(span, off, startVcn));
                    }
                });
            }
        }

        extents.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));
        if (extents.Count == 0 || mftBytes <= 0) throw new InvalidDataException("Emplacement de la table MFT introuvable.");
        return (extents, mftBytes);
    }

    private static byte[] ReadAttributeValue(IVolumeSource volume, Geometry geo, ReadOnlySpan<byte> rec, int off, bool nonRes)
    {
        if (!nonRes)
        {
            int vLen = (int)U32(rec, off + 0x10);
            int vOff = U16(rec, off + 0x14);
            return rec.Slice(off + vOff, vLen).ToArray();
        }
        long size = I64(rec, off + 0x30);
        var runs = DecodeRuns(rec, off, I64(rec, off + 0x10));
        var data = new byte[size];
        ReadVirtual(volume, geo, runs, 0, data);
        return data;
    }

    /// <summary>Lit une plage d'octets "virtuelle" d'un fichier fragmenté à partir de ses extents.</summary>
    private static void ReadVirtual(IVolumeSource volume, Geometry geo, List<Extent> extents, long vOff, Span<byte> dst)
    {
        int done = 0;
        foreach (var e in extents)
        {
            if (done >= dst.Length) break;
            long eStart = e.Vcn * geo.ClusterSize;
            long eEnd = eStart + e.Clusters * geo.ClusterSize;
            long pos = vOff + done;
            if (pos >= eEnd || pos < eStart) continue;
            int take = (int)Math.Min(dst.Length - done, eEnd - pos);
            var piece = dst.Slice(done, take);
            if (e.Lcn < 0) piece.Clear();
            else volume.ReadExactly(e.Lcn * geo.ClusterSize + (pos - eStart), piece);
            done += take;
        }
        if (done < dst.Length) dst[done..].Clear();
    }

    private static List<Extent> DecodeRuns(ReadOnlySpan<byte> rec, int attrOff, long startVcn)
    {
        var list = new List<Extent>();
        int attrLen = (int)U32(rec, attrOff + 4);
        int p = attrOff + U16(rec, attrOff + 0x20);
        int end = attrOff + attrLen;
        long vcn = startVcn, lcn = 0;
        while (p < end)
        {
            byte h = rec[p++];
            if (h == 0) break;
            int lenSize = h & 0x0F, offSize = h >> 4;
            if (lenSize == 0 || lenSize > 8 || offSize > 8 || p + lenSize + offSize > end) break;
            long len = ReadUnsigned(rec.Slice(p, lenSize));
            p += lenSize;
            if (offSize == 0)
            {
                list.Add(new Extent(vcn, -1, len));
            }
            else
            {
                lcn += ReadSigned(rec.Slice(p, offSize));
                list.Add(new Extent(vcn, lcn, len));
            }
            p += offSize;
            vcn += len;
        }
        return list;
    }

    private delegate void AttributeVisitor(ReadOnlySpan<byte> rec, int off, uint type, bool nonResident, byte nameLength);

    private static void ForEachAttribute(ReadOnlySpan<byte> rec, AttributeVisitor visit)
    {
        int off = U16(rec, 0x14);
        int used = (int)Math.Min(U32(rec, 0x18), (uint)rec.Length);
        while (off + 16 <= used)
        {
            uint type = U32(rec, off);
            if (type == AttrEnd) break;
            int len = (int)U32(rec, off + 4);
            if (len < 16 || off + len > rec.Length) break;
            visit(rec, off, type, rec[off + 8] != 0, rec[off + 9]);
            off += len;
        }
    }

    /// <summary>Corrige les 2 derniers octets de chaque secteur (protection NTFS contre les écritures partielles).</summary>
    private static bool ApplyFixup(Span<byte> rec)
    {
        if (rec[0] != (byte)'F' || rec[1] != (byte)'I' || rec[2] != (byte)'L' || rec[3] != (byte)'E') return false;
        int usaOff = U16(rec, 4);
        int usaCount = U16(rec, 6);
        if (usaCount < 2 || usaOff + usaCount * 2 > rec.Length) return false;
        int stride = rec.Length / (usaCount - 1);
        ushort usn = (ushort)U16(rec, usaOff);
        for (int i = 1; i < usaCount; i++)
        {
            int pos = i * stride - 2;
            if (pos + 2 > rec.Length) break;
            if (U16(rec, pos) != usn) return false;
            rec[pos] = rec[usaOff + 2 * i];
            rec[pos + 1] = rec[usaOff + 2 * i + 1];
        }
        return true;
    }

    private static void ParseRecord(Span<byte> rec, int index, int count, string?[] names, byte[] nameRank, int[] parents,
        long[] sizes, long[] allocs, long[] modified, byte[] flags)
    {
        if (!ApplyFixup(rec)) return;
        int recFlags = U16(rec, 0x16);
        if ((recFlags & 1) == 0) return; // enregistrement libre

        long baseRef = I64(rec, 0x20) & 0xFFFFFFFFFFFF;
        int target = index;
        if (baseRef != 0)
        {
            if (baseRef >= count) return;
            target = (int)baseRef; // enregistrement d'extension : ses attributs appartiennent au fichier de base
        }
        else
        {
            flags[index] = (byte)(1 | ((recFlags & 2) != 0 ? 2 : 0));
        }

        int off = U16(rec, 0x14);
        int used = (int)Math.Min(U32(rec, 0x18), (uint)rec.Length);
        while (off + 16 <= used)
        {
            uint type = U32(rec, off);
            if (type == AttrEnd) break;
            int len = (int)U32(rec, off + 4);
            if (len < 16 || off + len > rec.Length) break;
            bool nonRes = rec[off + 8] != 0;
            byte attrNameLen = rec[off + 9];

            switch (type)
            {
                case AttrStandardInformation when !nonRes:
                {
                    int vOff = off + U16(rec, off + 0x14);
                    if (vOff + 0x10 <= off + len) modified[target] = I64(rec, vOff + 0x08);
                    break;
                }
                case AttrFileName when !nonRes:
                {
                    int vOff = off + U16(rec, off + 0x14);
                    if (vOff + 0x42 > off + len) break;
                    int nLen = rec[vOff + 0x40];
                    byte ns = rec[vOff + 0x41];
                    byte rank = ns switch { 1 or 3 => 3, 0 => 2, _ => 1 }; // Win32 > POSIX > DOS (8.3)
                    if (rank <= nameRank[target] || vOff + 0x42 + nLen * 2 > off + len) break;
                    nameRank[target] = rank;
                    long parent = I64(rec, vOff) & 0xFFFFFFFFFFFF;
                    parents[target] = parent < count ? (int)parent : -1;
                    names[target] = new string(MemoryMarshal.Cast<byte, char>(rec.Slice(vOff + 0x42, nLen * 2)));
                    break;
                }
                case AttrData when attrNameLen == 0: // flux principal uniquement
                {
                    if (!nonRes)
                    {
                        sizes[target] = U32(rec, off + 0x10);
                        allocs[target] = 0; // contenu stocké directement dans la MFT
                    }
                    else if (I64(rec, off + 0x10) == 0) // premier morceau de l'attribut
                    {
                        long alloc = I64(rec, off + 0x28);
                        int attrFlags = U16(rec, off + 0x0C);
                        if ((attrFlags & 0x8001) != 0 && len >= 0x48) alloc = I64(rec, off + 0x40); // compressé ou fragmenté (sparse)
                        sizes[target] = I64(rec, off + 0x30);
                        allocs[target] = alloc;
                    }
                    break;
                }
            }
            off += len;
        }
    }

    private static FsNode BuildTree(string rootName, int count, string?[] names, int[] parents, long[] sizes, long[] allocs,
        long[] modified, byte[] flags, ScanStatus status)
    {
        if (count <= RootRecord || (flags[RootRecord] & 2) == 0) throw new InvalidDataException("Dossier racine introuvable dans la MFT.");

        var nodes = new FsNode?[count];
        for (int i = 0; i < count; i++)
        {
            if ((flags[i] & 1) == 0 || (names[i] == null && i != RootRecord)) continue;
            bool dir = (flags[i] & 2) != 0;
            var n = new FsNode(names[i] ?? "", dir);
            if (!dir)
            {
                n.Size = sizes[i];
                n.Allocated = allocs[i];
            }
            if (modified[i] > 0)
            {
                try { n.LastModifiedUtc = DateTime.FromFileTimeUtc(modified[i]); } catch (ArgumentOutOfRangeException) { }
            }
            nodes[i] = n;
        }

        var root = nodes[RootRecord]!;
        root.Name = rootName;

        for (int i = 0; i < count; i++)
        {
            var n = nodes[i];
            if (n == null || i == RootRecord) continue;
            int p = parents[i];
            if (p < 0 || p == i) continue;
            var parent = nodes[p];
            if (parent == null || !parent.IsDirectory) continue; // fichier orphelin : ignoré
            parent.AddChild(n);
        }

        // Détache la racine d'elle-même au cas où et calcule les totaux à partir d'elle uniquement :
        // les éléments non reliés à la racine (orphelins, cycles) sont naturellement exclus.
        root.Parent = null;
        TreeBuilder.Finalize(root);
        status.AddItems(root.FileCount + root.FolderCount, root.Size);
        return root;
    }

    private static int U16(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(o, 2));
    private static uint U32(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(o, 4));
    private static long I64(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadInt64LittleEndian(s.Slice(o, 8));

    private static long ReadUnsigned(ReadOnlySpan<byte> s)
    {
        long v = 0;
        for (int i = s.Length - 1; i >= 0; i--) v = (v << 8) | s[i];
        return v;
    }

    private static long ReadSigned(ReadOnlySpan<byte> s)
    {
        long v = (sbyte)s[^1];
        for (int i = s.Length - 2; i >= 0; i--) v = (v << 8) | s[i];
        return v;
    }
}
