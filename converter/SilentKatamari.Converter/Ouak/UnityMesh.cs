// Unity 2019+ mesh and texture decoding for Once Upon A KATAMARI (Unity 6). Vertex layout logic follows the
// public Unity serialisation (m_VertexData channels/streams, VertexFormat) as AssetStudio's MIT-licensed MeshHelper
// documents it; written fresh here. Output is in docs/CONTRACT.md space: cm, Source axes, top-left uv origin.
using System.Buffers.Binary;
using System.Numerics;
using AssetRipper.TextureDecoder.Astc;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Dxt;
using AssetRipper.TextureDecoder.Rgb.Formats;
using Bgra = AssetRipper.TextureDecoder.Rgb.Formats.ColorBGRA<byte>;

namespace SilentKatamari.Converter.Ouak;

public sealed record VertexChannel(int Stream, int Offset, int Format, int Dimension);

public static class UnityMesh
{
    /// <summary>Bytes per component for Unity's VertexFormat (2019+).</summary>
    public static int FormatSize(int format) => format switch
    {
        0 => 4, 1 => 2, 2 or 3 => 1, 4 or 5 => 2, 6 or 7 => 1, 8 or 9 => 2, 10 or 11 => 4,
        _ => throw new NotSupportedException($"vertex format {format}"),
    };

    public static float ReadComponent(ReadOnlySpan<byte> d, int format) => format switch
    {
        0 => BinaryPrimitives.ReadSingleLittleEndian(d),
        1 => (float)BinaryPrimitives.ReadHalfLittleEndian(d),
        2 => d[0] / 255f,
        3 => Math.Max((sbyte)d[0] / 127f, -1f),
        4 => BinaryPrimitives.ReadUInt16LittleEndian(d) / 65535f,
        5 => Math.Max(BinaryPrimitives.ReadInt16LittleEndian(d) / 32767f, -1f),
        6 => d[0], 7 => (sbyte)d[0],
        8 => BinaryPrimitives.ReadUInt16LittleEndian(d), 9 => BinaryPrimitives.ReadInt16LittleEndian(d),
        10 => BinaryPrimitives.ReadUInt32LittleEndian(d), 11 => BinaryPrimitives.ReadInt32LittleEndian(d),
        _ => throw new NotSupportedException($"vertex format {format}"),
    };

    /// <summary>Start of each stream inside m_VertexData: streams follow each other, each aligned to 16 bytes.</summary>
    public static (int[] Offsets, int[] Strides) StreamLayout(IReadOnlyList<VertexChannel> channels, int vertexCount)
    {
        int streams = channels.Count == 0 ? 0 : channels.Max(c => c.Stream) + 1;
        var strides = new int[streams];
        foreach (var c in channels)
            if (c.Dimension > 0) strides[c.Stream] = Math.Max(strides[c.Stream], c.Offset + FormatSize(c.Format) * (c.Dimension & 0xF));
        var offsets = new int[streams];
        int at = 0;
        for (int s = 0; s < streams; s++)
        {
            offsets[s] = at;
            at += strides[s] * vertexCount;
            at = (at + 15) & ~15;
        }
        return (offsets, strides);
    }

    /// <summary>One channel (0 position, 1 normal, 4 uv0, ...) for every vertex, as up to 4 floats each.</summary>
    public static float[][] ReadChannel(byte[] data, IReadOnlyList<VertexChannel> channels, int vertexCount, int channel)
    {
        if (channel >= channels.Count || channels[channel].Dimension == 0) return Array.Empty<float[]>();
        var c = channels[channel];
        var (offsets, strides) = StreamLayout(channels, vertexCount);
        int size = FormatSize(c.Format), dim = c.Dimension & 0xF;
        var outv = new float[vertexCount][];
        for (int v = 0; v < vertexCount; v++)
        {
            int at = offsets[c.Stream] + v * strides[c.Stream] + c.Offset;
            var f = new float[dim];
            for (int k = 0; k < dim; k++) f[k] = ReadComponent(data.AsSpan(at + k * size), c.Format);
            outv[v] = f;
        }
        return outv;
    }

    /// <summary>Unity (left-handed, +Y up, +Z forward, metres) to Source (+X forward, +Y left, +Z up), cm.</summary>
    public static Vector3 ToSource(float x, float y, float z, float scale = 100f) => new(z * scale, -x * scale, y * scale);

