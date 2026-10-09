using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using SilentKatamari.Converter;
using Xunit;

public class CoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sk-tests-" + Guid.NewGuid().ToString("N"));
    public CoreTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    private Steam FakeSteam()
    {
        string root = Path.Combine(_tmp, "Steam");
        string lib2 = Path.Combine(_tmp, "Second Library");
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
        // New format with an escaped path, as Steam writes it.
        File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + root.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" +
            lib2.Replace("\\", "\\\\") + "\"\n\t\t\"apps\" { \"2124490\" \"1\" }\n\t}\n}\n");
        void App(string lib, string id, string dir, string build, string probe)
        {
            File.WriteAllText(Path.Combine(lib, "steamapps", $"appmanifest_{id}.acf"),
                $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"{dir}\"\n\t\"buildid\"\t\t\"{build}\"\n}}\n");
            string folder = Path.Combine(lib, "steamapps", "common", dir);
            Directory.CreateDirectory(Path.Combine(folder, Path.GetDirectoryName(probe)!));
            File.WriteAllText(Path.Combine(folder, probe), "x");
        }
        App(root, "1880620", "OnceUponaKATAMARI", "111", "OnceUponaKATAMARI_Data/StreamingAssets/aa/x");
        App(lib2, "2124490", "SILENT HILL 2", "222", "SHProto/Content/Paks/SHProto-Windows.utoc");
        return new Steam { RootOverride = root, RegistryValue = (_, _, _) => null, EpicManifests = null };
    }

    [Fact]
    public void SteamFindsBothGamesAcrossLibraries()
    {
        var s = FakeSteam();
        var ouak = s.Find(Games.ouak);
        var sh2 = s.Find(Games.sh2);
        Assert.NotNull(ouak);
        Assert.NotNull(sh2);
        Assert.Equal("111", ouak!.Build);
        Assert.Equal("222", sh2!.Build);
        Assert.EndsWith("SILENT HILL 2", sh2.Folder);
        Assert.Contains("Second Library", sh2.Folder);
    }

    [Fact]
    public void MissingGameIsNull()
    {
        var s = new Steam { RootOverride = Path.Combine(_tmp, "nothing"), RegistryValue = (_, _, _) => null, EpicManifests = null };
        Assert.Null(s.Find(Games.sh2));
    }

    [Fact]
    public void VdfOldFormatAndSingleBackslashes()
    {
        var doc = Vdf.Parse("\"LibraryFolders\" { \"TimeNextStatsReport\" \"1\" \"1\" \"D:\\\\Games\\\\Steam\" \"2\" \"E:\\new games\" }");
        var paths = Steam.LibraryPaths(doc).ToList();
        Assert.Equal(new[] { "D:\\Games\\Steam", "E:\\new games" }, paths);
    }

    [Fact]
    public void NeedsConvertDecisions()
    {
        var g = new GameInstall("sh2", "/g/SILENT HILL 2", "222", "steam");
        JsonObject Status(string state, string? folder, string? build) => new()
        {
            ["converter"] = Runner.Version,
            ["games"] = new JsonObject
            {
                ["ouak"] = new JsonObject { ["state"] = "missing" },
                ["sh2"] = new JsonObject { ["state"] = state, ["folder"] = folder, ["build"] = build },
            },
        };
        GameInstall? Find(string id) => id == "sh2" ? g : null;
        Assert.True(Runner.NeedsConvert(null, Find, out _));
        Assert.False(Runner.NeedsConvert(Status("ok", "/g/SILENT HILL 2", "222"), Find, out var why), why);
        Assert.True(Runner.NeedsConvert(Status("ok", "/g/SILENT HILL 2", "221"), Find, out why));
        Assert.Contains("updated", why);
        Assert.True(Runner.NeedsConvert(Status("missing", null, null), Find, out why));
        Assert.Contains("installed", why);
        var old = Status("ok", "/g/SILENT HILL 2", "222");
        old["converter"] = "0.0.1";
        Assert.True(Runner.NeedsConvert(old, Find, out _));
    }

    [Fact]
    public void SwapRefusesForeignFolder()
    {
        string staging = Path.Combine(_tmp, "content.staging"), final = Path.Combine(_tmp, "content");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(final);
        File.WriteAllText(Path.Combine(final, "someone-elses.txt"), "keep me");
        using var log = new Log(null);
        Assert.Throws<IOException>(() => Runner.SwapIn(staging, final, log));
        Assert.True(File.Exists(Path.Combine(final, "someone-elses.txt")));
        File.Delete(Path.Combine(final, "someone-elses.txt"));
        File.WriteAllText(Path.Combine(final, "addon.json"), "{\"title\":\"" + Runner.ContentTitle + "\"}");
        File.WriteAllText(Path.Combine(staging, "new.txt"), "new");
        Runner.SwapIn(staging, final, log);
        Assert.True(File.Exists(Path.Combine(final, "new.txt")));
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public void ArgumentsPassThroughToGarrysMod()
    {
        var o = Program.Parse(new[] { "play", "--gmod", "C:\\Games\\GarrysMod", "-steam", "+gamemode", "silentkatamari", "+map", "gm_flatgrass" });
        Assert.Equal("play", o.Command);
        Assert.Equal("C:\\Games\\GarrysMod", o.Gmod);
        Assert.Equal(new[] { "-steam", "+gamemode", "silentkatamari", "+map", "gm_flatgrass" }, o.Passthrough);
        var o2 = Program.Parse(new[] { "convert", "--force", "--gmod", "x", "--", "--weird" });
        Assert.True(o2.Force);
        Assert.Equal(new[] { "--weird" }, o2.Passthrough);
    }

    [Fact]
    public void ConvertWithBothGamesMissingWritesStatusLast()
    {
        string gmod = Path.Combine(_tmp, "GarrysMod");
        Directory.CreateDirectory(Path.Combine(gmod, "garrysmod", "addons"));
        using var log = new Log(null);
        var steam = new Steam { RootOverride = Path.Combine(_tmp, "nothing"), RegistryValue = (_, _, _) => null, EpicManifests = null };
        var runner = new Runner(gmod, log, steam);
        var status = runner.Convert();
        Assert.Equal("missing", status["games"]!["ouak"]!["state"]!.GetValue<string>());
        Assert.Equal("missing", status["games"]!["sh2"]!["state"]!.GetValue<string>());
        Assert.True(File.Exists(runner.StatusPath));
        Assert.True(Runner.IsOurs(runner.ContentRoot));
        // A second run replaces our own folder without complaint.
        runner.Convert();
        Assert.False(runner.NeedsConvert(out var why), why);
    }
}

