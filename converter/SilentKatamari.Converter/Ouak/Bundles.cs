// Walking Once Upon A KATAMARI's Addressables bundles with AssetsTools.NET (MIT): one bundle open at a time,
// big compressed bundles unpacked to a temp file rather than memory.
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace SilentKatamari.Converter.Ouak;

public sealed class OpenBundle : IDisposable
{
    public required AssetsManager Am { get; init; }
    public required BundleFileInstance Bundle { get; init; }
    public required List<AssetsFileInstance> Files { get; init; }
    public required string Path { get; init; }
    public Stream? Temp { get; init; }
    public string? TempPath { get; init; }

    public void Dispose()
    {
        Am.UnloadAll(true);
        Temp?.Dispose();
        if (TempPath is not null) try { File.Delete(TempPath); } catch (IOException) { }
    }

    /// <summary>Bytes of a .resS/.resource stream inside this bundle (m_StreamData.path "archive:/CAB-.../CAB-....resS").</summary>
    public byte[]? StreamData(string path, long offset, long size)
    {
        if (size <= 0) return null;
        string name = path[(path.LastIndexOf('/') + 1)..];
        int idx = Bundle.file.GetFileIndex(name);
        if (idx < 0) return null;
        Bundle.file.GetFileRange(idx, out long start, out long len);
        if (offset + size > len) return null;
        var r = Bundle.file.DataReader;
        r.Position = start + offset;
        return r.ReadBytes((int)size);
    }
}

public static class Bundles
{
    private static readonly Regex HashSuffix = new(@"_[0-9a-f]{32}$", RegexOptions.IgnoreCase);

    public static string Folder(GameInstall g) => Path.Combine(g.Folder, "OnceUponaKATAMARI_Data", "StreamingAssets", "aa");

    public static List<string> All(GameInstall g)
    {
        string aa = Folder(g);
        string root = Directory.Exists(aa) ? aa : Path.Combine(g.Folder, "OnceUponaKATAMARI_Data");
        if (!Directory.Exists(root)) return new();
        return Directory.EnumerateFiles(root, "*.bundle", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Bundle name without its per-build hash: "prefab-model-props_assets_all_3f..ab.bundle" -> "prefab-model-props_assets_all".</summary>
    public static string Prefix(string path) => HashSuffix.Replace(System.IO.Path.GetFileNameWithoutExtension(path), "");

    public static OpenBundle Open(string path, string workFolder)
    {
        var am = new AssetsManager();
        BundleFileInstance bun;
        Stream? temp = null;
        string? tempPath = null;
        long size = new FileInfo(path).Length;
        var probe = am.LoadBundleFile(path, unpackIfPacked: false);
        if (probe.file.DataIsCompressed && size > 150L * 1024 * 1024)
        {
            // Unpacking a large bundle in memory can take many times its size; a temp file keeps RAM flat.
            Directory.CreateDirectory(workFolder);
            tempPath = System.IO.Path.Combine(workFolder, Guid.NewGuid().ToString("N") + ".unpacked");
            temp = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            probe.file.Unpack(new AssetsFileWriter(temp));
            temp.Position = 0;
            am.UnloadAll(true);
            am = new AssetsManager();
            bun = am.LoadBundleFile(temp, path, unpackIfPacked: false);
        }
        else
        {
            am.UnloadAll(true);
            am = new AssetsManager();
            bun = am.LoadBundleFile(path, unpackIfPacked: true);
        }
        var files = new List<AssetsFileInstance>();
        for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            if (bun.file.IsAssetsFile(i)) files.Add(am.LoadAssetsFileFromBundle(bun, i, loadDeps: false));
        return new OpenBundle { Am = am, Bundle = bun, Files = files, Path = path, Temp = temp, TempPath = tempPath };
    }

    /// <summary>Field by path ("m_VertexData/m_Channels"), or null when any step is missing.</summary>
    public static AssetTypeValueField? F(this AssetTypeValueField f, string path)
    {
        var cur = f;
        foreach (var part in path.Split('/'))
        {
            cur = cur[part];
            if (cur is null || cur.IsDummy) return null;
        }
        return cur;
    }

    /// <summary>Every field in a tree, depth first (bounded), with its path.</summary>
    public static IEnumerable<(string Path, AssetTypeValueField Field)> Walk(AssetTypeValueField root, int maxDepth = 6, string path = "")
    {
        if (maxDepth < 0 || root.Children is null) yield break;
        foreach (var c in root.Children)
        {
            string p = path.Length == 0 ? c.FieldName : path + "/" + c.FieldName;
            yield return (p, c);
            foreach (var x in Walk(c, maxDepth - 1, p)) yield return x;
        }
    }

    public static string ScriptClass(OpenBundle b, AssetsFileInstance inst, AssetTypeValueField mono)
    {
        try
        {
            var script = mono["m_Script"];
            if (script is null || script.IsDummy) return "";
            var ext = b.Am.GetExtAsset(inst, script);
            return ext.baseField?["m_ClassName"]?.AsString ?? "";
        }
        catch (Exception) { return ""; }
    }
}
