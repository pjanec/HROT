using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.Logging;
using CarKinem.Road;
using Fdp.Toolkit.Terrain;

namespace Hrot.Map.Common.Services;

/// <summary>
/// ⭐⭐⭐ <c>L5</c> — <b>making the NAMED terrain resident, as a service with several callers.</b>
///
/// <para>🔒 <b>User, <c>2026-09-18</c>:</b> <i>"the scenario load should do the terrain load first; terrain
/// load should happen as part of scenario load unless the terrain is already loaded; no separate
/// terrain-preload step is needed at the moment (although it could happen later — but that would likely
/// require new cluster node operation) — so terrain loading should be callable from multiple places
/// (pre-load or scenario load) … terrain loading should be idempotent; and we might need to support
/// terrain unloading as well if we wanted (in the future) the hosts to go to some kind of clean
/// low-memory standby mode without restarting them."</i></para>
///
/// <para>⭐⭐ <b>Why a service and not a cluster handler.</b> A handler CLAIMS an operation, and
/// <c>ClusterSlave</c> gives an operation to exactly one claimant — so a terrain loader registered as a
/// handler cancels whatever else needed that operation. 🔴 Measured: it cancelled CGF's scenario loader
/// and the cluster loaded zero entities. ⇒ the work lives here, with callers instead of claims:
/// <see cref="LoadPhase.TerrainLoadStep"/> today, a future operator-driven preload tomorrow, and
/// <see cref="Unload"/> whenever the standby mode arrives.</para>
///
/// <para>⭐ <b>The prepare/commit split is not ceremony.</b> A cluster round must do its I/O off the main
/// thread in a phase that may still abort, and its ECS writes on the main thread at commit — so the two
/// halves are separately callable. <see cref="EnsureTerrain"/> is the convenience for a caller that is
/// NOT inside a 2PC round.</para>
///
/// <para>📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §4.1c, <c>L5</c> ·
/// <c>docs/DESIGN_Terrain_Zones_And_Assets.md</c> §2.1d, §2.1e ①–③.</para>
/// </summary>
public sealed class TerrainResidency
{
    /// <summary>Everything a prepare built, waiting for a commit to publish it.</summary>
    public sealed class Staged
    {
        internal TerrainDefinition? Definition;
        internal RoadNetworkBlob    RoadNetwork;
        internal bool               HasRoadNetwork;
        internal string?            TerrainName;
        internal DateTime           FileTimestamp;

        /// <summary>⭐ True when this staged result would actually change residency.</summary>
        public bool HasWork => Definition != null;

        /// <summary>The terrain this staged result is for, or <c>null</c> when there was nothing to do.</summary>
        public string? Name => TerrainName;
    }

    private readonly string _terrainRoot;
    private readonly RoadNetworkHolder _roadNetworkHolder;

    // ⭐ The idempotency branch, and the only implementation of it: same name + same file ⇒ no work.
    private string?  _lastLoadedTerrainName;
    private DateTime _lastLoadedTimestamp;

    /// <param name="localStagingRoot">
    /// The node's own staging root. Terrain definitions are read from <c>{root}/Terrain/</c>.
    /// </param>
    /// <param name="roadNetworkHolder">
    /// ⭐ <b>Required.</b> The holder owns published graphs and is what makes a swap safe against a
    /// background reader still inside the old one. ⛔ Passing none is not "no road support" — it is a
    /// silent never-reload plus an unowned blob.
    /// </param>
    public TerrainResidency(string localStagingRoot, RoadNetworkHolder roadNetworkHolder)
    {
        if (string.IsNullOrWhiteSpace(localStagingRoot))
            throw new ArgumentException("A local staging root is required.", nameof(localStagingRoot));

        _terrainRoot = Path.Combine(localStagingRoot, "Terrain");
        _roadNetworkHolder = roadNetworkHolder
            ?? throw new ArgumentNullException(nameof(roadNetworkHolder),
                "The terrain loader must be given a RoadNetworkHolder: it owns the published road graph "
              + "and keeps a retired graph alive while a background solver is still inside it.");
    }

