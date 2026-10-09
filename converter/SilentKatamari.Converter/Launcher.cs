// Starts Garry's Mod with the arguments Melty's recipe passed through (+gamemode silentkatamari +map ...).
using System.Diagnostics;

namespace SilentKatamari.Converter;

public static class Launcher
{
    /// <summary>Same order as the original Katamari Sandbox launcher, which works on players' PCs.</summary>
    public static string? FindExe(string gmod) =>
        new[] { Path.Combine(gmod, "gmod.exe"), Path.Combine(gmod, "bin", "win64", "gmod.exe"), Path.Combine(gmod, "gmod_win64.exe") }
            .FirstOrDefault(File.Exists);

    public static bool AlreadyRunning() =>
        Process.GetProcessesByName("gmod").Length > 0 || Process.GetProcessesByName("gmod_win64").Length > 0;

    /// <summary>0 = started (or already running), 3 = Garry's Mod not found.</summary>
    public static int Start(string gmod, IReadOnlyList<string> args, Log log)
    {
        if (AlreadyRunning())
        {
            log.Info("Garry's Mod is already running; not starting another copy");
            return 0;
        }
        string? exe = FindExe(gmod);
        if (exe is null)
        {
            log.Error($"no gmod.exe in {gmod}");
            return 3;
        }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = gmod };
        foreach (var a in args) psi.ArgumentList.Add(a);
        log.Info($"starting {exe} {string.Join(' ', args)}");
        Process.Start(psi);
        return 0;
    }
}
