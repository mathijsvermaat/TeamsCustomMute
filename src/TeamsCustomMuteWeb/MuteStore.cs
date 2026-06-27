using System.Text.Json;

namespace TeamsCustomMute;

/// <summary>A chat that is muted until <see cref="ExpiresUtc"/>, at which point it auto-unmutes.</summary>
public sealed class PendingMute
{
    public string ChatName { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }

    public PendingMute() { }

    public PendingMute(string chatName, DateTime expiresUtc)
    {
        ChatName = chatName;
        ExpiresUtc = expiresUtc;
    }
}

/// <summary>Loads/saves the list of pending mutes to a JSON file under %AppData%.</summary>
public static class MuteStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TeamsCustomMute");

    private static readonly string FilePath = Path.Combine(Dir, "state.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<PendingMute> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<PendingMute>();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<PendingMute>>(json) ?? new List<PendingMute>();
        }
        catch
        {
            return new List<PendingMute>();
        }
    }

    public static void Save(IEnumerable<PendingMute> mutes)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(mutes, Options));
        }
        catch
        {
            // Persistence is best-effort; a failed write shouldn't crash the tray app.
        }
    }
}
