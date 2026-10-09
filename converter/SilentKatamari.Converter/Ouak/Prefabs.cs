// Prefab walking across Addressables bundles: an object's prefab, its meshes and materials may sit in other
// bundles, reached through externals named "archive:/CAB-<hash>/CAB-<hash>". A small cache keeps a few open.
using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace SilentKatamari.Converter.Ouak;

public sealed record AssetRef(string Bundle, int File, long PathId);

public sealed class BundleCache : IDisposable
{
    private readonly string _work;
    private readonly Log _log;
    private readonly Dictionary<string, string> _bundleByCab = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<OpenBundle> _open = new();
    private const int MaxOpen = 4;

    public BundleCache(IEnumerable<string> bundles, string work, Log log)
    {
        _work = work;
        _log = log;
        foreach (var b in bundles)
        {
            try
            {
                var am = new AssetsManager();
                var bun = am.LoadBundleFile(b, unpackIfPacked: false);
                foreach (var name in bun.file.GetAllFileNames()) _bundleByCab.TryAdd(name, b);
                am.UnloadAll(true);
            }
            catch (Exception e) { _log.Warn($"ouak: cannot index {Path.GetFileName(b)}: {e.Message}"); }
        }
    }

    public OpenBundle Get(string bundle)
    {
        var hit = _open.FirstOrDefault(o => o.Path == bundle);
        if (hit is not null)
        {
            _open.Remove(hit);
            _open.AddFirst(hit);
            return hit;
        }
        var b = Bundles.Open(bundle, _work);
        _open.AddFirst(b);
        while (_open.Count > MaxOpen)
        {
            _open.Last!.Value.Dispose();
            _open.RemoveLast();
        }
        return b;
    }

    /// <summary>Follows a PPtr (m_FileID, m_PathID) from a file in a bundle, across bundles when needed.</summary>
    public (OpenBundle B, AssetsFileInstance Inst, AssetTypeValueField Field)? Follow(OpenBundle from, AssetsFileInstance inst, AssetTypeValueField pptr)
    {
        int fileId = pptr["m_FileID"].AsInt;
        long pathId = pptr["m_PathID"].AsLong;
        if (pathId == 0) return null;
        if (fileId == 0) return Read(from, inst, pathId);
        var ext = inst.file.Metadata.Externals;
        if (fileId - 1 >= ext.Count) return null;
        string cab = ext[fileId - 1].PathName;
        cab = cab[(cab.LastIndexOf('/') + 1)..];
        if (!_bundleByCab.TryGetValue(cab, out var bundle)) return null;
        var b = Get(bundle);
        var target = b.Files.FirstOrDefault(f => string.Equals(f.name, cab, StringComparison.OrdinalIgnoreCase)) ?? b.Files.FirstOrDefault();
        return target is null ? null : Read(b, target, pathId);
    }

    public static (OpenBundle, AssetsFileInstance, AssetTypeValueField)? Read(OpenBundle b, AssetsFileInstance inst, long pathId)
    {
        var info = inst.file.GetAssetInfo(pathId);
        if (info is null) return null;
        var f = b.Am.GetBaseField(inst, info);
        return f is null ? null : (b, inst, f);
    }

    public void Dispose()
    {
        foreach (var o in _open) o.Dispose();
        _open.Clear();
    }
}

/// <summary>Builds a StaticMesh from a prefab root GameObject: every MeshFilter in its hierarchy with transforms baked.</summary>
public sealed class PrefabMesher
{
    private readonly BundleCache _cache;
    private readonly Log _log;
    private readonly Dictionary<string, RgbaImage?> _textures = new();

    public PrefabMesher(BundleCache cache, Log log) { _cache = cache; _log = log; }

    public StaticMesh Build(OpenBundle b, AssetsFileInstance inst, AssetTypeValueField gameObject, string name)
    {
        var mesh = new StaticMesh { Name = name };
        Visit(b, inst, gameObject, Matrix4x4.Identity, mesh, 0, isRoot: true);
        return mesh;
    }

