// Finds the player's installed games: Steam libraries (libraryfolders.vdf, then appmanifest_<appid>.acf), and for
// SILENT HILL 2 also GOG (registry) and Epic (launcher manifests). Read-only: never writes, never launches anything.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilentKatamari.Converter;

/// <summary>A game found on this PC plus the facts behind the choice, for the probe and the log.</summary>
public sealed record Found(GameInstall Install, bool ProbeFileFound, int? StateFlags, string? Library)
{
    public JsonObject ToJson() => new()
    {
        ["store"] = Install.Store,
        ["folder"] = Runner.Slashes(Install.Folder),
        ["build"] = Install.Build,
        ["probe_file"] = ProbeFileFound,
        ["state_flags"] = StateFlags,
        ["library"] = Library is null ? null : Runner.Slashes(Library),
    };
}

public sealed class Steam
{
    // Store ids outside Steam, per games-sheet id (the sheet has no columns for them yet).
    // Sources: docs/RESEARCH.md (Vortex and MO2 game plugins).
    private static readonly Dictionary<string, (string[] Gog, string[] Epic, string EpicName)> OtherStores = new()
    {
        ["sh2"] = (new[] { "2051029707", "1225972913" }, new[] { "c4dc308a1b69492aba4d47f7feaa1083" }, "SILENT HILL 2"),
    };

    public Log? Log { get; init; }

    /// <summary>When set (tests: env SK_STEAM_ROOT), the only Steam root searched.</summary>
    public string? RootOverride { get; init; } = NullIfBlank(Environment.GetEnvironmentVariable("SK_STEAM_ROOT"));

    /// <summary>(hive "HKCU"/"HKLM", subkey, value name) → string value or null. Injectable for tests.</summary>
    public Func<string, string, string, string?> RegistryValue { get; init; } = ReadRegistry;

    /// <summary>Epic Games Launcher's manifest folder (*.item JSON), or null to skip Epic.</summary>
    public string? EpicManifests { get; init; } = DefaultEpicManifests();

