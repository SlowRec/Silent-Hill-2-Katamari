// Writes the private content addon in the formats of docs/CONTRACT.md: JSON, PNG, mesh JSON, WAV, and Source
// models compiled with Garry's Mod's own studiomdl.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SilentKatamari.Converter;

public sealed class ContentWriter : IContentWriter
{
    private readonly ConvertContext _ctx;
    private readonly Dictionary<string, string> _textureByHash = new();

    public ContentWriter(string root, ConvertContext ctx)
    {
        Root = Path.GetFullPath(root);
        _ctx = ctx;
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public static string SafeName(string name)
    {
        string s = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_');
        return s.Length == 0 ? "m" : s.Length > 48 ? s[..48] : s;
    }

    /// <summary>Absolute path for a relative one; refuses anything that would leave Root.</summary>
    public string Full(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath) || Path.IsPathRooted(relPath) || relPath.Replace('\\', '/').Split('/').Contains(".."))
            throw new ArgumentException($"bad content path '{relPath}'");
        string full = Path.GetFullPath(Path.Combine(Root, relPath));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException($"bad content path '{relPath}'");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    public void WriteBytes(string relPath, byte[] bytes) => File.WriteAllBytes(Full(relPath), bytes);

    public void WriteJson(string relPath, JsonNode node) =>
        File.WriteAllText(Full(relPath), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    public void WritePng(string relPath, RgbaImage image, int maxSide = 1024) => WriteBytes(relPath, Png.Encode(image.Resized(maxSide)));

    public string WriteMeshJson(string relPath, StaticMesh mesh, string texturePrefix)
    {
        var mats = new JsonObject();
        var names = new Dictionary<string, string>();
        foreach (var m in mesh.Materials)
        {
            string key = SafeName(m.Name);
            for (int i = 2; mats.ContainsKey(key); i++) key = $"{SafeName(m.Name)}_{i}";
            names[m.Name] = key;
            string? tex = null;
            if (m.Texture is { } img)
            {
                // Identical images (shared atlases) are written once.
                string hash = Convert.ToHexString(SHA1.HashData(img.Pixels))[..16];
                if (!_textureByHash.TryGetValue(hash, out tex))
                {
                    tex = texturePrefix + key;
                    WritePng($"materials/{tex}.png", img);
                    _textureByHash[hash] = tex;
                }
            }
            mats[key] = new JsonObject
            {
                ["tex"] = tex,
                ["color"] = new JsonArray(Math.Round(m.Color.X, 4), Math.Round(m.Color.Y, 4), Math.Round(m.Color.Z, 4), Math.Round(m.Color.W, 4)),
            };
        }
        var parts = new JsonArray();
        foreach (var p in mesh.Parts)
        {
            if (p.I.Count < 3) continue;
            parts.Add(new JsonObject
            {
                ["mat"] = names.GetValueOrDefault(p.Material, names.Values.FirstOrDefault() ?? "m"),
                ["v"] = new JsonArray(p.V.Select(x => (JsonNode)Math.Round(x, 4)).ToArray()),
                ["i"] = new JsonArray(p.I.Select(x => (JsonNode)x).ToArray()),
            });
        }
        WriteJson(relPath, new JsonObject { ["materials"] = mats, ["parts"] = parts });
        return relPath;
    }

    public void WriteWav(string relPath, string sourceFile) => WriteWav(relPath, sourceFile, -1);

    /// <summary>16-bit PCM WAV: copied when it already is one, else decoded with the bundled vgmstream-cli
    /// (subsong >= 1 picks a stream inside a bank/container).</summary>
    public void WriteWav(string relPath, string sourceFile, int subsong)
    {
        string dst = Full(relPath);
        if (subsong < 0 && IsPcm16Wav(sourceFile))
        {
            File.Copy(sourceFile, dst, overwrite: true);
            return;
        }
        string tool = Vgmstream(_ctx) ?? throw new InvalidOperationException("the audio decoder (vgmstream) is missing from this mashup's files");
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(dst);
        if (subsong >= 1) { psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(subsong.ToString()); }
        psi.ArgumentList.Add(sourceFile);
        var (code, output) = Run(psi, TimeSpan.FromSeconds(120));
        if (code != 0 || !File.Exists(dst))
            throw new InvalidOperationException($"vgmstream could not decode {Path.GetFileName(sourceFile)}: {LastLine(output)}");
    }

    public static string? Vgmstream(ConvertContext ctx)
    {
        string? env = Environment.GetEnvironmentVariable("SK_VGMSTREAM");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        string p = Path.Combine(ctx.ToolsFolder, "vgmstream", OperatingSystem.IsWindows() ? "vgmstream-cli.exe" : "vgmstream-cli");
        return File.Exists(p) ? p : null;
    }

    internal static bool IsPcm16Wav(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var h = new byte[36];
            if (f.Read(h, 0, 36) < 36) return false;
            return Encoding.ASCII.GetString(h, 0, 4) == "RIFF" && Encoding.ASCII.GetString(h, 8, 4) == "WAVE"
                   && BitConverter.ToUInt16(h, 20) == 1 && BitConverter.ToUInt16(h, 34) == 16;
        }
        catch (IOException) { return false; }
    }