public class WriterTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sk-wtests-" + Guid.NewGuid().ToString("N"));
    public WriterTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch (IOException) { } }

    private ContentWriter Writer(out ConvertContext ctx)
    {
        ctx = new ConvertContext { Log = new Log(null), GmodFolder = _tmp, WorkFolder = Path.Combine(_tmp, "work") };
        return new ContentWriter(Path.Combine(_tmp, "content"), ctx);
    }

    [Fact]
    public void PngIsValidAndRoundTrips()
    {
        var px = new byte[3 * 2 * 4];
        for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 9);
        var bytes = Png.Encode(new RgbaImage(3, 2, px));
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
        // Walk chunks, check CRCs, inflate IDAT and compare pixels.
        int p = 8;
        var idat = new MemoryStream();
        while (p < bytes.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(p));
            string type = System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4);
            var crc = new System.IO.Hashing.Crc32();
            crc.Append(bytes.AsSpan(p + 4, 4 + len));
            Assert.Equal(crc.GetCurrentHashAsUInt32(), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p + 8 + len)));
            if (type == "IDAT") idat.Write(bytes, p + 8, len);
            p += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        var r = raw.ToArray();
        Assert.Equal(2 * (1 + 12), r.Length);
        Assert.Equal(px[12..24], r[14..26]);
    }

    [Fact]
    public void VtfHeaderAndSize()
    {
        var img = new RgbaImage(300, 100, Enumerable.Range(0, 300 * 100 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : 128)).ToArray());
        var vtf = Vtf.Encode(img);
        Assert.Equal("VTF\0", System.Text.Encoding.ASCII.GetString(vtf, 0, 4));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(vtf.AsSpan(4)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(vtf.AsSpan(8)));
        Assert.Equal(256, BinaryPrimitives.ReadUInt16LittleEndian(vtf.AsSpan(16)));
        Assert.Equal(64, BinaryPrimitives.ReadUInt16LittleEndian(vtf.AsSpan(18)));
        Assert.Equal(Vtf.FormatBgra8888, BinaryPrimitives.ReadUInt32LittleEndian(vtf.AsSpan(52)));
        int mips = vtf[56];
        Assert.Equal(9, mips); // 256x64 .. 1x1
        long expected = Vtf.HeaderSize;
        for (int m = 0, w = 256, h = 64; m < mips; m++, w = Math.Max(1, w / 2), h = Math.Max(1, h / 2)) expected += w * h * 4;
        Assert.Equal(expected, vtf.Length);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(vtf.AsSpan(20))); // opaque: no alpha flag
    }

    [Fact]
    public void EulerRoundTrip()
    {
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var q = Quaternion.Normalize(new Quaternion((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)));
            var back = SourceModel.FromEuler(SourceModel.ToEuler(q));
            Assert.True(MathF.Abs(Quaternion.Dot(q, back)) > 0.9999f, $"{q} -> {back}");
        }
        // Gimbal lock: pitch exactly +-90 degrees.
        foreach (float pitch in new[] { MathF.PI / 2, -MathF.PI / 2 })
        {
            var q = SourceModel.FromEuler(new Vector3(0.3f, pitch, 1.1f));
            var back = SourceModel.FromEuler(SourceModel.ToEuler(q));
            Assert.True(MathF.Abs(Quaternion.Dot(q, back)) > 0.999f);
        }
        // Source convention: yaw 90 degrees turns +X into +Y.
        var yaw = SourceModel.FromEuler(new Vector3(0, 0, MathF.PI / 2));
        var v = Vector3.Transform(Vector3.UnitX, yaw);
        Assert.True(Vector3.Distance(v, Vector3.UnitY) < 1e-5f, v.ToString());
    }

    private static SkinnedModel Quad()
    {
        var m = new SkinnedModel { Name = "quad" };
        m.Bones.Add(new Bone { Name = "root", Parent = -1 });
        m.Bones.Add(new Bone { Name = "unused", Parent = 0 });
        m.Bones.Add(new Bone { Name = "spine", Parent = 0, Position = new Vector3(0, 0, 10) });
        m.Bones.Add(new Bone { Name = "hand", Parent = 2, Position = new Vector3(5, 0, 0) });
        m.Materials.Add(new MaterialInfo { Name = "Body Mat" });
        var part = new SkinnedPart { Material = "Body Mat" };
        for (int i = 0; i < 4; i++)
            part.Vertices.Add(new SkinnedVertex { Position = new Vector3(i, 0, 0), Normal = Vector3.UnitZ, Uv = new Vector2(i / 3f, 0.25f), B0 = 3, W0 = 0.75f, B1 = 0, W1 = 0.25f });
        part.Indices.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
        m.Parts.Add(part);
        return m;
    }

    [Fact]
    public void BonePruningKeepsAncestors()
    {
        var map = SourceModel.PruneBones(Quad());
        Assert.Equal(new[] { 0, -1, 1, 2 }, map);
    }

    [Fact]
    public void ModelSourcesAreWritten()
    {
        var w = Writer(out var ctx);
        var clip = new AnimClip { Name = "idle" };
        var model = Quad();
        clip.Frames.Add(model.Bones.Select(b => (b.Position, b.Rotation)).ToArray());
        string qc = w.WriteModelSources("silent_katamari/quad", model, new[] { clip }, Path.Combine(ctx.WorkFolder, "q"));
        string text = File.ReadAllText(qc);
        Assert.Contains("$modelname \"silent_katamari/quad.mdl\"", text);
        Assert.Contains("$sequence \"idle\" \"anim_idle.smd\" loop fps 30", text);
        string smd = File.ReadAllText(Path.Combine(ctx.WorkFolder, "q", "ref.smd"));
        Assert.DoesNotContain("unused", smd);
        Assert.Contains("body_mat\n", smd);
        Assert.Contains(" 0.75 2", smd.Replace("2 0.75", " 0.75 2")); // hand (new index 2) carries 0.75
        Assert.Contains(" 0.75 ", smd); // v flipped: 1 - 0.25
        Assert.True(File.Exists(Path.Combine(w.Root, "materials/models/silent_katamari/quad/body_mat.vtf")));
        Assert.Contains("$basetexture\" \"models/silent_katamari/quad/body_mat\"", File.ReadAllText(Path.Combine(w.Root, "materials/models/silent_katamari/quad/body_mat.vmt")));
    }

    [Fact]
    public void MeshJsonAndPathSafety()
    {
        var w = Writer(out _);
        var mesh = new StaticMesh { Name = "o1" };
        mesh.Materials.Add(new MaterialInfo { Name = "Mat A", Texture = new RgbaImage(2, 2, new byte[16]) });
        var part = new MeshPart { Material = "Mat A" };
        part.V.AddRange(new float[] { 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 0, 1 });
        part.I.AddRange(new[] { 0, 1, 2 });
        mesh.Parts.Add(part);
        w.WriteMeshJson("data_static/silent_katamari/meshes/o1.json", mesh, "silent_katamari/ouak/");
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(w.Root, "data_static/silent_katamari/meshes/o1.json")))!;
        Assert.Equal("silent_katamari/ouak/mat_a", json["materials"]!["mat_a"]!["tex"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(w.Root, "materials/silent_katamari/ouak/mat_a.png")));
        Assert.Throws<ArgumentException>(() => w.WriteBytes("../escape.txt", new byte[1]));
        Assert.Throws<ArgumentException>(() => w.WriteBytes("/abs.txt", new byte[1]));
    }
}
