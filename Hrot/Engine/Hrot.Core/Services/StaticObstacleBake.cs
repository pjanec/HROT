using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Terrain;

namespace Hrot.Map.Common.Services;

/// <summary>
/// ⭐ <c>CE-3136</c> P-7a (R-243) — a node's handle on its <see cref="TerrainResidency"/> for the obstacle bake, published as a managed
/// world singleton by every terrain commit / unload. ⭐ Its ABSENCE is meaningful: the node holds no terrain, so there is nothing to
/// bake and an obstacle is acked at once (the "a host with nothing to make resident still acks" rule of <c>TerrainAssetHandler</c>).
/// </summary>
[ComponentId(GlobalComponentIds.StaticObstacleBakery)]
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
public sealed class StaticObstacleBakery
{
    internal StaticObstacleBakery(TerrainResidency residency) => Residency = residency;

    internal TerrainResidency Residency { get; }

    /// <summary>The residency's base version (bumped by every terrain commit / unload).</summary>
    public int BaseVersion => Residency.BaseVersion;

    // ⭐ CE-5039 — what the node's obstacle bake has COMMITTED of the live terrain overlay (the editor's blocks and features), so the load can wait for it.
    //   Written by StaticObstacleBakeSystem only (frame thread); read by the scenario load's readiness predicate (frame thread).
    [Fdp.Core.NotState("injected live-overlay dependency, handed over by the bake system")]
    internal LiveOverlay? Overlay;
    [Fdp.Core.WorldDerived("the overlay signature the last committed bake held; re-derived by the next bake")]
    internal ulong CommittedOverlay = LiveOverlay.EmptySignature;
    [Fdp.Core.WorldDerived("the base version that committed overlay was baked on; re-derived by the next bake")]
    internal int CommittedOverlayBase = int.MinValue;

    /// <summary>
    /// ⭐⭐ <c>CE-5039</c> (DECISIONS_Terrain CE-5039, <c>docs/DESIGN_Cluster_Load_Phase.md</c> §11.10d) — true while this node's live terrain overlay (the scenario's blocks and features, expanded
    /// live on the editor) holds something the terrain does NOT yet: its first bake for this world has not committed. ⭐ False at once on a node with no live overlay (every host but the
    /// editor: the compiled overlay reaches them in the terrain itself) and for a scenario with no blocks or features — those loads are not delayed. Allocation-free (a cached query).
    /// </summary>
    public bool AwaitsLiveOverlay(EntityRepository world)
    {
        var overlay = Overlay;
        if (overlay == null) return false;
        ulong now = overlay.SignatureOfWorld(world);
        if (now == LiveOverlay.EmptySignature) return false;
        return now != CommittedOverlay || CommittedOverlayBase != BaseVersion;
    }
}

/// <summary>
/// ⭐⭐ <c>CE-3136</c> P-7a (R-243, R-244; 📄 docs/DESIGN_Peek_And_Fire.md §9.2 O2, §9.4) — makes every static obstacle on this node
/// TERRAIN, and holds it out of the simulation until it is: the terrain lifecycle participant (<see cref="TerrainObstacles.LifecycleModuleId"/>).
/// <list type="number">
/// <item>Each frame it collects the static obstacles (Active and still Constructing) as prisms and hashes the set.</item>
/// <item>When the set differs from the last baked one (or the terrain under it changed), it waits until the set has been quiet for
/// <see cref="DebounceSeconds"/> of WALL-CLOCK time (R-244 — it must also fire while paused or loading), then bakes it ONCE in the
/// background: world + cover + touched navmesh tiles (<see cref="TerrainResidency.BakeObstacles"/>).</item>
/// <item>On completion it commits the bake (main thread) and only THEN acks every obstacle of the batch — so an obstacle is never
/// in the simulation before the world it belongs to, and a scenario load (which waits for nothing Constructing) finishes on the baked
/// world, whatever the machine's speed.</item>
/// <item>A change arriving while a bake runs is picked up when it ends (coalesced: one running, one pending at most).</item>
/// </list>
/// ⚠ A node with no terrain (no <see cref="StaticObstacleBakery"/> singleton) acks at once.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
[SingleInstance]
[TickMode(TickPolicy.EveryFrame)]
public sealed class StaticObstacleBakeSystem : IEcsModuleSystem
{
    /// <summary>R-244 — the wall-clock quiet period before a bake starts.</summary>
    public const double DebounceSeconds = 0.5;

