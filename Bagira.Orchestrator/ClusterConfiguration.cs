using System.Text.Json;

namespace Bagira.Orchestrator;

/// <summary>
/// Static cluster topology read at Orchestrator startup from <c>orchestrator-config.json</c>.
/// Only subsystem names are listed; node IDs are discovered dynamically via heartbeats.
/// </summary>
public sealed class ClusterConfiguration
{
    /// <summary>Subsystem names that must reach <c>Standby</c> before the bootstrap latch clears.</summary>
    public string[] Mandatory { get; init; } = Array.Empty<string>();

    /// <summary>Known optional subsystem names (treated as transient observers if absent).</summary>
    public string[] Optional  { get; init; } = Array.Empty<string>();

    /// <summary>Seconds without a heartbeat before a node is considered dead and ejected.</summary>
    public float HeartbeatTimeoutSeconds { get; init; } = 5f;

    /// <summary>Capacity of the <see cref="DrillMaster"/> 2PC history ring buffer.</summary>
    public int TransactionHistoryCapacity { get; init; } = 50;

    /// <summary>Default configuration: empty mandatory list, 5 s timeout, 50-entry history.</summary>
    public static ClusterConfiguration Default { get; } = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads a <see cref="ClusterConfiguration"/> from <paramref name="filePath"/>.
    /// Returns <see cref="Default"/> when the file does not exist or cannot be parsed.
    /// Never throws.
    /// </summary>
    public static ClusterConfiguration LoadFrom(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return Default;
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ClusterConfiguration>(json, _jsonOptions) ?? Default;
        }
        catch
        {
            return Default;
        }
    }
}