    private static Matrix4x4 Local(AssetTypeValueField t)
    {
        var p = t["m_LocalPosition"]; var r = t["m_LocalRotation"]; var s = t["m_LocalScale"];
        var pos = new Vector3(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat);
        var rot = new Quaternion(r["x"].AsFloat, r["y"].AsFloat, r["z"].AsFloat, r["w"].AsFloat);
        var scl = new Vector3(s["x"].AsFloat, s["y"].AsFloat, s["z"].AsFloat);
        return Matrix4x4.CreateScale(scl) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rot)) * Matrix4x4.CreateTranslation(pos);
    }

    private void Visit(OpenBundle b, AssetsFileInstance inst, AssetTypeValueField go, Matrix4x4 parent, StaticMesh mesh, int depth, bool isRoot)
    {
        if (depth > 12 || go["m_IsActive"] is { IsDummy: false } active && !active.AsBool) return;
        AssetTypeValueField? transform = null, filter = null, renderer = null;
        (OpenBundle, AssetsFileInstance)? rendererAt = null, filterAt = null;
        foreach (var c in go["m_Component"]["Array"].Children)
        {
            var comp = _cache.Follow(b, inst, c["component"]);
            if (comp is not { } x) continue;
            switch (x.Field.TypeName)
            {
                case "Transform" or "RectTransform": transform = x.Field; break;
                case "MeshFilter": filter = x.Field; filterAt = (x.B, x.Inst); break;
                case "MeshRenderer": renderer = x.Field; rendererAt = (x.B, x.Inst); break;
            }
        }
        // The root's own placement doesn't matter: the object is re-centred on its bottom face afterwards.
        var world = transform is null || isRoot ? parent : Local(transform) * parent;
        if (filter is not null && filterAt is { } fa && _cache.Follow(fa.Item1, fa.Item2, filter["m_Mesh"]) is { } m)
            AddMesh(m.B, m.Inst, m.Field, renderer, rendererAt, world, mesh);
        if (transform is null) return;
        foreach (var child in transform["m_Children"]["Array"].Children)
            if (_cache.Follow(b, inst, child) is { } ct && _cache.Follow(ct.B, ct.Inst, ct.Field["m_GameObject"]) is { } cgo)
                Visit(cgo.B, cgo.Inst, cgo.Field, world, mesh, depth + 1, isRoot: false);
    }

    private void AddMesh(OpenBundle b, AssetsFileInstance inst, AssetTypeValueField m, AssetTypeValueField? renderer,
        (OpenBundle, AssetsFileInstance)? rendererAt, Matrix4x4 world, StaticMesh mesh)
    {
        if (m["m_MeshCompression"] is { IsDummy: false } comp && comp.AsInt != 0)
        {
            _log.Warn($"ouak: {m["m_Name"].AsString}: compressed mesh not supported");
            return;
        }
        var vd = m["m_VertexData"];
        int count = (int)vd["m_VertexCount"].AsUInt;
        var channels = vd["m_Channels"]["Array"].Children
            .Select(c => new VertexChannel(c["stream"].AsInt, c["offset"].AsInt, c["format"].AsInt, c["dimension"].AsInt)).ToList();
        byte[] data = vd["m_DataSize"].AsByteArray ?? Array.Empty<byte>();
        if (data.Length == 0 && m["m_StreamData"] is { IsDummy: false } sd)
            data = b.StreamData(sd["path"].AsString, (long)sd["offset"].AsULong, sd["size"].AsUInt) ?? Array.Empty<byte>();
        if (count == 0 || data.Length == 0) return;
        var pos = UnityMesh.ReadChannel(data, channels, count, 0);
        var nrm = UnityMesh.ReadChannel(data, channels, count, 1);
        var uv = UnityMesh.ReadChannel(data, channels, count, 4);
        var ib = m["m_IndexBuffer"].AsByteArray;
        bool wide = m["m_IndexFormat"] is { IsDummy: false } fmt && fmt.AsInt == 1;
        var materials = renderer?["m_Materials"]["Array"].Children ?? new List<AssetTypeValueField>();
        int sub = 0;
        foreach (var s in m["m_SubMeshes"]["Array"].Children)
        {
            if (s["topology"].AsInt != 0) { sub++; continue; } // triangles only
            int first = (int)s["firstByte"].AsUInt / (wide ? 4 : 2), n = (int)s["indexCount"].AsUInt, baseVertex = (int)s["baseVertex"].AsUInt;
            var tris = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                int at = (first + i) * (wide ? 4 : 2);
                if (at + (wide ? 4 : 2) > ib.Length) break;
                tris.Add((wide ? (int)BitConverter.ToUInt32(ib, at) : BitConverter.ToUInt16(ib, at)) + baseVertex);
            }
            string matName = $"m{mesh.Materials.Count}";
            var info = new MaterialInfo { Name = matName };
            if (sub < materials.Count && rendererAt is { } ra && _cache.Follow(ra.Item1, ra.Item2, materials[sub]) is { } mat)
            {
                info.Name = matName = mat.Field["m_Name"].AsString is { Length: > 0 } nm ? nm : matName;
                (info.Texture, info.Color) = MaterialLook(mat.B, mat.Inst, mat.Field);
            }
            if (mesh.Materials.All(x => x.Name != info.Name)) mesh.Materials.Add(info);
            mesh.Parts.Add(UnityMesh.Part(info.Name, pos, nrm, uv, tris.Where(t => t < count).ToList(), world));
            sub++;
        }
    }

    private static readonly string[] TexNames = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_MainTexture", "_Diffuse" };
    private static readonly string[] ColorNames = { "_BaseColor", "_Color", "_MainColor" };

    private (RgbaImage?, Vector4) MaterialLook(OpenBundle b, AssetsFileInstance inst, AssetTypeValueField mat)
    {
        RgbaImage? tex = null;
        var color = Vector4.One;
        var props = mat["m_SavedProperties"];
        foreach (var want in TexNames)
        {
            var env = props["m_TexEnvs"]["Array"].Children.FirstOrDefault(e => e["first"].AsString == want);
            if (env is null) continue;
            var ptr = env["second"]["m_Texture"];
            string key = $"{inst.name}:{ptr["m_FileID"].AsInt}:{ptr["m_PathID"].AsLong}";
            if (!_textures.TryGetValue(key, out tex))
            {
                tex = _cache.Follow(b, inst, ptr) is { } t ? Texture(t.B, t.Field) : null;
                _textures[key] = tex;
            }
            if (tex is not null) break;
        }
        foreach (var want in ColorNames)
        {
            var c = props["m_Colors"]["Array"].Children.FirstOrDefault(e => e["first"].AsString == want);
            if (c is null) continue;
            var v = c["second"];
            color = new Vector4(v["r"].AsFloat, v["g"].AsFloat, v["b"].AsFloat, v["a"].AsFloat);
            break;
        }
        return (tex, color);
    }

    private RgbaImage? Texture(OpenBundle b, AssetTypeValueField t)
    {
        if (t.TypeName != "Texture2D") return null;
        int w = t["m_Width"].AsInt, h = t["m_Height"].AsInt, fmt = t["m_TextureFormat"].AsInt;
        byte[] data = t["image data"].AsByteArray ?? Array.Empty<byte>();
        if (data.Length == 0 && t["m_StreamData"] is { IsDummy: false } sd)
            data = b.StreamData(sd["path"].AsString, (long)sd["offset"].AsULong, sd["size"].AsUInt) ?? Array.Empty<byte>();
        if (data.Length == 0) return null;
        try
        {
            var img = UnityMesh.DecodeTexture(fmt, w, h, data);
            if (img is null) _log.Warn($"ouak: texture {t["m_Name"].AsString}: format {fmt} not decoded");
            return img?.Resized(1024);
        }
        catch (Exception e)
        {
            _log.Warn($"ouak: texture {t["m_Name"].AsString}: {e.Message}");
            return null;
        }
    }
}
