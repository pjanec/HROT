using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Per-frame housekeeping for all live bullet entities.
    /// <para>
    /// <b>Execution phase:</b> <see cref="PostSimulationSystemGroup"/>, <b>after</b>
    /// <c>LinearKinematicsSystem</c> (which advances positions) and before
    /// <c>SpatialHashSystem</c> (which rebuilds the grid).
    /// </para>
    /// <para>
    /// <b>Execution order rationale (Phase 0 Adaptation):</b><br/>
    /// Bullet movement is delegated to <c>LinearKinematicsSystem</c> which runs
    /// <c>pos += vel * dt</c>.  BallisticsSystem must run <em>before</em> that, so:
    /// <list type="number">
    ///   <item>
    ///     The raycast segment <c>Start = PreviousPosition, End = SimTransform.Position</c>
    ///     covers exactly the distance traversed in the <em>previous</em> frame.
    ///   </item>
    ///   <item>
    ///     <c>PreviousPosition</c> is then updated to <c>SimTransform.Position</c> so the
    ///     next frame's segment picks up from the correct starting point.
    ///   </item>
    /// </list>
    /// Required ordering: BallisticsSystem → LinearKinematicsSystem → RaycastSolverSystem.
    /// </para>
    /// <para>
    /// <b>Capacity guard (DEBT-021 pattern):</b> The batch is never written beyond
    /// <see cref="PhysicsConstants.RaycastBatchCapacity"/>; excess bullets are silently
    /// skipped this frame (no crash, no exception).
    /// </para>
    /// <!-- [UpdateBefore(typeof(LinearKinematicsSystem))] — LinearKinematicsSystem is not yet
    ///      defined in a referenceable assembly (Phase 0). Attribute will be added once the class
    ///      is introduced. Ordering is maintained by the host application's registration order. -->
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    // [UpdateAfter(typeof(HitResolutionSystem))] — ordering maintained by array position in CombatModule.
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public class BallisticsSystem : IEcsModuleSystem
    {
        /// <summary>⭐ <c>CE-4649</c> (bows BW-4) — at most this many rounds LIE (<see cref="WarheadRound.Landed"/>) on this node, oldest removed first. ⭐ For EVERY lying
        /// round (a stone, a grenade, a thrown blade, an arrow), not only arrows: before, nothing capped them.</summary>
        public const int LyingCap = 256;

        // ⭐ CE-4649 — the lying rounds, oldest first (an array and a count: no allocation, no LINQ — R-2084). Per node: the system runs once on the shooter's node.
        [Fdp.Core.WorldDerived("the landed rounds held for the lying cap, oldest first; reset at the world epoch; emptied, only the cleanup order changes")]
        private readonly Entity[] _lying = new Entity[LyingCap];
        [Fdp.Core.WorldDerived("the count of _lying (reset with it at the world epoch)")]
        private int _lyingCount;
        private int _worldEpoch;

        /// <summary>How many rounds this node holds as lying (the rail reads it).</summary>
        public int LyingCount => _lyingCount;

        /// <summary>
        /// ⭐ <c>CE-4649</c> (BW-4) — a round has just LANDED: remember it, and when that makes more than <see cref="LyingCap"/>, remove the OLDEST that still lies
        /// (entries whose round is gone — picked up, burst, expired — are dropped first, so the cap counts rounds that exist). ⛔ No allocation.
        /// </summary>
        public void NoteLying(EntityRepository repo, Entity round)
        {
            if (_lyingCount == LyingCap)
            {
                int w = 0;
                for (int i = 0; i < _lyingCount; i++)
                    if (StillLies(repo, _lying[i])) _lying[w++] = _lying[i];
                _lyingCount = w;
                if (_lyingCount == LyingCap)
                {
                    var oldest = _lying[0];
                    System.Array.Copy(_lying, 1, _lying, 0, LyingCap - 1);
                    _lyingCount = LyingCap - 1;
                    repo.DestroyEntity(oldest);   // StillLies ⇒ alive
                }
            }
            _lying[_lyingCount++] = round;
        }

        private static bool StillLies(EntityRepository repo, Entity e)
            => repo.IsAlive(e) && repo.HasComponent<BallisticProjectile>(e)
               && (repo.GetComponentRO<BallisticProjectile>(e).Warhead & WarheadRound.Landed) != 0;

        /// <summary>
        /// ⭐ <c>CE-4649</c> (BW-4) — a round whose look STICKS has stopped on a surface: it keeps its POSE — rotation = its flight direction (the shaft's +X along
        /// it), its point pushed a THIRD of its length into the surface along the flight — so every node's lying draw can show it stuck, not flat.
        /// </summary>
        internal static void Stick(EntityRepository repo, Entity round, in BallisticProjectile proj, System.Numerics.Vector3 flight)
        {
            if (flight.LengthSquared() < 1e-8f) return;
            var dir = System.Numerics.Vector3.Normalize(flight);
            float length = CombatTkb.VisualOf(repo, proj.Ammo)?.Length ?? 0.5f;
            ref var at = ref repo.GetComponentRW<SimTransform>(round);
            at.Position += dir * (length / 3f);
            at.Rotation = AlongX(dir);
        }

        /// <summary>The rotation that takes +X onto <paramref name="dir"/> (a unit vector), by the shortest turn.</summary>
        public static System.Numerics.Quaternion AlongX(System.Numerics.Vector3 dir)
        {
            float dot = System.Numerics.Vector3.Dot(System.Numerics.Vector3.UnitX, dir);
            if (dot > 0.99999f) return System.Numerics.Quaternion.Identity;
            if (dot < -0.99999f) return System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, MathF.PI);
            var axis = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.UnitX, dir));
            return System.Numerics.Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _lyingCount = 0;   // ⭐ R-4297 ② (CE-1980) — a world replacement (a reload reuses the handles, a restore) forgets the rounds it held as lying
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            // ⭐ CE-3293 S5 (R-3036 rule 8) — no hand pause gate: [TickMode(WhenTimeAdvances)] keeps this system off a frame that does not advance

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BallisticsSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            uint currentTick = repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            var cmd = view.GetCommandBuffer();

            // ⭐ CE-3064 — the unit grid the raycast solver uses; no grid or no NearMissEvent registered ⇒ no near-miss sensing.
            global::CarKinem.Spatial.SpatialHashGrid? nearMissGrid =
                repo.HasSingleton<global::CarKinem.Spatial.SpatialGridData>() && repo.Bus.IsRegistered<Events.NearMissEvent>()
                    ? repo.GetSingleton<global::CarKinem.Spatial.SpatialGridData>().Grid : null;

            // ⭐ R-217 — the world the rounds fly through; none ⇒ no terrain, as before. ⭐ CE-1035 Q1 — through the world query,
            //   bound to the doors as THIS view sees them (R-219), so nothing here knows the terrain stand-in.
            var terrain = global::Fdp.Toolkit.World.WorldQuery.Of(view);

            var shots = ShotLog.Peek(repo);   // ⭐ T-4 — rounds fired through FireProcessingSystem have a record
            // ⭐ CE-1128 (docs/DESIGN_Visual_Effects.md §10) — the surfaces rounds mark, for the map hosts' puffs and marks
            var impacts = repo.Bus.IsRegistered<Contracts.RoundImpactNotification>() ? (t_impacts ??= new List<RoundImpact>()) : null;
            bool guided = repo.IsComponentTypeRegistered<GuidedRound>();   // ⭐ M4
            // ⭐ CE-5033 (glass windows GW-2, D-4) — the panes rounds pass, for the window's hits; only with a world that has glass
            var paneWorld = terrain is Fdp.Toolkit.Terrain.TerrainWorldQuery twq && twq.World.PaneLeaves.Count > 0 && repo.Bus.IsRegistered<Fdp.Toolkit.Terrain.WindowHitEvent>() ? twq.World : null;
            var panes = paneWorld != null ? (t_panes ??= new List<PaneHit>()) : null;

            var query = repo.Query()
                .With<BallisticProjectile>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                if (repo.GetLifecycleState(entity) == EntityLifecycle.TearDown)
                {
                    repo.DestroyEntity(entity);
                    continue;
                }

                ref var proj = ref repo.GetComponentRW<BallisticProjectile>(entity);
                var shot = shots?.Of(entity);

                // ── ⭐ Stage 6 (CE-1032, W-4) — a warhead round lying where it landed waits for its time fuze, then bursts.
                if ((proj.Warhead & WarheadRound.Landed) != 0)
                {
                    proj.FuzeRemaining -= deltaTime;
                    if (proj.FuzeRemaining <= 0f)
                    {
                        // ⭐ CE-3235 (B-13, §7.10 PU-1) — a LINGERING inert round (a stone) has lain its time and goes without a burst
                        if ((proj.Warhead & WarheadRound.Lingers) == 0)
                        {
                            Detonate(repo, in proj, repo.GetComponent<SimTransform>(entity).Position, shot, currentTick, entity);
                            if (BeginVenting(ref proj)) { NoteLying(repo, entity); continue; }   // ⭐ CE-3236 (B-14, VC-1) — a gas / smoke grenade vents where it lies (⭐ CE-4649: and counts as lying)
                        }
                        repo.DestroyEntity(entity);
                    }
                    continue;
                }

                // ── 0. ⭐ R-217 — a round stopped by the terrain stays frozen at the wall for a grace period, so the raycasts of its
                //   last segments (three ticks in flight) still resolve — a unit IN FRONT of the wall is still struck — then goes.
                if (proj.StoppedTick != 0)
                {
                    // ⭐⭐ CE-2203 — an IMPACT round that stopped bursts only once the raycasts of its last segments have resolved
                    //   (ContactHitWaitTicks): one of them meeting a unit (a contact hit) ends the round first and it never bursts here.
                    if ((proj.Warhead & WarheadRound.Area) != 0 && (proj.Warhead & (WarheadRound.Spent | WarheadRound.TimeFuze)) == 0
                        && currentTick - proj.StoppedTick >= CombatConstants.ContactHitWaitTicks)
                    {
                        BurstWhereStopped(repo, in proj, repo.GetComponent<SimTransform>(entity).Position, shot, currentTick, entity);
                        if (!BeginVenting(ref proj)) repo.DestroyEntity(entity); else NoteLying(repo, entity);   // ⭐ CE-3236 (B-14, VC-1) — a canister vents where it lies (⭐ CE-4649: and counts as lying)
                        continue;
                    }
                    if (currentTick - proj.StoppedTick >= CombatConstants.StoppedRoundGraceTicks)
                    {
                        if (shot != null && (proj.Warhead & WarheadRound.Spent) == 0)
                            ShotLog.EndCarried(shot, ShotOutcome.StoppedByTerrain, currentTick, proj.PreviousPosition, terrain, in proj);
                        // ⭐ CE-3235 (B-13, §7.10 PU-1) — an inert HAND ITEM (a stone) LIES where it stopped for its lying time (carried in
                        //   FuzeRemaining): a hand may pick it up (RoundPickUp) — the Landed branch above ends it without a burst.
                        if ((proj.Warhead & (WarheadRound.Lingers | WarheadRound.Spent)) == WarheadRound.Lingers && proj.FuzeRemaining > 0f)
                        {
                            proj.Warhead |= WarheadRound.Landed;
                            NoteLying(repo, entity);   // ⭐ CE-4649 (BW-4) — the cap: at most LyingCap lying rounds
                        }
                        else
                            repo.DestroyEntity(entity);
                    }
                    continue;
                }

                // ── 1. Lifetime check ────────────────────────────────────────────
                // Unsigned subtraction handles tick-counter wrap correctly.
                // ⭐ CE-1032 (W-4) — a time fuze running out in flight bursts the round where it is.
                if ((proj.Warhead & WarheadRound.TimeFuze) != 0)
                {
                    proj.FuzeRemaining -= deltaTime;
                    if (proj.FuzeRemaining <= 0f)
                    {
                        Detonate(repo, in proj, repo.GetComponent<SimTransform>(entity).Position, shot, currentTick, entity);
                        repo.DestroyEntity(entity);
                        continue;
                    }
                }

                // ⭐ CE-1032 (W-8) — an arc round falls: gravity on its velocity (LinearKinematicsSystem integrates it).
                if ((proj.Warhead & WarheadRound.Arc) != 0 && repo.HasComponent<SimVelocity>(entity))
                    repo.GetComponentRW<SimVelocity>(entity).Linear.Z -= CombatConstants.Gravity * deltaTime;

                uint lifetime = (proj.Warhead & (WarheadRound.Arc | WarheadRound.TimeFuze)) != 0
                    ? CombatConstants.ArcRoundLifetimeTicks : CombatConstants.BulletLifetimeTicks;
                // ⭐ Indirect fire IF-1 (I5) — an arc round lives its own flight + 10 s (a 90 s howitzer round still lands)
                if ((proj.Warhead & WarheadRound.Arc) != 0 && proj.ArcLifetimeTicks > lifetime) lifetime = proj.ArcLifetimeTicks;
                // ⭐ M4 — a missile lives its flight (MaxFlightSeconds), not a bullet's 2 s: a 2 km TOW vanished mid-flight
                if (guided && repo.HasComponent<GuidedRound>(entity) && repo.GetComponentRO<GuidedRound>(entity).LifetimeTicks > 0)
                    lifetime = repo.GetComponentRO<GuidedRound>(entity).LifetimeTicks;
                // ⭐ CE-2436 AU-7 (A-10a) — a pellet past its range is spent (no reach beyond it)
                bool spentByRange = proj.RangeLimitM > 0f
                    && System.Numerics.Vector3.DistanceSquared(repo.GetComponentRO<SimTransform>(entity).Position, proj.Muzzle) > proj.RangeLimitM * proj.RangeLimitM;
                if (spentByRange || currentTick - proj.SpawnTick >= lifetime)
                {
                    if (shot != null) ShotLog.EndCarried(shot, ShotOutcome.Expired, currentTick, repo.GetComponent<SimTransform>(entity).Position, terrain, in proj);
                    repo.DestroyEntity(entity);
                    continue;   // do NOT submit a raycast for a just-destroyed bullet
                }

                // ── 2. Submit swept-segment raycast via event bus ─────────────────
                var tf = repo.GetComponent<SimTransform>(entity);

                // ── 2a. ⭐⭐ Buildings §3d P2 (R-217) — the segment through the TERRAIN: walls, fences and floors resist by the armour
                //   rule (the round's FRONT values, carried segment by segment); a crossing the round cannot pass ENDS the segment
                //   there — no unit beyond it is struck — and freezes the round at the wall. The damage a unit takes is NOT decided
                //   here: HitResolutionSystem carries the round from its muzzle to the unit, whenever the raycast resolves.
                var end = tf.Position;
                var segment = tf.Position - proj.PreviousPosition;   // ⭐ CE-4649 — the way it was flying this segment (a sticking round keeps it)
                if (terrain != null)
                {
                    if ((proj.TerrainFlags & 1) == 0)
                    {
                        proj.Muzzle = proj.PreviousPosition; proj.FrontDamage = proj.Damage; proj.FrontPenetration = proj.Penetration;
                        proj.TerrainFlags |= 1;
                    }
                    // ⭐ CE-5033 — firing OUT: once per round, on its first segment (bit 2 — a round FireProcessingSystem spawns already has its muzzle, bit 1)
                    if ((proj.TerrainFlags & 2) == 0)
                    {
                        proj.TerrainFlags |= 2;
                        if (paneWorld != null) ShatterPanesAtTheMuzzle(repo, cmd, paneWorld, (Fdp.Toolkit.Terrain.TerrainWorldQuery)terrain, in proj);
                    }
                    impacts?.Clear();
                    panes?.Clear();
                    bool stopped = TerrainPenetration.Carry(terrain, proj.PreviousPosition, tf.Position, ref proj.FrontDamage, ref proj.FrontPenetration, out float stopT, null, impacts, panes);
                    if (panes is { Count: > 0 }) PublishPaneHits(cmd, paneWorld!, in proj, panes);
                    if (stopped)
                    {
                        end = System.Numerics.Vector3.Lerp(proj.PreviousPosition, tf.Position, stopT);
                        proj.StoppedTick = currentTick == 0 ? 1u : currentTick;
                        ref var at = ref repo.GetComponentRW<SimTransform>(entity);
                        at.Position = end;
                        if (repo.HasComponent<SimVelocity>(entity)) repo.GetComponentRW<SimVelocity>(entity).Linear = System.Numerics.Vector3.Zero;
                    }
                }

                // ── 2a′. ⭐ CE-1128 (§10 IM-2) — ANY round meets the ground: the fire query does not count flat ground, so a bullet aimed
                //   at it flew on below it until it expired (no mark could exist). With a world, it stops where its segment enters the
                //   ground, like at a wall. (A warhead round's own ground check below then finds it already stopped and bursts there.)
                bool groundStop = false;
                if (terrain != null && (proj.Warhead & WarheadRound.Area) == 0 && proj.StoppedTick != (currentTick == 0 ? 1u : currentTick))
                {
                    float gz = terrain.GroundHeightAt(end.X, end.Y);
                    if (end.Z < gz && proj.PreviousPosition.Z >= gz)
                    {
                        float t = (proj.PreviousPosition.Z - gz) / (proj.PreviousPosition.Z - end.Z);
                        end = System.Numerics.Vector3.Lerp(proj.PreviousPosition, end, t);
                        proj.StoppedTick = currentTick == 0 ? 1u : currentTick;
                        repo.GetComponentRW<SimTransform>(entity).Position = end;
                        if (repo.HasComponent<SimVelocity>(entity)) repo.GetComponentRW<SimVelocity>(entity).Linear = System.Numerics.Vector3.Zero;
                        groundStop = true;
                    }
                }
                if (impacts != null) PublishImpacts(cmd, in proj, impacts, end, groundStop);
                // ⭐ CE-4649 (bows BW-4) — a round that STICKS and has just stopped freezes with its pose (the pushed-in point is read by nothing in the physics:
                //   the raycast below ends at `end`, the stop point)
                if ((proj.Warhead & WarheadRound.Sticks) != 0 && proj.StoppedTick == (currentTick == 0 ? 1u : currentTick))
                    Stick(repo, entity, in proj, segment.LengthSquared() > 1e-8f ? segment : end - (System.Numerics.Vector3)proj.Muzzle);

                // ── 2b′. ⭐⭐ Stage 6 (CE-1032, W-3/W-4) — a WARHEAD round that stops (on the terrain, or on the flat ground — which the
                //   terrain query does not count) does not just end: on an impact fuze it BURSTS there, a little back along its flight
                //   so the surface it struck stands between the burst and what is behind it (a roof over a room); on a time fuze it
                //   LANDS and waits. Its last raycast still goes out (a unit in the way is struck first — a contact burst).
                if ((proj.Warhead & WarheadRound.Area) != 0)
                {
                    // ⚠ the ground under the round's end (TH-D will make this a real ground trace — the ground is not flat, R-248)
                    float groundZ = terrain?.GroundHeightAt(end.X, end.Y) ?? 0f;
                    bool stoppedHere = proj.StoppedTick == (currentTick == 0 ? 1u : currentTick);
                    if (!stoppedHere && end.Z < groundZ && proj.PreviousPosition.Z >= groundZ)
                    {
                        float t = (proj.PreviousPosition.Z - groundZ) / (proj.PreviousPosition.Z - end.Z);
                        end = System.Numerics.Vector3.Lerp(proj.PreviousPosition, end, t);
                        proj.StoppedTick = currentTick == 0 ? 1u : currentTick;
                        ref var at = ref repo.GetComponentRW<SimTransform>(entity);
                        at.Position = end;
                        if (repo.HasComponent<SimVelocity>(entity)) repo.GetComponentRW<SimVelocity>(entity).Linear = System.Numerics.Vector3.Zero;
                        stoppedHere = true;
                    }
                    if (stoppedHere)
                    {
                        var back = proj.PreviousPosition - end;
                        var burst = back.LengthSquared() > 1e-8f
                            ? end + System.Numerics.Vector3.Normalize(back) * AreaEffect.BurstStandOffMetres : end;
                        if ((proj.Warhead & WarheadRound.TimeFuze) != 0)
                        {
                            proj.Warhead |= WarheadRound.Landed;
                            NoteLying(repo, entity);   // ⭐ CE-4649 (BW-4) — a fuzed grenade that lands lies too
                        }
                        // ⭐⭐ CE-2203 — an impact round waits at its burst point for the grace (the StoppedTick branch above bursts it):
                        //   ⛔ it burst HERE at once and was marked Spent, so the raycast of an EARLIER segment that crossed a unit —
                        //   resolving up to three ticks later — was dropped (HitResolutionSystem skips a Spent round). 📐 Live
                        //   ua-tank-ammo: a tank's HE / HEAT rounds at a pickup 230 m away burst on the ground 230 m beyond it, 13 / 13.
                        repo.GetComponentRW<SimTransform>(entity).Position = burst;
                    }
                }

                cmd.PublishEvent(new RaycastRequestEvent
                {
                    Start        = proj.PreviousPosition,
                    End          = end,
                    RayId        = PhysicsConstants.PackBulletRayId(entity),   // ⭐ CE-2204 — with its generation
                    LayerMask    = ~CombatConstants.BulletCollisionLayer,  // hit everything except other bullets
                    IgnoreEntity = proj.Shooter,
                });

                // ── 2b. ⭐ CE-3064 (R-206) — near misses along the same swept segment ──
                if (nearMissGrid.HasValue) ReportNearMisses(repo, cmd, nearMissGrid.Value, ref proj, proj.PreviousPosition, end);

                // ── 3. Update PreviousPosition ───────────────────────────────────
                // Record the bullet's current position so the next frame's raycast
                // sweeps the correct segment (after LinearKinematicsSystem advances it).
                proj.PreviousPosition = end;
            }
        }

        [Fdp.Core.WorldDerived("thread-static scratch list")]
        [ThreadStatic] private static List<RoundImpact>? t_impacts;

        /// <summary>
        /// ⭐ <c>CE-1128</c> (docs/DESIGN_Visual_Effects.md §10 IM-2, IM-5) — this segment's marks as events: each crossing passed (entry,
        /// exit) and the one that stopped the round — beyond <paramref name="end"/> only an exit can lie (the far face of a piece the round
        /// passed); a ground stop adds a Stop facing up. ⛔ A WARHEAD round's stop is not marked here: its burst draws the explosion and the
        /// scorch (VE-I step 1).
        /// </summary>
        /// <summary>The distance (m) from the muzzle within which a pane breaks on the first round — a man firing OUT through it (D-4).</summary>
        public const float FireOutShatterMetres = 1.5f;

        [Fdp.Core.FrameScratch("thread-static pane-hit list reused per call")]
        [ThreadStatic] private static List<PaneHit>? t_panes;
        [Fdp.Core.FrameScratch("thread-static muzzle-pane list reused per call")]
        [ThreadStatic] private static List<Fdp.Toolkit.Terrain.TerrainWorld.FireCrossing>? t_muzzlePanes;

        /// <summary>The height (m) above a shooter's feet his chest — where the gun is held — is taken at.</summary>
        public const float ShooterChestM = 1.4f;

        /// <summary>
        /// ⭐ CE-5033 (glass windows D-4, "a man firing out") — a man at a window pushes his BARREL through the opening: 📐 measured on <c>gw-fire-out</c>, the
        /// muzzle stood 0.28 m OUTSIDE the wall, so his rounds never crossed the pane. ⇒ on a round's first segment, every pane between the shooter's chest and
        /// the muzzle (and within <see cref="FireOutShatterMetres"/> of the muzzle) shatters. ⛔ No allocation in the steady state (a thread's list).
        /// </summary>
        private static void ShatterPanesAtTheMuzzle(EntityRepository repo, Fdp.Interfaces.IEntityCommandBuffer cmd, Fdp.Toolkit.Terrain.TerrainWorld world,
                                                    Fdp.Toolkit.Terrain.TerrainWorldQuery query, in BallisticProjectile proj)
        {
            if (!repo.IsAlive(proj.Shooter) || !repo.HasComponent<SimTransform>(proj.Shooter)) return;
            var feet = repo.GetComponentRO<SimTransform>(proj.Shooter).Position;
            var chest = new System.Numerics.Vector3(feet.X, feet.Y, feet.Z + ShooterChestM);
            var muzzle = (System.Numerics.Vector3)proj.Muzzle;
            if (System.Numerics.Vector3.DistanceSquared(chest, muzzle) > FireOutShatterMetres * FireOutShatterMetres * 4f) return;   // not a man holding this gun
            var list = t_muzzlePanes ??= new List<Fdp.Toolkit.Terrain.TerrainWorld.FireCrossing>();
            world.QueryFire(chest, muzzle, list, query.Doors);
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.Kind != Fdp.Toolkit.Terrain.TerrainWorld.PaneKind || c.Piece < 0) continue;
                var at = System.Numerics.Vector3.Lerp(chest, muzzle, c.T);
                if (System.Numerics.Vector3.Distance(at, muzzle) > FireOutShatterMetres) continue;
                var (panel, opening) = world.PaneOf(c.Piece - world.Prisms.Count);
                if (panel < 0 || world.Panels[panel].Building < 0) continue;
                cmd.PublishEvent(new Fdp.Toolkit.Terrain.WindowHitEvent
                {
                    Building = world.Panels[panel].Building, Panel = panel, Opening = opening, Shatter = 1, Source = proj.Shooter,
                });
            }
        }

        /// <summary>⭐ CE-5033 (D-4) — one <see cref="Fdp.Toolkit.Terrain.WindowHitEvent"/> per pane the segment passed: a hit with its crack, or a shatter when the muzzle is at the pane.</summary>
        private static void PublishPaneHits(Fdp.Interfaces.IEntityCommandBuffer cmd, Fdp.Toolkit.Terrain.TerrainWorld world, in BallisticProjectile proj, List<PaneHit> panes)
        {
            for (int i = 0; i < panes.Count; i++)
            {
                var (panel, opening) = world.PaneOf(panes[i].Piece - world.Prisms.Count);
                if (panel < 0 || world.Panels[panel].Building < 0) continue;
                var at = panes[i].At;
                var (u, v) = Fdp.Toolkit.Terrain.WindowDamages.PaneUv(world, panel, opening, at);
                bool fireOut = System.Numerics.Vector3.Distance((System.Numerics.Vector3)proj.Muzzle, at) <= FireOutShatterMetres;
                cmd.PublishEvent(new Fdp.Toolkit.Terrain.WindowHitEvent
                {
                    Building = world.Panels[panel].Building, Panel = panel, Opening = opening, U = u, V = v,
                    Shatter = fireOut ? (byte)1 : (byte)0, Source = proj.Shooter,
                });
            }
            panes.Clear();
        }

        private static void PublishImpacts(Fdp.Interfaces.IEntityCommandBuffer cmd, in BallisticProjectile proj, List<RoundImpact> impacts,
                                           System.Numerics.Vector3 end, bool groundStop)
        {
            if (groundStop) impacts.Add(new RoundImpact(end, System.Numerics.Vector3.UnitZ, ImpactKind.Stop, ImpactMaterial.Soil));   // ⭐ CE-11009 (RR-9) — the ground
            var travel = end - proj.PreviousPosition;
            bool warhead = (proj.Warhead & WarheadRound.Area) != 0;
            float reach = System.Numerics.Vector3.Dot(end - proj.PreviousPosition, travel);
            foreach (var i in impacts)
            {
                if (warhead && i.Kind == ImpactKind.Stop) continue;
                // an entry or a stop past where the round ended (it stopped at the ground first) never happened
                if (i.Kind != ImpactKind.Exit && System.Numerics.Vector3.Dot(i.At - proj.PreviousPosition, travel) > reach + 1e-4f) continue;
                cmd.PublishEvent(new Contracts.RoundImpactNotification
                {
                    Shooter = proj.Shooter, X = i.At.X, Y = i.At.Y, Z = i.At.Z,
                    NormalX = i.Normal.X, NormalY = i.Normal.Y, NormalZ = i.Normal.Z,
                    TravelX = travel.X, TravelY = travel.Y, TravelZ = travel.Z,
                    Ammo = proj.Ammo, Kind = (byte)i.Kind, Material = (byte)i.Material,   // ⭐ CE-11009 (RR-9)
                });
            }
            impacts.Clear();
        }

        /// <summary>
        /// ⭐ Stage 6 (<c>CE-1032</c>, W-3) — an off-target burst: a <see cref="DetonationNotification"/> with no target, carrying the
        /// munition (the area effect reads its warhead from it, next frame's Simulation) — and the end of the shot's record.
        /// </summary>
        internal static void Detonate(EntityRepository repo, in BallisticProjectile proj, System.Numerics.Vector3 at, ShotRecord? shot, uint tick,
                                      Entity round = default)
        {
            repo.Bus.Publish(new DetonationNotification
            {
                Shooter = proj.Shooter, Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = proj.Ammo,
                Round = round,   // ⭐ CE-3236 (B-14, VC-3) — the cloud's source and burn
                OriginalShooter = proj.OriginalShooter,   // ⭐ CE-4155 (SR-82)
                // ⭐ M1 — the way it was travelling (0 = unknown: a round lying where it landed has no last segment)
                TravelX = at.X - proj.PreviousPosition.X, TravelY = at.Y - proj.PreviousPosition.Y, TravelZ = at.Z - proj.PreviousPosition.Z,
            });
            if (shot != null) ShotLog.End(shot, ShotOutcome.Detonated, tick, at);
        }

        /// <summary>⭐ <c>CE-2203</c> — the delayed impact burst of a round waiting at <paramref name="burst"/> (its stop point is its
        /// <c>PreviousPosition</c>, the burst stands off it back along the flight): the travel is the flight's, toward the stop point.</summary>
        private static void BurstWhereStopped(EntityRepository repo, in BallisticProjectile proj, System.Numerics.Vector3 burst, ShotRecord? shot, uint tick,
                                              Entity round)
        {
            var forward = proj.PreviousPosition - burst;
            var asFlown = proj;
            asFlown.PreviousPosition = burst - forward;   // Detonate's travel = burst − PreviousPosition = the flight's direction
            Detonate(repo, in asFlown, burst, shot, tick, round);
        }

        /// <summary>
        /// ⭐ <c>CE-3236</c> (B-14, §7.15 VC-1) — a <see cref="WarheadRound.Vents"/> round that just burst on the ground LIES and vents for its
        /// burn: <see cref="WarheadRound.Landed"/> + <see cref="WarheadRound.Lingers"/> (its fuze is spent), counted down in
        /// <c>FuzeRemaining</c>; the <c>Landed</c> branch ends it without a burst. False for every other round (it goes).
        /// </summary>
        private static bool BeginVenting(ref BallisticProjectile proj)
        {
            if ((proj.Warhead & WarheadRound.Vents) == 0 || proj.BurnSeconds <= 0f) return false;
            proj.Warhead = (byte)((proj.Warhead | WarheadRound.Landed | WarheadRound.Lingers) & ~WarheadRound.TimeFuze);
            proj.FuzeRemaining = proj.BurnSeconds;
            return true;
        }

        /// <summary>
        /// ⭐ <c>CE-3064</c> — every unit within <see cref="CombatConstants.NearMissRadius"/> of the round's segment this tick, other
        /// than the shooter and the shooter's own force, gets ONE <see cref="Events.NearMissEvent"/> per pass (a round lingering
        /// near the same unit over two ticks reports it once). A round that HITS the unit also reports a near miss — harmless:
        /// both mean "fired upon".
        /// </summary>
        private static void ReportNearMisses(EntityRepository repo, Fdp.Interfaces.IEntityCommandBuffer cmd, global::CarKinem.Spatial.SpatialHashGrid grid,
                                             ref BallisticProjectile proj, System.Numerics.Vector3 start, System.Numerics.Vector3 end)
        {
            var a = new System.Numerics.Vector2(start.X, start.Y);
            var b = new System.Numerics.Vector2(end.X, end.Y);
            float half = System.Numerics.Vector2.Distance(a, b) * 0.5f;
            int shooterForce = repo.IsAlive(proj.Shooter) && repo.HasComponent<EntityInfo>(proj.Shooter)
                ? (int)repo.GetComponentRO<EntityInfo>(proj.Shooter).ForceId : -1;

            Span<(Entity entity, System.Numerics.Vector2 pos)> near = stackalloc (Entity, System.Numerics.Vector2)[64];
            int n = grid.QueryNeighbors((a + b) * 0.5f, half + CombatConstants.NearMissRadius, near);
            for (int i = 0; i < n; i++)
            {
                var unit = near[i].entity;
                if (unit == proj.Shooter || unit == proj.LastNearMiss || !repo.IsAlive(unit) || !repo.HasComponent<EntityInfo>(unit)) continue;
                if ((int)repo.GetComponentRO<EntityInfo>(unit).ForceId == shooterForce) continue;   // own side's rounds are not "shot at"
                var p = near[i].pos;
                var ab = b - a;
                if (ab.LengthSquared() <= 0f) continue;
                // ⭐⭐ CE-4652 — ONE near miss per man per pass: only the tick whose segment holds his CLOSEST approach (the unclamped projection in
                //   (0, 1]) reports him. 📐 Before: the projection was clamped to the segment, so a man just beyond this tick's end was reported now AND
                //   again next tick, and the one LastNearMiss slot flipped between men standing close together — a team got 2+ per round, the count
                //   moving with the frame rate (R-2076's "moves with the host's load"); regulars broke under over-head fire (ts-fire-discipline).
                float t = System.Numerics.Vector2.Dot(p - a, ab) / ab.LengthSquared();
                if (t <= 0f || t > 1f) continue;
                var closest = a + ab * t;
                if (System.Numerics.Vector2.DistanceSquared(p, closest) > CombatConstants.NearMissRadius * CombatConstants.NearMissRadius) continue;
                float z = start.Z + (end.Z - start.Z) * t;
                // ⭐ CE-4584 — the ONE near miss (event + under-fire stamp), shared with a shell's burst; the origin is the muzzle R-217
                //   keeps on the round (CE-3262, DA-14)
                HitModel.NearMiss(repo, cmd, unit, new System.Numerics.Vector3(closest.X, closest.Y, z),
                    proj.Muzzle != System.Numerics.Vector3.Zero ? (System.Numerics.Vector3)proj.Muzzle : (System.Numerics.Vector3?)null);
                proj.LastNearMiss = unit;
            }
        }
    }
}