    /// <summary>
    /// ⭐ <c>CE-4596</c> (P-0; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §9.1 V-1) — the LONGEST a change waits for its bake (wall-clock s,
    /// from the frame the system first sees it): past it the bake starts even while changes keep arriving. 📌 Every change reset the quiet
    /// period, so a barrage (a breach or a crater every &lt; 0.5 s) postponed every one of them until the shelling stopped.
    /// </summary>
    public const double MaxLatencySeconds = 2.0;

    private readonly Func<double> _wallSeconds;
    [Fdp.Core.WorldDerived("prism scratch cleared and refilled every frame from obstacle entities")]
    private readonly List<TerrainPrism> _prisms = new();
    [Fdp.Core.WorldDerived("deck scratch cleared and refilled when a bake starts (CE-4692)")]
    private readonly List<TerrainWalkable> _decks = new();
    [Fdp.Core.WorldDerived("pending-entity scratch cleared and refilled every frame")]
    private readonly List<Entity> _pending = new();
    private readonly ObstacleSignatureProbe _probe = new();   // ⭐ CE-3436 — the cached query the per-frame signature walks

    [Fdp.Core.WorldDerived("background obstacle bake; reset by WorldEpoch and re-baked from entities")]
    private Task<TerrainResidency.ObstacleBake>? _running;
    private TerrainResidency? _runningOn;
    [Fdp.Core.ProtocolCache("obstacles of the in-flight bake awaiting ConstructionAck; re-collected from Constructing entities")]
    private List<Entity> _runningBatch = new();
    [Fdp.Core.ProtocolCache("signature of the in-flight bake; just handshake bookkeeping")]
    private ulong _runningSignature;
    [Fdp.Core.WorldDerived("live-overlay signature of the in-flight bake (CE-5039); re-derived by the next bake")]
    private ulong _runningOverlay = LiveOverlay.EmptySignature;
    [Fdp.Core.FrameScratch("the live overlay's signature this frame, set by SignatureNow before every use")]
    private ulong _overlayNow = LiveOverlay.EmptySignature;

    [Fdp.Core.WorldDerived("what terrain holds baked; empty just triggers an identical rebake")]
    private ulong _bakedSignature = EmptySignature;
    [Fdp.Core.WorldDerived("terrain base version of last bake; empty just triggers a rebake")]
    private int _bakedBase = int.MinValue;
    [Fdp.Core.WorldDerived("debounce signature; empty restarts the quiet period")]
    private ulong _seenSignature = EmptySignature;
    [Fdp.Core.WorldDerived("wall-clock debounce timestamp; empty restarts the quiet period")]
    private double _seenAt;
    [Fdp.Core.WorldDerived("wall-clock when a pending obstacle change was first seen: debounce marker, reset at world change (140)")]
    private double _changedSince = double.NaN;   // ⭐ CE-4596 (V-1) — when the change now waiting was first seen (NaN = none waits)
    private int _epoch;   // ⭐ CE-3294 — the world the bake state above describes

    private const ulong EmptySignature = 1469598103934665603UL;

    /// <param name="wallSeconds">The wall clock, in seconds (R-244). Null ⇒ a <see cref="Stopwatch"/>; a test passes its own.</param>
    public StaticObstacleBakeSystem(Func<double>? wallSeconds = null)
    {
        if (wallSeconds != null) _wallSeconds = wallSeconds;
        else
        {
            var sw = Stopwatch.StartNew();
            _wallSeconds = () => sw.Elapsed.TotalSeconds;
        }
    }

    /// <summary>
    /// ⭐ O-7 B (CE-4418, docs/DESIGN_Terrain_Authoring.md §18e-2 B-1) — the editor's live block expansion; null on every other host (a block is inert data there: the compiled overlay reaches them at load).
    /// Set by the host that owns it, right after <c>EntityCreationPack.Build</c>; ⚠ a forwarding rail asserts it on the editor's constructed system (B-4).
    /// </summary>
    public LiveOverlay? LiveOverlay { get => _liveOverlay; set => _liveOverlay = value; }
    [Fdp.Core.NotState("injected live-overlay dependency (settable property)")]
    private LiveOverlay? _liveOverlay;