    /// <summary>⭐ The terrain currently resident, or <c>null</c>. Read by the identity check.</summary>
    public string? ResidentTerrainName => _lastLoadedTerrainName;

    /// <summary>
    /// ⭐⭐ <b>The I/O half — safe off the main thread, touches no ECS.</b>
    ///
    /// <para>⚠ Returns a staged result with <see cref="Staged.HasWork"/> <c>false</c> when the scenario
    /// names no terrain (legal and silent) or when the named terrain is already resident and unchanged
    /// (the idempotent no-op the user asked for: <i>"unless the terrain is already loaded"</i>).</para>
    ///
    /// <para>⛔ <b>A named-but-missing definition THROWS.</b> Loading the terrain a scenario names is
    /// mandatory, so that is a broken deployment rather than a terrain-less scenario.</para>
    /// </summary>
    public Staged Prepare(string? terrainName)
    {
        if (string.IsNullOrWhiteSpace(terrainName))
        {
            _lastLoadedTerrainName = null;
            FdpLog<TerrainResidency>.Info(
                "[Terrain] Scenario names no terrain — nothing to load. This is legal.");
            return new Staged();
        }

        string definitionPath = Path.Combine(_terrainRoot, $"{terrainName}.json");

        DateTime currentFileTime = File.Exists(definitionPath)
            ? File.GetLastWriteTimeUtc(definitionPath)
            : DateTime.MinValue;

        if (_lastLoadedTerrainName == terrainName && _lastLoadedTimestamp == currentFileTime)
        {
            FdpLog<TerrainResidency>.Info(
                "[Terrain] Terrain '{0}' already resident and unchanged — skipping re-ingestion.",
                terrainName);
            return new Staged();
        }

        if (!File.Exists(definitionPath))
            throw new FileNotFoundException(
                $"[Terrain] Terrain definition not found at '{definitionPath}'. The scenario names terrain "
              + $"'{terrainName}'; loading the terrain a scenario names is mandatory, so this is a broken "
              + "configuration rather than a terrain-less scenario.",
                definitionPath);

        var definition = TerrainDefinitionParser.Parse(File.ReadAllText(definitionPath));

        var staged = new Staged
        {
            Definition    = definition,
            TerrainName   = terrainName,
            FileTimestamp = currentFileTime,
        };

        // ── The road network(s) the terrain declares: roads are a property of the TERRAIN, not of a zone.
        if (definition.RoadNetworks.Count > 0)
        {
            // ⚠ Slice 1 loads the FIRST declared network — ZoneEnvironmentData carries exactly one blob,
            //   so merging N graphs is a model change, not a loop. It is not silently half-honoured.
            if (definition.RoadNetworks.Count > 1)
                FdpLog<TerrainResidency>.Info(
                    "[Terrain] Terrain '{0}' declares {1} road networks; slice 1 loads the first ('{2}') "
                  + "because ZoneEnvironmentData holds exactly one blob.",
                    terrainName, definition.RoadNetworks.Count, definition.RoadNetworks[0]);

            string roadPath = ResolveRelative(definitionPath, definition.RoadNetworks[0]);
            if (!File.Exists(roadPath))
                throw new FileNotFoundException(
                    $"[Terrain] Terrain '{terrainName}' declares road network '{definition.RoadNetworks[0]}', "
                  + $"which does not exist at '{roadPath}'.", roadPath);

            staged.RoadNetwork    = RoadNetworkLoader.LoadFromJson(roadPath);
            staged.HasRoadNetwork = true;
        }

        return staged;
    }

