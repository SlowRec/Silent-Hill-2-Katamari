// James Sunderland from the player's SILENT HILL 2: every skeletal mesh part that shares his body's skeleton, at
// the most detailed LOD within the characters sheet budget, his animations matched by the anims sheet, compiled
// into a Source model with Garry's Mod's studiomdl. Axes: UE (x, y, z) cm -> Source (x, -y, z), see docs/CONTRACT.md.
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;

namespace SilentKatamari.Converter.Sh2;

public static class Ue
{
    /// <summary>UE characters face +Y; after mirroring Y they face -Y, and +90 degrees about Z turns that to +X.</summary>
    public const float YawFixDeg = 90f;
    public static readonly Quaternion YawFix = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, YawFixDeg * MathF.PI / 180f);

    public static Vector3 Pos(FVector v) => new(v.X, -v.Y, v.Z);
    public static Vector3 Dir(float x, float y, float z) => Vector3.Normalize(new Vector3(x, -y, z) + new Vector3(1e-9f, 0, 0));
    /// <summary>Mirroring Y turns a rotation's quaternion (x, y, z, w) into (-x, y, -z, w).</summary>
    public static Quaternion Rot(FQuat q) => Quaternion.Normalize(new Quaternion(-q.X, q.Y, -q.Z, q.W));

    public static RgbaImage Image(CUE4Parse_Conversion.Textures.CTexture t)
    {
        int n = t.Width * t.Height;
        var px = new byte[n * 4];
        switch (t.PixelFormat)
        {
            case EPixelFormat.PF_B8G8R8A8:
                for (int i = 0; i < n; i++) { px[i * 4] = t.Data[i * 4 + 2]; px[i * 4 + 1] = t.Data[i * 4 + 1]; px[i * 4 + 2] = t.Data[i * 4]; px[i * 4 + 3] = t.Data[i * 4 + 3]; }
                break;
            case EPixelFormat.PF_R8G8B8A8:
                Buffer.BlockCopy(t.Data, 0, px, 0, Math.Min(px.Length, t.Data.Length));
                break;
            case EPixelFormat.PF_G8 or EPixelFormat.PF_L8 or EPixelFormat.PF_R8 or EPixelFormat.PF_A8:
                for (int i = 0; i < n; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = t.Data[i]; px[i * 4 + 3] = 255; }
                break;
            default:
                throw new NotSupportedException($"texture pixel format {t.PixelFormat}");
        }
        return new RgbaImage(t.Width, t.Height, px);
    }

    public static RgbaImage Decode(UTexture tex) =>
        Image(CUE4Parse_Conversion.Textures.TextureDecoder.Decode(tex, 2048, ETexturePlatform.DesktopMobile)
              ?? throw new InvalidOperationException($"texture {tex.Name} could not be decoded"));

    /// <summary>The base colour texture of a material instance, by parameter name.</summary>
    public static UTexture? BaseColor(UMaterialInterface? mat)
    {
        for (int depth = 0; mat is not null && depth < 4; depth++)
        {
            if (mat is UMaterialInstanceConstant mic)
            {
                var hit = mic.TextureParameterValues?
                    .OrderBy(p => Rank(p.Name))
                    .FirstOrDefault(p => Rank(p.Name) < 99 && p.ParameterValue.TryLoad(out var o) && o is UTexture);
                if (hit is not null && hit.ParameterValue.TryLoad(out var tex)) return tex as UTexture;
                mat = mic.Parent.TryLoad(out var parent) ? parent as UMaterialInterface : null;
            }
            else return null;
        }
        return null;
    }

    private static int Rank(string? name)
    {
        string n = (name ?? "").ToLowerInvariant();
        string[] order = { "basecolor", "base_color", "albedo", "diffuse", "color", "_bc", "_d" };
        for (int i = 0; i < order.Length; i++) if (n.Contains(order[i])) return i;
        return 99;
    }
}

public sealed class JamesExtractor
{
    private readonly DefaultFileProvider _p;
    private readonly IContentWriter _w;
    private readonly ConvertContext _ctx;
    private SkinnedModel? _model;
    private USkeleton? _skeleton;
    private int _lod;
    private readonly List<string> _sources = new();

    private static readonly Regex NotBody = new(@"(weapon|flashlight|radio|pipe|gun|rifle|shotgun|plank|proxy|_lod|lowpoly|cloth_sim|phys|mirror|corpse|dead|hood|outfit\d|_alt)", RegexOptions.IgnoreCase);

    public JamesExtractor(DefaultFileProvider p, IContentWriter w, ConvertContext ctx) { _p = p; _w = w; _ctx = ctx; }

