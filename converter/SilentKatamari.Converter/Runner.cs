// Orchestrates a conversion: finds each game, lets its reader probe and convert into a staging copy of the
// content addon, writes status.json last, then swaps the staging copy in. See docs/CONTRACT.md.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter;

public sealed class Runner
{
    public const string ContentDir = "silent_katamari_content";
    public const string ContentTitle = "Silent Katamari content (made on this PC from your own games)";
    public static readonly string[] GameOrder = { "ouak", "sh2" };

    private readonly string _gmod;
    private readonly Log _log;
    private readonly Steam _steam;
    private readonly IReadOnlyList<IGameReader> _readers;
    private readonly Func<string, ConvertContext, IContentWriter> _writerFactory;

    public Runner(string gmodFolder, Log log, Steam? steam = null, IReadOnlyList<IGameReader>? readers = null,
        Func<string, ConvertContext, IContentWriter>? writerFactory = null)
    {
        _gmod = Path.GetFullPath(gmodFolder);
        _log = log;
        _steam = steam ?? new Steam { Log = log };
        _readers = readers ?? new IGameReader[] { new Ouak.OuakReader(), new Sh2.Sh2Reader() };
        _writerFactory = writerFactory ?? ((root, ctx) => new ContentWriter(root, ctx));
    }

    public static string Slashes(string p) => p.Replace('\\', '/');