    /// <summary>
    /// ⭐⭐ <b>The ECS half — main thread, and the only place ECS is written.</b>
    /// A staged result with no work, or a host with no world, commits nothing and still succeeds.
    /// </summary>
    public void Commit(EntityRepository? world, Staged? staged)
    {
        if (staged == null || !staged.HasWork) return;

        if (world == null)
        {
            // A no-ECS host still participates; it simply has nowhere to publish.
            if (staged.HasRoadNetwork) staged.RoadNetwork.Dispose();
            return;
        }

        world.RegisterManagedComponent<TerrainDefinition>();
        world.SetSingletonManaged(staged.Definition!);

        if (staged.HasRoadNetwork)
        {
            // ⛔ The previous blob is NOT disposed here — the holder retires it and frees it once its last
            //    reader releases.
            _roadNetworkHolder.Publish(staged.RoadNetwork);
            world.SetSingleton(new ZoneEnvironmentData { RoadNetwork = staged.RoadNetwork });
        }

        _lastLoadedTerrainName = staged.TerrainName;
        _lastLoadedTimestamp   = staged.FileTimestamp;

        FdpLog<TerrainResidency>.Info(
            "[Terrain] Terrain '{0}' committed ({1} road network(s) declared, {2}).",
            staged.TerrainName ?? "(unnamed)", staged.Definition!.RoadNetworks.Count,
            staged.HasRoadNetwork ? "road graph published" : "no road graph");
    }

    /// <summary>
    /// ⭐ Frees what a prepare built and never published.
    /// ⚠ The idempotency cache is deliberately NOT advanced, so the next attempt re-ingests rather than
    /// believing a load that never committed.
    /// </summary>
    public void AbortStaged(Staged? staged)
    {
        if (staged?.HasRoadNetwork == true) staged.RoadNetwork.Dispose();
    }

    /// <summary>
    /// ⭐⭐ <b>The convenience for a caller that is NOT inside a 2PC round</b> — prepare and commit in one
    /// go, on the main thread. This is the entry point a future operator-driven terrain PRELOAD would use;
    /// it needs no new cluster operation.
    /// </summary>
    /// <returns><c>true</c> when residency changed, <c>false</c> when the call was an idempotent no-op.</returns>
    public bool EnsureTerrain(EntityRepository? world, string? terrainName)
    {
        var staged = Prepare(terrainName);
        if (!staged.HasWork) return false;
        Commit(world, staged);
        return true;
    }

    /// <summary>
    /// ⛔⛔ <b>DELIBERATELY UNWIRED.</b> Nothing calls this, by design — 🔒 the user asked for the
    /// capability to exist for a future <i>"clean low-memory standby mode without restarting"</i> the
    /// hosts, and explicitly did not ask for it to be used yet.
    ///
    /// <para>⚠ It is a real implementation rather than a stub, so that the first caller does not have to
    /// discover what unloading means: the singleton goes, the holder retires its graph (freeing it once the
    /// last background reader releases), and the idempotency cache is cleared so a later load re-ingests.</para>
    ///
    /// <para>⛔ <b>Do not wire this into the load path.</b> A load already replaces residency through
    /// <see cref="Commit"/>; unloading first would drop the graph a background solver may still be inside,
    /// for no benefit.</para>
    /// </summary>
    public void Unload(EntityRepository? world)
    {
        // An empty blob retires the live one through the holder's normal generation swap.
        _roadNetworkHolder.Publish(default);

        if (world != null && world.HasSingleton<ZoneEnvironmentData>())
            world.SetSingleton(new ZoneEnvironmentData { RoadNetwork = default });

        _lastLoadedTerrainName = null;
        _lastLoadedTimestamp   = default;

        FdpLog<TerrainResidency>.Info("[Terrain] Terrain residency released (standby).");
    }

    /// <summary>Resolves a definition-relative asset path against the definition's own folder.</summary>
    private static string ResolveRelative(string definitionPath, string relative)
        => Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(Path.GetDirectoryName(definitionPath) ?? string.Empty, relative);
}
