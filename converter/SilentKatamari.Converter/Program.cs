// silent-katamari-convert <probe|convert|play> --gmod <GarrysMod folder> [--force] [--only ouak|sh2] [game args...]
//   probe    read-only report of both games (also saved to %LOCALAPPDATA%/SilentKatamari/probe.json)
//   convert  read the player's games into garrysmod/addons/silent_katamari_content (Melty's setup step)
//   play     convert when a game changed, then start Garry's Mod with the remaining arguments (Melty's launch)
using System.Text.Json;

namespace SilentKatamari.Converter;

public static class Program
{
    public sealed record Options(string Command, string? Gmod, bool Force, string? Only, List<string> Passthrough);

    public static Options Parse(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        string? gmod = null, only = null;
        bool force = false;
        var rest = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--") { rest.AddRange(args[(i + 1)..]); break; }
            if (a == "--gmod" && i + 1 < args.Length) { gmod = args[++i]; continue; }
            if (a == "--force") { force = true; continue; }
            if (a == "--only" && i + 1 < args.Length) { only = args[++i]; continue; }
            rest.Add(a); // anything else goes to Garry's Mod, as the original launcher did
        }
        return new Options(cmd, gmod, force, only, rest);
    }

    public static int Main(string[] args)
    {
        var o = Parse(args);
        if (o.Command is not ("probe" or "convert" or "play"))
        {
            Console.Error.WriteLine("usage: silent-katamari-convert <probe|convert|play> --gmod <GarrysMod folder> [--force] [--only ouak|sh2] [game args...]");
            return 2;
        }
        if (o.Gmod is null || !Directory.Exists(Path.Combine(o.Gmod, "garrysmod")))
        {
            Console.Error.WriteLine($"--gmod must be the Garry's Mod folder (the one that contains garrysmod/); got '{o.Gmod}'");
            return 2;
        }
        using var log = new Log(Log.DefaultPath());
        log.Info($"silent-katamari-convert {Runner.Version} {o.Command} --gmod {o.Gmod}");
        var runner = new Runner(o.Gmod, log);
        try
        {
            switch (o.Command)
            {
                case "probe":
                {
                    var report = runner.Probe(o.Only);
                    string json = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    Console.WriteLine(json);
                    string path = Path.Combine(Path.GetDirectoryName(Log.DefaultPath())!, "probe.json");
                    File.WriteAllText(path, json);
                    log.Info($"probe saved to {path}");
                    return 0;
                }
                case "convert":
                    runner.Convert();
                    return 0;
                default: // play
                    try
                    {
                        string why = "forced";
                        if (o.Force || runner.NeedsConvert(out why))
                        {
                            log.Info($"converting: {why}");
                            runner.Convert();
                        }
                    }
                    catch (Exception e)
                    {
                        // Never block the launch: the game itself tells the player what is missing.
                        log.Error($"conversion failed, starting anyway: {e}");
                    }
                    return Launcher.Start(o.Gmod, o.Passthrough, log);
            }
        }
        catch (Exception e)
        {
            log.Error(e.ToString());
            return 1;
        }
    }
}
