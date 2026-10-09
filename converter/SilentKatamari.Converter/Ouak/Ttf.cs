using System.Buffers.Binary;
using System.Text;

namespace SilentKatamari.Converter.Ouak;

/// <summary>Reads a TrueType/OpenType font's family name (name table, nameID 1) so Garry's Mod's
/// surface.CreateFont can find it: it needs the family, not the file name.</summary>
public static class Ttf
{
    public static string? FamilyName(byte[] font)
    {
        try
        {
            var s = font.AsSpan();
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(s[4..]);
            for (int t = 0; t < numTables; t++)
            {
                int rec = 12 + t * 16;
                if (Encoding.ASCII.GetString(font, rec, 4) != "name") continue;
                int off = (int)BinaryPrimitives.ReadUInt32BigEndian(s[(rec + 8)..]);
                return FromNameTable(font, off);
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException) { }
        return null;
    }

    internal static string? FromNameTable(byte[] font, int off)
    {
        var s = font.AsSpan();
        int count = BinaryPrimitives.ReadUInt16BigEndian(s[(off + 2)..]);
        int strings = off + BinaryPrimitives.ReadUInt16BigEndian(s[(off + 4)..]);
        string? mac = null;
        for (int i = 0; i < count; i++)
        {
            int r = off + 6 + i * 12;
            int platform = BinaryPrimitives.ReadUInt16BigEndian(s[r..]);
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(s[(r + 6)..]);
            int len = BinaryPrimitives.ReadUInt16BigEndian(s[(r + 8)..]);
            int at = strings + BinaryPrimitives.ReadUInt16BigEndian(s[(r + 10)..]);
            if (nameId != 1) continue;
            if (platform == 3 || platform == 0) return Encoding.BigEndianUnicode.GetString(font, at, len); // Windows/Unicode: UTF-16BE
            if (platform == 1) mac ??= Encoding.Latin1.GetString(font, at, len);
        }
        return mac;
    }
}
