using System.Buffers.Binary;
using System.Numerics;
using SilentKatamari.Converter;
using SilentKatamari.Converter.Ouak;
using Xunit;

public class OuakTests
{
    [Fact]
    public void VertexStreamsHalfFloatsAndAlignment()
    {
        // 3 vertices. Stream 0: position float3 (12 B). Stream 1: normal half4 (8 B) + uv0 half2 (4 B) = 12 B.
        var channels = new List<VertexChannel>
        {
            new(0, 0, 0, 3), // 0 position
            new(1, 0, 1, 4), // 1 normal (half x4)
            new(0, 0, 0, 0), // 2 tangent: none
            new(0, 0, 0, 0), // 3 colour: none
            new(1, 8, 1, 2), // 4 uv0 (half x2)
        };
        int count = 3;
        var (offsets, strides) = UnityMesh.StreamLayout(channels, count);
        Assert.Equal(new[] { 12, 12 }, strides);
        Assert.Equal(new[] { 0, 48 }, offsets); // 36 bytes rounded up to 48
        var data = new byte[48 + 36];
        for (int v = 0; v < count; v++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(v * 12), v);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(v * 12 + 4), v * 2);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(v * 12 + 8), v * 3);
            int b = 48 + v * 12;
            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(b), (Half)0f);
            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(b + 2), (Half)1f);
            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(b + 4), (Half)0f);
            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(b + 8), (Half)(v * 0.25f));
            BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan(b + 10), (Half)0.5f);
        }
        var pos = UnityMesh.ReadChannel(data, channels, count, 0);
        var nrm = UnityMesh.ReadChannel(data, channels, count, 1);
        var uv = UnityMesh.ReadChannel(data, channels, count, 4);
        Assert.Equal(new[] { 2f, 4f, 6f }, pos[2]);
        Assert.Equal(1f, nrm[1][1]);
        Assert.Equal(0.5f, uv[2][0]);
        Assert.Empty(UnityMesh.ReadChannel(data, channels, count, 2));
    }

    [Fact]
    public void UnityToSourceAxesWindingAndUv()
    {
        // Unity: x right, y up, z forward, metres. A triangle in the XY plane facing -Z (toward a Unity camera).
        var pos = new[] { new[] { 0f, 0f, 0f }, new[] { 0f, 1f, 0f }, new[] { 1f, 0f, 0f } };
        var nrm = new[] { new[] { 0f, 0f, -1f }, new[] { 0f, 0f, -1f }, new[] { 0f, 0f, -1f } };
        var uv = new[] { new[] { 0f, 0f }, new[] { 0f, 1f }, new[] { 1f, 0f } };
        var part = UnityMesh.Part("m", pos, nrm, uv, new[] { 0, 1, 2 }, Matrix4x4.Identity);
        // Unity (0,1,0) m -> Source (0,0,100) cm; Unity (1,0,0) -> Source (0,-100,0).
        Assert.Equal(new[] { 0, 1, 2 }, part.I); // renumbered in visit order: Unity 0, 2, 1 (winding reversed)
        var v1 = new Vector3(part.V[8], part.V[9], part.V[10]); // second written vertex = Unity index 2
        Assert.Equal(new Vector3(0, -100, 0), v1);
        Assert.Equal(-1f, part.V[3], 3); // normal Unity -Z -> Source -X
        Assert.Equal(1f, part.V[7]); // v flipped: 1 - 0
        // Counter-clockwise from outside in Source: the face normal from the winding agrees with the vertex normal.
        Vector3 P(int i) => new(part.V[part.I[i] * 8], part.V[part.I[i] * 8 + 1], part.V[part.I[i] * 8 + 2]);
        var face = Vector3.Cross(P(1) - P(0), P(2) - P(0));
        Assert.True(Vector3.Dot(face, new Vector3(-1, 0, 0)) > 0, face.ToString());
    }

    [Fact]
    public void HullAndBottomCentre()
    {
        var mesh = new StaticMesh();
        var part = new MeshPart { Material = "m" };
        var rng = new Random(3);
        for (int i = 0; i < 500; i++)
        {
            part.V.AddRange(new[] { 10 + (float)rng.NextDouble() * 4, 20 + (float)rng.NextDouble() * 6, 5 + (float)rng.NextDouble() * 2, 0f, 0f, 1f, 0f, 0f });
            part.I.Add(i);
        }
        mesh.Parts.Add(part);
        var (min, max) = UnityMesh.PivotToBottomCentre(mesh);
        Assert.Equal(0, min.Z, 3);
        Assert.Equal(0, (min.X + max.X) / 2, 3);
        var pts = Enumerable.Range(0, 500).Select(i => new Vector3(part.V[i * 8], part.V[i * 8 + 1], part.V[i * 8 + 2])).ToList();
        var hull = UnityMesh.HullPoints(pts);
        Assert.InRange(hull.Count, 4, 64);
        Assert.All(hull, h => Assert.Contains(h, pts));
        // A flat sticker gets thickness so physics never sees a plane.
        var flat = UnityMesh.HullPoints(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) });
        Assert.True(flat.Max(p => p.Z) - flat.Min(p => p.Z) >= 0.2f - 1e-6f);
    }

    [Fact]
    public void TextureRgbaFlippedToTopLeft()
    {
        // 1x2 RGBA32: bottom row red, top row blue (Unity stores the bottom row first).
        var data = new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 };
        var img = UnityMesh.DecodeTexture(4, 1, 2, data)!;
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, img.Pixels[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, img.Pixels[4..]);
        Assert.Null(UnityMesh.DecodeTexture(9999, 1, 1, new byte[4]));
    }

    [Fact]
    public void BundlePrefixDropsTheBuildHash()
    {
        Assert.Equal("prefab-model-props_assets_all", Bundles.Prefix("/x/prefab-model-props_assets_all_3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c.bundle"));
        Assert.Equal("defaultlocalgroup_assets_all", Bundles.Prefix("defaultlocalgroup_assets_all.bundle"));
    }

    [Fact]
    public void FontFamilyFromNameTable()
    {
        // Minimal 'name' table: one Windows record, nameID 1, "Seurat" in UTF-16BE.
        var str = System.Text.Encoding.BigEndianUnicode.GetBytes("Seurat");
        var t = new byte[6 + 12 + str.Length];
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(4), 18);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(6), 3);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16BigEndian(t.AsSpan(14), (ushort)str.Length);
        str.CopyTo(t, 18);
        Assert.Equal("Seurat", Ttf.FromNameTable(t, 0));
        string? sys = new[] { "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans.ttf" }.FirstOrDefault(File.Exists);
        if (sys is not null) Assert.Equal("DejaVu Sans", Ttf.FamilyName(File.ReadAllBytes(sys)));
    }
}
