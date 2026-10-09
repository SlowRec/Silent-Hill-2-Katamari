// A street ground texture and Wwise sounds from the player's SILENT HILL 2.
using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using Newtonsoft.Json;

namespace SilentKatamari.Converter.Sh2;

public static class Sh2Assets
{
    /// <summary>Asphalt or road base colour, environment folders first.</summary>
    public static string? Ground(DefaultFileProvider p, IContentWriter w, ConvertContext ctx)
    {
        var keys = p.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                                           && Regex.IsMatch(Path.GetFileName(k), "(asphalt|road)", RegexOptions.IgnoreCase))
            .OrderByDescending(k => Regex.IsMatch(k, "/env", RegexOptions.IgnoreCase))
            .ThenByDescending(k => Regex.IsMatch(Path.GetFileNameWithoutExtension(k), "(_bc|_d|_basecolor|_albedo|_diffuse|_c)$", RegexOptions.IgnoreCase))
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        ctx.Log.Info("sh2: ground candidates: " + string.Join(", ", keys.Take(10).Select(Path.GetFileNameWithoutExtension)));
        foreach (var k in keys)
        {
            if (!p.TryLoadPackageObject(k[..k.LastIndexOf('.')], out UTexture2D tex)) continue;
            var img = Ue.Decode(tex);
            w.WritePng("materials/silent_katamari/sh2/ground.png", img, 1024);
            return Path.GetFileNameWithoutExtension(k);
        }
        throw new InvalidOperationException("no street texture found");
    }
}

/// <summary>Finds Wwise media by name: AkMediaAsset names, AkAudioEvent names (their cooked media ids), then loose
/// .wem paths. Each candidate's bytes go through vgmstream to a WAV.</summary>
public sealed class Sh2Audio
{
    private readonly DefaultFileProvider _p;
    private readonly IContentWriter _w;
    private readonly ConvertContext _ctx;
    private readonly List<string> _keys;
    private Dictionary<string, string>? _wemById;

    public Sh2Audio(DefaultFileProvider p, IContentWriter w, ConvertContext ctx)
    {
        _p = p; _w = w; _ctx = ctx;
        _keys = p.Files.Keys.ToList();
    }

    private Dictionary<string, string> WemById => _wemById ??= _keys
        .Where(k => k.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
        .GroupBy(k => Regex.Match(Path.GetFileNameWithoutExtension(k), @"\d+").Value)
        .Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.First());

    public string? Extract(string rowId, string must, string? also, bool preferLong)
    {
        var row = Extract_(rowId);
        bool Hit(string name) => Regex.IsMatch(name, must, RegexOptions.IgnoreCase) && (also is null || Regex.IsMatch(name, also, RegexOptions.IgnoreCase));
        var found = new List<(string From, byte[] Wem)>();

        // 1. AkMediaAsset packages (Wwise event-based packaging): readable names, media in the .ubulk.
        foreach (var k in _keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && Hit(Path.GetFileName(k))).Take(60))
        {
            string noExt = k[..k.LastIndexOf('.')];
            if (_p.TryLoadPackageObject(noExt, out UAkMediaAsset _) && WemIn(noExt + ".ubulk") is { } wem) found.Add((noExt, wem));
            else if (_p.TryLoadPackageObject(noExt, out UAkAudioEvent ev))
                foreach (var id in MediaIds(ev))
                    if (WemById.TryGetValue(id, out var wk) && _p.Files.TryGetValue(wk, out var gf) && gf.TryRead(out var bytes, null)) found.Add(($"{noExt} -> {id}", bytes));
            if (found.Count >= 12) break;
        }
        // 2. Loose media whose path names it.
        if (found.Count == 0)
            foreach (var k in _keys.Where(k => k.EndsWith(".wem", StringComparison.OrdinalIgnoreCase) && Hit(k)).Take(12))
                if (_p.Files.TryGetValue(k, out var gf) && gf.TryRead(out var bytes, null)) found.Add((k, bytes));

        _ctx.Log.Info($"sh2: {rowId} candidates: " + string.Join("; ", found.Select(f => $"{Path.GetFileName(f.From)} ({f.Wem.Length / 1024} KB)")));
        if (found.Count == 0) throw new InvalidOperationException("no matching sound found");
        var pick = preferLong ? found.OrderByDescending(f => f.Wem.Length).First() : found.First();
        string tmp = Path.Combine(_ctx.WorkFolder, rowId + ".wem");
        Directory.CreateDirectory(_ctx.WorkFolder);
        File.WriteAllBytes(tmp, pick.Wem);
        _w.WriteWav(row.Output, tmp);
        return Path.GetFileName(pick.From);
    }

    private static ExtractRow Extract_(string id) => SilentKatamari.Converter.Extract.ById(id) ?? throw new ArgumentException(id);

    private byte[]? WemIn(string key)
    {
        if (!_p.Files.TryGetValue(key, out var gf) || !gf.TryRead(out var data, null)) return null;
        int at = IndexOf(data, "RIFF"u8);
        return at < 0 ? null : data[at..];
    }

    /// <summary>Media ids cooked into an AkAudioEvent, read from its serialised form so the exact struct shape of
    /// this Wwise integration version doesn't matter.</summary>
    internal static IEnumerable<string> MediaIds(object ev)
    {
        string json;
        try { json = JsonConvert.SerializeObject(ev); } catch (Exception) { yield break; }
        foreach (Match m in Regex.Matches(json, "\"(?:MediaId|ShortId)\"\\s*:\\s*(\\d+)")) yield return m.Groups[1].Value;
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern) => data.AsSpan().IndexOf(pattern);
}
