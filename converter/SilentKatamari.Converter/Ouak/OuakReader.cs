// Once Upon A KATAMARI (Unity 6, IL2CPP, Addressables) reader. Read-only: opens the player's bundles with
// AssetsTools.NET, never launches the game or touches its saves or its online mode.
// Objects: the game's own object table (MonoInfo-like records: catch size, bounding box, volume rate) when it can
// be read and linked to prefabs; otherwise sizes come from each object's own mesh, flagged own_pickup = false.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace SilentKatamari.Converter.Ouak;

public sealed record TableRecord(long Id, string Name, double CatchCm, double VolumeCm3, double Rate);

public sealed record PrefabInfo(AssetRef Ref, string Name, Vector3 SizeCm)
{
    public long? IdHint => Regex.Match(Name, @"(\d{3,6})") is { Success: true } m ? long.Parse(m.Value, CultureInfo.InvariantCulture) : null;
}

public sealed class OuakReader : IGameReader
{
    public string GameId => "ouak";
    private static readonly Regex TableBundles = new("(scriptable|table|data|collection|mono|param|master)", RegexOptions.IgnoreCase);
    private static readonly Regex PropBundles = new("(prop|mono|object|model)", RegexOptions.IgnoreCase);
    private static readonly Regex NotProps = new("(ouji|itoko|king|cousin|prince|ui|font|effect|fx|sky|stage|map|hiroba|motion|anim|sound)", RegexOptions.IgnoreCase);
    private static readonly string[] AudioExt = { ".acb", ".awb", ".acf", ".bank", ".fsb", ".bnk", ".wem", ".ogg", ".wav" };