    private IEnumerable<string> FolderAssets(string sub = "") =>
        _p.Files.Keys.Where(k => k.Contains(Sh2Reader.JamesFolder + sub, StringComparison.OrdinalIgnoreCase)
                                 && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase);

    private static string NoExt(string key) => key[..key.LastIndexOf('.')];

    public string? Mesh()
    {
        var meshes = new List<(string Key, USkeletalMesh Mesh)>();
        foreach (var key in FolderAssets())
        {
            string name = Path.GetFileNameWithoutExtension(key);
            if (Regex.IsMatch(name, @"^(A_|AS_|AM_|BS_|ABP|AnimBP|PA_|PHYS|CR_|MI_|M_|T_)", RegexOptions.IgnoreCase) || key.Contains("/Anim", StringComparison.OrdinalIgnoreCase)) continue;
            if (_p.TryLoadPackageObject(NoExt(key), out USkeletalMesh mesh)) meshes.Add((key, mesh));
        }
        _ctx.Log.Info("sh2: skeletal meshes in James's folder: " + string.Join(", ", meshes.Select(m => Path.GetFileNameWithoutExtension(m.Key))));
        if (meshes.Count == 0) throw new InvalidOperationException("James's model was not found in SILENT HILL 2's files");

        // The body: the biggest mesh whose name says James and nothing else; then every part on its skeleton.
        var bodyish = meshes.Where(m => !NotBody.IsMatch(Path.GetFileNameWithoutExtension(m.Key))).ToList();
        var body = bodyish.OrderByDescending(m => Path.GetFileName(m.Key).Contains("james", StringComparison.OrdinalIgnoreCase))
                          .ThenByDescending(m => m.Mesh.ReferenceSkeleton?.FinalRefBoneInfo?.Length ?? 0).FirstOrDefault();
        if (body.Mesh is null) throw new InvalidOperationException("James's body mesh could not be told apart");
        string skel = body.Mesh.Skeleton?.Name ?? "";
        var parts = bodyish.Where(m => (m.Mesh.Skeleton?.Name ?? "") == skel).ToList();
        _skeleton = body.Mesh.Skeleton is { } si && si.TryLoad(out var so) ? so as USkeleton : null;

        var dtos = new List<(string Key, SkeletalMeshDto Dto)>();
        foreach (var (key, mesh) in parts)
        {
            try { dtos.Add((key, new SkeletalMeshDto(mesh, EMeshQuality.All, ENaniteMeshFormat.NoNanite, false))); }
            catch (Exception e) { _ctx.Log.Warn($"sh2: {Path.GetFileName(key)} not converted: {e.Message}"); }
        }
        if (dtos.Count == 0) throw new InvalidOperationException("James's model could not be converted");

        int maxTris = Characters.james.MaxTriangles;
        int lods = dtos.Min(d => d.Dto.LODs.Count);
        _lod = Enumerable.Range(0, lods).FirstOrDefault(l => dtos.Sum(d => d.Dto.LODs[l].Indices.Length / 3) <= maxTris, lods - 1);
        _model = Build(dtos, _lod);
        _sources.AddRange(dtos.Select(d => NoExt(d.Key)));
        return $"{dtos.Count} part(s), LOD {_lod}, {_model.Triangles} triangles, {_model.Bones.Count} bones";
    }