    /// <summary>Steam install folders that exist, most trusted first.</summary>
    public IReadOnlyList<string> Roots()
    {
        var c = new List<string?>();
        if (RootOverride is not null) c.Add(RootOverride);
        else
        {
            c.Add(RegistryValue("HKCU", @"Software\Valve\Steam", "SteamPath"));
            c.Add(RegistryValue("HKLM", @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            c.Add(RegistryValue("HKLM", @"SOFTWARE\Valve\Steam", "InstallPath"));
            if (OperatingSystem.IsWindows())
            {
                c.Add(@"C:\Program Files (x86)\Steam");
                c.Add(@"C:\Program Files\Steam");
            }
        }
        return Existing(c);
    }

    /// <summary>Every Steam library folder that exists: each root plus the libraries its libraryfolders.vdf lists.</summary>
    public IReadOnlyList<string> Libraries()
    {
        var libs = new List<string?>();
        foreach (var root in Roots())
        {
            libs.Add(root);
            // Newer Steam keeps the list in steamapps/, older builds in config/; reading both costs nothing.
            foreach (var vdf in new[] { Path.Combine(root, "steamapps", "libraryfolders.vdf"), Path.Combine(root, "config", "libraryfolders.vdf") })
            {
                if (!File.Exists(vdf)) continue;
                try { libs.AddRange(LibraryPaths(Vdf.Parse(File.ReadAllText(vdf)))); }
                catch (Exception e) { Log?.Warn($"cannot read {vdf}: {e.Message}"); }
            }
        }
        return Existing(libs);
    }

    /// <summary>Library paths listed in a parsed libraryfolders.vdf, old format ("1" "D:\\Lib") and new
    /// ("0" { "path" "D:\\Lib" ... }). Raw strings, not checked on disk.</summary>
    public static IEnumerable<string> LibraryPaths(VdfNode doc)
    {
        foreach (var top in doc.Children)
            foreach (var (key, node) in top.Value.Children)
            {
                if (key.Length == 0 || !key.All(char.IsAsciiDigit)) continue;
                string? p = node.Value ?? node["path"]?.Value;
                if (!string.IsNullOrWhiteSpace(p)) yield return p;
            }
    }

    public GameInstall? Find(GameRow game) => Locate(game)?.Install;

    /// <summary>Steam first, then (SILENT HILL 2 only) GOG and Epic. Null when not installed.</summary>
    public Found? Locate(GameRow game)
    {
        var found = FromSteam(game) ?? FromOtherStores(game);
        if (found is null)
        {
            Log?.Info($"{game.Id}: {game.Name} not found");
            return null;
        }
        var g = found.Install;
        Log?.Info($"{game.Id}: {game.Name} ({g.Store}) in {g.Folder}, build {g.Build ?? "unknown"}");
        // probe_file values are unverified (games sheet), so a miss is only a warning: the reader's probe decides.
        if (!found.ProbeFileFound)
            Log?.Warn($"{game.Id}: {game.ProbeFile} is not in that folder; using it anyway");
        return found;
    }

    private Found? FromSteam(GameRow game)
    {
        if (string.IsNullOrWhiteSpace(game.SteamAppid)) return null;
        var hits = new List<Found>();
        foreach (var lib in Libraries())
        {
            string acf = Path.Combine(lib, "steamapps", $"appmanifest_{game.SteamAppid}.acf");
            if (!File.Exists(acf)) continue;
            Dictionary<string, string> m;
            try { m = ReadAcf(File.ReadAllText(acf)); }
            catch (Exception e) { Log?.Warn($"cannot read {acf}: {e.Message}"); continue; }

            string dir = m.GetValueOrDefault("installdir") is { Length: > 0 } d && !Path.IsPathRooted(d) ? d : game.Installdir;
            string folder = Path.Combine(lib, "steamapps", "common", dir);
            // Steam can leave a manifest behind after moving or uninstalling a game: the folder must really be there.
            if (!Directory.Exists(folder))
            {
                Log?.Info($"{game.Id}: {acf} names '{dir}', but that folder is missing");
                continue;
            }
            int? flags = int.TryParse(m.GetValueOrDefault("StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : null;
            if (flags is int fl && (fl & 4) == 0)
                Log?.Warn($"{game.Id}: Steam says it is not fully installed (StateFlags {fl}); trying it anyway");
            hits.Add(new Found(new GameInstall(game.Id, Full(folder)!, NullIfBlank(m.GetValueOrDefault("buildid")), "steam"),
                HasProbeFile(folder, game), flags, lib));
        }
        // A game with manifests in two libraries: prefer the copy that looks complete.
        return hits.OrderByDescending(h => h.ProbeFileFound)
                   .ThenByDescending(h => h.StateFlags is int x && (x & 4) != 0)
                   .FirstOrDefault();
    }

    private Found? FromOtherStores(GameRow game)
    {
        if (!OtherStores.TryGetValue(game.Id, out var s)) return null;

        foreach (var id in s.Gog)
            foreach (var key in new[] { $@"SOFTWARE\WOW6432Node\GOG.com\Games\{id}", $@"SOFTWARE\GOG.com\Games\{id}" })
                if (Clean(RegistryValue("HKLM", key, "path")) is { } p && Directory.Exists(p))
                    return Other(game, p, "gog");

        if (EpicManifests is null || !Directory.Exists(EpicManifests)) return null;
        foreach (var item in Directory.EnumerateFiles(EpicManifests, "*.item").Order(StringComparer.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) continue;
                string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                var ids = new[] { Str("AppName"), Str("CatalogItemId"), Str("CatalogNamespace"), Str("MainGameAppName") };
                bool match = s.Epic.Any(e => ids.Any(v => string.Equals(v, e, StringComparison.OrdinalIgnoreCase)))
                             || string.Equals(Str("DisplayName")?.Trim(), s.EpicName, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                if (Clean(Str("InstallLocation")) is { } p && Directory.Exists(p))
                {
                    if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True)
                        Log?.Warn($"{game.Id}: Epic says the install is incomplete; trying it anyway");
                    return Other(game, p, "epic");
                }
            }
            catch (Exception e) { Log?.Warn($"cannot read {item}: {e.Message}"); }
        }
        return null;
    }

    // GOG and Epic have no Steam buildid; the main container's size and time stand in for it, so an update
    // (which rewrites that file) still triggers a re-conversion.
    private Found Other(GameRow game, string folder, string store) =>
        new(new GameInstall(game.Id, Full(folder)!, Fingerprint(folder, game), store), HasProbeFile(folder, game), null, null);

    /// <summary>"size-mtime" of the game's main .utoc: the probe_file when it is one, else the largest .utoc.</summary>
    public static string? Fingerprint(string folder, GameRow game)
    {
        FileInfo? f = new(Path.Combine(folder, game.ProbeFile));
        if (!f.Exists || !f.Name.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                f = new DirectoryInfo(folder)
                    .EnumerateFiles("*.utoc", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .OrderByDescending(x => x.Length).FirstOrDefault();
            }
            catch (Exception) { f = null; }
        }
        return f is null ? null
            : $"{f.Length.ToString(CultureInfo.InvariantCulture)}-{f.LastWriteTimeUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
    }

    private static bool HasProbeFile(string folder, GameRow game)
    {
        if (string.IsNullOrWhiteSpace(game.ProbeFile)) return true;
        string p = Path.Combine(folder, game.ProbeFile);
        return File.Exists(p) || Directory.Exists(p);
    }

    /// <summary>The AppState key/values of an appmanifest_*.acf (case-insensitive keys).</summary>
    public static Dictionary<string, string> ReadAcf(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var state = Vdf.Parse(text)["AppState"] ?? throw new FormatException("no AppState block");
        foreach (var (k, v) in state.Children)
            if (v.Value is not null) d.TryAdd(k, v.Value);
        return d;
    }

    /// <summary>A path from Steam, the registry or a manifest made usable here: trimmed, absolute, no trailing
    /// separator. On Linux (tests, Proton) backslashes become slashes. Null when empty or invalid.</summary>
    public static string? Clean(string? p)
    {
        p = p?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(p)) return null;
        if (!OperatingSystem.IsWindows()) p = p.Replace('\\', '/');
        return Full(p);
    }

    private static string? Full(string p)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)); }
        catch (Exception) { return null; }
    }

    private static IReadOnlyList<string> Existing(IEnumerable<string?> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in paths)
            if (Clean(raw) is { } p && Directory.Exists(p) && seen.Add(Runner.Slashes(p)))
                list.Add(p);
        return list;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? ReadRegistry(string hive, string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            // The base keys are process-wide singletons: only the opened subkey is disposed.
            var root = hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
            using var k = root.OpenSubKey(key);
            return k?.GetValue(value) as string;
        }
        catch (Exception) { return null; }
    }

    private static string? DefaultEpicManifests()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(pd)) pd = @"C:\ProgramData";
        return Path.Combine(pd, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }
}

