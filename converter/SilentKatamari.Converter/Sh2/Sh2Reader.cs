// SILENT HILL 2 (2024, UE 5.1) reader: checks the containers without any key, then - only when they are not
// encrypted and a mappings file and Oodle decoder are on this PC - reads James's mesh, skeleton, animations and
// textures, a ground texture and Wwise sounds with CUE4Parse (Apache-2.0). Never decrypts, never downloads.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Versions;
using OodleDotNet;

namespace SilentKatamari.Converter.Sh2;

public sealed class Sh2Reader : IGameReader
{
    public string GameId => "sh2";
    public const string EncryptedReason = "SILENT HILL 2's files are encrypted, and this mashup never decrypts them";
    public const string NoMappingsReason = "no mappings file for SILENT HILL 2 is available on this PC";
    public const string NoOodleReason = "SILENT HILL 2 uses Oodle compression and no Oodle decoder is available on this PC";
    internal const string JamesFolder = "Game/Characters/Humans/JamesSunderland/";

    public static string PaksFolder(GameInstall g) => Path.Combine(g.Folder, "SHProto", "Content", "Paks");

    /// <summary>First existing of env SK_SH2_USMAP, tools/mappings/SilentHill2.usmap, %LOCALAPPDATA%/SilentKatamari/SilentHill2.usmap.</summary>
    public static string? FindMappings(ConvertContext ctx) => FirstFile(
        Environment.GetEnvironmentVariable("SK_SH2_USMAP"),
        Path.Combine(ctx.ToolsFolder, "mappings", "SilentHill2.usmap"),
        Path.Combine(Path.GetDirectoryName(Log.DefaultPath())!, "SilentHill2.usmap"));

