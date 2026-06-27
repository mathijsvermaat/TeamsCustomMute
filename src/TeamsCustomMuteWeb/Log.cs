namespace TeamsCustomMute;

/// <summary>
/// Minimal append-only file logger. Writes to
/// <c>%LocalAppData%\TeamsCustomMute\log.txt</c> so problems on machines without a
/// debugger (e.g. a downloaded release) can be diagnosed after the fact. Best-effort:
/// any logging failure is swallowed so it can never affect the app.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeamsCustomMute", "log.txt");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(LogPath, line);
        }
        catch
        {
            // logging must never throw
        }
    }
}