    public JsonObject Probe(GameInstall game, ConvertContext ctx)
    {
        var o = new JsonObject();
        string data = Path.Combine(game.Folder, "OnceUponaKATAMARI_Data");
        o["data_folder"] = Directory.Exists(data);
        string aa = Bundles.Folder(game);
        o["aa_folder"] = Directory.Exists(aa);
        if (Directory.Exists(aa))
            o["catalogs"] = new JsonArray(Directory.EnumerateFiles(aa).Select(Path.GetFileName).Where(n => n!.StartsWith("catalog", StringComparison.OrdinalIgnoreCase)).Select(n => (JsonNode)n!).ToArray());
        var bundles = Bundles.All(game);
        o["bundles"] = bundles.Count;
        o["bundles_mb"] = Math.Round(bundles.Sum(b => new FileInfo(b).Length) / 1048576.0, 1);
        o["bundle_groups"] = new JsonObject(bundles.GroupBy(Bundles.Prefix).OrderByDescending(g => g.Count()).Take(80)
            .Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.Count())));
        if (Directory.Exists(data))
        {
            var audio = Directory.EnumerateFiles(data, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => AudioExt.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            o["audio_files"] = new JsonObject(audio.GroupBy(f => Path.GetExtension(f).ToLowerInvariant()).Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.Count())));
            o["audio_sample"] = new JsonArray(audio.Take(25).Select(f => (JsonNode)Runner.Slashes(Path.GetRelativePath(game.Folder, f))).ToArray());
        }
        if (bundles.Count == 0) return o;

        var sw = Stopwatch.StartNew();
        try
        {
            using var first = Bundles.Open(bundles.OrderBy(b => new FileInfo(b).Length).First(), ctx.WorkFolder);
            var meta = first.Files.FirstOrDefault()?.file.Metadata;
            o["unity_version"] = meta?.UnityVersion;
            o["type_trees"] = meta?.TypeTreeEnabled;
            o["compression"] = first.Bundle.file.GetCompressionType().ToString();
        }
        catch (Exception e) { o["open_error"] = ConvertContext.Short(e.Message); }

        // Script classes of MonoBehaviours in table-like bundles, and the fields of anything that looks like the object table.
        var classes = new Dictionary<string, int>();
        var tableFields = new JsonObject();
        foreach (var path in bundles.Where(b => TableBundles.IsMatch(Bundles.Prefix(b))).Take(40))
        {
            if (sw.Elapsed.TotalSeconds > 60) { o["probe_truncated"] = true; break; }
            try
            {
                using var b = Bundles.Open(path, ctx.WorkFolder);
                foreach (var inst in b.Files)
                foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour).Take(400))
                {
                    var f = b.Am.GetBaseField(inst, info);
                    string cls = Bundles.ScriptClass(b, inst, f);
                    classes[cls] = classes.GetValueOrDefault(cls) + 1;
                    if (tableFields.Count < 6 && Regex.IsMatch(cls, "(MonoInfo|Collection|Mono.*(Table|Data|Param))", RegexOptions.IgnoreCase) && !tableFields.ContainsKey(cls))
                        tableFields[cls] = new JsonArray(Bundles.Walk(f, 5).Take(60).Select(x => (JsonNode)$"{x.Path}:{x.Field.TypeName}").ToArray());
                }
            }
            catch (Exception e) { ctx.Log.Warn($"ouak probe {Path.GetFileName(path)}: {e.Message}"); }
        }
        o["script_classes"] = new JsonObject(classes.OrderByDescending(c => c.Value).Take(60).Select(c => new KeyValuePair<string, JsonNode?>(c.Key, c.Value)));
        o["table_fields"] = tableFields;
        o["probe_seconds"] = Math.Round(sw.Elapsed.TotalSeconds, 1);
        return o;
    }

    public void Convert(GameInstall game, IContentWriter writer, ConvertContext ctx)
    {
        var bundles = Bundles.All(game);
        if (bundles.Count == 0) throw new InvalidOperationException("Once Upon A KATAMARI's content files were not found in its folder");
        using var cache = new BundleCache(bundles, ctx.WorkFolder, ctx.Log);

        var table = new List<TableRecord>();
        ctx.Run("ouak_rules", () => Rules(bundles, ctx, writer, table));
        var prefabs = new List<PrefabInfo>();
        ctx.Run("ouak_objects", () => Objects(bundles, cache, ctx, writer, table, prefabs));
        ctx.Run("ouak_core", () => Core(cache, ctx, writer, prefabs));
        ctx.Run("ouak_hud", () => throw new NotSupportedException("the size display isn't read in this version"));
        ctx.Run("ouak_font", () => Font(bundles, ctx, writer));
        Sounds(game, writer, ctx);
    }

    // ---------------- object table ----------------

    private static readonly Regex IdField = new("^(id|monoid|mono_id|u32id|s32id|syncmonoid|.*_id)$", RegexOptions.IgnoreCase);
    private static readonly Regex CatchField = new("catch", RegexOptions.IgnoreCase);
    private static readonly Regex RateField = new("(volumerate|volume_rate|rate)", RegexOptions.IgnoreCase);
    private static readonly Regex VolumeField = new("^(f?volume|floatvolume)$", RegexOptions.IgnoreCase);
    private static readonly Regex NameField = new("(name|label)", RegexOptions.IgnoreCase);

    /// <summary>Every array element (a struct) with a catch-size field, from MonoBehaviours in table-like bundles.</summary>
    internal static List<TableRecord> FindRecords(AssetTypeValueField root)
    {
        var list = new List<TableRecord>();
        foreach (var (_, f) in Bundles.Walk(root, 4))
        {
            if (f.FieldName != "Array" || f.Children is null || f.Children.Count == 0) continue;
            foreach (var el in f.Children)
            {
                var flat = Bundles.Walk(el, 3).ToList();
                var catchF = flat.FirstOrDefault(x => CatchField.IsMatch(x.Field.FieldName) && IsNumber(x.Field));
                if (catchF.Field is null) break; // not this array
                var idF = flat.FirstOrDefault(x => IdField.IsMatch(x.Field.FieldName) && IsNumber(x.Field));
                var rateF = flat.FirstOrDefault(x => RateField.IsMatch(x.Field.FieldName) && IsNumber(x.Field));
                var volF = flat.FirstOrDefault(x => VolumeField.IsMatch(x.Field.FieldName) && IsNumber(x.Field));
                var nameF = flat.FirstOrDefault(x => NameField.IsMatch(x.Field.FieldName) && x.Field.TypeName == "string");
                var min = flat.FirstOrDefault(x => Regex.IsMatch(x.Path, "(box)?min$", RegexOptions.IgnoreCase) && x.Field.Children?.Count >= 3);
                var max = flat.FirstOrDefault(x => Regex.IsMatch(x.Path, "(box)?max$", RegexOptions.IgnoreCase) && x.Field.Children?.Count >= 3);
                double catchRaw = Num(catchF.Field);
                // REROLL stored the catch size in mm (u32CatchSize) and the box in cm x 10; assume the same family here.
                double catchCm = catchF.Field.FieldName.StartsWith("u32", StringComparison.OrdinalIgnoreCase) || catchRaw > 100 ? catchRaw / 10 : catchRaw;
                double vol = volF.Field is not null ? Num(volF.Field) * 1e6 : 0; // m^3 -> cm^3
                if (vol <= 0 && min.Field is not null && max.Field is not null)
                {
                    double V(AssetTypeValueField v, int i) => Num(v.Children[i]);
                    double dx = Math.Abs(V(max.Field, 0) - V(min.Field, 0)), dy = Math.Abs(V(max.Field, 1) - V(min.Field, 1)), dz = Math.Abs(V(max.Field, 2) - V(min.Field, 2));
                    vol = dx * dy * dz / 1000.0 * 0.5; // mm^3 -> cm^3, half the box
                }
                list.Add(new TableRecord(idF.Field is not null ? (long)Num(idF.Field) : list.Count, nameF.Field?.AsString ?? "",
                    catchCm, vol, rateF.Field is not null ? Num(rateF.Field) : 0));
            }
        }
        return list;
    }

    private static bool IsNumber(AssetTypeValueField f) => f.Value is not null && f.TypeName is "int" or "unsigned int" or "SInt32" or "UInt32" or "float" or "double" or "SInt16" or "UInt16" or "SInt64" or "UInt64" or "short" or "unsigned short";
    private static double Num(AssetTypeValueField f) => f.TypeName is "float" ? f.AsFloat : f.TypeName is "double" ? f.AsDouble : f.AsLong;

    private static string? Rules(List<string> bundles, ConvertContext ctx, IContentWriter writer, List<TableRecord> table)
    {
        var sw = Stopwatch.StartNew();
        foreach (var path in bundles.Where(b => TableBundles.IsMatch(Bundles.Prefix(b))))
        {
            if (sw.Elapsed.TotalMinutes > 2) break;
            try
            {
                using var b = Bundles.Open(path, ctx.WorkFolder);
                foreach (var inst in b.Files)
                foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var recs = FindRecords(b.Am.GetBaseField(inst, info));
                    if (recs.Count > table.Count) { table.Clear(); table.AddRange(recs); }
                }
            }
            catch (Exception e) { ctx.Log.Warn($"ouak rules {Path.GetFileName(path)}: {e.Message}"); }
        }
        if (table.Count < 20) throw new InvalidOperationException("the object table wasn't found in Once Upon A KATAMARI's files");
        var rules = new JsonObject { ["objects_read"] = table.Count, ["source"] = "object table medians" };
        var rates = table.Select(t => t.Rate).Where(r => r > 0 && r <= 2).Order().ToList();
        if (rates.Count > 0) rules["volume_rate"] = Math.Round(rates[rates.Count / 2], 4);
        var ratios = table.Where(t => t.CatchCm > 0 && t.VolumeCm3 > 0).Select(t => Math.Pow(6 * t.VolumeCm3 / Math.PI, 1 / 3.0) / t.CatchCm).Order().ToList();
        if (ratios.Count > 20) rules["catch_ratio"] = Math.Round(ratios[ratios.Count / 2], 4);
        writer.WriteJson("data_static/silent_katamari/rules.json", rules);
        return $"{table.Count} objects in the table";
    }

    // ---------------- objects ----------------

    private static IEnumerable<(OpenBundle B, AssetsFileInstance Inst, AssetFileInfo Info, AssetTypeValueField Go)> PrefabRoots(OpenBundle b)
    {
        foreach (var inst in b.Files)
        foreach (var tinfo in inst.file.GetAssetsOfType(AssetClassID.Transform))
        {
            var t = b.Am.GetBaseField(inst, tinfo);
            if (t["m_Father"]["m_PathID"].AsLong != 0) continue;
            var go = BundleCache.Read(b, inst, t["m_GameObject"]["m_PathID"].AsLong);
            if (go is { } g) yield return (b, inst, tinfo, g.Item3);
        }
    }

    private static string? Objects(List<string> bundles, BundleCache cache, ConvertContext ctx, IContentWriter writer, List<TableRecord> table, List<PrefabInfo> prefabs)
    {
        var mesher = new PrefabMesher(cache, ctx.Log);
        var sw = Stopwatch.StartNew();
        // 1. Prefab roots in prop-like bundles, with a quick size from their meshes' bounds.
        foreach (var path in bundles.Where(b => PropBundles.IsMatch(Bundles.Prefix(b)) && !NotProps.IsMatch(Bundles.Prefix(b))))
        {
            if (sw.Elapsed.TotalMinutes > 3 || prefabs.Count > 6000) break;
            try
            {
                var b = cache.Get(path);
                foreach (var (ob, inst, info, go) in PrefabRoots(b))
                {
                    string name = go["m_Name"].AsString;
                    var mesh = mesher.Build(ob, inst, go, name);
                    if (mesh.Parts.Count == 0) continue;
                    var (min, max) = mesh.Bounds();
                    prefabs.Add(new PrefabInfo(new AssetRef(path, ob.Files.IndexOf(inst), info.PathId), name, max - min));
                }
            }
            catch (Exception e) { ctx.Log.Warn($"ouak objects {Path.GetFileName(path)}: {e.Message}"); }
        }
        ctx.Log.Info($"ouak: {prefabs.Count} object prefabs with meshes, {table.Count} table records");
        if (prefabs.Count == 0) throw new InvalidOperationException("no object models were found in Once Upon A KATAMARI's files");

        // 2. Sizes: the game's own record when its id is in the prefab name; else from the mesh.
        var byId = table.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        const double catchRatio = 0.464; // rules.catch_ratio: Katamari needs 10x the object's volume (REROLL datamine)
        var candidates = new List<(PrefabInfo P, double Pickup, double Volume, double Rate, bool Own, string Name)>();
        foreach (var p in prefabs)
        {
            var s = p.SizeCm;
            if (s.X <= 0.1 || s.Y <= 0.1 || s.Z <= 0.1 || Math.Max(s.X, Math.Max(s.Y, s.Z)) > 5000) continue;
            if (p.IdHint is long id && byId.TryGetValue(id, out var rec) && rec.CatchCm > 0)
            {
                double vol = rec.VolumeCm3 > 0 ? rec.VolumeCm3 : s.X * s.Y * s.Z * 0.5;
                candidates.Add((p, rec.CatchCm, vol, rec.Rate > 0 ? rec.Rate : 0.8, true, rec.Name.Length > 0 ? rec.Name : p.Name));
            }
            else
            {
                double vol = s.X * s.Y * s.Z * 0.35; // fill factor of a typical prop in its box (tuned, rules research note)
                double pickup = Math.Floor(Math.Pow(6 * vol / Math.PI, 1 / 3.0) / catchRatio * 10) / 10;
                candidates.Add((p, pickup, vol, 0.8, false, p.Name));
            }
        }

        // 3. Each tier takes its 'kinds' objects: preferred words first, then stable by name.
        var objects = new JsonArray();
        var used = new HashSet<string>();
        string[] words = { "yokai", "youkai", "ghost", "lantern", "chochin", "grave", "haka", "haniwa", "daruma", "jizo", "torii", "kappa", "oni", "skull", "candle", "doll", "statue", "edo", "shrine", "umbrella", "mask" };
        foreach (var tier in Tiers.All)
        {
            var pick = candidates.Where(c => c.Pickup >= tier.PickupMinCm && c.Pickup < tier.PickupMaxCm && !used.Contains(c.P.Name))
                .OrderByDescending(c => words.Count(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || c.P.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(c => c.Own).ThenBy(c => c.P.Name, StringComparer.Ordinal).Take(tier.Kinds).ToList();
            if (pick.Count < tier.Kinds) ctx.Log.Warn($"ouak: tier {tier.Id} has only {pick.Count} of {tier.Kinds} kinds");
            foreach (var c in pick)
            {
                used.Add(c.P.Name);
                var b = cache.Get(c.P.Ref.Bundle);
                var inst = b.Files[c.P.Ref.File];
                var tf = BundleCache.Read(b, inst, c.P.Ref.PathId);
                if (tf is not { } t) continue;
                var goF = BundleCache.Read(b, inst, t.Item3["m_GameObject"]["m_PathID"].AsLong);
                if (goF is not { } g) continue;
                var mesh = mesher.Build(b, inst, g.Item3, c.P.Name);
                var (min, max) = UnityMesh.PivotToBottomCentre(mesh);
                var pts = new List<Vector3>();
                foreach (var part in mesh.Parts) for (int i = 0; i + 2 < part.V.Count; i += 8) pts.Add(new Vector3(part.V[i], part.V[i + 1], part.V[i + 2]));
                var hull = UnityMesh.HullPoints(pts);
                string id = "o" + Regex.Replace(c.P.IdHint?.ToString(CultureInfo.InvariantCulture) ?? ContentWriter.SafeName(c.P.Name), "[^a-z0-9_]", "");
                for (int k = 2; objects.Any(x => x!["id"]!.GetValue<string>() == id); k++) id = $"{id}_{k}";
                writer.WriteMeshJson($"data_static/silent_katamari/meshes/{id}.json", mesh, $"silent_katamari/ouak/{id}_");
                var size = max - min;
                objects.Add(new JsonObject
                {
                    ["id"] = id, ["name"] = c.Name, ["tier"] = tier.Id,
                    ["pickup_cm"] = Math.Round(c.Pickup, 1), ["volume_cm3"] = Math.Round(c.Volume, 3), ["rate"] = c.Rate, ["own_pickup"] = c.Own,
                    ["size"] = new JsonArray(Math.Round(size.X, 2), Math.Round(size.Y, 2), Math.Round(size.Z, 2)),
                    ["mesh"] = $"meshes/{id}.json",
                    ["hull"] = new JsonArray(hull.SelectMany(h => new[] { Math.Round(h.X, 3), Math.Round(h.Y, 3), Math.Round(h.Z, 3) }).Select(x => (JsonNode)x).ToArray()),
                });
            }
        }
        if (objects.Count == 0) throw new InvalidOperationException("no Once Upon A KATAMARI objects fit the stage's sizes");
        writer.WriteJson("data_static/silent_katamari/objects.json", new JsonObject { ["objects"] = objects });
        int own = objects.Count(o => o!["own_pickup"]!.GetValue<bool>());
        return $"{objects.Count} objects ({own} with the game's own catch size)";
    }

    private static string? Core(BundleCache cache, ConvertContext ctx, IContentWriter writer, List<PrefabInfo> prefabs)
    {
        var core = prefabs.Where(p => Regex.IsMatch(p.Name, "(core|katamari)", RegexOptions.IgnoreCase))
            .OrderBy(p => Math.Abs(p.SizeCm.X - p.SizeCm.Z)).FirstOrDefault()
            ?? throw new InvalidOperationException("the katamari's core wasn't found");
        var b = cache.Get(core.Ref.Bundle);
        var inst = b.Files[core.Ref.File];
        var t = BundleCache.Read(b, inst, core.Ref.PathId) ?? throw new InvalidOperationException("core unreadable");
        var g = BundleCache.Read(b, inst, t.Item3["m_GameObject"]["m_PathID"].AsLong) ?? throw new InvalidOperationException("core unreadable");
        var mesh = new PrefabMesher(cache, ctx.Log).Build(b, inst, g.Item3, core.Name);
        var (min, max) = mesh.Bounds();
        var c = (min + max) / 2;
        float r = Math.Max(1e-3f, Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z)) / 2);
        foreach (var p in mesh.Parts)
            for (int i = 0; i + 2 < p.V.Count; i += 8) { p.V[i] = (p.V[i] - c.X) / r; p.V[i + 1] = (p.V[i + 1] - c.Y) / r; p.V[i + 2] = (p.V[i + 2] - c.Z) / r; }
        writer.WriteMeshJson("data_static/silent_katamari/core.json", mesh, "silent_katamari/ouak/core_");
        return core.Name;
    }

    // ---------------- font ----------------

    private static string? Font(List<string> bundles, ConvertContext ctx, IContentWriter writer)
    {
        foreach (var path in bundles.OrderByDescending(b => Bundles.Prefix(b).Contains("font", StringComparison.OrdinalIgnoreCase)).Take(60))
        {
            try
            {
                using var b = Bundles.Open(path, ctx.WorkFolder);
                foreach (var inst in b.Files)
                foreach (var info in inst.file.GetAssetsOfType(AssetClassID.Font))
                {
                    var f = b.Am.GetBaseField(inst, info);
                    var bytes = f["m_FontData"].AsByteArray;
                    if (bytes is null || bytes.Length < 1024) continue;
                    string? family = Ttf.FamilyName(bytes);
                    if (family is null) continue;
                    writer.WriteBytes("resource/fonts/silent_katamari.ttf", bytes);
                    writer.WriteJson("data_static/silent_katamari/font.json", new JsonObject { ["family"] = family });
                    return family;
                }
            }
            catch (Exception e) { ctx.Log.Warn($"ouak font {Path.GetFileName(path)}: {e.Message}"); }
        }
        throw new InvalidOperationException("no font found");
    }

    // ---------------- sounds ----------------

    /// <summary>CRI banks (.acb/.awb) listed with vgmstream; streams picked by their cue names.</summary>
    private static void Sounds(GameInstall game, IContentWriter writer, ConvertContext ctx)
    {
        var rows = new (string Id, string Pattern)[]
        {
            ("ouak_snd_rollup_s", "(maki|roll|catch|get|pick).*(_a|_s|01|small)"),
            ("ouak_snd_rollup_m", "(maki|roll|catch|get|pick).*(_b|_c|_m|02|03|mid)"),
            ("ouak_snd_rollup_l", "(maki|roll|catch|get|pick).*(_d|_e|_l|04|05|big|large)"),
            ("ouak_snd_hit", "(hit|bump|crash|butsu)"),
            ("ouak_snd_dash", "(dash|boost)"),
            ("ouak_snd_scaleup", "(size.?up|scale|grow|levelup)"),
        };
        string? tool = ContentWriter.Vgmstream(ctx);
        string data = Path.Combine(game.Folder, "OnceUponaKATAMARI_Data");
        var banks = Directory.Exists(data)
            ? Directory.EnumerateFiles(data, "*.acb", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Order().ToList()
            : new List<string>();
        var streams = new List<(string File, int Index, string Name)>();
        if (tool is not null)
            foreach (var bank in banks.Where(b => Regex.IsMatch(Path.GetFileName(b), "(se|sfx|sound|game|katamari|common)", RegexOptions.IgnoreCase)).Take(12))
                streams.AddRange(ListStreams(tool, bank, ctx));
        ctx.Log.Info($"ouak: {banks.Count} CRI banks, {streams.Count} named streams read");
        foreach (var (id, pattern) in rows)
        {
            ctx.Run(id, () =>
            {
                if (tool is null) throw new InvalidOperationException("the audio decoder (vgmstream) is missing from this mashup's files");
                var hit = streams.FirstOrDefault(s => Regex.IsMatch(s.Name, pattern, RegexOptions.IgnoreCase));
                if (hit.File is null) throw new InvalidOperationException("no matching sound found");
                var row = Extract.ById(id)!;
                if (writer is ContentWriter cw) cw.WriteWav(row.Output, hit.File, hit.Index);
                else throw new InvalidOperationException("writer can't pick a stream");
                return hit.Name;
            });
        }
    }

    internal static IEnumerable<(string File, int Index, string Name)> ListStreams(string tool, string bank, ConvertContext ctx)
    {
        var first = Meta(tool, bank, 1);
        int count = int.TryParse(Regex.Match(first, @"stream count:\s*(\d+)").Groups[1].Value, out var n) ? n : 1;
        for (int i = 1; i <= Math.Min(count, 400); i++)
        {
            string meta = i == 1 ? first : Meta(tool, bank, i);
            string name = Regex.Match(meta, @"stream name:\s*(.+)").Groups[1].Value.Trim();
            if (name.Length > 0) yield return (bank, i, name);
        }
    }

    private static string Meta(string tool, string bank, int subsong)
    {
        var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-m", "-s", subsong.ToString(CultureInfo.InvariantCulture), bank }) psi.ArgumentList.Add(a);
        return ContentWriter.Run(psi, TimeSpan.FromSeconds(20)).Output;
    }
}
