// STUB: replaced by the Once Upon A KATAMARI reader.
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter.Ouak;

public sealed class OuakReader : IGameReader
{
    public string GameId => "ouak";
    public JsonObject Probe(GameInstall game, ConvertContext ctx) => new() { ["stub"] = true };
    public void Convert(GameInstall game, IContentWriter writer, ConvertContext ctx) => throw new NotImplementedException("Once Upon A KATAMARI reader not built yet");
}