    public static string Version
    {
        get
        {
            var v = typeof(Runner).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public string ContentRoot => Path.Combine(_gmod, "garrysmod", "addons", ContentDir);
    public string StatusPath => Path.Combine(ContentRoot, "data_static", "silent_katamari", "status.json");

    private static JsonSerializerOptions Pretty => new() { WriteIndented = true };

    private ConvertContext NewContext() => new()
    {
        Log = _log,
        GmodFolder = _gmod,
        WorkFolder = Path.Combine(Path.GetTempPath(), "SilentKatamari", "work"),
        ToolsFolder = AppContext.BaseDirectory,
    };

    /// <summary>Read-only report: where each game is and what its reader sees. Never writes into game folders.</summary>
    public JsonObject Probe(string? only = null)
    {
        var ctx = NewContext();
        var games = new JsonObject();
        foreach (var reader in _readers)
        {
            if (only is not null && reader.GameId != only) continue;
            var row = Games.ById(reader.GameId)!;
            var found = _steam.Locate(row);
            var g = found?.ToJson() ?? new JsonObject { ["found"] = false };
            if (found is not null)
            {
                g["found"] = true;
                try { g["probe"] = reader.Probe(found.Install, ctx); }
                catch (Exception e) { g["probe"] = new JsonObject { ["error"] = e.ToString() }; }
            }
            games[reader.GameId] = g;
        }
        return new JsonObject
        {
            ["converter"] = Version,
            ["gmod"] = Slashes(_gmod),
            ["studiomdl"] = FindStudioMdl(_gmod) is { } s ? Slashes(s) : null,
            ["steam_libraries"] = new JsonArray(_steam.Libraries().Select(l => (JsonNode)Slashes(l)).ToArray()),
            ["games"] = games,
        };
    }

    public static string? FindStudioMdl(string gmod) =>
        new[] { Path.Combine(gmod, "bin", "studiomdl.exe"), Path.Combine(gmod, "bin", "win64", "studiomdl.exe") }.FirstOrDefault(File.Exists);

    /// <summary>Runs every reader into a staging folder and swaps it in. Returns the status written.</summary>
    public JsonObject Convert()
    {
        using var _ = AcquireLock(_log);
        string staging = ContentRoot + ".staging";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        var ctx = NewContext();
        if (Directory.Exists(ctx.WorkFolder)) TryDelete(ctx.WorkFolder);
        Directory.CreateDirectory(ctx.WorkFolder);
        var writer = _writerFactory(staging, ctx);

        var games = new JsonObject();
        foreach (var reader in _readers)
        {
            var row = Games.ById(reader.GameId)!;
            var found = _steam.Locate(row);
            if (found is null)
            {
                games[reader.GameId] = GameState("missing", null, null, null);
                continue;
            }
            var g = found.Install;
            string? fault = null;
            try { ctx.Probe[reader.GameId] = reader.Probe(g, ctx); }
            catch (Exception e) { ctx.Probe[reader.GameId] = new JsonObject { ["error"] = ConvertContext.Short(e.Message) }; }
            try { reader.Convert(g, writer, ctx); }
            catch (Exception e)
            {
                _log.Error($"{reader.GameId}: {e}");
                fault = ConvertContext.Short(e.Message);
            }
            var failed = Extract.All.Where(x => x.Game == reader.GameId && x.Required && !ctx.RowOk(x.Id)).ToList();
            string? reason = fault ?? failed.Select(x => ctx.Rows[x.Id]?["note"]?.GetValue<string>()).FirstOrDefault(n => n is not null)
                             ?? (failed.Count > 0 ? $"{failed[0].Id} was not read" : null);
            games[reader.GameId] = GameState(fault is null && failed.Count == 0 ? "ok" : "unreadable", Slashes(g.Folder), g.Build, reason);
        }

        File.WriteAllText(Path.Combine(staging, "addon.json"), new JsonObject
        {
            ["title"] = ContentTitle, ["type"] = "servercontent", ["tags"] = new JsonArray("fun"), ["ignore"] = new JsonArray(),
        }.ToJsonString(Pretty));

        var status = new JsonObject
        {
            ["converter"] = Version,
            ["finished_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["games"] = games,
            ["rows"] = ctx.Rows.DeepClone(),
            ["probe"] = ctx.Probe.DeepClone(),
        };
        // status.json last: its presence means the conversion finished (Melty's setup.done file).
        string statusRel = Path.Combine("data_static", "silent_katamari", "status.json");
        Directory.CreateDirectory(Path.Combine(staging, "data_static", "silent_katamari"));
        File.WriteAllText(Path.Combine(staging, statusRel), status.ToJsonString(Pretty));

        SwapIn(staging, ContentRoot, _log);
        TryDelete(ctx.WorkFolder);
        foreach (var id in GameOrder)
            _log.Info($"{id}: {games[id]?["state"]} {games[id]?["reason"]}");
        return status;
    }

    private static JsonObject GameState(string state, string? folder, string? build, string? reason) => new()
    {
        ["state"] = state, ["folder"] = folder, ["build"] = build, ["reason"] = reason,
    };

    /// <summary>Whether play must convert first, and why.</summary>
    public bool NeedsConvert(out string why)
    {
        JsonObject? status = null;
        try { if (File.Exists(StatusPath)) status = JsonNode.Parse(File.ReadAllText(StatusPath)) as JsonObject; }
        catch (Exception) { status = null; }
        return NeedsConvert(status, id => _steam.Find(Games.ById(id)!), out why);
    }

    internal static bool NeedsConvert(JsonObject? status, Func<string, GameInstall?> find, out string why)
    {
        if (status is null) { why = "never converted"; return true; }
        if (status["converter"]?.GetValue<string>() != Version) { why = "converter updated"; return true; }
        foreach (var id in GameOrder)
        {
            var was = status["games"]?[id];
            string? state = was?["state"]?.GetValue<string>();
            var now = find(id);
            if (now is null)
            {
                if (state != "missing") { why = $"{id} was uninstalled"; return true; }
                continue;
            }
            if (state == "missing" || state is null) { why = $"{id} was installed"; return true; }
            if (!string.Equals(was?["folder"]?.GetValue<string>(), Slashes(now.Folder), StringComparison.OrdinalIgnoreCase))
            { why = $"{id} moved"; return true; }
            if (was?["build"]?.GetValue<string>() != now.Build) { why = $"{id} was updated"; return true; }
        }
        why = "up to date";
        return false;
    }

    /// <summary>Replaces final with staging. Refuses to delete a folder this converter did not make.</summary>
    internal static void SwapIn(string staging, string final, Log log)
    {
        if (Directory.Exists(final))
        {
            if (!IsOurs(final))
                throw new IOException($"{Path.GetFileName(final)} exists but was not made by this converter; leaving it alone");
            for (int attempt = 1; ; attempt++)
            {
                try { Directory.Delete(final, recursive: true); break; }
                catch (Exception e) when (attempt < 5 && e is IOException or UnauthorizedAccessException)
                {
                    log.Warn($"old content is in use ({e.Message}); retrying");
                    Thread.Sleep(1000 * attempt);
                }
            }
        }
        Directory.Move(staging, final);
    }

    internal static bool IsOurs(string folder)
    {
        if (File.Exists(Path.Combine(folder, "data_static", "silent_katamari", "status.json"))) return true;
        try
        {
            string addon = Path.Combine(folder, "addon.json");
            return File.Exists(addon) && (JsonNode.Parse(File.ReadAllText(addon))?["title"]?.GetValue<string>() ?? "")
                .StartsWith("Silent Katamari content", StringComparison.Ordinal);
        }
        catch (Exception) { return false; }
    }

    /// <summary>One conversion at a time across processes (Melty's setup step and play can overlap).</summary>
    internal static IDisposable AcquireLock(Log log)
    {
        string path = Path.Combine(Path.GetDirectoryName(Log.DefaultPath())!, "convert.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                log.Info("another conversion is running; waiting");
                Thread.Sleep(2000);
            }
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (Exception) { /* temp files: best effort */ }
    }
}