    /// <summary>
    /// ⭐ CE-4427 R-5 — the node's lifecycle module (set by the pack): the bake asks it which constructions wait for the terrain module ALONE. One of those with no obstacle components (the template translator
    /// failed, <c>SimTransform</c> or <c>ObstacleShape</c> is missing) could never be baked and, the wait being unbounded, would hang for ever — it is acked here, loudly.
    /// </summary>
    public Fdp.Toolkit.Lifecycle.EntityLifecycleModule? Lifecycle { get; set; }
    [Fdp.Core.FrameScratch("sole-waiter list: filled by CollectSoleWaiters each pass")]
    private readonly List<Entity> _soleWaiters = new();
    [Fdp.Core.ProtocolCache("entities already acked to the lifecycle module for unbakeable obstacles: ack latch")]
    private readonly HashSet<Entity> _unbakeableAcked = new();

    /// <summary>The overlay parts of the last committed bake (diagnostics / rails) — empty when none.</summary>
    [field: Fdp.Core.WorldDerived("last bake output parts, rebuilt by the bake and read by consumers")]
    public IReadOnlyList<OverlayPart> BakedParts { get; private set; } = Array.Empty<OverlayPart>();

    /// <summary>The last committed bake's dirty rectangles (null = unknown / everything) — what ui's 3-D chunk rebuild redoes (B-5).</summary>
    [field: Fdp.Core.WorldDerived("last bake dirty rectangles, rebuilt by the bake and read by consumers")]
    public IReadOnlyList<(Vector2 Min, Vector2 Max)>? LastDirty { get; private set; }

    /// <summary>Bakes committed (diagnostics / rails).</summary>
    [field: Fdp.Core.WorldDerived("diagnostics counter of bakes")]
    public int Bakes { get; private set; }

    /// <summary>The last bake's wall-clock duration (ms; diagnostics).</summary>
    [field: Fdp.Core.WorldDerived("diagnostics: last bake duration")]
    public long LastBakeMilliseconds { get; private set; }

    /// <summary>True while a bake runs in the background.</summary>
    public bool Baking => _running != null;

