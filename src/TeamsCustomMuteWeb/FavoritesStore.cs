using System.Text.Json;

namespace TeamsCustomMute;

/// <summary>Loads/saves the user's list of favorite chat names to a JSON file under %AppData%.</summary>
public static class FavoritesStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TeamsCustomMute");

    private static readonly string FilePath = Path.Combine(Dir, "favorites.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<string>();

            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return list
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static void Save(IEnumerable<string> favorites)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var clean = favorites
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(clean, Options));
        }
        catch
        {
            // Best-effort; a failed write shouldn't crash the tray app.
        }
    }

    /// <summary>Add a favorite if not already present. Returns the updated list.</summary>
    public static List<string> Add(string name)
    {
        var list = Load();
        if (!string.IsNullOrWhiteSpace(name) &&
            !list.Any(s => s.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(name.Trim());
            Save(list);
        }
        return list;
    }
}
