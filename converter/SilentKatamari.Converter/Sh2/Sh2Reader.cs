// STUB: replaced by the SILENT HILL 2 reader.
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter.Sh2;

public sealed class Sh2Reader : IGameReader
{
    public string GameId => "sh2";
    public JsonObject Probe(GameInstall game, ConvertContext ctx) => new() { ["stub"] = true };
    public void Convert(GameInstall game, IContentWriter writer, ConvertContext ctx) => throw new NotImplementedException("SILENT HILL 2 reader not built yet");
}
