using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Async vision broadphase â€” runs inside <see cref="PerceptionModule"/> on the
    /// background thread via the Snapshot-on-Demand (SoD) pattern.
    /// <para>
    /// For each observer entity with a <see cref="PerceptionReceptor"/>:
    /// <list type="number">
    ///   <item>Queries the module-private <see cref="SpatialHashGrid"/> for candidates within VisionRange.</item>
    ///   <item>Skips candidates that are not alive, lack an <see cref="EntityInfo"/>, or share the observer's force affiliation.</item>
    ///   <item>Performs a dot-product FOV cone check using the precomputed <c>FieldOfViewCos</c> cosine.</item>
    ///   <item>Emits a <see cref="LosCheckRequestEvent"/> via the entity command buffer for candidates that pass.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Grid injection:</b> The grid is supplied via the constructor by <see cref="PerceptionModule"/>.
    /// <see cref="LocalGridBuilderSystem"/> populates the grid before this system executes each tick,
    /// so the broadphase never performs a brute-force world scan.
    /// </para>
    /// <para>
    /// <b>SoD rules (strictly enforced):</b>
    /// <list type="bullet">
    ///   <item>Only <c>view.GetComponentRO&lt;T&gt;</c> â€” no <c>GetComponentRW</c>.</item>
    ///   <item>All writes are queued via <c>view.GetCommandBuffer().PublishEvent</c>.</item>
    ///   <item>The snapshot is treated as immutable throughout execution.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Forward vector:</b> Derived from <see cref="SimTransform.Rotation"/> using
    /// <c>Vector3.Transform(Vector3.UnitX, tf.Rotation)</c>. <c>Vector3.UnitX</c> is the
    /// forward-east axis in FDP's coordinate system (X = east, Y = north, Z = up).
    /// Using <c>Vector3.UnitY</c> would point north regardless of yaw â€” a BATCH-01 regression.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Manual)]
    public class VisionBroadphaseSystem : IEcsModuleSystem
    {
        /// <summary>
        /// ⭐ CE-3032 — at most this many sight checks per observer per tick: the NEAREST that pass the filters.
        /// 🔴 It was 256, taken in grid-scan order. ⭐ 2 × <see cref="PerceptionConstants.MaxTrackedTargets"/>: a unit
        /// remembers 16 contacts, so checking 256 was mostly work no memory could hold; the margin covers candidates
        /// a wall hides. Measured (2026-10-04): the sight checks are the dominant per-tick cost (~2 µs each).
        /// </summary>
        public const int MaxCandidatesPerObserver = 2 * PerceptionConstants.MaxTrackedTargets;

        /// <summary>⭐ CE-3032 — the edge of the coarse cells an observer's vision query walks (metres).</summary>
        internal const float CoarseCellSize = 50f;

        // Value-copy of PerceptionModule._localGrid; shares the same native-memory pointers.
        // LocalGridBuilderSystem populates the grid before this system runs.
        private readonly SpatialHashGrid _grid;

        // ⭐ CE-3032 — a COARSE index over the fine grid's contents, rebuilt once per tick. 🔴 The fine perception grid
        //   has 5 m cells; a 500 m vision query walked ~40 000 of them per observer — measured (2026-10-04) 175 ms for
        //   500 observers, alone past the module's 100 ms limit. The fine grid is still the source of truth (same
        //   entity set, same positions, same footprint); this only changes how an observer FINDS its neighbours.
        private readonly Dictionary<long, List<(Entity Entity, Vector2 Pos, ForceId Force)>> _coarse = new();
        private readonly Stack<List<(Entity, Vector2, ForceId)>> _pool = new();
        private readonly List<(float DistSq, Entity Target)> _passed = new();

        /// <summary>
        /// Initialises the system with the module-private spatial grid.
        /// The grid struct is copied by value; native-memory arrays are shared.
        /// </summary>
        public VisionBroadphaseSystem(SpatialHashGrid grid)
        {
            _grid = grid;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var ecb = view.GetCommandBuffer();
            BuildCoarseIndex(view);

            // Query all observer entities (must have a receptor, entity info, and spatial presence).
            var observerQuery = view.Query()
                .With<PerceptionReceptor>()
                .With<EntityInfo>()
                .With<SimTransform>()
                .Build();

            foreach (var observer in observerQuery)
            {
                ref readonly var receptor  = ref view.GetComponentRO<PerceptionReceptor>(observer);
                ref readonly var obsInfo   = ref view.GetComponentRO<EntityInfo>(observer);
                ref readonly var obsTf     = ref view.GetComponentRO<SimTransform>(observer);

                var obsPos2D = new Vector2(obsTf.Position.X, obsTf.Position.Y);

                // Derive 2-D forward from quaternion — X-forward (east) convention.
                // Using Vector3.UnitX (not UnitY) to match the FDP yaw convention:
                //   yaw=0 → facing east (+X), yaw=90° → facing north (+Y).
                Vector3 fwd3D   = Vector3.Transform(Vector3.UnitX, obsTf.Rotation);
                Vector2 forward = Vector2.Normalize(new Vector2(fwd3D.X, fwd3D.Y));

                float range   = receptor.VisionRange;
                float rangeSq = range * range;
                _passed.Clear();

                int x0 = Cell(obsPos2D.X - range), x1 = Cell(obsPos2D.X + range);
                int y0 = Cell(obsPos2D.Y - range), y1 = Cell(obsPos2D.Y + range);
                for (int cy = y0; cy <= y1; cy++)
                {
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        if (!_coarse.TryGetValue(Key(cx, cy), out var cell)) continue;
                        foreach (var (target, targetPos2D, targetForce) in cell)
                        {
                            if (target.Index == observer.Index) continue; // skip self

                            // Same-force exclusion — allies are invisible to the broadphase. (Liveness and the
                            // EntityInfo requirement were applied ONCE per entity when the index was built.)
                            if (targetForce == obsInfo.ForceId) continue;

                            // Distance — the same inclusive test the fine grid's QueryNeighbors applied.
                            float distSq = Vector2.DistanceSquared(obsPos2D, targetPos2D);
                            if (distSq > rangeSq) continue;

                            Vector2 toTarget = targetPos2D - obsPos2D;
                            float dist = MathF.Sqrt(distSq);
                            if (dist < float.Epsilon) continue; // degenerate case

                            // FOV cone check: dot(forward, dir_to_target) >= cos(half_FOV).
                            float dot = Vector2.Dot(forward, toTarget / dist);
                            if (dot < receptor.FieldOfViewCos) continue; // outside FOV cone

                            _passed.Add((distSq, target));
                        }
                    }
                }

                // ⭐ CE-3032 — over the cap, the NEAREST candidates win (ties by entity index — deterministic).
                //   🔴 It used to keep whichever 256 the cell scan reached first, FRIENDLIES INCLUDED, so in a dense
                //   scene a near enemy could be dropped for a far one.
                if (_passed.Count > MaxCandidatesPerObserver)
                {
                    _passed.Sort(static (a, b) => a.DistSq != b.DistSq ? a.DistSq.CompareTo(b.DistSq) : a.Target.Index.CompareTo(b.Target.Index));
                    _passed.RemoveRange(MaxCandidatesPerObserver, _passed.Count - MaxCandidatesPerObserver);
                }

                // Passed all broadphase filters → queue a line-of-sight check.
                foreach (var (_, target) in _passed)
                {
                    ecb.PublishEvent(new LosCheckRequestEvent
                    {
                        Observer = observer,
                        Target   = target,
                    });
                }
            }
        }

        /// <summary>Walks the fine grid's cells once and buckets every entry that can be SEEN — alive, with an
        /// <see cref="EntityInfo"/> — by coarse cell, with its force read once (not once per observer).</summary>
        private void BuildCoarseIndex(ISimulationView view)
        {
            foreach (var list in _coarse.Values) { list.Clear(); _pool.Push(list); }
            _coarse.Clear();
            if (!_grid.GridHead.IsCreated) return;

            int cells = _grid.Width * _grid.Height;
            for (int c = 0; c < cells; c++)
            {
                for (int head = _grid.GridHead[c]; head >= 0; head = _grid.GridNext[head])
                {
                    var entity = _grid.GridValues[head];
                    // Generational liveness check — grid stores full Entity handles.
                    if (!view.IsAlive(entity)) continue;
                    // Target must have an EntityInfo to participate in vision checks.
                    if (!view.HasComponent<EntityInfo>(entity)) continue;
                    var force = view.GetComponentRO<EntityInfo>(entity).ForceId;

                    var pos = _grid.Positions[head];
                    long key = Key(Cell(pos.X), Cell(pos.Y));
                    if (!_coarse.TryGetValue(key, out var list))
                    {
                        list = _pool.Count > 0 ? _pool.Pop() : new List<(Entity, Vector2, ForceId)>(8);
                        _coarse[key] = list;
                    }
                    list.Add((entity, pos, force));
                }
            }
        }

        private static int Cell(float v) => (int)MathF.Floor(v / CoarseCellSize);
        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
    }
}
