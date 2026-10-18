using System;
using System.Threading;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;

namespace Fdp.Toolkit.Physics.Systems
{
    /// <summary>
    /// Background-capable system that resolves all pending <see cref="RaycastRequestEvent"/>s.
    ///
    /// <para><b>Default mode (spatial-hash):</b> broad-phase spatial-hash query followed by
    /// per-entity <see cref="Intersection2D.RaycastCircle"/> narrow-phase checks.
    /// Reads <see cref="RaycastRequestEvent"/>s, resolves each cast, publishes one
    /// <see cref="RaycastResultEvent"/> per request via
    /// <see cref="IEntityCommandBuffer.PublishEvent{T}"/>.
    /// <see cref="RaycastResultMaterializationSystem"/> (main thread) writes the results into
    /// the <see cref="RaycastBatchData"/> ring buffer so BTree consumers can poll them.</para>
    ///
    /// <para><b>Backend override (STR-P3-T3):</b> When <see cref="RaycastBackend"/> is set
    /// (e.g. to the Stride/Bullet adapter <c>StrideRaycastBackend</c> in
    /// <c>Hrot.Stride.Core</c>), every ray is resolved via that backend instead.
    /// The spatial-hash path is bypassed entirely.  This allows the Stride node to use real
    /// 3-D scene-geometry raycasts (walls, ramps, obstacles) without changing the event
    /// plumbing.  <c>Fdp.Toolkits</c> never references <c>Hrot.Stride.Core</c>; the
    /// dependency goes the other way via the <see cref="IRaycastBackend"/> seam.</para>
    ///
    /// <para><b>Parallelism:</b> The narrow-phase loop uses <see cref="Parallel.For"/> in
    /// spatial-hash mode.  Backend mode runs serially (backends are single-threaded by the
    /// host-thread invariant, design §8.3).</para>
    ///
    /// <para><b>Thread safety of <c>World.GetComponent&lt;T&gt;</c> inside Parallel.For:</b>
    /// <see cref="EntityRepository"/> states that component access is not formally thread-safe.
    /// In practice the reads here are safe because:
    /// (a) No other system writes to <c>SimTransform</c> or <c>PhysicsCollider</c> concurrently.
    /// (b) <c>Dictionary&lt;Type, IComponentTable&gt;</c> (.NET) allows concurrent reads when no
    ///     write is in progress.
    /// See BATCH-08 report Q2 for the full analysis.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class RaycastSolverSystem : IEcsModuleSystem
    {
        /// <summary>
        /// Optional 3-D raycast backend (STR-P3-T3).
        ///
        /// <para>
        /// When non-null, every <see cref="RaycastRequestEvent"/> is resolved via this
        /// backend instead of the default flat spatial-hash + circle-sweep path.
        /// Set by the Stride node after the <c>PhysicsProcessor</c> is running.
        /// Leave null on non-Stride nodes (SimHost, headless tests) — they continue to
        /// use the spatial-hash approximation.
        /// </para>
        /// </summary>
        public IRaycastBackend? RaycastBackend { get; set; }

        /// <summary>
        /// ⭐⭐ <c>CE-3209</c> (docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md HL-2) — a ROUND meets a PERSON by his body
        /// parts (tier ② the pose's bounding sphere, tier ③ the oriented boxes), not by his circle and stance band. On by default; off only
        /// to measure the old path (the crowd benchmark) — ⛔ never a production switch.
        /// </summary>
        public bool PersonParts { get; set; } = true;

        /// <summary>⭐ <c>CE-3209</c> (HL-2 tier ①) — the cell walk along the segment. On by default; off only to measure the old midpoint disc
        /// (½ length + 5 m, 64 candidates) in the crowd benchmark — ⛔ never a production switch.</summary>
        public bool CellWalk { get; set; } = true;

        /// <summary>⭐⭐ <c>CE-3211</c> (hit location §9) — a ROUND meets a ground VEHICLE by its parts (its kit's boxes, the turret and gun
        /// posed by <c>TurretPose</c>), not by its circle and height band. On by default; off only to measure the old path — ⛔ never a
        /// production switch.</summary>
        public bool VehicleParts { get; set; } = true;

        /// <summary>⭐ <c>CE-3209</c> — rounds whose tier-② sphere test rejected a person (so no part was tested). Diagnostics and rails.</summary>
        public long SphereRejects => Interlocked.Read(ref _sphereRejects);

        /// <summary>⭐ <c>CE-3209</c> — persons whose parts a round was tested against (tier ③).</summary>
        public long PartTests => Interlocked.Read(ref _partTests);

        /// <summary>⭐ <c>CE-3209</c> — casts whose broad phase found more candidates than its buffer holds (a crowd of hundreds along one
        /// segment). The cap used to be 64 and silent; it is now <see cref="MaxCandidates"/> and counted.</summary>
        public long CandidateOverflows => Interlocked.Read(ref _candidateOverflows);

        /// <summary>The broad phase's candidate buffer per cast (tier ①).</summary>
        public const int MaxCandidates = 512;

        private long _sphereRejects, _partTests, _candidateOverflows;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(RaycastSolverSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var requests = view.ReadEvents<RaycastRequestEvent>();
            if (requests.IsEmpty) return;

            // ── Backend override path (STR-P3-T3) ─────────────────────────────────
            // When a 3-D raycast backend is registered, delegate every request to it
            // and bypass the spatial-hash path entirely.
            if (RaycastBackend != null)
            {
                var backendCmd = view.GetCommandBuffer();
                for (int i = 0; i < requests.Length; i++)
                {
                    ref readonly var req = ref requests[i];
                    var hit = RaycastBackend.Raycast(
                        start:          req.Start,
                        end:            req.End,
                        rayId:          req.RayId,
                        layerMask:      req.LayerMask,
                        ignoreEntity:   req.IgnoreEntity,
                        observerEntity: req.Observer,
                        targetEntity:   req.Target);
                    // Echo the request fields that HitResolutionSystem needs.
                    hit.Start        = req.Start;
                    hit.End          = req.End;
                    hit.IgnoreEntity = req.IgnoreEntity;
                    hit.Observer     = req.Observer;
                    hit.Target       = req.Target;
                    hit.SourceNodeId = req.SourceNodeId;
                    backendCmd.PublishEvent(new RaycastResultEvent { Hit = hit });
                }
                return;
            }

            if (!repo.HasSingleton<SpatialGridData>()) return;
            // Value-copy of the SpatialGridData struct — carries native pointers to grid arrays.
            var gridData = repo.GetSingleton<SpatialGridData>();
            var grid     = gridData.Grid;

            int count = requests.Length;

            // Snapshot events to a plain array so the lambda can safely capture it
            // (ReadEvents returns a ref-backed buffer that cannot be captured in closures).
            var requestsSnapshot = new RaycastRequestEvent[count];
            for (int k = 0; k < count; k++)
                requestsSnapshot[k] = requests[k];

            // Resolve all casts in parallel into a local hit-result array, then publish serially.
            var results = new RaycastHit[count];

            // ⭐ CE-3211 — each vehicle's main turret, found ONCE per tick (the parallel loop only reads the map)
            var turretOf = VehicleParts ? TurretsOf(repo) : null;
            // ⭐ CE-3215 — every type's shape built once per database, so the pad below already covers the largest body (a helicopter's tail)
            if (VehicleParts && repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>())
                Fdp.Toolkit.Physics.BodyParts.VehicleBody.Prime(repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>());

            Parallel.For(0, count, i =>
            {
                var req = requestsSnapshot[i];

                var start2D = new Vector2(req.Start.X, req.Start.Y);
                var end2D   = new Vector2(req.End.X,   req.End.Y);

                // ⭐⭐ CE-3209 (HL-2, tier ①) — the cells ALONG the segment, padded by the largest body radius in the grid (a person's
                //   body reaches PersonBody.MaxBoundRadius from its point). ⛔ Was a disc of ½ length + 5 m round the midpoint, capped at
                //   64 silently: a 70 m segment visited ~16 × 16 cells, and in a crowd the man on the line could be dropped.
                float pad = gridData.MaxRadius > 0f
                    ? MathF.Max(MathF.Max(gridData.MaxRadius, Fdp.Toolkit.Physics.BodyParts.PersonBody.MaxBoundRadius + Fdp.Toolkit.Combat.Leans.Shift),   // ⭐ CE-3257 — a leaning man
                                Fdp.Toolkit.Physics.BodyParts.VehicleBody.MaxBoundRadius)   // ⭐ CE-3211 (S2-9) — a turret swung past the circle
                    : PhysicsConstants.QueryExpansionRadius;   // a grid built by hand (no radius measured): the old pad

                unsafe
                {
                Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[MaxCandidates];
                Span<Fdp.Toolkit.Physics.BodyParts.BodyBox> vehicleBoxes = stackalloc Fdp.Toolkit.Physics.BodyParts.BodyBox[Fdp.Toolkit.Physics.BodyParts.VehicleBody.MaxBoxes];   // ⭐ CE-3211
                bool overflow = false;
                int nc = CellWalk
                    ? grid.QuerySegment(start2D, end2D, pad, candidates, out overflow)
                    : grid.QueryNeighbors((start2D + end2D) * 0.5f, Vector2.Distance(start2D, end2D) * 0.5f + PhysicsConstants.QueryExpansionRadius,
                                          candidates[..PhysicsConstants.MaxBroadphaseCandidates]);   // the old disc, measured only
                if (overflow) Interlocked.Increment(ref _candidateOverflows);

                float  bestT   = float.MaxValue;
                Entity bestEnt = default;
                bool   anyHit  = false;
                bool   bestHasPart = false;
                Fdp.Toolkit.Physics.BodyParts.BodyPartHitResult bestPart = default;

                // ── Narrow phase ──────────────────────────────────────────────────────────────
                for (int j = 0; j < nc; j++)
                {
                    Entity candidate = candidates[j].entity;

                    if (!repo.IsAlive(candidate)) continue;

                    if (!req.IgnoreEntity.IsNull && candidate == req.IgnoreEntity) continue;

                    if (!repo.HasComponent<PhysicsCollider>(candidate)) continue;

                    // ⭐ CE-3136 P-7a (R-243) — a static obstacle is TERRAIN: a round crosses it by the wall rule (TerrainPenetration,
                    //   its material × the chord), never stopped dead by its movement collider.
                    if (PhysicsConstants.IsBulletRay(req.RayId) && Fdp.Toolkit.Terrain.TerrainObstacles.IsObstacle(repo, candidate)) continue;
                    // ⭐ CE-2182 (AT1) — a WEAPON SYSTEM is not met by a round: it stands at its host's seat or on its carrier (Weapon
                    //   Systems §6) with a 2.5 m fallback collider and no height, so meeting it would shadow the pickup or the man who
                    //   holds it. Fragments and blast break it (CE-3203). This is today's behaviour, kept: before AT1 the round's mask
                    //   skipped the combat layer it sits on alone.
                    //   ⭐⭐ CE-3214 (hit location §11 S2b-1) — EXCEPT a weapon NOT embarked (placed, deployed, lying stowed) whose type has a body:
                    //   it is met by its parts below, like a vehicle. Embarked (on a back, in a station) it keeps the skip.
                    if (PhysicsConstants.IsBulletRay(req.RayId) && Fdp.Toolkit.Combat.WeaponSystems.IsWeaponSystem(repo, candidate)
                        && !(VehicleParts && !IsEmbarked(repo, candidate) && Fdp.Toolkit.Physics.BodyParts.VehicleBody.Of(repo, candidate) != null)) continue;

                    var collider = repo.GetComponent<PhysicsCollider>(candidate);

                    if ((req.LayerMask & collider.CollisionLayer) == 0) continue;

                    var tf  = repo.GetComponent<SimTransform>(candidate);
                    var c2D = new Vector2(tf.Position.X, tf.Position.Y);

                    // ⭐⭐ CE-3209 (HL-2 tiers ②–③, HL-5) — a ROUND meets a PERSON by his parts: the pose's bounding sphere first (most
                    //   candidates stop there), then the oriented boxes the figure draws (PersonBody, generated from it). The first box
                    //   entered wins; the hit carries the part, the face entered and the point in the part.
                    if (PersonParts && PhysicsConstants.IsBulletRay(req.RayId) && PhysicsColliderReaders.IsPerson(repo, candidate))
                    {
                        // ⭐ CE-1126 — the one body test the FPS crosshair also makes (BodyPartRay)
                        var test = Fdp.Toolkit.Physics.BodyParts.BodyPartRay.Person(repo, candidate, tf, collider.Height, req.Start, req.End, out var part);
                        if (test == Fdp.Toolkit.Physics.BodyParts.BodyPartTest.SphereMiss) { Interlocked.Increment(ref _sphereRejects); continue; }
                        Interlocked.Increment(ref _partTests);
                        if (test != Fdp.Toolkit.Physics.BodyParts.BodyPartTest.Hit) continue;
                        if (part.T < bestT) { bestT = part.T; bestEnt = candidate; anyHit = true; bestHasPart = true; bestPart = part; }
                        continue;
                    }

                    // ⭐⭐ CE-3211 (§9) — a ROUND meets a ground VEHICLE by its parts: the bound sphere (every turret pose) first, then the
                    //   kit's boxes the 3-D map draws, the turret and gun posed by the replicated TurretPose. The main hull box's engine third
                    //   is the Engine (S2-4); a turret's face is judged in its own frame (D-G lifted).
                    if (VehicleParts && PhysicsConstants.IsBulletRay(req.RayId)
                        && Fdp.Toolkit.Physics.BodyParts.VehicleBody.Of(repo, candidate) is { } vehicle)
                    {
                        Entity turretPart = default;
                        if (turretOf != null) turretOf.TryGetValue(candidate, out turretPart);
                        // ⭐ CE-1126 — the one body test the FPS crosshair also makes (BodyPartRay)
                        var test = Fdp.Toolkit.Physics.BodyParts.BodyPartRay.Vehicle(repo, candidate, vehicle, tf, turretPart, req.Start, req.End, vehicleBoxes, out var vpart);
                        if (test == Fdp.Toolkit.Physics.BodyParts.BodyPartTest.SphereMiss) { Interlocked.Increment(ref _sphereRejects); continue; }
                        Interlocked.Increment(ref _partTests);
                        if (test != Fdp.Toolkit.Physics.BodyParts.BodyPartTest.Hit) continue;
                        if (vpart.T < bestT) { bestT = vpart.T; bestEnt = candidate; anyHit = true; bestHasPart = true; bestPart = vpart; }
                        continue;
                    }

                    // ⭐ M1 (DESIGN_Armour_Hit_Model §2) — a ROUND hits a VEHICLE only inside its height band (feet … feet + collider
                    //   Height): a round passing over a hull misses it, and one dropping onto it meets the roof — the first point of the
                    //   segment inside the circle AND the band (a cylinder; RaycastCircle cannot answer a segment wholly inside). A
                    //   collider of Height 0 (unknown) keeps the circle.
                    if (PhysicsConstants.IsBulletRay(req.RayId) && collider.Height > 0f && !PhysicsColliderReaders.IsPerson(repo, candidate))
                    {
                        if (!Intersection2D.SegmentInCircle(start2D, end2D, c2D, collider.Radius, out float tIn, out float tOut)) continue;
                        if (!ZBandOverlap(req.Start.Z, req.End.Z, tf.Position.Z, tf.Position.Z + collider.Height, ref tIn, ref tOut)) continue;
                        if (tIn < bestT) { bestT = tIn; bestEnt = candidate; anyHit = true; bestHasPart = false; }
                        continue;
                    }

                    if (Intersection2D.RaycastCircle(start2D, end2D, c2D, collider.Radius, out float t))
                    {
                        // ⭐ CE-3136 P-2 (peek-and-fire D2) — a ROUND hits a person only inside the body band of its logical stance
                        //   (feet … highest body point): a round through a window passes over a man lying prone below the sill.
                        //   Vehicles have their own band (M1, above); sight and query rays are unchanged.
                        if (PhysicsConstants.IsBulletRay(req.RayId) && PhysicsColliderReaders.IsPerson(repo, candidate))
                        {
                            float z = req.Start.Z + t * (req.End.Z - req.Start.Z);
                            float feet = tf.Position.Z;
                            var stance = repo.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>()
                                ? Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(repo, candidate)
                                : Fdp.Toolkit.Tkb.Domain.StanceId.Standing;   // a world with no stance runtime: standing
                            float top = feet + Fdp.Toolkit.Perception.LineOfSight.BodyProfile.PersonTop(repo, candidate, stance);
                            if (z < feet || z > top) continue;
                        }
                        if (t < bestT)
                        {
                            bestT   = t;
                            bestEnt = candidate;
                            anyHit  = true;
                            bestHasPart = false;
                        }
                    }
                }

                results[i] = new RaycastHit
                {
                    T            = bestT,
                    HitEntity    = bestEnt,
                    RayId        = req.RayId,
                    Observer     = req.Observer,
                    Target       = req.Target,
                    HasHit       = (byte)(anyHit ? 1 : 0),
                    SourceNodeId = req.SourceNodeId,
                    Start        = req.Start,
                    End          = req.End,
                    IgnoreEntity = req.IgnoreEntity,
                    HasBodyPart  = (byte)(anyHit && bestHasPart ? 1 : 0),   // ⭐ CE-3209
                    BodyPart     = bestHasPart ? bestPart.Part : (byte)255,
                    Face         = bestHasPart ? (byte)bestPart.Face : (byte)0,
                    LocalPoint   = bestHasPart ? bestPart.LocalPoint : Vector3.Zero,
                };
                } // end unsafe
            });

            // Publish results serially via the command buffer (keeps cmd-buffer access on one thread).
            var cmd = view.GetCommandBuffer();
            for (int i = 0; i < count; i++)
                cmd.PublishEvent(new RaycastResultEvent { Hit = results[i] });
        }

        /// <summary>⭐ M1 — narrows [<paramref name="tIn"/>, <paramref name="tOut"/>] of a segment whose height runs
        /// <paramref name="z0"/>→<paramref name="z1"/> to where it lies in [<paramref name="lo"/>, <paramref name="hi"/>]; false when it
        /// never does.</summary>
        /// <summary>⭐ <c>CE-3214</c> (S2b-1) — is <paramref name="e"/> aboard something (carried, in a station, manning)?</summary>
        private static bool IsEmbarked(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
               && repo.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(e);

        /// <summary>⭐ <c>CE-3211</c> — parent → its main turret part (<c>TurretParts</c> id 2000), for this tick; null when no turrets exist.</summary>
        private static Dictionary<Entity, Entity>? TurretsOf(EntityRepository repo)
        {
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.TurretPose>()
                || !repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) return null;
            Dictionary<Entity, Entity>? map = null;
            foreach (var e in repo.Query().With<Fdp.Toolkit.Replication.Components.PartMetadata>().With<Fdp.Toolkit.Combat.Components.TurretPose>().Build())
            {
                ref readonly var meta = ref repo.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(e);
                if (meta.InstanceId == Fdp.Toolkit.Combat.TurretParts.FirstPartId) (map ??= new())[meta.ParentEntity] = e;
            }
            return map;
        }

        public static bool ZBandOverlap(float z0, float z1, float lo, float hi, ref float tIn, ref float tOut)
        {
            float dz = z1 - z0;
            if (MathF.Abs(dz) < 1e-9f) return z0 >= lo && z0 <= hi;
            float ta = (lo - z0) / dz, tb = (hi - z0) / dz;
            if (ta > tb) (ta, tb) = (tb, ta);
            tIn = MathF.Max(tIn, ta); tOut = MathF.Min(tOut, tb);
            return tIn <= tOut;
        }
    }
}
