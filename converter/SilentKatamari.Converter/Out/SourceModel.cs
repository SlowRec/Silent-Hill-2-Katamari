// Source model sources for studiomdl: SMD (reference + one per animation) and QC. Model space in cm,
// Source axes, as Model.cs says; studiomdl compiles them with Garry's Mod's own copy on the player's PC.
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SilentKatamari.Converter;

public static class SourceModel
{
    public const int MaxBones = 128;     // MAXSTUDIOBONES
    public const int MaxMaterials = 32;  // studiomdl's per-model limit
    public const int MaxLinks = 3;       // weights per vertex studiomdl keeps

    private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Source RadianEuler (x = roll, y = pitch, z = yaw) with matrix = Rz(z) * Ry(y) * Rx(x), as studiomdl's
    /// AngleMatrix(RadianEuler) builds it from an SMD line's "rx ry rz".</summary>
    public static Vector3 ToEuler(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        var m = Matrix4x4.CreateFromQuaternion(q); // row-vector convention: m.Mij = R[j][i]
        float r00 = m.M11, r10 = m.M12, r20 = m.M13, r01 = m.M21, r11 = m.M22, r21 = m.M23, r22 = m.M33;
        float sp = Math.Clamp(-r20, -1f, 1f);
        float pitch = MathF.Asin(sp);
        if (MathF.Abs(sp) > 0.99999f)
        {
            // Gimbal lock: roll and yaw share one axis; put it all in yaw.
            return new Vector3(0, pitch, MathF.Atan2(-r01, r11));
        }
        return new Vector3(MathF.Atan2(r21, r22), pitch, MathF.Atan2(r10, r00));
    }

