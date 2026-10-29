using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Orchestration;
using Fdp.Core.Serialization;

namespace Hrot.Orchestrator;

/// <summary>
/// Static cluster topology read at Orchestrator startup from <c>orchestrator-config.json</c>.
/// Only subsystem names are listed; node IDs are discovered dynamically via heartbeats.
/// </summary>
public sealed class ClusterConfiguration
{
    /// <summary>Subsystem NAMES that must reach <c>Standby</c> before the bootstrap latch clears. ⭐ The ROLES half of the mandatory set is <see cref="MandatoryRoles"/>.</summary>
    public string[] Mandatory { get; init; } = Array.Empty<string>();

    /// <summary>Known optional subsystem names (treated as transient observers if absent).</summary>
    public string[] Optional  { get; init; } = Array.Empty<string>();

    /// <summary>Seconds without a heartbeat before a node is considered dead and ejected.</summary>
    public float HeartbeatTimeoutSeconds { get; init; } = 5f;

    /// <summary>Capacity of the <see cref="ClusterMaster"/> 2PC history ring buffer.</summary>
    public int TransactionHistoryCapacity { get; init; } = 50;

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4087</c> — the ROLES half of the mandatory set (the bootstrap latch): at least one node holding EACH flag set here
    /// must be present before a scenario LOAD starts, alongside the <see cref="Mandatory"/> subsystem NAMES. 🔒 User, <c>2026-10-28</c>:
    /// <i>"scenario should not be able to load at all without muscle role node loaded, as it can not be edited nor started."</i>
    /// Default: a <c>Brain</c> (the scenario's creator) and a <c>MuscleGround</c> (the units' bodies). ⚠ A role counts only after its
    /// node's capability advert has landed (the mask reads <c>None</c> until then, <c>R-4005</c>); the master's own host counts through
    /// <c>ClusterMaster.LocalRoles</c> (the editor plays every simulation role in-process). <see cref="Fdp.Core.NodeRole.None"/> leaves
    /// only the names.
    /// </summary>
    public Fdp.Core.NodeRole MandatoryRoles { get; init; } = Fdp.Core.NodeRole.Brain | Fdp.Core.NodeRole.MuscleGround;

    /// <summary>
    /// ⭐⭐ <c>CE-4087</c> — how long a scenario load WAITS on the mandatory set (names and roles) before the master REFUSES it (a failed
    /// load the operator sees — never a silent hang). A booting cluster registers its nodes within seconds; 30 s is well past that and
    /// short enough that nobody stares at a dead panel.
    /// </summary>
    public double LoadWaitSeconds { get; init; } = 30.0;

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

    /// <summary>⭐ The TKB name every deployment uses unless configured otherwise — the one name in use today.</summary>
    public const string DefaultTkbName = "HrotDefault";

    /// <summary>
    /// ⭐⭐ <c>R-4018</c> — the deployment's ONE TKB, synced from <c>{NasBasePath}/tkb/&lt;TkbName&gt;.zip</c> to every node
    /// before every scenario load. ⛔ Never read from a scenario. ⭐ Works as is: the code default needs no configuration
    /// file, and a missing master copy leaves every node on its built-in catalogue (🔒 user, <c>2026-10-15</c>: <i>"The
    /// TKB name in the configuration must default to the single tkb file that we are having now … It must work with
    /// default settings as is."</i>). 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §0.
    /// </summary>
    public string TkbName { get; init; } = DefaultTkbName;

    /// <summary>⭐ CE-3341 / R-1043 — the master's INITIAL armed run mode: a BOOTSTRAP parameter (the one place every master is built through), default <see cref="ClusterArmedMode.Live"/>.
    /// The editor's one-node cluster (<see cref="ForOneNodeCluster"/>) passes <see cref="ClusterArmedMode.Preview"/> — the only bootstrap difference. ⛔ Set by code, never read from the config file.</summary>
    [JsonIgnore]
    public ClusterArmedMode InitialArmedMode { get; init; } = ClusterArmedMode.Live;

    /// <summary>⭐ CE-3346 / R-3072 (AQ110 E) — what a node joining while a scenario load is PARKED gets: the default is today's behaviour (admitted into the load). A BOOTSTRAP parameter, ⛔ set by code, never read from the config file.
    /// ⛔ <c>RefuseUntilLoaded</c> is not built — only when a deployment needs it.</summary>
    [JsonIgnore]
    public Fdp.Toolkit.Orchestration.AdmissionPolicy AdmissionPolicy { get; init; } = Fdp.Toolkit.Orchestration.AdmissionPolicy.JoinTheLoadWhenParked;

    /// <summary>Default configuration: empty mandatory list, 5 s timeout, 50-entry history.</summary>
    /// <remarks>
    /// ⭐ <c>CE-4103</c> — a FRESH instance per read, not one cached for the process: <see cref="NasBasePath"/> is read from the environment
    /// (<c>FDP_STAGING_ROOT</c>) when the instance is built, so a cached <c>Default</c> froze the FIRST reader's root for the whole process —
    /// a second host constructed in the same process (a test, a re-initialised editor) with another root prepared its scenario load against
    /// the FIRST one's NAS (measured: <c>NAS source directory '…' does not exist</c>, the load abandoned, the view left at Idle). In a real
    /// process the environment does not change, so every read is equal.
    /// </remarks>
    public static ClusterConfiguration Default => new();

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
        TkbName                    = TkbName,   // ⭐ R-4018 — the one-node cluster syncs the same TKB
        MandatoryRoles             = MandatoryRoles,    // ⭐ CE-4087 — the editor's own in-process roles satisfy it (ClusterMaster.LocalRoles)
        LoadWaitSeconds            = LoadWaitSeconds,
        InitialArmedMode           = ClusterArmedMode.Preview,   // ⭐ R-1043 — the editor is the Preview-armed host; ForOneNodeCluster is its only factory
    };

    private static readonly JsonSerializerOptions _jsonOptions = FdpJsonOptionsRegistry.DefaultRelaxed;

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
