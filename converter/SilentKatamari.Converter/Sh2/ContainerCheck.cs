// Reads just enough of SILENT HILL 2's containers to know, without any key, whether they are encrypted and how
// they are compressed. Layouts follow CUE4Parse (Apache-2.0): UE4/IO/Objects/FIoStoreTocHeader.cs (144-byte header,
// ContainerFlags at byte 80), FIoStoreTocResource.cs (what precedes the compression method names) and
// UE4/Pak/Objects/FPakInfo.cs (pak trailer: EncryptionKeyGuid, bEncryptedIndex, Magic, Version, ...).
using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter.Sh2;

public sealed record ContainerInfo(string Name, long Size, string Kind, int Version, bool Encrypted, bool Compressed,
    bool Signed, IReadOnlyList<string> CompressionMethods, string? Error)
{
    public JsonObject ToJson() => new()
    {
        ["name"] = Name, ["size"] = Size, ["kind"] = Kind, ["version"] = Version, ["encrypted"] = Encrypted,
        ["compressed"] = Compressed, ["signed"] = Signed,
        ["compression"] = new JsonArray(CompressionMethods.Select(c => (JsonNode)c).ToArray()), ["error"] = Error,
    };
}

public static class ContainerCheck
{
    public static readonly byte[] TocMagic = "-==--==--==--==-"u8.ToArray();
    public const int TocHeaderSize = 144;
    public const uint PakMagic = 0x5A6F12E1;
    private const byte FlagCompressed = 0x01, FlagEncrypted = 0x02, FlagSigned = 0x04;
    private const int TocVersionPerfectHash = 4, TocVersionPerfectHashWithOverflow = 5, TocVersionContainerEncryptionMethod = 10;

    public static ContainerInfo ReadToc(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            using var f = File.OpenRead(path);
            var h = new byte[TocHeaderSize];
            if (f.Read(h, 0, h.Length) < h.Length || !h.AsSpan(0, 16).SequenceEqual(TocMagic))
                return new ContainerInfo(name, f.Length, "utoc", 0, false, false, false, Array.Empty<string>(), "not an IoStore table of contents");
            return ParseToc(name, f.Length, h, f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ContainerInfo(name, 0, "utoc", 0, false, false, false, Array.Empty<string>(), e.Message);
        }
    }

    /// <summary>Parses the header bytes, then seeks to the compression method names (stream positioned anywhere).</summary>
    internal static ContainerInfo ParseToc(string name, long size, byte[] h, Stream s)
    {
        var r = h.AsSpan();
        int version = r[16];
        uint entries = BinaryPrimitives.ReadUInt32LittleEndian(r[24..]);
        uint blocks = BinaryPrimitives.ReadUInt32LittleEndian(r[28..]);
        uint blockEntrySize = BinaryPrimitives.ReadUInt32LittleEndian(r[32..]);
        uint nameCount = BinaryPrimitives.ReadUInt32LittleEndian(r[36..]);
        uint nameLength = BinaryPrimitives.ReadUInt32LittleEndian(r[40..]);
        byte flags = r[80];
        uint seeds = version >= TocVersionPerfectHash ? BinaryPrimitives.ReadUInt32LittleEndian(r[84..]) : 0;
        uint withoutHash = version >= TocVersionPerfectHashWithOverflow ? BinaryPrimitives.ReadUInt32LittleEndian(r[96..]) : 0;
        // Encryption IVs exist only from the ContainerEncryptionMethod version on; their count sits in the
        // UE 5.6+ header tail at byte 104 and each IV is 16 bytes.
        uint ivs = version >= TocVersionContainerEncryptionMethod ? BinaryPrimitives.ReadUInt32LittleEndian(r[104..]) : 0;

        var methods = new List<string>();
        string? error = null;
        long namesAt = TocHeaderSize + entries * 12L + entries * 10L + seeds * 4L + withoutHash * 4L + blocks * (long)blockEntrySize + ivs * 16L;
        if (nameCount > 16 || nameLength > 64) error = "unexpected compression method table";
        else if (namesAt + nameCount * nameLength > size) error = "compression method table past the end of the file";
        else
        {
            s.Position = namesAt;
            var buf = new byte[nameCount * nameLength];
            s.ReadExactly(buf);
            for (int i = 0; i < nameCount; i++)
            {
                string m = Encoding.ASCII.GetString(buf, (int)(i * nameLength), (int)nameLength).TrimEnd('\0');
                if (m.Length > 0) methods.Add(m);
            }
        }
        return new ContainerInfo(name, size, "utoc", version, (flags & FlagEncrypted) != 0, (flags & FlagCompressed) != 0,
            (flags & FlagSigned) != 0, methods, error);
    }

    /// <summary>Legacy .pak trailer: found by its magic in the last 1 KB (its size changes with the pak version).</summary>
    public static ContainerInfo ReadPak(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            using var f = File.OpenRead(path);
            int tail = (int)Math.Min(1024, f.Length);
            var buf = new byte[tail];
            f.Position = f.Length - tail;
            f.ReadExactly(buf);
            return ParsePakTail(name, f.Length, buf);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ContainerInfo(name, 0, "pak", 0, false, false, false, Array.Empty<string>(), e.Message);
        }
    }

    internal static ContainerInfo ParsePakTail(string name, long size, byte[] tail)
    {
        for (int i = tail.Length - 8; i >= 17; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != PakMagic) continue;
            int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 4));
            if (version < 1 || version > 32) continue;
            bool encryptedIndex = tail[i - 1] != 0;
            var methods = new List<string>();
            // v8+: after Magic, Version, IndexOffset (8), IndexSize (8), IndexHash (20) [+ bIndexIsFrozen on v9]
            // come up to five 32-byte compression method names.
            int at = i + 4 + 4 + 8 + 8 + 20 + (version == 9 ? 1 : 0);
            if (version >= 8)
                for (int k = 0; k < 5 && at + 32 <= tail.Length; k++, at += 32)
                {
                    string m = Encoding.ASCII.GetString(tail, at, 32).TrimEnd('\0');
                    if (m.Length > 0 && m.All(c => c >= 32 && c < 127)) methods.Add(m);
                }
            return new ContainerInfo(name, size, "pak", version, encryptedIndex, methods.Count > 0, false, methods, null);
        }
        return new ContainerInfo(name, size, "pak", 0, false, false, false, Array.Empty<string>(), "no pak trailer found");
    }

    /// <summary>Every .utoc and .pak in the folder (not recursive).</summary>
    public static List<ContainerInfo> ReadAll(string paksFolder)
    {
        var list = new List<ContainerInfo>();
        if (!Directory.Exists(paksFolder)) return list;
        foreach (var p in Directory.EnumerateFiles(paksFolder).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (p.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase)) list.Add(ReadToc(p));
            else if (p.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) list.Add(ReadPak(p));
        }
        return list;
    }
}
