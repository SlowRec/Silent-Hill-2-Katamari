using System.Buffers.Binary;
using System.Numerics;
using CUE4Parse.UE4.Objects.Core.Math;
using SilentKatamari.Converter;
using SilentKatamari.Converter.Sh2;
using Xunit;

public class Sh2Tests
{
    /// <summary>A synthetic .utoc laid out exactly in the order CUE4Parse's FIoStoreTocResource reads it.</summary>
    private static byte[] Toc(byte version, byte flags, uint entries, uint blocks, uint seeds, uint withoutHash, string[] methods)
    {
        const int nameLen = 32;
        var ms = new MemoryStream();
        var h = new byte[ContainerCheck.TocHeaderSize];
        ContainerCheck.TocMagic.CopyTo(h, 0);
        h[16] = version;
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(20), ContainerCheck.TocHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(24), entries);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(28), blocks);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(32), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(36), (uint)methods.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(40), nameLen);
        h[80] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(84), seeds);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(96), withoutHash);
        ms.Write(h);
        ms.Write(new byte[entries * 12 + entries * 10 + seeds * 4 + withoutHash * 4 + blocks * 12]);
        foreach (var m in methods)
        {
            var b = new byte[nameLen];
            System.Text.Encoding.ASCII.GetBytes(m).CopyTo(b, 0);
            ms.Write(b);
        }
        ms.Write(new byte[64]); // directory index etc.
        return ms.ToArray();
    }

    private static ContainerInfo Parse(byte[] toc)
    {
        using var s = new MemoryStream(toc);
        return ContainerCheck.ParseToc("test.utoc", toc.Length, toc[..ContainerCheck.TocHeaderSize], s);
    }

    [Fact]
    public void TocUnencryptedOodle()
    {
        var c = Parse(Toc(5, 0x01 | 0x08, 1000, 400, 1000, 3, new[] { "Oodle" }));
        Assert.False(c.Encrypted);
        Assert.True(c.Compressed);
        Assert.Equal(5, c.Version);
        Assert.Equal(new[] { "Oodle" }, c.CompressionMethods);
        Assert.Null(c.Error);
    }

    [Fact]
    public void TocEncryptedFlagIsByte80()
    {
        var c = Parse(Toc(4, 0x02 | 0x01, 10, 5, 10, 0, new[] { "Zlib", "Oodle" }));
        Assert.True(c.Encrypted);
        Assert.Equal(new[] { "Zlib", "Oodle" }, c.CompressionMethods); // version 4: no overflow list
    }

    [Fact]
    public void PakTrailer()
    {
        // FPakInfo v11: guid(16) encryptedIndex(1) magic version offset(8) size(8) hash(20) 5 x 32 names
        var t = new byte[16 + 1 + 4 + 4 + 8 + 8 + 20 + 160];
        t[16] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(17), ContainerCheck.PakMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(21), 11);
        System.Text.Encoding.ASCII.GetBytes("Oodle").CopyTo(t, 17 + 4 + 4 + 8 + 8 + 20);
        var tail = new byte[300];
        t.CopyTo(tail, tail.Length - t.Length);
        var c = ContainerCheck.ParsePakTail("x.pak", 12345, tail);
        Assert.Equal(11, c.Version);
        Assert.True(c.Encrypted);
        Assert.Equal(new[] { "Oodle" }, c.CompressionMethods);
    }

    [Fact]
    public void AxisConversionAndFacing()
    {
        // UE: +X forward, +Y right, +Z up (left-handed). A character faces +Y. After conversion it must face +X.
        var nose = Ue.Pos(new FVector(0, 10, 170));
        var faced = Vector3.Transform(nose, Ue.YawFix);
        Assert.True(Vector3.Distance(faced, new Vector3(10, 0, 170)) < 1e-4f, faced.ToString());
        // Mirroring keeps rotations consistent: rotating a point in UE then converting equals converting then rotating.
        var q = new FQuat(0.1f, 0.2f, 0.3f, 0.927f);
        var qs = Ue.Rot(q);
        var nq = Quaternion.Normalize(new Quaternion(q.X, q.Y, q.Z, q.W));
        var p = new Vector3(3, 4, 5);
        var ueRotated = Vector3.Transform(p, nq);
        var a = Ue.Pos(new FVector(ueRotated.X, ueRotated.Y, ueRotated.Z));
        var b = Vector3.Transform(Ue.Pos(new FVector(p.X, p.Y, p.Z)), qs);
        Assert.True(Vector3.Distance(a, b) < 1e-4f, $"{a} vs {b}");
    }

    [Fact]
    public void AnimationsMatchTheSheet()
    {
        var keys = new[]
        {
            "SHProto/Content/Game/Characters/Humans/JamesSunderland/Animation/Locomotion/AS_James_Walk_F.uasset",
            "SHProto/Content/Game/Characters/Humans/JamesSunderland/Animation/Locomotion/AS_James_Walk_F_Injured.uasset",
            "SHProto/Content/Game/Characters/Humans/JamesSunderland/Animation/Locomotion/AS_James_Walk_B.uasset",
            "SHProto/Content/Game/Characters/Humans/JamesSunderland/Animation/Locomotion/AS_James_Idle.uasset",
            "SHProto/Content/Game/Characters/Humans/JamesSunderland/Animation/Locomotion/AS_James_Idle_Flashlight.uasset",
        };
        Assert.EndsWith("AS_James_Idle.uasset", JamesExtractor.MatchAnim(keys, Anims.idle.Match, Anims.idle.Avoid));
        Assert.EndsWith("AS_James_Walk_F.uasset", JamesExtractor.MatchAnim(keys, Anims.walk.Match, Anims.walk.Avoid));
        Assert.EndsWith("AS_James_Walk_B.uasset", JamesExtractor.MatchAnim(keys, Anims.walk_back.Match, Anims.walk_back.Avoid));
        Assert.Null(JamesExtractor.MatchAnim(keys, Anims.run.Match, Anims.run.Avoid));
    }

    [Fact]
    public void ArmBonesByName()
    {
        var bones = new[] { "root", "pelvis", "clavicle_l", "upperarm_l", "upperarm_twist_01_l", "lowerarm_l", "hand_l", "clavicle_r", "upperarm_r", "lowerarm_r" };
        var arms = JamesExtractor.ArmBones(bones).ToDictionary(x => x.Role, x => x.Name);
        Assert.Equal("upperarm_l", arms["upperarm_l"]);
        Assert.Equal("lowerarm_r", arms["lowerarm_r"]);
        var mixamo = JamesExtractor.ArmBones(new[] { "Hips", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm" }).ToDictionary(x => x.Role, x => x.Name);
        Assert.Equal("LeftArm", mixamo["upperarm_l"]);
        Assert.Equal("RightForeArm", mixamo["lowerarm_r"]);
    }

    [Fact]
    public void LocalToolsOnlyNeverDownloads()
    {
        var ctx = new ConvertContext { Log = new Log(null), GmodFolder = "/nowhere", ToolsFolder = Path.Combine(Path.GetTempPath(), "sk-no-tools") };
        Environment.SetEnvironmentVariable("SK_SH2_USMAP", null);
        Environment.SetEnvironmentVariable("SK_OODLE_DLL", null);
        Assert.Null(Sh2Reader.FindOodle(ctx, Path.Combine(Path.GetTempPath(), "sk-no-game")));
        string tmp = Path.Combine(Path.GetTempPath(), "sk-usmap-" + Guid.NewGuid().ToString("N") + ".usmap");
        File.WriteAllBytes(tmp, new byte[] { 0xC4, 0x30 });
        Environment.SetEnvironmentVariable("SK_SH2_USMAP", tmp);
        try { Assert.Equal(tmp, Sh2Reader.FindMappings(ctx)); }
        finally { Environment.SetEnvironmentVariable("SK_SH2_USMAP", null); File.Delete(tmp); }
    }
}
