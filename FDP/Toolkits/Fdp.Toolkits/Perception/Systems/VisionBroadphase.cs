using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// ⭐⭐ <b>What an observer can look at</b> — the vision broadphase the visual EQS sensor
    /// (<c>VisualSensorGenerator</c>, <c>CE-3038</c>) calls. ⭐ <c>CE-3052</c>: its other caller, the toolkit's own
    /// <c>VisionBroadphaseSystem</c> chain, is retired; the rule — other forces, range, the FOV cone with forward =
    /// <c>Vector3.Transform(Vector3.UnitX, rotation)</c> (X east, Y north) — is unchanged. 📄 docs/DESIGN_Sensors_And_Doctrine.md §5.5.
    /// <para>Per tick: <see cref="Rebuild"/> buckets every live entity with an <see cref="EntityInfo"/> into 50 m cells from
    /// the fine perception grid. Per observer: <see cref="Select"/> keeps the other forces within range and the FOV cone,
    /// the NEAREST <see cref="MaxCandidatesPerObserver"/> first (ties by entity index — deterministic).</para>
    /// </summary>
    public sealed class VisionBroadphase
    {
        /// <summary>
        /// ⭐ CE-3032 — at most this many sight checks per observer per tick: the NEAREST that pass the filters.
        /// 🔴 It was 256, taken in grid-scan order. ⭐ 2 × <see cref="PerceptionConstants.MaxTrackedTargets"/>: a unit
        /// remembers 16 contacts, so checking 256 was mostly work no memory could hold; the margin covers candidates
        /// a wall hides. Measured (2026-10-04): the sight checks are the dominant per-tick cost (~2 µs each).
        /// </summary>
        public const int MaxCandidatesPerObserver = 2 * PerceptionConstants.MaxTrackedTargets;

        /// <summary>⭐ CE-3032 — the edge of the coarse cells an observer's vision query walks (metres).</summary>
        public const float CoarseCellSize = 50f;

        // ⭐ CE-3032 — a COARSE index over the fine grid's contents, rebuilt once per tick. 🔴 The fine perception grid
        //   has 5 m cells; a 500 m vision query walked ~40 000 of them per observer — measured (2026-10-04) 175 ms for
        //   500 observers, alone past the module's 100 ms limit. The fine grid is still the source of truth (same
        //   entity set, same positions, same footprint); this only changes how an observer FINDS its neighbours.
        private readonly Dictionary<long, List<(Entity Entity, Vector2 Pos, ForceId Force)>> _coarse = new();
        private readonly Stack<List<(Entity, Vector2, ForceId)>> _pool = new();
        private readonly List<(float DistSq, Entity Target)> _passed = new();

        /// <summary>⭐ CE-3146 — how many <see cref="Rebuild"/>s have seen a malformed chain; rate-limits the warning.</summary>
        private int _malformedRebuilds;

        /// <summary>
        /// The candidates <paramref name="observer"/> can look at (it needs <see cref="EntityInfo"/> and
        /// <see cref="SimTransform"/>; otherwise none), nearest first when over the cap, else in cell-scan order.
        /// The list is reused by the next call.
        /// </summary>
        public IReadOnlyList<(float DistSq, Entity Target)> Select(ISimulationView view, Entity observer, float range, float fovCos)
        {
            _passed.Clear();
            if (!view.HasComponent<EntityInfo>(observer) || !view.HasComponent<SimTransform>(observer)) return _passed;
            ref readonly var obsInfo = ref view.GetComponentRO<EntityInfo>(observer);
            ref readonly var obsTf   = ref view.GetComponentRO<SimTransform>(observer);

            var obsPos2D = new Vector2(obsTf.Position.X, obsTf.Position.Y);

            // Derive 2-D forward from quaternion — X-forward (east) convention.
            // Using Vector3.UnitX (not UnitY) to match the FDP yaw convention:
            //   yaw=0 → facing east (+X), yaw=90° → facing north (+Y).
            Vector3 fwd3D   = Vector3.Transform(Vector3.UnitX, obsTf.Rotation);
            Vector2 forward = Vector2.Normalize(new Vector2(fwd3D.X, fwd3D.Y));

            float rangeSq = range * range;

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
                        if (dot < fovCos) continue; // outside FOV cone

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
            return _passed;
        }

        /// <summary>Walks the fine grid's cells once and buckets every entry that can be SEEN — alive, with an
        /// <see cref="EntityInfo"/> — by coarse cell, with its force read once (not once per observer).</summary>
        public void Rebuild(ISimulationView view, SpatialHashGrid grid)
        {
            foreach (var list in _coarse.Values) { list.Clear(); _pool.Push(list); }
            _coarse.Clear();
            if (!grid.GridHead.IsCreated) return;

            int cells = grid.Width * grid.Height;

            // ⭐⭐⭐ CE-3146 — THE CHAIN GUARD. 🔴 This walk follows the perception grid's INTRUSIVE linked list and
            //   terminates ONLY when that list does: a cycle (`GridNext[i] == i`, or any loop) spins here forever, and
            //   a duplicate explosion makes it effectively forever. 📌 Reported by the user `2026-10-09` as "the editor
            //   easily gets stuck, looping inside Rebuild"; corroborated the same evening by this module's own breaker
            //   firing on a `--mode all` run (`Module 'Eqs' timed out after 400ms` → `CIRCUIT-OPEN`). ⚠ The timeout
            //   ABANDONS the task ("may continue running in background as zombie"), so the thread keeps spinning —
            //   which is why it presents as "stuck" rather than as a clean 400 ms miss.
            //
            //   ⭐ The bound is EXACT, not a heuristic: every live entry occupies one slot, so a well-formed chain can
            //   never be longer than the slot capacity. Exceeding it is PROOF the list is malformed — never a false
            //   positive on a merely dense cell.
            //   ⭐ On breach: abandon THAT cell and keep going. Perception degrades for one cell instead of hanging the
            //   host, and the log names the cell so the producer can be found. ⛔ Do not throw — this runs on a
            //   background module thread whose exceptions the module host SWALLOWS.
            int slotCapacity = grid.GridValues.Length;
            bool reportedMalformedChain = false;   // ⭐ once per Rebuild: a warning that floods is a warning nobody reads

            // ⭐⭐⭐ THE BOUND IS GLOBAL, NOT PER-CELL — 📌 measured `2026-10-09`, and the per-cell version was MY OWN
            //   defect: with a 50 000-slot capacity and 40 000 cells, a per-cell bound still permits 2·10⁹ steps per
            //   Rebuild, which is a hang with extra steps. ⭐ Every live entry is visited exactly once across the WHOLE
            //   walk, so the TOTAL can never exceed the slot capacity either. That makes the whole Rebuild O(cells +
            //   capacity) even on a fully corrupt grid.
            int walkedTotal = 0;

            // ⭐ the outer condition is what makes the GLOBAL bound actually stop the walk, not just this cell's.
            for (int c = 0; c < cells && walkedTotal <= slotCapacity; c++)
            {
                int steps = 0;
                for (int head = grid.GridHead[c]; head >= 0; head = grid.GridNext[head])
                {
                    steps++;
                    walkedTotal++;
                    if (steps > slotCapacity || walkedTotal > slotCapacity)
                    {
                        // ⚠ RATE-LIMITED ACROSS REBUILDS, not just within one. 📌 User, `2026-10-09`: "prints lots of
                        //   messages" — `reportedMalformedChain` alone is per-Rebuild and this runs at 10 Hz, so a
                        //   standing corruption printed ~10 lines/s and buried the log it exists to serve.
                        bool first = _malformedRebuilds == 0;
                        if (!reportedMalformedChain)
                        {
                            reportedMalformedChain = true;
                            _malformedRebuilds++;
                            if (first || _malformedRebuilds % 600 == 0)   // ⭐ the first, then ~once a minute at 10 Hz
                            {
                                // ⚠ FdpLog.Warn takes at most 4 format args — pre-format instead of splitting it.
                                int next = grid.GridNext[head];
                                Fdp.Core.Logging.FdpLog<VisionBroadphase>.Warn(
                                    $"[VisionBroadphase] CE-3146 — MALFORMED grid chain in cell {c}: walked {steps} "
                                    + $"slots, capacity is {slotCapacity}. head={head}, GridNext[head]={next}"
                                    + (next == head ? " (SELF-CYCLE)" : string.Empty)
                                    + $". Abandoning this cell; perception under-reports here. [rebuild #{_malformedRebuilds} "
                                    + "with a malformed grid] ⭐ CE-3152 found ONE producer — SpatialHashGrid.Create did "
                                    + "not initialise GridHead to -1, so a grid was malformed until its first Clear(); "
                                    + "if this still fires AFTER that fix, the producer is a different one.");
                            }
                        }
                        break;
                    }

                    var entity = grid.GridValues[head];
                    // Generational liveness check — grid stores full Entity handles.
                    if (!view.IsAlive(entity)) continue;
                    // Target must have an EntityInfo to participate in vision checks.
                    if (!view.HasComponent<EntityInfo>(entity)) continue;
                    var force = view.GetComponentRO<EntityInfo>(entity).ForceId;

                    var pos = grid.Positions[head];
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
