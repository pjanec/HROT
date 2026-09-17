using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;

namespace Hrot.SimHost.Orchestration.Handlers;

/// <summary>
/// Cluster state handler that intercepts <see cref="NodeOpType.PrepareLive"/> and
/// <see cref="NodeOpType.PrepareEdit"/> to load the scenario's TERRAIN — its definition asset and the
/// road network(s) that definition declares — before the scenario is deserialized.
///
/// <para><b>⭐ It is <c>TkbLoadClusterStateHandler</c> with a different artifact</b>: the name comes from
/// the same locally staged scenario header, the artifact from the same local staging area, it intercepts
/// the same two node ops for the same reason (entities depend on terrain for ground clamping, physics and
/// LOS), it uses the same differential cache keyed on <c>(name, file timestamp)</c>, and a scenario with
/// no terrain name is legal and loads. ⛔ Resolution, caching and ordering were NOT designed afresh.</para>
///
/// <para><b>⚠ ONE DELIBERATE DEVIATION from "mirror field for field", and it is required.</b>
/// <c>TkbLoadClusterStateHandler</c> does all its work in <c>PrepareAsync</c> and has a no-op
/// <c>Commit</c> — legal for it because <c>ITkbDatabase</c> is NOT ECS. Terrain publishes ECS singletons,
/// and <see cref="IClusterStateHandler.PrepareAsync"/> is contractually forbidden from mutating ECS. So
/// this splits: <b>PrepareAsync parses and loads into a STAGED buffer</b> (pure I/O, no ECS), and
/// <b>Commit publishes</b> on the main thread. That is exactly what the design's own §3.1 sequence draws
/// ("build into STAGED buffer (no ECS mutation)" → "publish staged → ZoneEnvironmentData").</para>
///
/// <para><b>⛔ THE ROAD BLOB IS OWNED BY THE HOLDER, NOT BY THE SINGLETON.</b> The previous graph is NOT
/// disposed inline — that is the use-after-free this programme just closed (a background solver may be
/// mid-traversal). <see cref="RoadNetworkHolder.Publish"/> retires the old generation and frees it when
/// its last reader releases; the <c>ZoneEnvironmentData</c> singleton carries a non-owning copy of the
/// struct for the synchronous readers.</para>
///
/// <para><b>⭐ Registered UNCONDITIONALLY on every ECS host.</b> ⛔ It must never hang off the scenario
/// LOAD handlers: those are registered inside a conditional that a pure MuscleGround node fails, and the
/// muscle is precisely the role that consumes terrain (road network → <c>CarKinematicsSystem</c>).
/// Hanging terrain off them would leave it unloaded on the node that needs it most.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §2.1e ②a ③ ④, §2.1d, §3.1.
/// </summary>
public sealed class TerrainLoadClusterStateHandler : IClusterStateHandler
{
    /// <summary>Everything <c>PrepareAsync</c> built, waiting for <c>Commit</c> to publish it.</summary>
    private sealed class Staged
    {
        public TerrainDefinition? Definition;
        public RoadNetworkBlob    RoadNetwork;
        public bool               HasRoadNetwork;
        public string?            TerrainName;
        public DateTime           FileTimestamp;
    }

    private readonly string _stagingRoot;
    private readonly string _terrainRoot;
    private readonly RoadNetworkHolder _roadNetworkHolder;

    // Differential cache — identical shape to the TKB handler's.
    private string?  _lastLoadedTerrainName;
    private DateTime _lastLoadedTimestamp;

    // Staged state keyed by transaction, so two overlapping rounds cannot clobber each other.
    private readonly Dictionary<Guid, Staged> _staged = new();
    private readonly object _stagedGate = new();

    /// <param name="localStagingRoot">
    /// Root of the node's local staging area. Terrain definition assets are expected under
    /// <c>{localStagingRoot}/Terrain/</c>.
    /// </param>
    /// <param name="roadNetworkHolder">
    /// ⭐ <b>Required.</b> The holder is what owns published graphs and what makes a swap safe against a
    /// background reader. ⛔ Passing none is not "no road support" — it is a silent never-reload plus an
    /// unowned blob, so it is rejected here rather than defaulted (the silent-default pattern).
    /// </param>
    public TerrainLoadClusterStateHandler(string localStagingRoot, RoadNetworkHolder roadNetworkHolder)
    {
        if (string.IsNullOrWhiteSpace(localStagingRoot))
            throw new ArgumentException("A local staging root is required.", nameof(localStagingRoot));

        _stagingRoot = localStagingRoot;
        _terrainRoot = Path.Combine(localStagingRoot, "Terrain");
        _roadNetworkHolder = roadNetworkHolder
            ?? throw new ArgumentNullException(nameof(roadNetworkHolder),
                "The terrain loader must be given a RoadNetworkHolder: it owns the published road graph "
              + "and is what keeps a retired graph alive while a background solver is still inside it.");
    }

    /// <inheritdoc/>
    public bool CanHandle(NodeOpType operation) =>
        operation == NodeOpType.PrepareLive || operation == NodeOpType.PrepareEdit;

    /// <inheritdoc/>
    /// <remarks>⛔ Pure I/O. Mutates no ECS state — see the class remarks.</remarks>
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        string? requested = ExtractTerrainNameFromLocalScenario(_stagingRoot);

        // ── Graceful absence: a scenario with no terrain is legal, exactly like one with no TKB. ──
        if (string.IsNullOrWhiteSpace(requested))
        {
            _lastLoadedTerrainName = null;
            FdpLog<TerrainLoadClusterStateHandler>.Info(
                "[TerrainLoad] Scenario names no terrain — nothing to load. This is legal.");
            return Task.FromResult<object?>(null);
        }

        string definitionPath = Path.Combine(_terrainRoot, $"{requested}.json");

