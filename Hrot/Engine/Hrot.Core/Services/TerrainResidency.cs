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
        internal TerrainWorld?      World;
        internal RoadNetworkBlob    RoadNetwork;
        internal bool               HasRoadNetwork;
        internal string?            TerrainName;
        internal DateTime           FileTimestamp;
        internal Fdp.Toolkit.Navigation.INavmeshProvider? Navmesh;

        /// <summary>⭐ True when this staged result would actually change residency.</summary>
        public bool HasWork => Definition != null;

        /// <summary>The terrain this staged result is for, or <c>null</c> when there was nothing to do.</summary>
        public string? Name => TerrainName;
    }

    private readonly TerrainCatalog _catalog;
    private readonly RoadNetworkHolder _roadNetworkHolder;

    // ⭐ W6 — set only on a node that solves navigation (AttachNavmesh). Null ⇒ no bake, which is correct for a
    //   node without the NavigationSolver role (IG, a viewer).
    private Fdp.Toolkit.Navigation.INavmeshFactory?            _navmeshFactory;
    private Fdp.Toolkit.Navigation.SwitchableNavmeshProvider?  _navmesh;

    // ⭐ The idempotency branch, and the only implementation of it: same name + same files ⇒ no work.
    private string?  _lastLoadedTerrainName;
    private DateTime _lastLoadedTimestamp;

    /// <summary>
    /// The production constructor: resolves terrains through the standard node search list —
    /// <c>{staging}/Terrain</c>, then the shared NAS stand-in, then the shipped terrains (W12).
    /// </summary>
    public TerrainResidency(string localStagingRoot, RoadNetworkHolder roadNetworkHolder)
        : this(BuildNodeCatalog(localStagingRoot), roadNetworkHolder)
    {
    }

    /// <summary>Resolves through an explicit <paramref name="catalog"/> (tests, tools).</summary>
    public TerrainResidency(TerrainCatalog catalog, RoadNetworkHolder roadNetworkHolder)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _roadNetworkHolder = roadNetworkHolder
            ?? throw new ArgumentNullException(nameof(roadNetworkHolder),
                "The terrain loader must be given a RoadNetworkHolder: it owns the published road graph "
              + "and keeps a retired graph alive while a background solver is still inside it.");
    }

    private static TerrainCatalog BuildNodeCatalog(string localStagingRoot)
    {
        if (string.IsNullOrWhiteSpace(localStagingRoot))
            throw new ArgumentException("A local staging root is required.", nameof(localStagingRoot));
        return TerrainCatalog.ForNode(
            localStagingRoot, Fdp.Toolkit.Orchestration.OrchestrationConstants.GetSharedRoot());
    }

    /// <summary>The catalog this residency resolves terrain names through.</summary>
    public TerrainCatalog Catalog => _catalog;

    public string? ResidentTerrainName => _lastLoadedTerrainName;

    /// <summary>
    /// ⭐⭐ <b>W6 — make this node bake a navmesh from every terrain it loads</b> (docs/DESIGN_Terrain_World.md §4.1).
    /// The bake runs in <see cref="Prepare"/> (off the main thread); <see cref="Commit"/> publishes it into
    /// <paramref name="target"/> — the one provider the node's navigation singleton and its background solver
    /// share. ⛔ A host that composes a navigation solver MUST call this (the silent-default rule): without it the
    /// solver plans straight lines through buildings.
    /// </summary>
    public void AttachNavmesh(
        Fdp.Toolkit.Navigation.INavmeshFactory factory, Fdp.Toolkit.Navigation.SwitchableNavmeshProvider target)
    {
        _navmeshFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _navmesh        = target  ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The navmesh this residency publishes into, when attached.</summary>
    public Fdp.Toolkit.Navigation.SwitchableNavmeshProvider? Navmesh => _navmesh;

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

        // ⭐ W12 — a terrain is a FOLDER, resolved by the one catalog (staging → shared → shipped).
        string? definitionPath = _catalog.ResolveDefinition(terrainName);
        if (definitionPath == null)
            throw new FileNotFoundException(
                $"[Terrain] Terrain '{terrainName}' was not found: no '{terrainName}/{TerrainCatalog.DefinitionFileName}' "
              + $"under any of [{string.Join(", ", _catalog.Roots)}]. The scenario names this terrain; loading the "
              + "terrain a scenario names is mandatory, so this is a broken configuration rather than a "
              + "terrain-less scenario.",
                terrainName);

        DateTime currentFileTime = LatestWriteTime(definitionPath);

        if (_lastLoadedTerrainName == terrainName && _lastLoadedTimestamp == currentFileTime)
        {
            FdpLog<TerrainResidency>.Info(
                "[Terrain] Terrain '{0}' already resident and unchanged — skipping re-ingestion.",
                terrainName);
            return new Staged();
        }

        var definition = TerrainDefinitionParser.Parse(File.ReadAllText(definitionPath));

        var staged = new Staged
        {
            Definition    = definition,
            TerrainName   = terrainName,
            FileTimestamp = currentFileTime,
        };

        // ⭐ Schema v2 — the terrain WORLD every derived query reads (docs/DESIGN_Terrain_World.md).
        if (!string.IsNullOrEmpty(definition.World))
        {
            string worldPath = ResolveRelative(definitionPath, definition.World);
            if (!File.Exists(worldPath))
                throw new FileNotFoundException(
                    $"[Terrain] Terrain '{terrainName}' declares world '{definition.World}', which does not exist "
                  + $"at '{worldPath}'.", worldPath);
            staged.World = TerrainWorldParser.Parse(File.ReadAllText(worldPath), terrainName);

            // ⭐ W6 — the bake rides Prepare, whose contract is already "off-thread, no ECS mutation".
            if (_navmeshFactory != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                staged.Navmesh = _navmeshFactory.Build(staged.World);
                FdpLog<TerrainResidency>.Info(
                    $"[Terrain] Navmesh for '{terrainName}' baked in {sw.ElapsedMilliseconds} ms"
                  + (staged.Navmesh == null ? " — nothing walkable." : "."));
            }
        }

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
        // ⭐ CE-3015 — remember the name the scenario resolved it by, so a save writes it back.
        staged.Definition!.ResolvedName = staged.TerrainName ?? string.Empty;
        world.SetSingletonManaged(staged.Definition!);

        // ⭐ The world model — every ECS node holds it (map, LOS, movement Z, navmesh source). A terrain
        //   with no world file still publishes an EMPTY world, so a re-load never leaves the previous
        //   terrain's buildings behind.
        world.RegisterManagedComponent<TerrainWorld>();
        world.SetSingletonManaged(staged.World ?? new TerrainWorld { Name = staged.TerrainName });   // CE-3028: named even when it has no world file

        // ⭐ W9 / CE-3018 — the spatial grids REBASE to this world on their own threads (SpatialHashSystem,
        //   LocalGridBuilderSystem — docs/DESIGN_Terrain_World.md §4.4); log where they will sit, and stay LOUD if even the
        //   fitted grids cannot cover it.
        if (staged.World != null)
        {
            FdpLog<TerrainResidency>.Info($"[Terrain] '{staged.TerrainName}': {TerrainGridCoverage.Describe(staged.World)}");
            foreach (var problem in TerrainGridCoverage.Problems(staged.World))
                FdpLog<TerrainResidency>.Warn($"[Terrain] '{staged.TerrainName}': {problem}");
        }

        // ⭐ W6 — publish the bake (null ⇒ the straight-line fallback, so a terrain without a world never
        //   keeps the previous terrain's navmesh).
        _navmesh?.Publish(staged.Navmesh);

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
            $"[Terrain] Terrain '{staged.TerrainName ?? "(unnamed)"}' committed "
          + $"({staged.Definition!.RoadNetworks.Count} road network(s) declared, "
          + (staged.HasRoadNetwork ? "road graph published" : "no road graph")
          + $"; world: {staged.World?.Prisms.Count ?? 0} prism(s), {staged.World?.Walkables.Count ?? 0} walkable(s), "
          + $"{staged.World?.Surfaces.Count ?? 0} surface(s)).");
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
        if (world != null && world.HasSingletonManaged<TerrainWorld>())
            world.SetSingletonManaged(new TerrainWorld());
        _navmesh?.Publish(null);

        _lastLoadedTerrainName = null;
        _lastLoadedTimestamp   = default;

        FdpLog<TerrainResidency>.Info("[Terrain] Terrain residency released (standby).");
    }

    /// <summary>
    /// The newest write time of anything in the terrain folder — the idempotency key, so editing the world
    /// file (not just the definition) makes the next load re-ingest.
    /// </summary>
    private static DateTime LatestWriteTime(string definitionPath)
    {
        var folder = Path.GetDirectoryName(definitionPath);
        var latest = File.GetLastWriteTimeUtc(definitionPath);
        if (folder != null)
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                var t = File.GetLastWriteTimeUtc(f);
                if (t > latest) latest = t;
            }
        return latest;
    }

    /// <summary>Resolves a definition-relative asset path against the definition's own folder.</summary>
    private static string ResolveRelative(string definitionPath, string relative)
        => Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(Path.GetDirectoryName(definitionPath) ?? string.Empty, relative);
}
