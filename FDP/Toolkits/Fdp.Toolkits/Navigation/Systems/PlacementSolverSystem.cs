using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Navigation.Systems
{
    /// <summary>
    /// ⭐ Macros S3a ⑤ (docs/DESIGN_Scenario_Macros.md §14.2 SR-181 / SR-182, §14.4 SR-190) — answers every <see cref="PlacementRequestBatch"/> of the
    /// frame with ONE <see cref="PlacementResponseBatch"/>. Per slot, in order:
    /// <list type="number">
    ///   <item><see cref="PlacementLayer.Air"/> ⇒ Exact at its point.</item>
    ///   <item>a navmesh resident ⇒ project onto it with the layer's mask; within MaxShift (XY) ⇒ Exact (&lt; 0.25 m) / Shifted, else Failed.</item>
    ///   <item>no navmesh ⇒ terrain only: not <see cref="TerrainSurfaceType.Water"/> (unless amphibious), else a ring search (2 m steps, 16 bearings)
    ///     up to MaxShift. No terrain at all ⇒ Exact, Reason "no terrain".</item>
    ///   <item>spacing (SR-190): never within (its Radius + the other's) of a slot already placed in the batch nor of an existing entity — a taken spot
    ///     continues the ring search under the same MaxShift budget.</item>
    /// </list>
    /// Registered by <see cref="Modules.NavigationSolverModule"/> as an ordinary main-thread system, ⭐ EVERY FRAME: a scenario loads while
    /// the cluster is paused, and the load waits on the answer (§15.2). On the live world it also reads the resident <see cref="TerrainWorld"/>.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class PlacementSolverSystem : IEcsModuleSystem
    {
        /// <summary>A projected point within this of the requested one counts as the requested one.</summary>
        public const float ExactToleranceMetres = 0.25f;
        /// <summary>The ring search's radial step.</summary>
        public const float RingStepMetres = 2f;
        /// <summary>Bearings per ring.</summary>
        public const int RingBearings = 16;

        private readonly INavmeshProvider? _navmesh;

        /// <summary>⭐ Macros debt A2 (SR-190) — frames a slot this solver handed out stays TAKEN for later requests: the children it placed
        /// are created a few ticks after the answer, so until then the world does not show them and a second expansion could land on them.</summary>
        public const int ReservedFrames = 600;
        private readonly List<(Vector3 Position, float Radius, long Frame, int Self)> _reserved = new();
        private long _frame;

        public PlacementSolverSystem(INavmeshProvider? navmesh = null) => _navmesh = navmesh;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
            var requests = view.ReadManagedEvents<PlacementRequestBatch>();
            if (requests.Count == 0) return;

            // ⚠ the managed-event publish is on the concrete buffer (IEntityCommandBuffer has PublishEvent<unmanaged> only) — EqsSolverSystem's pattern.
            if (view.GetCommandBuffer() is not EntityCommandBuffer cmd) return;
            var terrain = view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            _frame++;
            _reserved.RemoveAll(r => _frame - r.Frame > ReservedFrames);
            // once per tick: every batch is placed against the same world PLUS every slot already handed out (this tick's earlier batches
            // included) — ⭐ A2: two instances dropped on one spot do not overlap
            foreach (var req in requests)
                cmd.PublishManagedEvent(SolveAndReserve(req, terrain, ExistingEntities(view, req)));   // ⭐ A3: per request — its moved entities are not obstacles
        }

        private static HashSet<int> SelvesOf(PlacementRequestBatch? req)
        {
            var set = new HashSet<int>();
            if (req != null) foreach (var s in req.Slots) if (s.Self != 0) set.Add(s.Self);
            return set;
        }

        /// <summary>⭐ A2 — remember the placed slots of <paramref name="answer"/> as taken (the test seam drives it through <see cref="SolveAndReserve"/>).</summary>
        private void Reserve(PlacementRequestBatch req, PlacementResponseBatch answer)
        {
            var slots = new Dictionary<int, PlacementSlot>();
            foreach (var s in req.Slots) slots[s.Key] = s;
            foreach (var a in answer.Answers)
                if (a.Outcome != PlacementOutcome.Failed && slots.TryGetValue(a.Key, out var slot))
                {
                    if (slot.Self != 0) _reserved.RemoveAll(r => r.Self == slot.Self);   // a moved entity's old reservation goes with it
                    _reserved.Add((a.Point, MathF.Max(0f, slot.Radius), _frame, slot.Self));
                }
        }

        /// <summary>⭐ A2 — <see cref="Solve"/> against <paramref name="existing"/> plus every slot this solver already handed out, then reserves this
        /// answer's slots (what <see cref="Execute"/> does per request; the test seam).</summary>
        public PlacementResponseBatch SolveAndReserve(PlacementRequestBatch req, TerrainWorld? terrain, List<(Vector3 Position, float Radius)> existing)
        {
            var world = new List<(Vector3 Position, float Radius)>(existing);
            var moving = SelvesOf(req);
            foreach (var r in _reserved) if (r.Self == 0 || !moving.Contains(r.Self)) world.Add((r.Position, r.Radius));
            var answer = Solve(req, terrain, world);
            Reserve(req, answer);
            return answer;
        }

        /// <summary>Solves one request (the test seam). <paramref name="existing"/> are the footprints already in the world.</summary>
        public PlacementResponseBatch Solve(PlacementRequestBatch req, TerrainWorld? terrain, IReadOnlyList<(Vector3 Position, float Radius)> existing)
        {
            var response = new PlacementResponseBatch { RequestId = req.RequestId, SourceNodeId = req.SourceNodeId };
            var placed = new List<(Vector3 Position, float Radius)>();
            float maxShift = float.IsFinite(req.MaxShift) && req.MaxShift > 0f ? req.MaxShift : 0f;

            foreach (var slot in req.Slots)
            {
                var answer = PlaceOne(slot, maxShift, terrain, existing, placed);
                if (answer.Outcome != PlacementOutcome.Failed) placed.Add((answer.Point, MathF.Max(0f, slot.Radius)));
                response.Answers.Add(answer);
            }
            return response;
        }

        private PlacementAnswer PlaceOne(PlacementSlot slot, float maxShift, TerrainWorld? terrain,
            IReadOnlyList<(Vector3 Position, float Radius)> existing, List<(Vector3 Position, float Radius)> placed)
        {
            var want = slot.Point;
            if (slot.Layer == PlacementLayer.Air) return Answer(slot, PlacementOutcome.Exact, want, string.Empty);

            bool amphibious = slot.Layer == PlacementLayer.Amphibious;
            uint mask = slot.Layer switch
            {
                PlacementLayer.Vehicle => (uint)NavLayerMask.Vehicle,
                PlacementLayer.Amphibious => (uint)(NavLayerMask.Vehicle | NavLayerMask.Naval),
                _ => (uint)NavLayerMask.Infantry,
            };
            bool noGroundData = _navmesh == null && terrain == null;
            float radius = MathF.Max(0f, slot.Radius);

            bool sawGround = false;   // some candidate was acceptable ground (so a failure is "taken", not "no ground")

            bool TryCandidate(Vector3 candidate, out Vector3 result)
            {
                result = candidate;
                if (_navmesh != null)
                {
                    if (!_navmesh.ProjectToNavmesh(candidate, out var snapped, mask)) return false;
                    result = snapped;
                }
                else if (terrain != null)
                {
                    if (!amphibious && terrain.SurfaceTypeAt(candidate.X, candidate.Y) == TerrainSurfaceType.Water) return false;
                }
                if (Distance2D(result, want) > maxShift + 1e-4f) return false;
                sawGround = true;
                return !Taken(result, radius, existing, placed);
            }

            Vector3 found;
            if (TryCandidate(want, out found)) return Done(slot, want, found, noGroundData);

            for (float r = RingStepMetres; r <= maxShift + 1e-4f; r += RingStepMetres)
                for (int b = 0; b < RingBearings; b++)
                {
                    float a = b * (MathF.PI * 2f / RingBearings);
                    var c = new Vector3(want.X + MathF.Cos(a) * r, want.Y + MathF.Sin(a) * r, want.Z);
                    if (TryCandidate(c, out found)) return Done(slot, want, found, noGroundData);
                }

            string why = !sawGround
                ? (_navmesh != null ? $"no walkable ground within {maxShift:0.#} m" : $"no dry ground within {maxShift:0.#} m")
                : $"no free ground within {maxShift:0.#} m";
            return Answer(slot, PlacementOutcome.Failed, want, why);
        }

        private static PlacementAnswer Done(PlacementSlot slot, Vector3 want, Vector3 found, bool noGroundData)
        {
            var outcome = Distance2D(found, want) < ExactToleranceMetres ? PlacementOutcome.Exact : PlacementOutcome.Shifted;
            return Answer(slot, outcome, found, noGroundData ? "no terrain" : string.Empty);
        }

        private static PlacementAnswer Answer(PlacementSlot slot, PlacementOutcome outcome, Vector3 point, string reason)
            => new() { Key = slot.Key, Outcome = outcome, Point = point, Reason = reason };

        private static bool Taken(Vector3 p, float radius, IReadOnlyList<(Vector3 Position, float Radius)> existing, List<(Vector3 Position, float Radius)> placed)
        {
            foreach (var o in placed) if (Distance2D(p, o.Position) < radius + o.Radius) return true;
            foreach (var o in existing) if (Distance2D(p, o.Position) < radius + o.Radius) return true;
            return false;
        }

        private static float Distance2D(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>⭐ SR-190 — every entity with a <c>SimTransform</c>, with its footprint radius — ⛔ except a macro INSTANCE: it is a marker
        /// standing at its anchor, not a body, and its own children are placed around that very point.</summary>
        /// <param name="moving">⭐ A3 — a request whose slots MOVE existing entities (<see cref="PlacementSlot.Self"/>): those entities are not obstacles.</param>
        public static List<(Vector3 Position, float Radius)> ExistingEntities(ISimulationView view, PlacementRequestBatch? moving = null)
        {
            var list = new List<(Vector3, float)>();
            var ignore = SelvesOf(moving);
            foreach (var e in view.Query().With<SimTransform>().Build())
            {
                if (view.HasManagedComponent<Fdp.Toolkit.Scenario.MacroInstance>(e)) continue;
                if (ignore.Count > 0 && view.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                    && ignore.Contains(view.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value)) continue;
                var pos = view.GetComponentRO<SimTransform>(e).Position;
                list.Add((new Vector3(pos.X, pos.Y, pos.Z), Diagnostics.Gizmos.EntityFootprint.InteractionRadiusMetres(view, e)));
            }
            return list;
        }
    }
}