        DateTime currentFileTime = File.Exists(definitionPath)
            ? File.GetLastWriteTimeUtc(definitionPath)
            : DateTime.MinValue;

        // ── Differential cache: same name, same file ⇒ no re-ingestion. ──
        if (_lastLoadedTerrainName == requested && _lastLoadedTimestamp == currentFileTime)
        {
            FdpLog<TerrainLoadClusterStateHandler>.Info(
                "[TerrainLoad] Terrain '{0}' already resident and unchanged — skipping re-ingestion.",
                requested);
            return Task.FromResult<object?>(null);
        }

        if (!File.Exists(definitionPath))
            throw new FileNotFoundException(
                $"[TerrainLoad] Terrain definition not found at '{definitionPath}'. The scenario names "
              + $"terrain '{requested}'; loading the terrain a scenario names is mandatory, so this is a "
              + "broken configuration rather than a terrain-less scenario.",
                definitionPath);

        var definition = TerrainDefinitionParser.Parse(File.ReadAllText(definitionPath));

        var staged = new Staged
        {
            Definition    = definition,
            TerrainName   = requested,
            FileTimestamp = currentFileTime,
        };

        // ── The road network(s) the terrain declares. This is the job taken over from the retiring
        //    per-zone RoadNetworkPath: roads are a property of the TERRAIN, not of any zone. ──
        if (definition.RoadNetworks.Count > 0)
        {
            // ⚠ Slice 1 loads the FIRST declared network: ZoneEnvironmentData carries exactly one
            //   RoadNetworkBlob, so merging N graphs is a model change, not a loop. Declaring more than
            //   one is not silently half-honoured — it says so.
            if (definition.RoadNetworks.Count > 1)
                FdpLog<TerrainLoadClusterStateHandler>.Info(
                    "[TerrainLoad] Terrain '{0}' declares {1} road networks; slice 1 loads the first "
                  + "('{2}') because ZoneEnvironmentData holds exactly one blob.",
                    requested, definition.RoadNetworks.Count, definition.RoadNetworks[0]);

            string roadPath = ResolveRelative(definitionPath, definition.RoadNetworks[0]);
            if (!File.Exists(roadPath))
                throw new FileNotFoundException(
                    $"[TerrainLoad] Terrain '{requested}' declares road network '{definition.RoadNetworks[0]}', "
                  + $"which does not exist at '{roadPath}'.", roadPath);

            staged.RoadNetwork    = RoadNetworkLoader.LoadFromJson(roadPath);
            staged.HasRoadNetwork = true;
        }

        lock (_stagedGate) _staged[intent.TransactionId] = staged;
        return Task.FromResult<object?>(null);
    }

    /// <inheritdoc/>
    /// <remarks>Main thread. This is where ECS is touched, and only here.</remarks>
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        Staged? staged = TakeStaged(intent.TransactionId);
        if (staged == null) return;            // cache hit, no terrain, or nothing prepared

        // A no-ECS host (ExCon / CGF skeleton) still ACKs; it simply has nowhere to publish.
        if (repo == null)
        {
            if (staged.HasRoadNetwork) staged.RoadNetwork.Dispose();
            return;
        }

        repo.RegisterManagedComponent<TerrainDefinition>();
        repo.SetSingletonManaged(staged.Definition!);

        if (staged.HasRoadNetwork)
        {
            // ⛔ The previous blob is NOT disposed here. The holder retires it and frees it once its last
            //    reader releases — see the class remarks.
            _roadNetworkHolder.Publish(staged.RoadNetwork);
            repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = staged.RoadNetwork });
        }

        _lastLoadedTerrainName = staged.TerrainName;
        _lastLoadedTimestamp   = staged.FileTimestamp;

        FdpLog<TerrainLoadClusterStateHandler>.Info(
            "[TerrainLoad] Terrain '{0}' committed ({1} road network(s) declared, {2}).",
            staged.TerrainName ?? "(unnamed)", staged.Definition!.RoadNetworks.Count,
            staged.HasRoadNetwork ? "road graph published" : "no road graph");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Frees what <c>PrepareAsync</c> built but never published. ⚠ The differential cache is deliberately
    /// NOT advanced on abort, so the next attempt re-ingests rather than believing a load that never
    /// committed.
    /// </remarks>
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
    {
        Staged? staged = TakeStaged(intent.TransactionId);
        if (staged?.HasRoadNetwork == true) staged.RoadNetwork.Dispose();
    }

    private Staged? TakeStaged(Guid transactionId)
    {
        lock (_stagedGate)
        {
            if (!_staged.TryGetValue(transactionId, out var staged)) return null;
            _staged.Remove(transactionId);
            return staged;
        }
    }

    /// <summary>Resolves a definition-relative asset path against the definition's own folder.</summary>
    private static string ResolveRelative(string definitionPath, string relative)
        => Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(Path.GetDirectoryName(definitionPath) ?? string.Empty, relative);

    /// <summary>
    /// Peeks <c>TerrainName</c> from the node's locally staged scenario header, forward-only.
    /// <para>⚠ The staged header lives under <c>{root}/TKB/ScenarioHeader.json</c> — that is where the TKB
    /// handler writes and reads it, and there is ONE staged header per node. Terrain reads the same file
    /// rather than inventing a second location that could disagree about which scenario is staged.</para>
    /// </summary>
    private static string? ExtractTerrainNameFromLocalScenario(string localStagingRoot)
    {
        string headerPath = Path.Combine(localStagingRoot, "TKB", "ScenarioHeader.json");
        if (!File.Exists(headerPath)) return null;

        var reader = new Utf8JsonReader(File.ReadAllBytes(headerPath));
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.ValueTextEquals("TerrainName"))
            {
                reader.Read();
                return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            }
        }
        return null;
    }
}