/// <summary>A Valve KeyValues (VDF/ACF) node: a string value, or an ordered list of children.</summary>
public sealed class VdfNode
{
    public string? Value { get; init; }
    public List<KeyValuePair<string, VdfNode>> Children { get; } = new();

    /// <summary>First child with this key, ignoring case (Steam writes both "LibraryFolders" and "libraryfolders").</summary>
    public VdfNode? this[string key]
    {
        get
        {
            foreach (var c in Children)
                if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) return c.Value;
            return null;
        }
    }
}

/// <summary>Tolerant reader for Valve's KeyValues text: quoted or bare tokens, nested braces, // comments,
/// [$WIN32] conditionals, a missing closing brace or quote at the end of a truncated file.</summary>
public static class Vdf
{
    private const int MaxDepth = 64;

    public static VdfNode Parse(string text)
    {
        var root = new VdfNode();
        ReadChildren(new Lexer(text), root, 0);
        return root;
    }

    private static void ReadChildren(Lexer lx, VdfNode into, int depth)
    {
        if (depth > MaxDepth) throw new FormatException("VDF nested too deeply");
        while (true)
        {
            var (kind, key) = lx.Next();
            if (kind == Tok.End) return;
            if (kind == Tok.Close)
            {
                if (depth == 0) continue; // stray brace at top level
                return;
            }
            if (kind == Tok.Open) // block without a key: read and drop it
            {
                ReadChildren(lx, new VdfNode(), depth + 1);
                continue;
            }
            var (vk, val) = lx.Next();
            if (vk == Tok.Open)
            {
                var n = new VdfNode();
                ReadChildren(lx, n, depth + 1);
                into.Children.Add(new(key, n));
            }
            else if (vk == Tok.Str) into.Children.Add(new(key, new VdfNode { Value = val }));
            else
            {
                into.Children.Add(new(key, new VdfNode { Value = "" }));
                if (vk == Tok.End || depth > 0) return;
            }
        }
    }

    private enum Tok { End, Open, Close, Str }

    private sealed class Lexer(string s)
    {
        private int _i = s.Length > 0 && s[0] == '\uFEFF' ? 1 : 0;

        public (Tok, string) Next()
        {
            while (_i < s.Length)
            {
                char c = s[_i];
                if (char.IsWhiteSpace(c)) { _i++; continue; }
                if (c == '/' && _i + 1 < s.Length && s[_i + 1] == '/')
                {
                    while (_i < s.Length && s[_i] != '\n') _i++;
                    continue;
                }
                if (c == '[') // platform conditional such as [$WIN32]: not data
                {
                    int end = s.IndexOf(']', _i);
                    _i = end < 0 ? s.Length : end + 1;
                    continue;
                }
                if (c == '{') { _i++; return (Tok.Open, "{"); }
                if (c == '}') { _i++; return (Tok.Close, "}"); }
                return (Tok.Str, c == '"' ? Quoted() : Bare());
            }
            return (Tok.End, "");
        }

        // Steam escapes only \\ and \" in these files. Any other backslash is kept as written, so a path saved
        // with single backslashes (D:\new games) is not mangled into control characters.
        private string Quoted()
        {
            var sb = new StringBuilder();
            _i++;
            while (_i < s.Length)
            {
                char c = s[_i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && _i < s.Length && (s[_i] == '\\' || s[_i] == '"')) c = s[_i++];
                sb.Append(c);
            }
            return sb.ToString(); // unterminated at end of file: keep what is there
        }

        private string Bare()
        {
            int start = _i;
            while (_i < s.Length && !char.IsWhiteSpace(s[_i]) && s[_i] is not ('"' or '{' or '}')) _i++;
            return s[start.._i];
        }
    }
}