    /// <summary>The rotation an SMD Euler triple stands for (column-vector R = Rz Ry Rx), as a quaternion.</summary>
    public static Quaternion FromEuler(Vector3 e) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, e.Z) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, e.Y)
        * Quaternion.CreateFromAxisAngle(Vector3.UnitX, e.X);

    /// <summary>Keeps bones that carry weight plus their ancestors. Returns old index → new index (-1 = dropped).</summary>
    public static int[] PruneBones(SkinnedModel model)
    {
        var used = new bool[model.Bones.Count];
        foreach (var p in model.Parts)
            foreach (var v in p.Vertices)
            {
                if (v.W0 > 0) used[v.B0] = true;
                if (v.W1 > 0) used[v.B1] = true;
                if (v.W2 > 0) used[v.B2] = true;
                if (v.W3 > 0) used[v.B3] = true;
            }
        for (int i = 0; i < used.Length; i++)
            if (used[i])
                for (int b = model.Bones[i].Parent; b >= 0 && !used[b]; b = model.Bones[b].Parent) used[b] = true;
        if (model.Bones.Count > 0) used[0] = true; // keep the root
        var map = new int[used.Length];
        int n = 0;
        for (int i = 0; i < used.Length; i++) map[i] = used[i] ? n++ : -1;
        return map;
    }

    private static List<(int Bone, float Weight)> Links(SkinnedVertex v, int[] map)
    {
        var l = new List<(int, float)>(4);
        void Add(int b, float w) { if (w >= 0.01f && map[b] >= 0) l.Add((map[b], w)); }
        Add(v.B0, v.W0); Add(v.B1, v.W1); Add(v.B2, v.W2); Add(v.B3, v.W3);
        l.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        if (l.Count > MaxLinks) l.RemoveRange(MaxLinks, l.Count - MaxLinks);
        float sum = l.Sum(x => x.Item2);
        if (l.Count == 0 || sum <= 0) return new() { (0, 1f) };
        return l.Select(x => (x.Item1, x.Item2 / sum)).ToList();
    }

    private static void Nodes(StringBuilder sb, SkinnedModel model, int[] map)
    {
        sb.Append("nodes\n");
        for (int i = 0; i < model.Bones.Count; i++)
        {
            if (map[i] < 0) continue;
            int parent = model.Bones[i].Parent >= 0 ? map[model.Bones[i].Parent] : -1;
            sb.Append(CultureInfo.InvariantCulture, $"{map[i]} \"{model.Bones[i].Name.Replace("\"", "")}\" {parent}\n");
        }
        sb.Append("end\n");
    }

    private static void Frame(StringBuilder sb, int time, IReadOnlyList<(Vector3 Pos, Quaternion Rot)> pose, int[] map)
    {
        sb.Append(CultureInfo.InvariantCulture, $"time {time}\n");
        for (int i = 0; i < pose.Count && i < map.Length; i++)
        {
            if (map[i] < 0) continue;
            var e = ToEuler(pose[i].Rot);
            var p = pose[i].Pos;
            sb.Append(CultureInfo.InvariantCulture, $"{map[i]} {F(p.X)} {F(p.Y)} {F(p.Z)} {F(e.X)} {F(e.Y)} {F(e.Z)}\n");
        }
    }

    private static (Vector3, Quaternion)[] BindPose(SkinnedModel model) =>
        model.Bones.Select(b => (b.Position, b.Rotation)).ToArray();

    /// <summary>Material names safe for Source paths, at most 32 (extras merged onto the first).</summary>
    public static Dictionary<string, string> MaterialNames(SkinnedModel model, Log? log = null)
    {
        var names = new Dictionary<string, string>();
        var used = new HashSet<string>();
        foreach (var m in model.Materials)
        {
            string safe = ContentWriter.SafeName(m.Name);
            string unique = safe;
            for (int i = 2; !used.Add(unique); i++) unique = $"{safe}_{i}";
            names[m.Name] = unique;
        }
        if (names.Count > MaxMaterials)
        {
            string first = names.Values.First();
            foreach (var k in names.Keys.Skip(MaxMaterials).ToList()) names[k] = first;
            log?.Warn($"{model.Name}: {model.Materials.Count} materials, merged the extras onto '{first}'");
        }
        return names;
    }

    public static string ReferenceSmd(SkinnedModel model, int[] map, Dictionary<string, string> matNames)
    {
        var sb = new StringBuilder("version 1\n");
        Nodes(sb, model, map);
        sb.Append("skeleton\n");
        Frame(sb, 0, BindPose(model), map);
        sb.Append("end\ntriangles\n");
        foreach (var part in model.Parts)
        {
            string mat = matNames.GetValueOrDefault(part.Material, matNames.Values.FirstOrDefault() ?? "default");
            for (int t = 0; t + 2 < part.Indices.Count; t += 3)
            {
                sb.Append(mat).Append('\n');
                for (int k = 0; k < 3; k++)
                {
                    var v = part.Vertices[part.Indices[t + k]];
                    var links = Links(v, map);
                    // SMD texture coordinates have v pointing up; ours (docs/CONTRACT.md) have the origin top-left.
                    sb.Append(CultureInfo.InvariantCulture,
                        $"{links[0].Bone} {F(v.Position.X)} {F(v.Position.Y)} {F(v.Position.Z)} {F(v.Normal.X)} {F(v.Normal.Y)} {F(v.Normal.Z)} {F(v.Uv.X)} {F(1 - v.Uv.Y)} {links.Count}");
                    foreach (var (bone, w) in links) sb.Append(CultureInfo.InvariantCulture, $" {bone} {F(w)}");
                    sb.Append('\n');
                }
            }
        }
        sb.Append("end\n");
        return sb.ToString();
    }

    public static string AnimationSmd(SkinnedModel model, int[] map, AnimClip clip)
    {
        var sb = new StringBuilder("version 1\n");
        Nodes(sb, model, map);
        sb.Append("skeleton\n");
        var frames = clip.Frames.Count > 0 ? clip.Frames : new List<(Vector3, Quaternion)[]> { BindPose(model) };
        for (int f = 0; f < frames.Count; f++) Frame(sb, f, frames[f], map);
        sb.Append("end\n");
        return sb.ToString();
    }

    public static string Qc(string modelName, IReadOnlyList<AnimClip> clips)
    {
        var sb = new StringBuilder();
        sb.Append($"$modelname \"{modelName}.mdl\"\n");
        sb.Append($"$cdmaterials \"models/{modelName}/\"\n");
        sb.Append("$body \"body\" \"ref.smd\"\n");
        sb.Append("$surfaceprop \"flesh\"\n");
        sb.Append("$illumposition 0 0 90\n");
        sb.Append("$mostlyopaque\n");
        if (clips.Count == 0) sb.Append("$sequence \"idle\" \"ref.smd\" loop fps 30\n");
        foreach (var c in clips)
            sb.Append(CultureInfo.InvariantCulture, $"$sequence \"{c.Name}\" \"anim_{c.Name}.smd\"{(c.Loop ? " loop" : "")} fps {F(c.Fps)}\n");
        return sb.ToString();
    }

    public static string Vmt(string modelName, string mat, bool cutout) =>
        "\"VertexLitGeneric\"\n{\n" +
        $"\t\"$basetexture\" \"models/{modelName}/{mat}\"\n\t\"$model\" \"1\"\n" +
        (cutout ? "\t\"$alphatest\" \"1\"\n" : "") + "}\n";
}