    /// <summary>Writes ref.smd, anim_*.smd, model.qc in the work folder and the VTF/VMT textures into the content
    /// addon. Returns the QC path. Separate from compiling so tests can check the sources on any OS.</summary>
    internal string WriteModelSources(string modelName, SkinnedModel model, IReadOnlyList<AnimClip> clips, string workDir)
    {
        var map = SourceModel.PruneBones(model);
        int kept = map.Count(x => x >= 0);
        if (kept > SourceModel.MaxBones) throw new InvalidOperationException($"{model.Name} still has {kept} bones after pruning (limit {SourceModel.MaxBones})");
        var matNames = SourceModel.MaterialNames(model, _ctx.Log);
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "ref.smd"), SourceModel.ReferenceSmd(model, map, matNames));
        foreach (var c in clips) File.WriteAllText(Path.Combine(workDir, $"anim_{c.Name}.smd"), SourceModel.AnimationSmd(model, map, c));
        string qc = Path.Combine(workDir, "model.qc");
        File.WriteAllText(qc, SourceModel.Qc(modelName, clips));
        foreach (var m in model.Materials)
        {
            string mat = matNames[m.Name];
            if (File.Exists(Full($"materials/models/{modelName}/{mat}.vmt"))) continue; // merged duplicate
            bool cutout = m.Texture is { } t && Vtf.HasAlpha(t);
            File.WriteAllText(Full($"materials/models/{modelName}/{mat}.vmt"), SourceModel.Vmt(modelName, mat, cutout));
            var img = m.Texture ?? new RgbaImage(4, 4, Enumerable.Repeat((byte)200, 64).ToArray());
            WriteBytes($"materials/models/{modelName}/{mat}.vtf", Vtf.Encode(img));
        }
        _ctx.Log.Info($"{model.Name}: {kept}/{model.Bones.Count} bones, {model.Triangles} triangles, {matNames.Values.Distinct().Count()} materials, {clips.Count} clips");
        return qc;
    }

    public string? CompileModel(string modelName, SkinnedModel model, IReadOnlyList<AnimClip> clips, string gmodFolder)
    {
        string work = Path.Combine(_ctx.WorkFolder, "model", SafeName(modelName));
        if (Directory.Exists(work)) Directory.Delete(work, true);
        string qc = WriteModelSources(modelName, model, clips, work);

        string? studiomdl = Runner.FindStudioMdl(gmodFolder);
        if (studiomdl is null) return "Garry's Mod's model compiler (studiomdl) was not found";
        string gameDir = Path.Combine(gmodFolder, "garrysmod");
        string outBase = Path.Combine(gameDir, "models", modelName.Replace('/', Path.DirectorySeparatorChar));
        var started = DateTime.UtcNow.AddSeconds(-2);

        var psi = new ProcessStartInfo(studiomdl) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-game", gameDir, "-nop4", "-verbose", qc }) psi.ArgumentList.Add(a);
        var (code, output) = Run(psi, TimeSpan.FromMinutes(5));
        foreach (var line in output.Split('\n')) if (line.Trim().Length > 0) _ctx.Log.Info($"studiomdl: {line.TrimEnd()}");

        bool ok = new[] { ".mdl", ".vvd", ".dx90.vtx" }.All(ext => File.Exists(outBase + ext) && File.GetLastWriteTimeUtc(outBase + ext) >= started);
        if (!ok)
        {
            string err = output.Split('\n').LastOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))?.Trim() ?? $"exit code {code}";
            return $"studiomdl failed: {ConvertContext.Short(err)}";
        }
        // Move our compiled files from garrysmod/models into the content addon; touch nothing else there.
        string outDir = Path.GetDirectoryName(outBase)!, stem = Path.GetFileName(outBase);
        foreach (var f in Directory.EnumerateFiles(outDir, stem + ".*"))
        {
            string rel = "models/" + modelName.Replace('\\', '/')[..(modelName.LastIndexOf('/') + 1)] + Path.GetFileName(f);
            string dst = Full(rel);
            File.Copy(f, dst, overwrite: true);
            File.Delete(f);
        }
        try { if (!Directory.EnumerateFileSystemEntries(outDir).Any()) Directory.Delete(outDir); } catch (IOException) { }
        return null;
    }

    internal static (int Code, string Output) Run(ProcessStartInfo psi, TimeSpan timeout)
    {
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {psi.FileName}");
        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (sb) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(timeout))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, sb + "\nERROR: timed out");
        }
        p.WaitForExit();
        return (p.ExitCode, sb.ToString());
    }

    private static string LastLine(string s) => s.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
}
