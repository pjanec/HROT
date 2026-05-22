using System.Text.Json;

namespace Hrot.Blueprints.Editor;

public sealed class BlueprintEditorPreferences
{
    public bool AutoReloadOnSave { get; set; } = false;
    public bool WatchPanelVisible { get; set; } = true;
    public float GraphEditorGridSnap { get; set; } = 8.0f;
    public int NodeHistorySize { get; set; } = 64;
    public int HotReloadLogMaxEntries { get; set; } = 1000;

    public static BlueprintEditorPreferences Defaults => new();

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Saves preferences to <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, s_jsonOpts);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads preferences from <paramref name="path"/>.
    /// Returns defaults if the file does not exist or cannot be parsed.
    /// </summary>
    public static BlueprintEditorPreferences Load(string path)
    {
        if (!File.Exists(path)) return Defaults;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BlueprintEditorPreferences>(json, s_jsonOpts)
                   ?? Defaults;
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }
}
