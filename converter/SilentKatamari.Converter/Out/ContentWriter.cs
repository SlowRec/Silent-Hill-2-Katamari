// STUB: replaced by the writers implementation.
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter;

public sealed class ContentWriter : IContentWriter
{
    public ContentWriter(string root, ConvertContext ctx) { Root = root; }
    public string Root { get; }
    public void WriteJson(string relPath, JsonNode node) => throw new NotImplementedException();
    public void WritePng(string relPath, RgbaImage image, int maxSide = 1024) => throw new NotImplementedException();
    public string WriteMeshJson(string relPath, StaticMesh mesh, string texturePrefix) => throw new NotImplementedException();
    public void WriteWav(string relPath, string sourceFile) => throw new NotImplementedException();
    public string? CompileModel(string modelName, SkinnedModel model, IReadOnlyList<AnimClip> clips, string gmodFolder) => throw new NotImplementedException();
    public void WriteBytes(string relPath, byte[] bytes) => throw new NotImplementedException();
}
