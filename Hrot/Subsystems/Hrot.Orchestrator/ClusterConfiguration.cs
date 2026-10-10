using System.IO;
using System.Text.Json;

namespace Hrot.Orchestrator;

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

    /// <summary>Capacity of the <see cref="ClusterMaster"/> 2PC history ring buffer.</summary>
    public int TransactionHistoryCapacity { get; init; } = 50;

    /// <summary>
    /// Base path of the shared NAS directory used by process managers to pull
    /// files from nodes.  Must differ from each node's <c>LocalTempRoot</c> to
    /// prevent source == destination errors.  Default is for single-machine dev use only.
    /// </summary>
    /// <remarks>
    /// ⭐ The default routes through <c>OrchestrationConstants.GetSharedRoot()</c> so the literal
    /// <c>"shared"</c> has ONE definition — <c>Hrot.CGF</c> cannot reference this assembly and resolves
    /// the same root from <c>Fdp.Toolkits</c>. 📄 <c>SharedDirectoryName</c>'s remarks carry the measurement.
    /// </remarks>
    public string NasBasePath { get; init; } = Fdp.Toolkit.Orchestration.OrchestrationConstants.GetSharedRoot();

    /// <summary>Default configuration: empty mandatory list, 5 s timeout, 50-entry history.</summary>
    public static ClusterConfiguration Default { get; } = new();

    /// <summary>
    /// ⭐ Q86 §4-E (R-215) — the ONE place every host reads its cluster configuration from:
    /// <c>orchestrator-config.json</c> in the working directory, else <see cref="Default"/>.
    /// </summary>
    public static ClusterConfiguration LoadFromWorkingDirectory()
        => LoadFrom(Path.Combine(Directory.GetCurrentDirectory(), "orchestrator-config.json"));

    /// <summary>
    /// ⭐ Q86 §4-E — the same configuration for a ONE-NODE cluster (the editor): nothing is mandatory, because the
    /// host is the only node. Every other value — the NAS root above all — is kept, so both hosts store in one place.
    /// </summary>
    public ClusterConfiguration ForOneNodeCluster() => new()
    {
        Mandatory                  = Array.Empty<string>(),
        Optional                   = Optional,
        HeartbeatTimeoutSeconds    = HeartbeatTimeoutSeconds,
        TransactionHistoryCapacity = TransactionHistoryCapacity,
        NasBasePath                = NasBasePath,
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads a <see cref="ClusterConfiguration"/> from <paramref name="filePath"/>.
    /// <para>
    /// <b>Rule:</b> If the file is <em>absent</em>, <see cref="Default"/> is returned (zero-config dev mode).
    /// If the file <em>exists</em> but is unreadable or contains invalid JSON, a clear
    /// <see cref="InvalidOperationException"/> is thrown — fail-fast prevents silent misconfiguration.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="filePath"/> exists but cannot be read or deserialized.
    /// </exception>
    public static ClusterConfiguration LoadFrom(string filePath)
    {
        if (!File.Exists(filePath)) return Default;

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[Orchestrator] Failed to read cluster configuration from '{filePath}': {ex.Message}", ex);
        }

        ClusterConfiguration? result;
        try
        {
            result = JsonSerializer.Deserialize<ClusterConfiguration>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[Orchestrator] Failed to deserialize cluster configuration from '{filePath}': {ex.Message}", ex);
        }

        return result ?? throw new InvalidOperationException(
            $"[Orchestrator] Cluster configuration file '{filePath}' deserialized to null.");
    }
}