    /// <summary>Turns decoded triangles (Unity winding, bottom-left uv) into a mesh part in contract space.</summary>
    public static MeshPart Part(string material, float[][] pos, float[][] nrm, float[][] uv, IReadOnlyList<int> tris, Matrix4x4 bake)
    {
        var part = new MeshPart { Material = material };
        var remap = new Dictionary<int, int>();
        var normalBake = bake;
        normalBake.Translation = Vector3.Zero;
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            foreach (int idx in new[] { tris[t], tris[t + 2], tris[t + 1] }) // reverse winding: handedness changes
            {
                if (!remap.TryGetValue(idx, out int ni))
                {
                    ni = part.VertexCount;
                    remap[idx] = ni;
                    var p = Vector3.Transform(new Vector3(pos[idx][0], pos[idx][1], pos[idx][2]), bake);
                    var n = nrm.Length > idx ? Vector3.TransformNormal(new Vector3(nrm[idx][0], nrm[idx][1], nrm[idx][2]), normalBake) : Vector3.UnitY;
                    var sp = ToSource(p.X, p.Y, p.Z);
                    var sn = Vector3.Normalize(ToSource(n.X, n.Y, n.Z, 1f) + new Vector3(1e-9f, 0, 0));
                    float u = uv.Length > idx ? uv[idx][0] : 0, v = uv.Length > idx && uv[idx].Length > 1 ? uv[idx][1] : 0;
                    part.V.AddRange(new[] { sp.X, sp.Y, sp.Z, sn.X, sn.Y, sn.Z, u, 1 - v });
                }
                part.I.Add(ni);
            }
        }
        return part;
    }

    /// <summary>Unity TextureFormat to RGBA, rows flipped to top-left origin. Null for formats we don't decode.</summary>
    public static RgbaImage? DecodeTexture(int format, int w, int h, byte[] data)
    {
        byte[] bgra;
        switch (format)
        {
            case 1: // Alpha8
                bgra = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++) { bgra[i * 4] = bgra[i * 4 + 1] = bgra[i * 4 + 2] = 255; bgra[i * 4 + 3] = data[i]; }
                break;
            case 3: // RGB24
                bgra = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++) { bgra[i * 4] = data[i * 3 + 2]; bgra[i * 4 + 1] = data[i * 3 + 1]; bgra[i * 4 + 2] = data[i * 3]; bgra[i * 4 + 3] = 255; }
                break;
            case 4: // RGBA32
                bgra = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++) { bgra[i * 4] = data[i * 4 + 2]; bgra[i * 4 + 1] = data[i * 4 + 1]; bgra[i * 4 + 2] = data[i * 4]; bgra[i * 4 + 3] = data[i * 4 + 3]; }
                break;
            case 5: // ARGB32
                bgra = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++) { bgra[i * 4] = data[i * 4 + 3]; bgra[i * 4 + 1] = data[i * 4 + 2]; bgra[i * 4 + 2] = data[i * 4 + 1]; bgra[i * 4 + 3] = data[i * 4]; }
                break;
            case 14: // BGRA32
                bgra = data[..(w * h * 4)];
                break;
            case 10: DxtDecoder.DecompressDXT1<Bgra, byte>(data, w, h, out bgra); break;
            case 12: DxtDecoder.DecompressDXT5<Bgra, byte>(data, w, h, out bgra); break;
            case 26: Bc4.Decompress<Bgra, byte>(data, w, h, out bgra); break;
            case 27: Bc5.Decompress<Bgra, byte>(data, w, h, out bgra); break;
            case 25: Bc7.Decompress<Bgra, byte>(data, w, h, out bgra); break;
            case 24: Bc6h.Decompress<Bgra, byte>(data, w, h, false, out bgra); break;
            case 48 or 66: AstcDecoder.DecodeASTC<Bgra, byte>(data, w, h, 4, 4, out bgra); break;
            case 50 or 68: AstcDecoder.DecodeASTC<Bgra, byte>(data, w, h, 6, 6, out bgra); break;
            case 51 or 69: AstcDecoder.DecodeASTC<Bgra, byte>(data, w, h, 8, 8, out bgra); break;
            default: return null;
        }
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * w * 4, dst = y * w * 4;
            for (int x = 0; x < w * 4; x += 4)
            {
                rgba[dst + x] = bgra[src + x + 2]; rgba[dst + x + 1] = bgra[src + x + 1]; rgba[dst + x + 2] = bgra[src + x]; rgba[dst + x + 3] = bgra[src + x + 3];
            }
        }
        return new RgbaImage(w, h, rgba);
    }

    /// <summary>Up to 64 points on the convex hull: the farthest vertex along fixed directions. Flat objects get a
    /// minimum thickness so PhysicsInitConvex never sees a plane.</summary>
    public static List<Vector3> HullPoints(IEnumerable<Vector3> points, float minThickness = 0.2f)
    {
        var pts = points.ToList();
        var dirs = new List<Vector3>();
        for (int i = 0; i < 42; i++)
        {
            // Fibonacci sphere: even directions without a table.
            float y = 1 - 2 * (i + 0.5f) / 42, r = MathF.Sqrt(1 - y * y), a = i * 2.39996323f;
            dirs.Add(new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r));
        }
        var hull = new List<Vector3>();
        foreach (var d in dirs)
        {
            var best = pts.MaxBy(p => Vector3.Dot(p, d));
            if (!hull.Any(h => Vector3.DistanceSquared(h, best) < 1e-6f)) hull.Add(best);
        }
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var size = max - min;
        for (int axis = 0; axis < 3; axis++)
            if (size[axis] < minThickness)
            {
                var add = new Vector3(axis == 0 ? minThickness : 0, axis == 1 ? minThickness : 0, axis == 2 ? minThickness : 0);
                hull = hull.Concat(hull.Select(h => h + add)).ToList();
            }
        return hull.Take(64).ToList();
    }

    /// <summary>Moves a mesh so its pivot is the centre of its bottom face (it sits on the floor at z = 0).</summary>
    public static (Vector3 Min, Vector3 Max) PivotToBottomCentre(StaticMesh mesh)
    {
        var (min, max) = mesh.Bounds();
        var shift = new Vector3(-(min.X + max.X) / 2, -(min.Y + max.Y) / 2, -min.Z);
        foreach (var p in mesh.Parts)
            for (int i = 0; i + 2 < p.V.Count; i += 8) { p.V[i] += shift.X; p.V[i + 1] += shift.Y; p.V[i + 2] += shift.Z; }
        return (min + shift, max + shift);
    }
}
