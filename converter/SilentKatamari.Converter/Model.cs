// Shared in-memory shapes the game readers produce and the writers consume.
// All lengths in centimetres, Source axes (+X forward, +Y left, +Z up, right-handed). See docs/CONTRACT.md.
using System.Numerics;

namespace SilentKatamari.Converter;

/// <summary>One installed game found on this PC.</summary>
public sealed record GameInstall(string Id, string Folder, string? Build, string Store);

/// <summary>A texture decoded to 8-bit RGBA, rows top to bottom.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    public RgbaImage Resized(int maxSide)
    {
        int longest = Math.Max(Width, Height);
        if (longest <= maxSide) return this;
        double s = (double)maxSide / longest;
        int w = Math.Max(1, (int)Math.Round(Width * s)), h = Math.Max(1, (int)Math.Round(Height * s));
        var dst = new byte[w * h * 4];
        // Box filter: average every source pixel that falls in each destination pixel.
        for (int y = 0; y < h; y++)
        {
            int y0 = y * Height / h, y1 = Math.Max(y0 + 1, (y + 1) * Height / h);
            for (int x = 0; x < w; x++)
            {
                int x0 = x * Width / w, x1 = Math.Max(x0 + 1, (x + 1) * Width / w);
                long r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                {
                    int i = (sy * Width + sx) * 4;
                    r += Pixels[i]; g += Pixels[i + 1]; b += Pixels[i + 2]; a += Pixels[i + 3]; n++;
                }
                int o = (y * w + x) * 4;
                dst[o] = (byte)(r / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(b / n); dst[o + 3] = (byte)(a / n);
            }
        }
        return new RgbaImage(w, h, dst);
    }
}

/// <summary>A material: an optional base colour texture times a colour.</summary>
public sealed class MaterialInfo
{
    public string Name = "";
    public RgbaImage? Texture;
    public Vector4 Color = Vector4.One;
}

/// <summary>A static mesh part: 8 floats per vertex (pos xyz, normal xyz, uv) and triangle indices.</summary>
public sealed class MeshPart
{
    public string Material = "";
    public List<float> V = new();
    public List<int> I = new();
    public int VertexCount => V.Count / 8;
}

public sealed class StaticMesh
{
    public string Name = "";
    public List<MaterialInfo> Materials = new();
    public List<MeshPart> Parts = new();

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var p in Parts)
            for (int i = 0; i + 2 < p.V.Count; i += 8)
            {
                var v = new Vector3(p.V[i], p.V[i + 1], p.V[i + 2]);
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
            }
        return (min, max);
    }
}

/// <summary>One Katamari object ready for objects.json.</summary>
public sealed class KatamariObject
{
    public string Id = "";          // "o" + game id
    public string Name = "";
    public string Tier = "";
    public double PickupCm;
    public double VolumeCm3;
    public double Rate;
    public bool OwnPickup;          // true when pickup_cm came from the game's own table
    public StaticMesh Mesh = new();
}

public sealed class Bone
{
    public string Name = "";
    public int Parent = -1;
    public Vector3 Position;        // local, cm, Source axes
    public Quaternion Rotation = Quaternion.Identity; // local, Source axes
}

/// <summary>Skinned mesh vertex: position, normal, uv and up to 4 bone weights (bind pose, model space).</summary>
public struct SkinnedVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
    public int B0, B1, B2, B3;
    public float W0, W1, W2, W3;
}

public sealed class SkinnedPart
{
    public string Material = "";
    public List<SkinnedVertex> Vertices = new();
    public List<int> Indices = new(); // counter-clockwise from outside, Source axes
}

public sealed class SkinnedModel
{
    public string Name = "";
    public List<Bone> Bones = new();
    public List<MaterialInfo> Materials = new();
    public List<SkinnedPart> Parts = new();
    public int Triangles => Parts.Sum(p => p.Indices.Count / 3);
}

/// <summary>An animation sampled at a fixed rate: Frames[f][bone] = local transform (Source axes, cm).</summary>
public sealed class AnimClip
{
    public string Name = "";
    public string Source = "";
    public float Fps = 30;
    public bool Loop = true;
    public List<(Vector3 Pos, Quaternion Rot)[]> Frames = new();
}

/// <summary>A sound decoded or extracted to a file the writer can copy in.</summary>
public sealed record SoundFile(string Path, string Note);