    private SkinnedModel Build(List<(string Key, SkeletalMeshDto Dto)> dtos, int lod)
    {
        var model = new SkinnedModel { Name = "james" };
        var boneIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, dto) in dtos)
        {
            var map = new int[dto.Bones.Length];
            for (int i = 0; i < dto.Bones.Length; i++)
            {
                var b = dto.Bones[i];
                if (!boneIndex.TryGetValue(b.Name, out int idx))
                {
                    idx = model.Bones.Count;
                    boneIndex[b.Name] = idx;
                    int parent = b.ParentIndex >= 0 && b.ParentIndex < i ? map[b.ParentIndex] : -1;
                    var bone = new Bone { Name = b.Name, Parent = parent, Position = Ue.Pos(b.Transform.Translation), Rotation = Ue.Rot(b.Transform.Rotation) };
                    if (parent < 0) { bone.Position = Vector3.Transform(bone.Position, Ue.YawFix); bone.Rotation = Ue.YawFix * bone.Rotation; }
                    model.Bones.Add(bone);
                }
                map[i] = idx;
            }

            var l = dto.LODs[lod];
            var verts = new List<SkinnedVertex>(l.Vertices.Length);
            foreach (var v in l.Vertices)
            {
                var sv = new SkinnedVertex
                {
                    Position = Vector3.Transform(Ue.Pos(v.Position), Ue.YawFix),
                    Normal = Vector3.TransformNormal(Ue.Dir(v.Normal.X, v.Normal.Y, v.Normal.Z), Matrix4x4.CreateFromQuaternion(Ue.YawFix)),
                    Uv = new Vector2(v.Uv.U, v.Uv.V),
                };
                var inf = v.Influences ?? Array.Empty<MeshBoneInfluenceDto>();
                var top = inf.OrderByDescending(x => x.Weight).Take(4).ToArray();
                int B(int k) => k < top.Length && top[k].Bone < map.Length ? map[top[k].Bone] : 0;
                float W(int k) => k < top.Length ? top[k].Weight : 0;
                sv.B0 = B(0); sv.W0 = W(0); sv.B1 = B(1); sv.W1 = W(1); sv.B2 = B(2); sv.W2 = W(2); sv.B3 = B(3); sv.W3 = W(3);
                if (sv.W0 + sv.W1 + sv.W2 + sv.W3 <= 0) { sv.W0 = 1; }
                verts.Add(sv);
            }

            foreach (var section in l.Sections)
            {
                string matName = $"mat{model.Materials.Count}";
                var mat = new MaterialInfo { Name = matName };
                var slot = dto.GetMaterial(section);
                if (slot is { } s)
                {
                    mat.Name = string.IsNullOrEmpty(s.SlotName) ? matName : s.SlotName;
                    if (s.Material.TryLoad(out var mo) && Ue.BaseColor(mo as UMaterialInterface) is { } tex)
                    {
                        try { mat.Texture = Ue.Decode(tex).Resized(1024); }
                        catch (Exception e) { _ctx.Log.Warn($"sh2: {mat.Name}: {e.Message}"); }
                    }
                }
                if (model.Materials.All(m => m.Name != mat.Name)) model.Materials.Add(mat);
                var part = new SkinnedPart { Material = mat.Name };
                var remap = new Dictionary<uint, int>();
                for (int t = 0; t < section.NumFaces; t++)
                {
                    int i0 = section.FirstIndex + t * 3;
                    // Mirroring flips handedness: reverse each triangle's winding.
                    foreach (uint idx in new[] { l.Indices[i0], l.Indices[i0 + 2], l.Indices[i0 + 1] })
                    {
                        if (!remap.TryGetValue(idx, out int ni)) { ni = part.Vertices.Count; part.Vertices.Add(verts[(int)idx]); remap[idx] = ni; }
                        part.Indices.Add(ni);
                    }
                }
                if (part.Indices.Count > 0) model.Parts.Add(part);
            }
        }
        return model;
    }

    /// <summary>Picks and samples his animations, compiles the model, writes james.json.</summary>
    public string? Finish()
    {
        var model = _model ?? throw new InvalidOperationException("James's mesh was not read");
        var names = FolderAssets().Where(k => k.Contains("/Anim", StringComparison.OrdinalIgnoreCase)).ToList();
        var clips = new List<AnimClip>();
        var chosen = new JsonObject();
        var missing = new JsonArray();
        foreach (var row in Anims.All.Where(a => a.Character == "james"))
        {
            string? key = MatchAnim(names, row.Match, row.Avoid);
            AnimClip? clip = null;
            if (key is not null && _skeleton is not null && _p.TryLoadPackageObject(NoExt(key), out UAnimSequence seq) && !seq.IsValidAdditive())
            {
                try { clip = Sample(seq, model, row.Id, row.Loop); chosen[row.Id] = NoExt(key); }
                catch (Exception e) { _ctx.Log.Warn($"sh2: {row.Id} from {key}: {e.Message}"); }
            }
            if (clip is null)
            {
                missing.Add(row.Id);
                var copy = clips.FirstOrDefault(c => c.Name == "idle");
                clip = new AnimClip { Name = row.Id, Loop = row.Loop, Source = "copy" };
                clip.Frames.AddRange(copy?.Frames ?? new List<(Vector3, Quaternion)[]> { model.Bones.Select(b => (b.Position, b.Rotation)).ToArray() });
            }
            clips.Add(clip);
        }
        string? fail = _w.CompileModel("silent_katamari/james", model, clips, _ctx.GmodFolder);
        if (fail is not null) throw new InvalidOperationException(fail);

        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in model.Parts.SelectMany(p => p.Vertices)) { minZ = Math.Min(minZ, v.Position.Z); maxZ = Math.Max(maxZ, v.Position.Z); }
        var arms = new JsonObject();
        foreach (var (role, name) in ArmBones(model.Bones.Select(b => b.Name).ToList())) arms[role] = name;
        _w.WriteJson("data_static/silent_katamari/james.json", new JsonObject
        {
            ["height_cm"] = Math.Round(maxZ - minZ, 1),
            ["bones"] = model.Bones.Count,
            ["triangles"] = model.Triangles,
            ["lod"] = _lod,
            ["source_mesh"] = string.Join(", ", _sources),
            ["clips"] = chosen,
            ["missing_clips"] = missing,
            ["arm_bones"] = arms,
            ["yaw_fix_deg"] = Ue.YawFixDeg,
        });
        return $"{clips.Count - missing.Count}/{clips.Count} clips matched{(missing.Count > 0 ? ", missing: " + string.Join(", ", missing.Select(m => m!.ToString())) : "")}";
    }

    /// <summary>First name (sorted, case-insensitive) containing every 'match' and no 'avoid' substring; '-' alone = none.</summary>
    public static string? MatchAnim(IEnumerable<string> keys, IReadOnlyList<string> match, IReadOnlyList<string> avoid)
    {
        var avoids = avoid.Count == 1 && avoid[0] == "-" ? Array.Empty<string>() : avoid.ToArray();
        return keys.OrderBy(k => Path.GetFileName(k), StringComparer.OrdinalIgnoreCase).FirstOrDefault(k =>
        {
            string n = Path.GetFileNameWithoutExtension(k);
            return match.All(m => n.Contains(m, StringComparison.OrdinalIgnoreCase)) && !avoids.Any(a => n.Contains(a, StringComparison.OrdinalIgnoreCase));
        });
    }

    private AnimClip Sample(UAnimSequence seq, SkinnedModel model, string name, bool loop)
    {
        var set = AnimConverter.ConvertAnims(_skeleton!, seq);
        var s = set.Sequences.FirstOrDefault() ?? throw new InvalidOperationException("no animation data");
        var refNames = _skeleton!.ReferenceSkeleton.FinalNameToIndexMap;
        float duration = s.NumFrames > 1 && s.FramesPerSecond > 0 ? (s.NumFrames - 1) / s.FramesPerSecond : 0;
        int frames = Math.Max(1, (int)Math.Round(duration * 30) + 1);
        var clip = new AnimClip { Name = name, Loop = loop, Source = seq.Name, Fps = 30 };
        for (int f = 0; f < frames; f++)
        {
            float srcFrame = frames == 1 ? 0 : f * (s.NumFrames - 1) / (float)(frames - 1);
            var pose = new (Vector3, Quaternion)[model.Bones.Count];
            for (int b = 0; b < model.Bones.Count; b++)
            {
                var bone = model.Bones[b];
                pose[b] = (bone.Position, bone.Rotation);
                if (!refNames.TryGetValue(bone.Name, out int si) || si >= s.Tracks.Count || !s.Tracks[si].HasKeys()) continue;
                // Start from the skeleton's reference pose: a track may key only rotation or only position.
                var refPose = _skeleton.ReferenceSkeleton.FinalRefBonePose[si];
                FQuat q = refPose.Rotation;
                FVector p = refPose.Translation;
                FVector scale = new(1f);
                s.Tracks[si].GetBoneTransform(srcFrame, s.NumFrames, ref q, ref p, ref scale);
                var rot = Ue.Rot(q);
                var pos = Ue.Pos(p);
                if (bone.Parent < 0)
                {
                    rot = Ue.YawFix * rot;
                    pos = bone.Position; // in place: he walks on the spot behind the ball, the ball does the moving
                }
                pose[b] = (pos, rot);
            }
            clip.Frames.Add(pose);
        }
        return clip;
    }

    /// <summary>Upper and lower arm bones for the push pose, by common naming patterns.</summary>
    public static List<(string Role, string Name)> ArmBones(IReadOnlyList<string> bones)
    {
        static bool Side(string n, char s) => Regex.IsMatch(n, s == 'l' ? @"(_l$|\.l$|_l_|left|^l_)" : @"(_r$|\.r$|_r_|right|^r_)", RegexOptions.IgnoreCase);
        var list = new List<(string, string)>();
        foreach (char s in new[] { 'l', 'r' })
        {
            string? up = bones.FirstOrDefault(b => Regex.IsMatch(b, "upper_?arm", RegexOptions.IgnoreCase) && !b.Contains("twist", StringComparison.OrdinalIgnoreCase) && Side(b, s))
                      ?? bones.FirstOrDefault(b => Regex.IsMatch(b, "arm", RegexOptions.IgnoreCase) && !Regex.IsMatch(b, "fore|lower|twist|hand|armor|alarm|harm", RegexOptions.IgnoreCase) && Side(b, s));
            string? low = bones.FirstOrDefault(b => Regex.IsMatch(b, "(lower_?arm|forearm|fore_arm)", RegexOptions.IgnoreCase) && !b.Contains("twist", StringComparison.OrdinalIgnoreCase) && Side(b, s));
            if (up is not null) list.Add(($"upperarm_{s}", up));
            if (low is not null) list.Add(($"lowerarm_{s}", low));
        }
        return list;
    }
}
