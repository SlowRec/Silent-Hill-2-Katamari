namespace SilentKatamari.Converter;

/// <summary>Appends to %LOCALAPPDATA%/SilentKatamari/converter.log and echoes to the console.</summary>
public sealed class Log : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly object _lock = new();
    public string? FilePath { get; }

    public Log(string? filePath)
    {
        FilePath = filePath;
        if (filePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _file = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"log file unavailable: {e.Message}");
        }
    }

    public static string DefaultPath()
    {
        string root = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "SilentKatamari", "converter.log");
    }

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {level} {message}";
        lock (_lock)
        {
            Console.WriteLine(line);
            _file?.WriteLine(line);
        }
    }

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    public void Dispose() => _file?.Dispose();
}