    public void Execute(ISimulationView view, float dt)
    {
        if (view is not EntityRepository world) return;
        // ⭐ CE-3294 — a reload starts a new world: it bakes as the first load did (debounce, bake, ack — the same obstacles on the same
        //   base were acked at once before), and a bake still running from the last world acks nothing when it lands (its batch names
        //   the old world's handles, which the reload hands to new entities).
        if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch))
        {
            _bakedSignature = EmptySignature; _bakedBase = int.MinValue; _seenSignature = EmptySignature; _seenAt = 0; _changedSince = double.NaN;
            _runningBatch = new List<Entity>();
        }
        AckTheUnbakeable(world);   // ⭐ CE-4427 R-5 — before any early return: a world without the obstacle types hangs nothing either
        if (!world.IsComponentTypeRegistered<StaticObstacle>()) return;

        var bakery = world.HasSingletonManaged<StaticObstacleBakery>() ? world.GetSingletonManaged<StaticObstacleBakery>() : null;
        // ⭐ CE-3436 (HOT PATH) — ONE allocation-free walk per frame over a cached query: the hash of the obstacle INPUTS (position, yaw, shape, material) and the Constructing ones into the reused
        //   _pending list. The prisms (a query, a prism and a label each) are built only when a bake STARTS, below. ⭐ Change detection is now this world-signature, not SignatureOf(prisms):
        //   both move exactly when an obstacle is added, removed, moved ≥ 1 cm, reshaped or re-materialled — debounce, base version and epoch are untouched.
        ulong obstacleSig = SignatureOfWorld(world, _probe, _pending);
        bool hasObstacles = obstacleSig != EmptySignature;

        if (bakery != null) bakery.Overlay = _liveOverlay;   // ⭐ CE-5039 — the load's readiness predicate asks the bakery whether the overlay is baked yet
        if (bakery == null)
        {
            // No terrain on this node: nothing to bake — the obstacle needs no wait here.
            foreach (var e in _pending) Ack(world, e);
            return;
        }

        if (_running != null)
        {
            if (!_running.IsCompleted)
            {
                // ⭐ CE-4600 (Breaching §7.5 item 16, MEASURED on city-battle: a change waited 7.5 s) — a change made WHILE a bake runs starts its latency clock NOW;
                //   ⛔ was: the clock started only after the commit, so it waited the rest of the running bake + the whole cap + its own bake
                if (double.IsNaN(_changedSince) && SignatureNow(world, bakery, obstacleSig, out _, out _, out _, out _) != _runningSignature) _changedSince = _wallSeconds();
                return;
            }
            Finish(world);
        }

        _liveOverlay?.PollFiles(_wallSeconds());   // ⭐ CE-4427 R-7 — a recipe / template edit (a stat per file, once a second)
        ulong signature = SignatureNow(world, bakery, obstacleSig, out var damage, out var craters, out var trees, out bool hasOverlay);
        ulong navHash = (damage?.NavHash ?? 0UL) | (craters != null ? 1UL : 0UL) | (trees != null ? 1UL : 0UL) | (bakery.Residency.BakesDamage && MinefieldSet.SignatureOf(world) != 0UL ? 1UL : 0UL);
        bool changed = signature != _bakedSignature || ((hasObstacles || navHash != 0 || hasOverlay) && bakery.BaseVersion != _bakedBase);
        if (!changed)
        {
            _changedSince = double.NaN;
            foreach (var e in _pending) Ack(world, e);   // already in the baked world (e.g. it arrived while that bake ran)
            return;
        }

        double now = _wallSeconds();
        if (double.IsNaN(_changedSince)) _changedSince = now;
        // ⭐ CE-4596 (P-0, V-1) — a change never waits past MaxLatencySeconds: under a barrage the quiet period never comes
        bool overdue = now - _changedSince >= MaxLatencySeconds;
        if (signature != _seenSignature) { _seenSignature = signature; _seenAt = now; if (!overdue) return; }
        else if (!overdue && now - _seenAt < DebounceSeconds) return;

        _changedSince = double.NaN;
        StartBake(world, bakery.Residency, damage, signature, hasOverlay, craters, trees, bakery.Residency.BakesDamage ? MinefieldSet.Of(world) : null);
    }

    /// <summary>
    /// The signature of everything the bake reads, NOW: the obstacles' (<paramref name="obstacleSig"/>), the building damage the navmesh follows (a node that bakes damage), the
    /// craters and felled trunks (every node), the editor's blocks and features. ONE place for the idle path and the running-bake check (CE-4600). Allocation-free: the view
    /// tables are cached (<c>ViewTableCache</c>), the overlay signature a cached query.
    /// </summary>
    private ulong SignatureNow(EntityRepository world, StaticObstacleBakery bakery, ulong obstacleSig, out BuildingDamageTable? damage, out CraterTable? craters, out FelledTrees? trees, out bool hasOverlay)
    {
        // ⭐ CE-4391 S-3 (B-10, B-12) — the building damage the navmesh must follow (passages and collapses; a hole moves nothing) joins the input hash,
        //   on a node that does world queries ONLY: a display node's terrain is never rewritten by damage.
        damage = bakery.Residency.BakesDamage ? DamageOf(world) : null;
        ulong navHash = damage?.NavHash ?? 0UL;
        // ⭐ CE-4591 (P-4 part 2, V-6) — the craters join the input hash on EVERY node: the ground they lower is drawn everywhere
        craters = CratersOf(world);
        if (craters != null) navHash = navHash == 0 ? craters.Hash : (navHash ^ craters.Hash) * 1099511628211UL;
        trees = TreesOf(world);   // ⭐ CE-4603 (B-6) — the felled trunks, on every node (the drawing hides them everywhere)
        if (trees != null) navHash = navHash == 0 ? trees.Hash : (navHash ^ trees.Hash) * 1099511628211UL;
        // ⭐ CE-4624 MS-T3 (MR-3) — the minefields (outlines, lanes — a lane record gone in a seek too) join the input hash, on a node that does world queries ONLY (the navmesh is what stamps them); allocation-free
        if (bakery.Residency.BakesDamage && MinefieldSet.SignatureOf(world) is var mineSig && mineSig != 0UL) navHash = navHash == 0 ? mineSig : (navHash ^ mineSig) * 1099511628211UL;
        ulong signature = obstacleSig;
        if (navHash != 0) { signature ^= navHash; signature *= 1099511628211UL; }
        // ⭐ O-7 B — the editor's blocks and features join the input hash (allocation-free: a cached query, no strings)
        ulong overlayHash = _liveOverlay?.SignatureOfWorld(world) ?? LiveOverlay.EmptySignature;
        hasOverlay = overlayHash != LiveOverlay.EmptySignature;
        _overlayNow = overlayHash;
        if (hasOverlay) { signature ^= overlayHash; signature *= 1099511628211UL; }
        return signature;
    }

    /// <summary>
    /// ⭐ ONE bake for everything that has gathered — a whole scenario's obstacles arrive within the quiet period. ⚠ CE-3436: its OWN method on purpose — the task's lambda captures locals, and a lambda in
    /// <c>Execute</c> made the compiler allocate its closure object at the top of EVERY frame (measured: 56 B per steady frame).
    /// </summary>
    private void StartBake(EntityRepository world, TerrainResidency residency, BuildingDamageTable? damage, ulong signature, bool hasOverlay, CraterTable? craters, FelledTrees? trees, MinefieldSet? minefields)
    {
        CollectPrisms(world, _prisms, _decks);   // ⭐ CE-3436 — only now (a bake is starting), never per frame; ⭐ CE-4692 — and the laid bridges' decks
        var prisms = new List<TerrainPrism>(_prisms);
        var decks = new List<TerrainWalkable>(_decks);
        _runningOn = residency;
        _runningBatch = new List<Entity>(_pending);
        _runningSignature = signature;
        _runningOverlay = hasOverlay ? _overlayNow : LiveOverlay.EmptySignature;   // ⭐ CE-5039 — what this bake will have committed of the overlay
        // ⭐ O-7 B — the blocks are read here, on the frame thread; their EXPANSION runs on the bake's task (B-2: only a changed entity re-expands)
        var overlay = _liveOverlay;
        var sources = hasOverlay && overlay != null ? overlay.Snapshot(world) : null;
        _running = Task.Run(() =>
        {
            LiveOverlayResult? expanded = null;
            if (overlay != null && sources != null)
            {
                var (@base, version, folder) = residency.ExpansionBase();
                expanded = overlay.Expand(sources, @base, version, folder);
            }
            return residency.BakeObstacles(prisms, damage, expanded, craters, trees, minefields, decks);
        });
    }

    /// <summary>
    /// ⭐ CE-4427 R-5 — acks, with a Warn, every construction that waits for THIS module alone and that the bake can never serve: its world has no <see cref="StaticObstacle"/> / <see cref="ObstacleShape"/> type, or
    /// the entity lacks one of those or its <see cref="SimTransform"/> (the query below never sees it). Allocation-free while nothing waits.
    /// </summary>
    private void AckTheUnbakeable(EntityRepository world)
    {
        var elm = Lifecycle;
        if (elm == null) return;
        elm.CollectSoleWaiters(TerrainObstacles.LifecycleModuleId, _soleWaiters);
        if (_soleWaiters.Count == 0) { if (_unbakeableAcked.Count > 0) _unbakeableAcked.Clear(); return; }
        bool typesRegistered = world.IsComponentTypeRegistered<StaticObstacle>() && world.IsComponentTypeRegistered<ObstacleShape>() && world.IsComponentTypeRegistered<SimTransform>();
        foreach (var e in _soleWaiters)
        {
            if (typesRegistered && world.HasComponent<StaticObstacle>(e) && world.HasComponent<ObstacleShape>(e) && world.HasComponent<SimTransform>(e)) continue;   // bakeable: the normal path acks it
            if (!_unbakeableAcked.Add(e)) continue;   // acked already; the lifecycle module takes the ack on its next tick
            FdpLog<StaticObstacleBakeSystem>.Warn(
                $"[Obstacles] {e}: waits for the terrain bake but cannot be baked (no StaticObstacle / ObstacleShape / SimTransform here): acked without a bake, so it does not hang.");
            Ack(world, e);
        }
    }

    /// <summary>⭐ CE-4603 — the trunks this node's world has felled (the view's <see cref="DoorStates.Trees"/>), or null.</summary>
    private static FelledTrees? TreesOf(EntityRepository world)
        => world.IsComponentTypeRegistered<TreeDamage>() && world.HasSingletonManaged<TerrainWorld>() && world.GetSingletonManaged<TerrainWorld>() is { } t
            ? DoorStates.Of(world, t).Trees : null;

    /// <summary>⭐ CE-4591 — the craters this node's world holds (the view's <see cref="DoorStates.Craters"/>), or null.</summary>
    private static CraterTable? CratersOf(EntityRepository world)
        => world.IsComponentTypeRegistered<GroundDamage>() && world.HasSingletonManaged<TerrainWorld>() && world.GetSingletonManaged<TerrainWorld>() is { } t
            ? DoorStates.Of(world, t).Craters : null;

    /// <summary>The building damage this node's world holds (the view's <see cref="DoorStates.Damage"/>), or null.</summary>
    private static BuildingDamageTable? DamageOf(EntityRepository world)
        => world.HasSingletonManaged<TerrainWorld>() && world.GetSingletonManaged<TerrainWorld>() is { Panels.Count: > 0 } t
            ? DoorStates.Of(world, t).Damage : null;

    private void Finish(EntityRepository world)
    {
        var task = _running!;
        var batch = _runningBatch;
        var residency = _runningOn!;
        _running = null; _runningOn = null; _runningBatch = new List<Entity>();

        if (task.IsFaulted)
        {
            // ⛔ Never silent: the obstacles still join the simulation (an obstacle must not hang a scenario load), but this
            //    node's terrain does not hold them — said loudly.
            FdpLog<StaticObstacleBakeSystem>.Error(
                $"[Obstacles] Bake of {batch.Count} obstacle(s) FAILED — they are live but NOT in this node's terrain: {task.Exception?.GetBaseException().Message}");
            MarkOverlayCommitted(world, residency);   // ⭐ CE-5039 — a failed bake must not hold the load for ever either (said loudly above)
            foreach (var e in batch) Ack(world, e);
            return;
        }

        var bake = task.Result;
        if (!residency.CommitObstacles(world, bake))
            return;   // the terrain changed under the bake: the next frame bakes again over the new base (the batch keeps waiting)

        _bakedSignature = _runningSignature;
        _bakedBase = residency.BaseVersion;
        MarkOverlayCommitted(world, residency);
        Bakes++;
        LastBakeMilliseconds = bake.Milliseconds;
        BakedParts = bake.Parts; LastDirty = bake.Dirty;
        // ⭐ Terrain authoring U-3 / U-6 — the views read the block bounds and the dirty rectangles of THIS bake (one publication, immutable).
        // ⭐ CE-4596 (P-0, V-3) — on EVERY host, not the editor's alone: the 3-D map (one map pack for every host) rebuilds only the chunks under
        //   the rectangles; without it a player or a display node rebuilt the WHOLE 3-D terrain on every obstacle, breach or crater (a pop).
        world.RegisterManagedComponent<LiveTerrainBakeInfo>();
        world.SetSingletonManaged(LiveTerrainBakeInfo.From(Bakes, bake.Parts, bake.Dirty));
        FdpLog<StaticObstacleBakeSystem>.Info(
            $"[Obstacles] Baked {bake.Obstacles} static obstacle(s) into the terrain in {bake.Milliseconds} ms; acking {batch.Count}.");
        foreach (var e in batch) Ack(world, e);
    }

    /// <summary>⭐ CE-5039 — the bakery learns what of the live overlay the terrain now holds (the load's readiness predicate reads it: <see cref="StaticObstacleBakery.AwaitsLiveOverlay"/>).</summary>
    private void MarkOverlayCommitted(EntityRepository world, TerrainResidency residency)
    {
        if (!world.HasSingletonManaged<StaticObstacleBakery>() || world.GetSingletonManaged<StaticObstacleBakery>() is not { } bakery || !ReferenceEquals(bakery.Residency, residency)) return;
        bakery.CommittedOverlay = _runningOverlay;
        bakery.CommittedOverlayBase = residency.BaseVersion;
    }

    /// <summary>
    /// The static obstacles of <paramref name="world"/> (Active, and still Constructing when <paramref name="pending"/> is given) as terrain prisms. ⭐ O-8 (CE-4420) — the ONE collector: the node's
    /// bake system and the Replay Browser (which bakes the recording's obstacles into its displayed terrain once per seek) both read the world through it.
    /// </summary>
    public static void CollectPrisms(EntityRepository world, List<TerrainPrism> prisms, List<Entity>? pending = null)
        => CollectPrisms(world, prisms, null, pending);

    /// <summary>
    /// ⭐ <c>CE-4692</c> BL-5 — as above, and every WALKABLE-DECK obstacle (a laid bridge, <see cref="ObstacleKind.WalkableDeck"/>) into <paramref name="decks"/> as its deck walkables
    /// (<see cref="TerrainObstacles.DeckOf"/>, laid on the world's ground) instead of a prism. <paramref name="decks"/> null ⇒ a deck is skipped (a caller that bakes no walkables).
    /// </summary>
    public static void CollectPrisms(EntityRepository world, List<TerrainPrism> prisms, List<TerrainWalkable>? decks, List<Entity>? pending = null)
    {
        prisms.Clear();
        decks?.Clear();
        pending?.Clear();
        if (!world.IsComponentTypeRegistered<StaticObstacle>() || !world.IsComponentTypeRegistered<ObstacleShape>()) return;
        var library = (world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>()?.Materials : null) ?? TerrainMaterialLibrary.Shared;
        var tkb = world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() ? world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null;
        bool tkbKnown = world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>();

        foreach (var e in world.Query().With<StaticObstacle>().With<ObstacleShape>().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build())
        {
            var state = world.GetLifecycleState(e);
            if (state != EntityLifecycle.Active && state != EntityLifecycle.Constructing) continue;
            if (state == EntityLifecycle.Constructing) pending?.Add(e);

            ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
            ref readonly var shape = ref world.GetComponentRO<ObstacleShape>(e);
            ref readonly var obstacle = ref world.GetComponentRO<StaticObstacle>(e);
            if (obstacle.Kind == ObstacleKind.WalkableDeck)   // ⭐ CE-4692 BL-1 — a deck you stand on, never a box
            {
                if (decks != null)
                {
                    var ground = world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
                    float flat = t.Position.Z;
                    Func<float, float, float> groundAt = ground != null ? (x, y) => ground.GroundHeightAt(x, y) : (_, _) => flat;
                    TerrainObstacles.DeckOf(t.Position, TerrainObstacles.Yaw(t.Rotation), shape, groundAt, e.Index, DrawsItsOwnBody(world, e, tkb, tkbKnown), decks);
                }
                continue;
            }
            string materialName = obstacle.Material.ToString() ?? "concrete";
            var material = TerrainObstacles.MaterialOf(library, materialName, out bool known);
            if (!known)
                FdpLog<StaticObstacleBakeSystem>.Warn($"[Obstacles] {e}: unknown material '{materialName}' — baked as concrete.");
            prisms.Add(TerrainObstacles.PrismOf(t.Position, TerrainObstacles.Yaw(t.Rotation), shape, material,
                TerrainObstacles.LabelPrefix + e.Index, drawnByEntity: DrawsItsOwnBody(world, e, tkb, tkbKnown)));
        }
    }

    /// <summary>
    /// ⭐ CE-4656 — true when obstacle <paramref name="e"/> is drawn by the entity layer as a body of its own: its TKB type is a PLATFORM (DIS kind 1 — the parked
    /// car, a ground-vehicle avatar and the 3-D car kit, rail <c>Map3DTests.CE3148_*</c>). Its baked prism is then collision / navmesh / sight only. ⚠ A cultural
    /// feature (kind 5: sandbags, a block, a crate, a wreck …) keeps its baked box drawn — the 3-D layer has no kit of its own for it.
    /// </summary>
    public static bool DrawsItsOwnBody(EntityRepository world, Entity e, Fdp.Interfaces.ITkbDatabase? tkb, bool tkbKnown)
        => tkb != null && tkbKnown && world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(e)
           && tkb.TryGetByType((long)world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType, out var t) && t.DisType.Kind == 1;

    /// <summary>
    /// ⭐ O-8 (CE-4420) — the hash of the obstacle set read STRAIGHT FROM THE COMPONENTS, allocating nothing (HOT PATH: the Replay Browser asks it on every tick at playback speeds above one frame per tick):
    /// per Active / Constructing obstacle the position at 1 cm, the yaw, the <see cref="ObstacleShape"/> and the material's hash. It moves when the set an obstacle bake would see moves (an obstacle added, removed,
    /// moved by ≥ a few cm, reshaped, re-materialled) and is stable otherwise; ⚠ it is NOT numerically equal to <see cref="SignatureOf"/> (it hashes the inputs, that hashes the derived footprint), and the node's own
    /// system keeps <see cref="SignatureOf"/>. Pass a <paramref name="probe"/> held across calls so the query is built once per world.
    /// </summary>
    public static ulong SignatureOfWorld(EntityRepository world, ObstacleSignatureProbe? probe = null) => SignatureOfWorld(world, probe, null);

    /// <summary>⭐ CE-3436 — as above, and the Constructing obstacles are added to <paramref name="constructing"/> (cleared first; a list the caller reuses, so no allocation once it has grown) in the SAME walk.</summary>
    public static ulong SignatureOfWorld(EntityRepository world, ObstacleSignatureProbe? probe, List<Entity>? constructing)
    {
        constructing?.Clear();
        ulong h = EmptySignature;
        if (!world.IsComponentTypeRegistered<StaticObstacle>() || !world.IsComponentTypeRegistered<ObstacleShape>()) return h;
        var query = (probe ??= new ObstacleSignatureProbe()).QueryOf(world);
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        void MixF(float f) => Mix((long)MathF.Round(f * 100f));
        foreach (var e in query)
        {
            var state = world.GetLifecycleState(e);
            if (state != EntityLifecycle.Active && state != EntityLifecycle.Constructing) continue;
            if (state == EntityLifecycle.Constructing) constructing?.Add(e);
            ref readonly var t = ref world.GetComponentRO<SimTransform>(e);
            ref readonly var shape = ref world.GetComponentRO<ObstacleShape>(e);
            MixF(t.Position.X); MixF(t.Position.Y); MixF(t.Position.Z);
            Mix((long)MathF.Round(TerrainObstacles.Yaw(t.Rotation) * 1000f));
            MixF(shape.Length); MixF(shape.Width); MixF(shape.Height);
            ref readonly var o = ref world.GetComponentRO<StaticObstacle>(e);
            Mix(o.Material.GetHashCode());
            Mix((long)o.Kind);   // ⭐ CE-4692 — a deck and a box of the same size are different terrain
        }
        return h;
    }

    /// <summary>The hash of an obstacle set (FNV-1a over geometry and material, 1 cm quantised) — what "the set changed" means for the bake and for the Replay Browser.</summary>
    public static ulong SignatureOf(List<TerrainPrism> prisms)
    {
        // FNV-1a over every prism's identity and geometry, in query order (stable within a world).
        // ⭐ Quantised to 1 cm: a REPLICA's position arrives through the geo transform with float noise (105.000015), and an
        //   exact-bits hash re-baked the world on every position refresh — measured live on bt-window-duel (~150 bakes, CE-3136 P-8).
        ulong h = EmptySignature;
        void Mix(float f) { h ^= (ulong)(long)MathF.Round(f * 100f); h *= 1099511628211UL; }
        foreach (var p in prisms)
        {
            foreach (var v in p.Footprint) { Mix(v.X); Mix(v.Y); }
            Mix(p.BaseZ); Mix(p.TopZ);
            h ^= (p.Material?.Name is { } m ? Fdp.Core.StableHash.Fnv1a32(m) : 0u);   // ⭐ CE-3302 — not string.GetHashCode (per process)
            h *= 1099511628211UL;
        }
        return h;
    }

    private static void Ack(EntityRepository world, Entity e)
        => world.Bus.Publish(new ConstructionAck { Entity = e, ModuleId = TerrainObstacles.LifecycleModuleId, Success = true });
}

/// <summary>⭐ O-8 — the cached obstacle query of <see cref="StaticObstacleBakeSystem.SignatureOfWorld"/>: built once per world (a query object allocates), reused while the world is the same.</summary>
[Fdp.Core.WorldDerived("a cached obstacle query, rebuilt per world")]
public sealed class ObstacleSignatureProbe
{
    private EntityRepository? _for;
    private EntityQuery? _query;

    internal EntityQuery QueryOf(EntityRepository world)
    {
        if (!ReferenceEquals(world, _for) || _query == null)
        {
            _query = world.Query().With<StaticObstacle>().With<ObstacleShape>().With<SimTransform>().WithLifecycle(EntityLifecycle.All).Build();
            _for = world;
        }
        return _query;
    }
}
