using System;
using System.Collections.Generic;
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
        /// <summary>⭐ CE-4122 — the empty world a terrain with NO world file hands its content loaders, published as the singleton (one object, so what a loader attaches survives).</summary>
        internal TerrainWorld?      PreparedEmptyWorld;
        internal RoadNetworkBlob    RoadNetwork;
        internal bool               HasRoadNetwork;
        internal string?            TerrainName;
        internal DateTime           FileTimestamp;
        internal long               BakeSerial = -1;
        internal string?            BaseHash;        // ⭐ O-8 — the base terrain's content hash, known when an overlay is merged
        internal string?            OverlayFolder;   // ⭐ O-4 — the CONTENT HASH of the overlay merged into World, if any
        internal Fdp.Toolkit.Navigation.INavmeshProvider? Navmesh;
        internal Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider? Cover;
        /// <summary>⭐ AQ93 R-4021 — what each attached <see cref="ITerrainContentLoader"/> prepared, committed in attach order.</summary>
        internal List<(ITerrainContentLoader Loader, object? Prepared)> Contents = new();
        /// <summary>⭐ <c>CE-3075</c> — the scenario names NO terrain while one is resident: the commit unloads it.</summary>
        internal string? UnloadResident;

        /// <summary>⭐ True when this staged result would actually change residency.</summary>
        public bool HasWork => Definition != null || UnloadResident != null;

        /// <summary>The terrain this staged result is for, or <c>null</c> when there was nothing to do.</summary>
        public string? Name => TerrainName;
    }

    private readonly TerrainCatalog _catalog;
    private readonly RoadNetworkHolder _roadNetworkHolder;

    // ⭐ W6 — set only on a node that solves navigation (AttachNavmesh). Null ⇒ no bake, which is correct for a
    //   node without the NavigationSolver role (IG, a viewer).
    private Fdp.Toolkit.Navigation.INavmeshFactory?            _navmeshFactory;
    private Fdp.Toolkit.Navigation.SwitchableNavmeshProvider?  _navmesh;

    // ⭐ CE-3136 P-7a (R-243) — the terrain WITHOUT static obstacles (what Commit/Unload published), and its version: the obstacle
    //   bake derives every world from it, and a new terrain re-bakes the obstacles over the new one.
    private volatile TerrainWorld _baseWorld = new();
    private StaticObstacleBakery? _bakery;

    /// <summary>⭐ <c>CE-3136</c> P-7a — bumped by every terrain commit / unload: an obstacle bake made over an older base is stale.</summary>
    public int BaseVersion { get; private set; }

    // ⭐ The idempotency branch, and the only implementation of it: same name + same files ⇒ no work.
    private string?  _lastLoadedTerrainName;
    private DateTime _lastLoadedTimestamp;
    private string?  _lastLoadedOverlay;   // ⭐ O-4 — a resident terrain is only 'unchanged' for the same overlay HASH

    /// <summary>
    /// The production constructor: resolves terrains through the standard node search list —
    /// <c>{staging}/Terrain</c>, then the shared NAS stand-in, then the shipped terrains (W12).
    /// </summary>
    /// <param name="content">⭐ CE-4013 — the NODE's terrain content (<c>HrotNodeContext.TerrainContent</c>); null ⇒ none.</param>
    public TerrainResidency(string localStagingRoot, RoadNetworkHolder roadNetworkHolder, TerrainContentRegistry? content = null)
        : this(BuildNodeCatalog(localStagingRoot), roadNetworkHolder, content)
    {
    }

    /// <summary>Resolves through an explicit <paramref name="catalog"/> (tests, tools).</summary>
    public TerrainResidency(TerrainCatalog catalog, RoadNetworkHolder roadNetworkHolder, TerrainContentRegistry? content = null)
    {
        _content = content;
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

    /// <summary>⭐ O-8 (CE-4420) — the content hash of the overlay merged into the resident terrain, or null.</summary>
    public string? ResidentOverlayHash => _lastLoadedOverlay;

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

    private readonly List<ITerrainContentLoader> _contentLoaders = new();

    /// <summary>
    /// ⭐ AQ93 R-4021 — make this residency load an extra piece of terrain content (the first: the SUMO net) with every
    /// terrain: <see cref="ITerrainContentLoader.Prepare"/> runs inside <see cref="Prepare"/> (off the main thread),
    /// <see cref="ITerrainContentLoader.Commit"/> inside <see cref="Commit"/>, <see cref="ITerrainContentLoader.Unload"/> on
    /// <see cref="Unload"/>. The same shape as <see cref="AttachNavmesh"/>, for content that is not a navmesh.
    /// </summary>
    public void AttachContent(ITerrainContentLoader loader)
        => _contentLoaders.Add(loader ?? throw new ArgumentNullException(nameof(loader)));

    // ⭐ CE-4013 (§4.1ae T1) — the NODE's content registry, attached at Prepare (a capability may add to it after this residency was built).
    //   ⛔ Not process-wide: in `--mode all` a node that composes no traffic must not load the SUMO net because another node does.
    private readonly TerrainContentRegistry? _content;
    private readonly HashSet<string> _attachedKeys = new();

    /// <summary>The node's terrain content this residency loads (null ⇒ none).</summary>
    public TerrainContentRegistry? Content => _content;

    private void AttachRegisteredContent()
    {
        if (_content is null) return;
        foreach (var (key, factory) in _content.Entries())
            if (_attachedKeys.Add(key)) _contentLoaders.Add(factory());
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
    public Staged Prepare(string? terrainName, string? overlayFolder = null)
    {
        if (string.IsNullOrWhiteSpace(terrainName))
        {
            // ⭐⭐ CE-3075 — 🔒 user, 2026-10-05: "unload terrain when scenario names none. The terrain only stays
            //   untouched if scenario load requests same terrain as already loaded." A terrain-less scenario used to
            //   INHERIT the previous one (measured: hill-attack-close after test-town — its platoon outside test-town's
            //   bounds, the mission halted). The unload itself is ECS work, so it happens at the commit.
            if (_lastLoadedTerrainName != null)
            {
                FdpLog<TerrainResidency>.Info(
                    "[Terrain] Scenario names no terrain — '{0}' is resident and will be unloaded.", _lastLoadedTerrainName);
                return new Staged { UnloadResident = _lastLoadedTerrainName };
            }
            FdpLog<TerrainResidency>.Info(
                "[Terrain] Scenario names no terrain and none is resident — nothing to do. This is legal.");
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

        if (_lastLoadedTerrainName == terrainName && _lastLoadedTimestamp == currentFileTime && string.Equals(_lastLoadedOverlay, OverlayHashOf(overlayFolder), StringComparison.Ordinal))
        {
            FdpLog<TerrainResidency>.Info(
                "[Terrain] Terrain '{0}' already resident and unchanged — skipping re-ingestion.",
                terrainName);
            return new Staged();
        }

        var definition = TerrainFolderWorld.ReadDefinition(definitionPath);   // ⭐ CE-2417 — every read and stage below reports (§12); ⭐ CE-4393 — the ONE folder → world step

        var staged = new Staged
        {
            Definition    = definition,
            TerrainName   = terrainName,
            FileTimestamp = currentFileTime,
            OverlayFolder = OverlayHashOf(overlayFolder),
        };

        // ⭐ Schema v2 — the terrain WORLD every derived query reads (docs/DESIGN_Terrain_World.md).
        if (!string.IsNullOrEmpty(definition.World))
        {
            // ⭐ Stage 1 — with the terrain folder's side files (building templates, material overrides). ⭐ CE-4393 — through the ONE
            //   folder → world step the importer's navmesh prefill uses too (UTM §12.1 N-2): the same world, the same tile keys.
            var baseWorld = TerrainFolderWorld.ReadWorld(definitionPath, definition, terrainName)!;
            // ⭐ O-2 (CE-4416, §18a) — a scenario's terrain overlay: the base world plus the overlay's, merged into ONE world (the obstacle bake builds on it). The hash check is O-4.
            if (!string.IsNullOrEmpty(overlayFolder))
            {
                // ⭐ O-4 (C-5) — an overlay is bound to the base it was compiled against, by CONTENT: a name stands for nothing
                var header = TerrainOverlayHeader.Parse(File.ReadAllText(Path.Combine(overlayFolder, TerrainOverlayHeader.FileName)), overlayFolder);
                string actualBase = TerrainContentHash.OfTerrain(definitionPath);
                staged.BaseHash = actualBase;
                if (!string.Equals(actualBase, header.BaseHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"[Terrain] The overlay '{overlayFolder}' was compiled against terrain '{header.BaseName}' with content hash '{header.BaseHash}', but the resident terrain '{terrainName}' "
                      + $"('{definitionPath}') has content hash '{actualBase}'. Refusing to merge: recompile the overlay (save the scenario) against this terrain.");
                string actualOverlay = TerrainContentHash.OfOverlay(overlayFolder);
                if (!string.Equals(actualOverlay, header.ContentHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"[Terrain] The overlay '{overlayFolder}' is torn: its header says content hash '{header.ContentHash}', its files hash to '{actualOverlay}' (terrain '{terrainName}').");
                var overlay = TerrainFolderWorld.ReadOverlay(overlayFolder, terrainName, TerrainAssets.RecipesSibling(Path.GetDirectoryName(Path.GetFullPath(definitionPath))!));
                baseWorld = TerrainWorldMerge.Merge(baseWorld, overlay.World);
            }
            staged.World = Indexed(baseWorld);

            Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the world is parsed (its features reported as they went)

            // ⭐ W6 — the bake rides Prepare, whose contract is already "off-thread, no ECS mutation".
            if (_navmeshFactory != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (_navmeshFactory is Fdp.Toolkit.Navigation.IIncrementalNavmeshFactory incLoad)
                {
                    staged.Navmesh = incLoad.Build(staged.World, null, -1, out long loadSerial);
                    staged.BakeSerial = loadSerial;   // ⭐ O-7 A / CE-4427 R-6 — THIS bake's serial, handed back with it (never read from the factory afterwards)
                }
                else staged.Navmesh = _navmeshFactory.Build(staged.World);
                FdpLog<TerrainResidency>.Info(
                    $"[Terrain] Navmesh for '{terrainName}' baked in {sw.ElapsedMilliseconds} ms"
                  + (staged.Navmesh == null ? " — nothing walkable." : "."));
            }

            // ⭐ EQS §19 — the cover database is derived from the same world, off-thread like the bake.
            Fdp.Core.LoadProgress.Report();
            staged.Cover = Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(staged.World);
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

        // ⭐ AQ93 R-4021 — every attached content loader prepares its part of this terrain, off the main thread.
        string folder = Path.GetDirectoryName(definitionPath)!;
        AttachRegisteredContent();
        foreach (var loader in _contentLoaders)
        {
            Fdp.Core.LoadProgress.Report();
            staged.Contents.Add((loader, loader.Prepare(definition, folder, terrainName, staged.World ?? (staged.PreparedEmptyWorld ??= new TerrainWorld { Name = terrainName }))));
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

        if (staged.UnloadResident != null)
        {
            Unload(world);   // CE-3075 — a scenario that names no terrain gets none
            return;
        }

        if (world == null)
        {
            // A no-ECS host still participates; it simply has nowhere to publish.
            if (staged.HasRoadNetwork) staged.RoadNetwork.Dispose();
            return;
        }

        // ⭐ CE-3126 (R-229) — the terrain says where on the Earth its local metres are. Set FIRST, in the same commit, so no
        //   frame sees the new terrain with the old origin. 📄 docs/DESIGN_Geo_Origin.md §2 D.
        ApplyGeoOrigin(world, staged.Definition!.Origin, staged.TerrainName, staged.Definition!.Utm);

        world.RegisterManagedComponent<TerrainDefinition>();
        // ⭐ CE-3015 — remember the name the scenario resolved it by, so a save writes it back.
        staged.Definition!.ResolvedName = staged.TerrainName ?? string.Empty;
        staged.Definition.ResolvedOverlayHash = staged.OverlayFolder ?? string.Empty;   // ⭐ O-8 — stamped into a recording's metadata
        staged.Definition.ResolvedBaseHash = staged.OverlayFolder != null ? staged.BaseHash ?? string.Empty : string.Empty;
        world.SetSingletonManaged(staged.Definition!);
        Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417 — the commit's stages report (docs/DESIGN_Cluster_Load_Phase.md §12)

        // ⭐ The world model — every ECS node holds it (map, LOS, movement Z, navmesh source). A terrain
        //   with no world file still publishes an EMPTY world, so a re-load never leaves the previous
        //   terrain's buildings behind.
        world.RegisterManagedComponent<TerrainWorld>();
        world.SetSingletonManaged(staged.World ?? staged.PreparedEmptyWorld ?? new TerrainWorld { Name = staged.TerrainName });   // CE-3028: named even when it has no world file
        _baseWorld = world.GetSingletonManaged<TerrainWorld>() ?? new TerrainWorld();   // ⭐ CE-3136 P-7a — the base the obstacle bake builds on
        BaseVersion++;
        // ⭐ O-7 A — the load's bake is the baseline of the incremental obstacle bake: no obstacles, no damage, this base version
        lock (_dirtyGate) { _lastPrisms = new List<TerrainPrism>(); _lastDamage = new Dictionary<int, ulong>(); _lastParts = System.Array.Empty<OverlayPart>(); _lastBakeBase = BaseVersion; _committedSerial = staged.Navmesh != null ? staged.BakeSerial : -1; }
        PublishBakery(world);

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
        Fdp.Core.LoadProgress.Report();

        // ⭐ EQS §19 — the terrain's cover points (an EMPTY database when it has no world, so a re-load never keeps the
        //   previous terrain's cover). Read by CoverPointsGenerator through the solver snapshot.
        world.SetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>(
            staged.Cover ?? Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(new TerrainWorld()));

        PublishHolder(world);   // ⭐ CE-3128 — background readers lease the graph through the world (RoadNetworkSource)
        if (staged.HasRoadNetwork)
        {
            // ⛔ The previous blob is NOT disposed here — the holder retires it and frees it once its last
            //    reader releases.
            _roadNetworkHolder.Publish(staged.RoadNetwork);
            world.SetSingleton(new ZoneEnvironmentData { RoadNetwork = staged.RoadNetwork });
        }

        // ⭐ AQ93 R-4021 — the attached content, published in the same commit as the world it belongs to.
        foreach (var (loader, prepared) in staged.Contents)
        {
            Fdp.Core.LoadProgress.Report();   // ⭐ CE-2417
            loader.Commit(world, prepared);
        }

        _lastLoadedTerrainName = staged.TerrainName;
        _lastLoadedTimestamp   = staged.FileTimestamp;
        _lastLoadedOverlay     = staged.OverlayFolder;

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
    /// ⭐⭐ <b>Releases the resident terrain.</b> ⚠ SUPERSEDED `2026-10-05` (<c>CE-3075</c>): this was deliberately unwired,
    /// kept for a future standby mode; 🔒 the user has since ruled that a scenario naming NO terrain unloads the
    /// resident one, so <see cref="Commit"/> calls it on that path. A scenario naming a DIFFERENT terrain still replaces
    /// residency through <see cref="Commit"/> without unloading first (that would drop the graph a background solver may
    /// still be inside, for no benefit); only the SAME terrain is left untouched.
    ///
    /// <para>What unloading means: the terrain definition singleton is cleared (so a save does not stamp the old name),
    /// the world becomes an EMPTY one (flat ground — readers keep a valid model), the cover database and the navmesh are
    /// emptied, the holder retires its road graph (freeing it once the last background reader releases), and the
    /// idempotency cache is cleared so a later load re-ingests.</para>
    /// </summary>
    public void Unload(EntityRepository? world)
    {
        // An empty blob retires the live one through the holder's normal generation swap.
        _roadNetworkHolder.Publish(default);
        PublishHolder(world);

        if (world != null && world.HasSingleton<ZoneEnvironmentData>())
            world.SetSingleton(new ZoneEnvironmentData { RoadNetwork = default });
        if (world != null && world.HasSingletonManaged<TerrainDefinition>())
            world.SetSingletonManaged<TerrainDefinition>(null!);   // CE-3075 — no terrain: a save must not stamp the old name
        if (world != null && world.HasSingletonManaged<TerrainWorld>())
            world.SetSingletonManaged(new TerrainWorld());
        _baseWorld = new TerrainWorld();   // ⭐ CE-3136 P-7a — obstacles re-bake over flat ground
        BaseVersion++;
        PublishBakery(world);
        if (world != null && world.HasSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>())
            world.SetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>(
                Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(new TerrainWorld()));
        _navmesh?.Publish(null);
        if (world != null)
            foreach (var loader in _contentLoaders) loader.Unload(world);   // AQ93 R-4021 — the content goes with its terrain
        ApplyGeoOrigin(world, null, terrainName: null);   // CE-3126 / CE-4396 — no terrain: no geo frame (local-only)

        _lastLoadedTerrainName = null;
        _lastLoadedTimestamp   = default;
        _lastLoadedOverlay     = null;

        FdpLog<TerrainResidency>.Info("[Terrain] Terrain residency released — the world has no terrain (flat ground).");
    }

    /// <summary>
    /// ⭐ <c>CE-3118</c> — copies the terrain a residency committed into <paramref name="source"/> onto <paramref name="target"/>:
    /// the definition, the world, the road network and its holder, and the cover database — exactly what the map's terrain, road and cover gizmos read. For a
    /// host that DRAWS several worlds over one terrain (the Replay Browser: one sandbox per node plus the merged master, rebound
    /// on every seek). ⭐ Always writes the world and the road network, an empty one when the source has none, so nothing a
    /// replay restored can survive in their place. ⛔ Touches no geo origin. 📄 docs/DESIGN_Geo_Origin.md §5.
    /// </summary>
    public static void MirrorTerrain(EntityRepository source, EntityRepository target)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));

        target.RegisterManagedComponent<TerrainDefinition>();
        target.RegisterManagedComponent<TerrainWorld>();
        if (source.HasSingletonManaged<TerrainDefinition>() && source.GetSingletonManaged<TerrainDefinition>() is { } definition)
            target.SetSingletonManaged(definition);
        else if (target.HasSingletonManaged<TerrainDefinition>())
            target.SetSingletonManaged<TerrainDefinition>(null!);

        target.SetSingletonManaged(source.HasSingletonManaged<TerrainWorld>() && source.GetSingletonManaged<TerrainWorld>() is { } world
            ? world : new TerrainWorld());
        target.SetSingleton(source.HasSingleton<ZoneEnvironmentData>()
            ? source.GetSingleton<ZoneEnvironmentData>() : new ZoneEnvironmentData());
        if (source.HasSingletonManaged<RoadNetworkHolder>() && source.GetSingletonManaged<RoadNetworkHolder>() is { } holder)
            target.SetSingletonManaged(holder);
        // ⭐ CE-3134 — the cover database too, so the Cover layer draws in replay (an empty one when the source has none).
        target.SetSingletonManaged(source.HasSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>()
            && source.GetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>() is { } cover
            ? cover : Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(new TerrainWorld()));
    }

    /// <summary>⭐ <c>CE-3128</c> — the node's road graph carrier as a world singleton (idempotent).</summary>
    /// <summary>
    /// ⭐ <c>CE-3279</c> (a) — every world this residency publishes carries the terrain's spatial index (World Query Seam WQ-D):
    /// its segment and point queries visit only the pieces the index names, with identical answers. 📐 Built in tens of ms, off
    /// the main thread (Prepare and the obstacle bake both run there). ⛔ It was built and attached nowhere, so every live sight
    /// line and every EQS point placement scanned every piece.
    /// </summary>
    private static string? OverlayHashOf(string? overlayFolder)
    {
        if (string.IsNullOrEmpty(overlayFolder)) return null;
        var header = Path.Combine(overlayFolder, TerrainOverlayHeader.FileName);
        return TerrainOverlayHeader.Parse(File.ReadAllText(header), header).ContentHash;
    }

    /// <summary>⭐ O-4 (CE-4415) — the folder of the overlay with this content hash, searched in the catalog's roots (the node's staging, then the NAS); null when none has it.</summary>
    public string? ResolveOverlay(string contentHash) => _catalog.ResolveOverlay(contentHash);

    private static TerrainWorld Indexed(TerrainWorld world)
    {
        Fdp.Toolkit.Spatial.Bepu.BepuTerrainIndex.AttachTo(world);
        return world;
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a (R-243) — what an obstacle bake built off-thread: the terrain + obstacle prisms, its cover database and
    /// (on a navigation node) its navmesh. Published by <see cref="CommitObstacles"/>.
    /// </summary>
    public sealed class ObstacleBake
    {
        internal TerrainWorld World = new();
        internal Fdp.Toolkit.Navigation.INavmeshProvider? Navmesh;
        internal Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider? Cover;
        internal int BaseVersion;
        internal long BakeSerial = -1;
        internal List<TerrainPrism> Prisms = new();
        internal Dictionary<int, ulong> Damage = new();
        internal IReadOnlyList<OverlayPart> Parts = System.Array.Empty<OverlayPart>();
        /// <summary>
        /// ⭐ O-7 B (B-5) — where this bake differs from the last one committed on the same base (obstacle prisms, building damage, live-overlay parts): the rectangles a 3-D chunk rebuild must redo.
        /// Null = unknown (the first bake after a base change, or another bake in between) = everything. Set on every bake, whether or not this node bakes a navmesh.
        /// </summary>
        public IReadOnlyList<(System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)>? Dirty { get; internal set; }
        /// <summary>Wall-clock duration of the bake (ms) — diagnostics only (R-143's exemptions).</summary>
        public long Milliseconds { get; internal set; }
        /// <summary>The obstacle prisms it holds.</summary>
        public int Obstacles { get; internal set; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-7a — the OFF-THREAD half of an obstacle rebuild (touches no ECS): the current base terrain +
    /// <paramref name="obstacles"/> → a new immutable world (R-218), its cover database, and on a navigation node a navmesh baked
    /// through the per-tile cache (only the tiles the obstacles touch are new). 📄 docs/DESIGN_Peek_And_Fire.md §9.
    /// </summary>
    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-3 / R-4196 (UTM §8.2 B-12) — true on a node that does WORLD QUERIES: the one holding a navmesh factory (the muscle's). Only such a node lays building
    /// damage over its terrain: a display node's terrain "is for displaying their map, no querying or modification allowed" (user), so it bakes obstacles as before and IGNORES damage —
    /// it draws breaches and rubble from the replicated entities instead (S-4). ⚠ The honest test is "has a navmesh factory", not a role name: the factory is installed only by
    /// the navigation-capable (muscle) hosts, and a node that bakes a navmesh is exactly one whose terrain is queried.
    /// </summary>
    public bool BakesDamage => _navmeshFactory != null;

    public ObstacleBake BakeObstacles(IReadOnlyList<TerrainPrism> obstacles) => BakeObstacles(obstacles, null);

    /// <inheritdoc cref="BakeObstacles(IReadOnlyList{TerrainPrism})"/>
    /// <param name="damage">The live building damage to lay over the terrain (<see cref="TerrainObstacles.With(TerrainWorld, IReadOnlyList{TerrainPrism}, BuildingDamageTable?)"/>); ignored on a node that does not <see cref="BakesDamage"/>.</param>
    public ObstacleBake BakeObstacles(IReadOnlyList<TerrainPrism> obstacles, BuildingDamageTable? damage) => BakeObstacles(obstacles, damage, null);

    /// <summary>
    /// ⭐ O-7 B (§18e-2 B-1) — an obstacle bake with the editor's LIVE overlay as a third input: the world is <c>With(Merge(base, overlay), obstacles, damage)</c>, and the dirty region also holds the bounds of every overlay part
    /// that differs by key + geometry (old and new). The overlay is expanded against ONE base: when that is not the current one the bake is made over the base it was expanded for and carries ITS version, so the commit refuses it.
    /// </summary>
    public ObstacleBake BakeObstacles(IReadOnlyList<TerrainPrism> obstacles, BuildingDamageTable? damage, LiveOverlayResult? overlay)
    {
        if (!BakesDamage) damage = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var baseWorld = overlay?.Base ?? _baseWorld;
        int baseVersion = overlay?.BaseVersion ?? BaseVersion;
        var bake = new ObstacleBake { BaseVersion = baseVersion, Obstacles = obstacles.Count };
        var terrain = overlay != null ? TerrainWorldMerge.Merge(baseWorld, overlay.World) : baseWorld;   // base indices kept, overlay appended: every key and saved state keyed by a base index stays valid
        var parts = overlay?.Parts ?? System.Array.Empty<OverlayPart>();
        // ⭐ O-7 A (§18e A-2) — where this bake differs from the last one committed on this base: the factory reuses every tile outside it. null = unknown (first bake after a base change, another bake in between) = full.
        // ⭐ CE-4427 R-4 — the damage table was built for the world the frame thread saw; this bake's world may number its buildings and panels differently (the live overlay re-sorts its parts): lay it over by KEY
        if (overlay != null && damage != null) damage = damage.RemapTo(terrain);
        var damageNow = DamageHashes(terrain, damage);
        var dirty = DirtyRegions(obstacles, damageNow, terrain, baseVersion, parts, out bool diffed);
        bake.Prisms = new List<TerrainPrism>(obstacles); bake.Damage = damageNow; bake.Parts = parts;
        bake.Dirty = diffed ? dirty : null;
        bake.World = Indexed(TerrainObstacles.With(terrain, obstacles, damage));   // ⭐ CE-3279 (a) — a derived world gets its own index
        if (_navmeshFactory is Fdp.Toolkit.Navigation.IIncrementalNavmeshFactory inc)
        {
            // ⭐ CE-4427 R-6 — the factory checks "its last bake is the one I committed" UNDER its own lock and hands back THIS bake's serial
            long committed;
            lock (_dirtyGate) committed = diffed ? _committedSerial : -1;
            bake.Navmesh = inc.Build(bake.World, diffed ? dirty : null, committed, out long serial);
            bake.BakeSerial = serial;
        }
        else if (_navmeshFactory != null) bake.Navmesh = _navmeshFactory.Build(bake.World);
        bake.Cover = Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(bake.World);
        bake.Milliseconds = sw.ElapsedMilliseconds;
        return bake;
    }

    /// <summary>⭐ O-7 B — the base terrain and where it lives, for the live overlay's expansion: the resident base world, its version, and the terrain's folder (null when none is resident or its definition is not found).</summary>
    public (TerrainWorld Base, int Version, string? Folder) ExpansionBase()
    {
        int version = BaseVersion;   // version first: a base committed in between makes the pair look OLDER than the world, and the commit refuses it
        var world = _baseWorld;
        string? name = _lastLoadedTerrainName;
        return (world, version, string.IsNullOrEmpty(name) ? null : _catalog.ResolveFolder(name));
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-7a — the MAIN-THREAD half: publishes a bake's world, cover and navmesh in one commit. Returns false (and
    /// publishes nothing) when the terrain changed since the bake began — the caller bakes again over the new base.
    /// </summary>
    public bool CommitObstacles(EntityRepository world, ObstacleBake bake)
    {
        if (bake.BaseVersion != BaseVersion) return false;
        lock (_dirtyGate) { _lastPrisms = bake.Prisms; _lastDamage = bake.Damage; _lastParts = bake.Parts; _lastBakeBase = bake.BaseVersion; _committedSerial = bake.Navmesh != null ? bake.BakeSerial : -1; }   // ⭐ O-7 A — the next bake diffs against this one
        world.RegisterManagedComponent<TerrainWorld>();
        // ⭐ CE-4427 R-4 — a world that numbers its buildings / panels differently replaces the old one: every BuildingDamage entity is re-keyed to it BEFORE it is published (main thread, here)
        var replaced = world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
        if (replaced != null) RemapDamageEntities(world, replaced, bake.World);
        world.SetSingletonManaged(bake.World);
        _navmesh?.Publish(bake.Navmesh);
        world.SetSingletonManaged<Fdp.Toolkit.Spatial.Eqs.ICoverProvider>(
            bake.Cover ?? Fdp.Toolkit.Spatial.Eqs.TerrainCoverProvider.Build(bake.World));
        return true;
    }

    /// <summary>⭐ CE-4427 R-4 — re-keys every <see cref="BuildingDamage"/> entity from <paramref name="from"/>'s numbering to <paramref name="to"/>'s (stable key, within-building panel); a no-op when the two share their building and panel lists.</summary>
    internal static void RemapDamageEntities(EntityRepository world, TerrainWorld from, TerrainWorld to)
    {
        if (ReferenceEquals(from, to) || (ReferenceEquals(from.Panels, to.Panels) && ReferenceEquals(from.Buildings, to.Buildings))) return;
        if (!world.IsComponentTypeRegistered<BuildingDamage>()) return;
        foreach (var e in world.Query().With<BuildingDamage>().WithLifecycle(Fdp.Core.EntityLifecycle.All).Build())
        {
            var d = world.GetComponentRO<BuildingDamage>(e);
            if (BuildingDamages.Remap(ref d, from, to)) world.SetComponent(e, d);
        }
    }

    // ── ⭐ O-7 A (§18e A-2) — the dirty region of an obstacle bake ───────────────────────────────────────────────────────────────────

    private readonly object _dirtyGate = new();
    private List<TerrainPrism>? _lastPrisms;
    private Dictionary<int, ulong> _lastDamage = new();
    private IReadOnlyList<OverlayPart> _lastParts = System.Array.Empty<OverlayPart>();
    private int _lastBakeBase = int.MinValue;
    private long _committedSerial = -1;

    /// <summary>A prism's identity for "did it change": its footprint and heights at 1 cm and its material (the quantisation of <see cref="StaticObstacleBakeSystem.SignatureOf"/>).</summary>
    internal static ulong PrismKey(TerrainPrism p)
    {
        ulong h = 1469598103934665603UL;
        void Mix(float f) { h ^= (ulong)(long)MathF.Round(f * 100f); h *= 1099511628211UL; }
        foreach (var v in p.Footprint) { Mix(v.X); Mix(v.Y); }
        Mix(p.BaseZ); Mix(p.TopZ);
        h ^= p.Material?.Name is { } m ? Fdp.Core.StableHash.Fnv1a32(m) : 0u; h *= 1099511628211UL;
        return h;
    }

    /// <summary>Per building, a hash of what its damage does to the navmesh (its passage breaches and whether it collapsed); empty when no damage is laid over the terrain.</summary>
    private static Dictionary<int, ulong> DamageHashes(TerrainWorld world, BuildingDamageTable? damage)
    {
        var map = new Dictionary<int, ulong>();
        if (damage == null || damage.NavHash == 0) return map;
        foreach (var b in damage.Passages())
        {
            if ((uint)b.Panel >= (uint)world.Panels.Count) continue;
            int building = world.Panels[b.Panel].Building;
            if (building < 0) continue;
            ulong h = map.TryGetValue(building, out var cur) ? cur : 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            Mix(b.Panel); Mix((long)MathF.Round(b.From * 100f)); Mix((long)MathF.Round(b.To * 100f)); Mix((long)MathF.Round(b.Bottom * 100f)); Mix((long)MathF.Round(b.Top * 100f));
            map[building] = h;
        }
        foreach (int c in damage.CollapsedBuildings) map[c] = (map.TryGetValue(c, out var cur) ? cur : 1469598103934665603UL) ^ (0xC011A95EUL + (ulong)c) * 1099511628211UL;
        return map;
    }

    /// <summary>
    /// The rectangles where <paramref name="obstacles"/> / <paramref name="damage"/> differ from the last committed bake on THIS base: the bounds of every prism added or removed (a moved one is both — its old and
    /// its new place) and of every building whose damage changed. <paramref name="diffed"/> false (⇒ the caller bakes everything) on a base change or before any baseline.
    /// </summary>
    internal List<(System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)> DirtyRegions(IReadOnlyList<TerrainPrism> obstacles, Dictionary<int, ulong> damage, TerrainWorld baseWorld, int baseVersion, out bool diffed)
        => DirtyRegions(obstacles, damage, baseWorld, baseVersion, System.Array.Empty<OverlayPart>(), out diffed);

    /// <summary>⭐ O-7 B — also the bounds of every live-overlay part that was added, removed or changed (key + geometry hash): a moved block is its old and its new parts, woods included.</summary>
    internal List<(System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)> DirtyRegions(IReadOnlyList<TerrainPrism> obstacles, Dictionary<int, ulong> damage, TerrainWorld baseWorld, int baseVersion, IReadOnlyList<OverlayPart> parts, out bool diffed)
    {
        var rects = new List<(System.Numerics.Vector2, System.Numerics.Vector2)>();
        List<TerrainPrism>? last; Dictionary<int, ulong> lastDamage; IReadOnlyList<OverlayPart> lastParts;
        lock (_dirtyGate) { last = _lastPrisms; lastDamage = _lastDamage; lastParts = _lastParts; diffed = last != null && _lastBakeBase == baseVersion; }
        if (!diffed) return rects;
        var oldParts = new Dictionary<string, OverlayPart>(lastParts.Count, StringComparer.Ordinal);
        foreach (var p in lastParts) oldParts[p.Key] = p;
        foreach (var p in parts)
        {
            if (oldParts.TryGetValue(p.Key, out var prev) && prev.Hash == p.Hash) { oldParts.Remove(p.Key); continue; }   // unchanged
            rects.Add((p.Min, p.Max));                                                                                   // added / changed: where it is now
        }
        foreach (var p in oldParts.Values) rects.Add((p.Min, p.Max));                                                    // removed / changed: where it was
        var before = new Dictionary<ulong, List<TerrainPrism>>();
        foreach (var p in last!) { var k = PrismKey(p); if (!before.TryGetValue(k, out var l)) before[k] = l = new List<TerrainPrism>(1); l.Add(p); }
        foreach (var p in obstacles)
        {
            var k = PrismKey(p);
            if (before.TryGetValue(k, out var l) && l.Count > 0) l.RemoveAt(l.Count - 1);   // unchanged
            else rects.Add((p.Min, p.Max));                                                   // added / moved there
        }
        foreach (var l in before.Values) foreach (var p in l) rects.Add((p.Min, p.Max));      // removed / moved from there
        foreach (var b in new HashSet<int>(damage.Keys.Concat(lastDamage.Keys)))
        {
            damage.TryGetValue(b, out var now); lastDamage.TryGetValue(b, out var was);
            if (now == was || (uint)b >= (uint)baseWorld.Buildings.Count || baseWorld.Buildings[b].Footprint.Length == 0) continue;
            var lo = new System.Numerics.Vector2(float.MaxValue); var hi = new System.Numerics.Vector2(float.MinValue);
            foreach (var v in baseWorld.Buildings[b].Footprint) { lo = System.Numerics.Vector2.Min(lo, v); hi = System.Numerics.Vector2.Max(hi, v); }
            rects.Add((lo, hi));
        }
        return rects;
    }

    private void PublishBakery(EntityRepository? world)
    {
        if (world == null) return;
        _bakery ??= new StaticObstacleBakery(this);
        if (world.HasSingletonManaged<StaticObstacleBakery>() && ReferenceEquals(world.GetSingletonManaged<StaticObstacleBakery>(), _bakery)) return;
        world.SetSingletonManaged(_bakery);
    }

    private void PublishHolder(EntityRepository? world)
    {
        if (world == null) return;
        if (world.HasSingletonManaged<RoadNetworkHolder>() && ReferenceEquals(world.GetSingletonManaged<RoadNetworkHolder>(), _roadNetworkHolder)) return;
        world.SetSingletonManaged(_roadNetworkHolder);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3126</c> (R-229) — sets the node's geo transform (the world's <see cref="Fdp.Modules.Geographic.IGeographicTransform"/>
    /// singleton — the ONE instance the node's translators and modules hold, docs/DESIGN_Geo_Origin.md §2 C) to
    /// <paramref name="origin"/>; <c>null</c> means the terrain declares none, which is 0,0,0 and is said in the log (⛔ no
    /// default in code — user: <i>"No default berlin. Missing geo = zeros."</i>). A world with no transform has nothing to
    /// convert and is left alone. Public because the Replay Browser applies a RECORDING's origin the same way (§2 F).
    /// </summary>
    public static void ApplyGeoOrigin(EntityRepository? world, TerrainGeoOrigin? origin, string? terrainName, TerrainUtmFrame? utm = null)
    {
        if (world == null || !world.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()) return;
        var geo = world.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>();
        if (geo == null) return;
        // ⭐ CE-4396 (docs/DESIGN_Geo_Origin.md §6.1a, R-4197) — the node's ONE transform is switchable and its only frames are UTM and "none": the
        //   stored lat / lon origin IS the frame (zone = its longitude band, hemisphere = its latitude's sign, E0 / N0 = it projected there); a terrain
        //   that declares its exact `frame.utm` (an imported one, or a zone override) uses that; no origin ⇒ LOCAL-ONLY (geodetic conversions refused).
        //   A world whose transform is not switchable (a test double) keeps SetOrigin.
        if (geo is Fdp.Modules.Geographic.Transforms.SwitchableGeoTransform switchable)
        {
            string who = terrainName != null ? $"terrain '{terrainName}'" : "no terrain";
            if (utm != null)
            {
                if (origin is { } og)
                {
                    var (pe, pn) = Fdp.Modules.Geographic.Transforms.Utm.FromGeodetic(og.Lat, og.Lon, utm.Zone, utm.South);
                    if (Math.Abs(pe - utm.Easting) > 0.01 || Math.Abs(pn - utm.Northing) > 0.01)
                        FdpLog<TerrainResidency>.Warn(
                            $"[Terrain] {who}: frame.utm (E {utm.Easting}, N {utm.Northing}) and the origin projected into zone {utm.Zone} (E {pe:F3}, N {pn:F3}) differ by {pe - utm.Easting:F3} m, {pn - utm.Northing:F3} m — the frame is used.");
                }
                if (switchable.Inner is Fdp.Modules.Geographic.Transforms.UtmTransform u && u.Zone == utm.Zone && u.South == utm.South
                    && u.E0 == utm.Easting && u.N0 == utm.Northing && u.H0 == (origin?.Alt ?? 0.0)) return;
                switchable.UseUtm(utm.Zone, utm.South, utm.Easting, utm.Northing, origin?.Alt ?? 0.0);
                FdpLog<TerrainResidency>.Info($"[Terrain] geo frame set to UTM zone {utm.Zone}{(utm.South ? "S" : "N")} at E {utm.Easting}, N {utm.Northing} ({who}).");
                return;
            }
            if (origin is { } o)
            {
                var want = Fdp.Modules.Geographic.Transforms.UtmTransform.AtOrigin(o.Lat, o.Lon, o.Alt);
                if (switchable.Inner is Fdp.Modules.Geographic.Transforms.UtmTransform cur && cur.Zone == want.Zone && cur.South == want.South
                    && cur.E0 == want.E0 && cur.N0 == want.N0 && cur.H0 == want.H0) return;
                switchable.UseOrigin(o.Lat, o.Lon, o.Alt);
                FdpLog<TerrainResidency>.Info($"[Terrain] geo frame set to UTM zone {want.Zone}{(want.South ? "S" : "N")} at the origin {o.Lat}, {o.Lon}, {o.Alt} ({who}).");
                return;
            }
            if (terrainName != null)
                FdpLog<TerrainResidency>.Warn($"[Terrain] '{terrainName}' declares no 'origin' — its local metres have no geo frame (geodetic conversions are refused).");
            if (switchable.Inner.HasFrame)
            {
                switchable.UseLocalOnly();
                FdpLog<TerrainResidency>.Info($"[Terrain] geo frame cleared — local-only ({who}).");
            }
            return;
        }
        if (utm != null && terrainName != null)
            FdpLog<TerrainResidency>.Warn($"[Terrain] '{terrainName}' declares a UTM frame, but this node's geo transform cannot switch — SetOrigin at its origin is used.");
        var oo = origin ?? TerrainGeoOrigin.Zero;
        var current = geo.Origin;
        if (current.lat == oo.Lat && current.lon == oo.Lon && current.alt == oo.Alt) return;
        geo.SetOrigin(oo.Lat, oo.Lon, oo.Alt);
        FdpLog<TerrainResidency>.Info($"[Terrain] geo origin set to {oo.Lat}, {oo.Lon}, {oo.Alt} ({(terrainName != null ? $"terrain '{terrainName}'" : "no terrain")}).");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2242</c> — sets the origin of the terrain a load is ABOUT to commit, ahead of <see cref="Prepare"/>'s navmesh
    /// bake: other nodes commit at once and their entities replicate in while this node still bakes, and a geodetic position
    /// converted before the origin is set lands hundreds of km out (never re-sent if the entity is static). Reads only the
    /// definition; a missing one is left to <see cref="Prepare"/> to report. ⛔ Call it only where the world holds nothing
    /// the old origin placed (a world boundary, a late joiner's catch-up) — <see cref="Commit"/> sets the same value again.
    /// 📄 docs/DESIGN_Geo_Origin.md §4.
    /// </summary>
    public void ApplyOriginAhead(EntityRepository? world, string? terrainName)
    {
        if (string.IsNullOrWhiteSpace(terrainName))
        {
            ApplyGeoOrigin(world, null, terrainName: null);
            return;
        }
        var definition = _catalog.ReadDefinition(terrainName);   // ⭐ AQ107 C — the catalog's one locate-and-parse (the master reads it the same way)
        if (definition == null) return;
        ApplyGeoOrigin(world, definition.Origin, terrainName, definition.Utm);
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

/// <summary>
/// ⭐ AQ93 R-4021 — an extra piece of content a terrain folder carries (the first: the SUMO net), loaded with the terrain by
/// <see cref="TerrainResidency.AttachContent"/>. ⛔ <see cref="Prepare"/> runs OFF the main thread and must touch no ECS;
/// <see cref="Commit"/> and <see cref="Unload"/> run on it. A loader whose content the terrain does not declare returns
/// <c>null</c> from <see cref="Prepare"/> and clears its singleton in <see cref="Commit"/>, so a re-load never keeps the previous
/// terrain's content. 📄 docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md §11.
/// </summary>
public interface ITerrainContentLoader
{
    /// <summary>Reads and builds this terrain's content from <paramref name="terrainFolder"/>; <c>null</c> when it has none.</summary>
    /// <param name="world">The terrain world staged with it (parsed just before; empty when the terrain has none) — e.g. its footprint,
    /// which the SUMO road layer's ambient ground stays outside of (AQ93 V8).</param>
    object? Prepare(TerrainDefinition definition, string terrainFolder, string terrainName, TerrainWorld world);

    /// <summary>Publishes what <see cref="Prepare"/> built (or clears the content when it built nothing).</summary>
    void Commit(EntityRepository world, object? prepared);

    /// <summary>The terrain is gone: clear the content.</summary>
    void Unload(EntityRepository world);
}
