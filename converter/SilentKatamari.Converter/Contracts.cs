// The seams between the converter's parts: game readers produce Model.cs shapes and hand them to an
// IContentWriter, which writes the formats fixed in docs/CONTRACT.md. Each part lives in its own folder:
//   Steam.cs        finding games          Ouak/  Once Upon A KATAMARI reader
//   Out/            writers                Sh2/   SILENT HILL 2 reader
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter;

public interface IGameReader
{
    /// <summary>Games sheet id: "ouak" or "sh2".</summary>
    string GameId { get; }

    /// <summary>Quick, read-only look at the install: versions, containers, encryption, formats. Never writes.
    /// Returns facts for status.json "probe" and the log. Must not throw for an unexpected layout: report it.</summary>
    JsonObject Probe(GameInstall game, ConvertContext ctx);

    /// <summary>Runs every extract row of this game. Each row reports through ctx.Row(...). A failing optional row
    /// must not stop the others. Throw only for a fault that makes the whole game unreadable (the message becomes
    /// status.games.&lt;id&gt;.reason, so keep it short and free of paths).</summary>
    void Convert(GameInstall game, IContentWriter writer, ConvertContext ctx);
}

/// <summary>Writes into the staging copy of garrysmod/addons/silent_katamari_content. Paths are relative to it,
/// with forward slashes, e.g. "materials/silent_katamari/ouak/o12.png".</summary>
public interface IContentWriter
{
    string Root { get; }

    void WriteJson(string relPath, JsonNode node);

    /// <summary>materials/&lt;material&gt;.png, longest side clamped to maxSide.</summary>
    void WritePng(string relPath, RgbaImage image, int maxSide = 1024);

    /// <summary>Mesh JSON (docs/CONTRACT.md "Mesh JSON"); textures written next to it under
    /// materials/&lt;texturePrefix&gt;&lt;material&gt;.png. Returns the relative JSON path.</summary>
    string WriteMeshJson(string relPath, StaticMesh mesh, string texturePrefix);

    /// <summary>Copies (or converts with the bundled vgmstream-cli) a sound to a 16-bit PCM WAV.</summary>
    void WriteWav(string relPath, string sourceFile);

    /// <summary>Compiles a Source model with Garry's Mod's own studiomdl: writes SMD/QC/VTF/VMT in a work folder,
    /// runs studiomdl, moves the result to models/&lt;modelName&gt;.* and materials/models/&lt;modelName&gt;/.
    /// Returns null on success, else a short reason.</summary>
    string? CompileModel(string modelName, SkinnedModel model, IReadOnlyList<AnimClip> clips, string gmodFolder);

    void WriteBytes(string relPath, byte[] bytes);
}

/// <summary>Per-run state: the log, which rows succeeded, and the facts that go into status.json.</summary>
public sealed class ConvertContext
{
    public required Log Log { get; init; }
    public required string GmodFolder { get; init; }
    public string WorkFolder { get; init; } = Path.Combine(Path.GetTempPath(), "SilentKatamari");
    public string ToolsFolder { get; init; } = AppContext.BaseDirectory;
    public JsonObject Rows { get; } = new();
    public JsonObject Probe { get; } = new();

    public void Row(string extractId, bool ok, string? note = null, double seconds = 0)
    {
        if (Extract.ById(extractId) is null) throw new ArgumentException($"no extract row '{extractId}'");
        Rows[extractId] = new JsonObject { ["ok"] = ok, ["seconds"] = Math.Round(seconds, 2), ["note"] = note };
        Log.Info($"{(ok ? "ok  " : "FAIL")} {extractId,-20} {seconds,6:0.0}s {note}");
    }

    public bool RowOk(string extractId) => Rows[extractId]?["ok"]?.GetValue<bool>() == true;

    /// <summary>Runs one extract row, timing it and turning an exception into a failed row.</summary>
    public bool Run(string extractId, Func<string?> body)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string? note = body();
            Row(extractId, true, note, sw.Elapsed.TotalSeconds);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"{extractId}: {e}");
            Row(extractId, false, Short(e.Message), sw.Elapsed.TotalSeconds);
            return false;
        }
    }

    /// <summary>A reason fit for a player: one line, no file paths.</summary>
    public static string Short(string message)
    {
        string line = message.Split('\n')[0].Trim();
        line = System.Text.RegularExpressions.Regex.Replace(line, @"[A-Za-z]:[\\/][^\s'""]*", "<path>");
        return line.Length > 160 ? line[..157] + "..." : line;
    }
}