    /// <summary>A local Oodle DLL: env SK_OODLE_DLL, tools/oodle/*.dll, or one the game itself ships.</summary>
    public static string? FindOodle(ConvertContext ctx, string? gameFolder)
    {
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("SK_OODLE_DLL") };
        string tools = Path.Combine(ctx.ToolsFolder, "oodle");
        if (Directory.Exists(tools)) candidates.AddRange(Directory.EnumerateFiles(tools, "*.dll").Order());
        if (gameFolder is not null) candidates.AddRange(OodleDllsIn(gameFolder));
        return FirstFile(candidates.ToArray());
    }

    internal static IEnumerable<string> OodleDllsIn(string folder)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        try
        {
            return Directory.EnumerateFiles(folder, "*.dll", opts)
                .Where(p => Regex.IsMatch(Path.GetFileName(p), @"^(oo2core|oodle).*\.dll$", RegexOptions.IgnoreCase)).Order().ToList();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private static string? FirstFile(params string?[] paths) => paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));

    public JsonObject Probe(GameInstall game, ConvertContext ctx)
    {
        var o = new JsonObject();
        string paks = PaksFolder(game);
        o["paks_folder_exists"] = Directory.Exists(paks);
        var containers = ContainerCheck.ReadAll(paks);
        o["containers"] = new JsonArray(containers.Select(c => (JsonNode)c.ToJson()).ToArray());
        o["any_encrypted"] = containers.Any(c => c.Encrypted);
        o["compression"] = new JsonArray(containers.SelectMany(c => c.CompressionMethods).Distinct().Select(c => (JsonNode)c).ToArray());
        o["oodle_dlls_in_game"] = new JsonArray(OodleDllsIn(game.Folder).Select(p => (JsonNode)Runner.Slashes(Path.GetRelativePath(game.Folder, p))).ToArray());
        o["mappings"] = FindMappings(ctx) is { } m ? Path.GetFileName(m) : null;
        o["oodle"] = FindOodle(ctx, game.Folder) is { } d ? Path.GetFileName(d) : null;
        if (containers.Any(c => c.Encrypted)) return o;
        try
        {
            using var provider = Mount(game, ctx, requireMappings: false, out string? why);
            if (provider is null) { o["mount"] = why; return o; }
            var keys = provider.Files.Keys.ToList();
            o["files"] = keys.Count;
            o["by_extension"] = new JsonObject(keys.GroupBy(k => Path.GetExtension(k).ToLowerInvariant())
                .OrderByDescending(g => g.Count()).Take(15).Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.Count())));
            JsonArray Sample(Func<string, bool> pred, int n) => new(keys.Where(pred).Order().Take(n).Select(k => (JsonNode)k).ToArray());
            o["james_files"] = Sample(k => k.Contains(JamesFolder, StringComparison.OrdinalIgnoreCase), 120);
            o["sound_candidates"] = Sample(k => Regex.IsMatch(k, @"(radio|static|noise|amb|mus|bgm)", RegexOptions.IgnoreCase)
                && Regex.IsMatch(k, @"\.(uasset|wem|bnk)$", RegexOptions.IgnoreCase), 80);
            o["ground_candidates"] = Sample(k => Regex.IsMatch(k, @"(asphalt|road)", RegexOptions.IgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase), 40);
        }
        catch (Exception e) { o["mount_error"] = ConvertContext.Short(e.Message); }
        return o;
    }

    /// <summary>Mounts the containers with no keys. Null (and a reason) when the gate says no.</summary>
    internal static DefaultFileProvider? Mount(GameInstall game, ConvertContext ctx, bool requireMappings, out string? why)
    {
        string paks = PaksFolder(game);
        var containers = ContainerCheck.ReadAll(paks);
        if (containers.Count == 0) { why = "SILENT HILL 2's game files were not found in its folder"; return null; }
        if (containers.Any(c => c.Encrypted)) { why = EncryptedReason; return null; }
        string? mappings = FindMappings(ctx);
        if (mappings is null && requireMappings) { why = NoMappingsReason; return null; }
        bool needsOodle = containers.SelectMany(c => c.CompressionMethods).Any(m => m.Contains("oodle", StringComparison.OrdinalIgnoreCase));
        if (needsOodle && OodleHelper.Instance is null)
        {
            // Only ever a local DLL: OodleHelper.Initialize(string) would download one when the path is missing.
            string? dll = FindOodle(ctx, game.Folder);
            if (dll is not null) OodleHelper.Initialize(new Oodle(dll));
            else if (requireMappings) { why = NoOodleReason; return null; }
        }
        var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_SilentHill2Remake), StringComparer.OrdinalIgnoreCase);
        if (mappings is not null) provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.Mount();
        ctx.Log.Info($"sh2: mounted {provider.Files.Count} files (mappings: {(mappings is null ? "none" : Path.GetFileName(mappings))})");
        why = null;
        return provider;
    }

    public void Convert(GameInstall game, IContentWriter writer, ConvertContext ctx)
    {
        using var provider = Mount(game, ctx, requireMappings: true, out string? why);
        if (provider is null)
        {
            foreach (var row in Extract.All.Where(r => r.Game == GameId)) ctx.Row(row.Id, false, why);
            throw new InvalidOperationException(why);
        }
        ctx.Row("sh2_containers", true, "not encrypted");
        var james = new JamesExtractor(provider, writer, ctx);
        ctx.Run("sh2_james_mesh", james.Mesh);
        if (ctx.RowOk("sh2_james_mesh")) ctx.Run("sh2_james_anims", james.Finish);
        else ctx.Row("sh2_james_anims", false, "needs James's mesh first");
        ctx.Run("sh2_ground_tex", () => Sh2Assets.Ground(provider, writer, ctx));
        var sounds = new Sh2Audio(provider, writer, ctx);
        ctx.Run("sh2_snd_radio", () => sounds.Extract("sh2_snd_radio", @"radio", @"static|noise", preferLong: true));
        ctx.Run("sh2_snd_ambience", () => sounds.Extract("sh2_snd_ambience", @"amb", null, preferLong: true));
        ctx.Run("sh2_snd_music", () => sounds.Extract("sh2_snd_music", @"mus|bgm", null, preferLong: true));
    }
}
