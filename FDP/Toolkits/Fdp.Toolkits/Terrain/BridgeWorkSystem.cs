using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4693</c> (bridge laying BL-S3 ⑥, BL-10, BL-17, BL-18; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12a) — THE MOVER LAYS AND RECOVERS: a bridge engineer
    /// whose brain set <see cref="BridgeCarried.Order"/> and whose hull stands at the work point (within <see cref="BridgeLaying.StandReachMetres"/> of the stand
    /// <see cref="BridgeLaying"/> computes — the same function the brain planned with) holds it for the engineer's build / recover seconds, then:
    /// <list type="bullet">
    /// <item>Lay — asks for the bridge obstacle through the ONE creation request (<see cref="EntityCreationRequest"/>, the <c>WreckObstacleSystem</c> precedent): its TKB type,
    ///   the deck pose and its per-instance <see cref="ObstacleShape"/> (Length = the planned deck, BL-20). Terrain holds it Constructing until every node baked it, then Open.</item>
    /// <item>Recover — ends the bridge through the ONE network destroy (<see cref="Fdp.Toolkit.NetworkSpawning.NetworkEntityDestroy"/>), ⛔ never Health 0 (a demolition:
    ///   <c>WaterFallSystem</c> drops whoever is on the deck, BL-18).</item>
    /// </list>
    /// ⭐ It NEVER writes <see cref="BridgeCarried"/> (the brain's, R-4212): the progress is this node's own state per hull (the <c>DozerCutSystem</c> precedent), and the
    /// brain sees the RESULT (the deck appears / goes). ⛔ Never in a replay; only on the node that moves the hull (<see cref="DozerCutSystem.WorldPosOrdinal"/>'s owner — a hull
    /// with no authority is ours).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class BridgeWorkSystem : IEcsModuleSystem
    {
        [Fdp.Core.SimulationMemory("per-engineer seconds held at the work point toward the current order, and whether the order was done (requested); lost progress restarts")]
        private readonly Dictionary<int, (BridgeOrder Order, Vector3 Key, float Seconds, bool Done, LaidDeckPlan? Plan)> _progress = new();
        private EntityQuery? _query;
        private EntityRepository? _queryRepo;

        /// <summary>Bridges asked for / recovered by this node (rails, diagnostics).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter of bridges laid")]
        public int Laid { get; private set; }
        [field: Fdp.Core.WorldDerived("diagnostic counter of bridges recovered")]
        public int Recovered { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<BridgeCarried>()) return;
            if (!ReferenceEquals(repo, _queryRepo)) { _query = repo.Query().With<BridgeCarried>().With<SimTransform>().Build(); _queryRepo = repo; _progress.Clear(); }
            long key = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DozerCutSystem.WorldPosOrdinal, 0);
            foreach (var e in _query!)
            {
                var c = repo.GetComponentRO<BridgeCarried>(e);
                if (c.Order == BridgeOrder.None) { _progress.Remove(e.Index); continue; }
                if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkAuthority>() && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkAuthority>(e)
                    && !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(repo, e, key)) continue;   // another node moves it
                if (!BridgeLaying.Figures(repo, e, out var layer, out var span)) continue;

                Vector3 orderKey = c.Order == BridgeOrder.Lay ? (Vector3)c.NearBank + (Vector3)c.FarBank * 3f : new Vector3(c.Target.NetworkId, 0f, 0f);
                var have = _progress.TryGetValue(e.Index, out var p) && p.Order == c.Order && p.Key == orderKey ? p : (Order: c.Order, Key: orderKey, Seconds: 0f, Done: false, Plan: (LaidDeckPlan?)null);
                if (have.Done) { _progress[e.Index] = have; continue; }   // asked for; the brain clears its order when it sees the result

                var at3 = repo.GetComponentRO<SimTransform>(e).Position;
                var here = new Vector2(at3.X, at3.Y);
                float half = HullHalf(repo, e);
                if (c.Order == BridgeOrder.Lay)
                {
                    // planned once per order (the same inputs the brain planned with ⇒ the same deck), not every frame
                    var plan = have.Plan ??= BridgeLaying.Plan(EqsTerrainSight.World(repo), ((Vector3)c.NearBank).AsXY(), ((Vector3)c.FarBank).AsXY(), layer.SpanMaxM, span.Length, span.Height, half);
                    if (!plan.Ok || Vector2.Distance(here, plan.Stand) > BridgeLaying.StandReachMetres) { have.Seconds = 0f; _progress[e.Index] = have; continue; }
                    have.Seconds += deltaTime;
                    if (have.Seconds >= MathF.Max(layer.BuildSeconds, 0.1f) && RequestBridge(repo, layer.BridgeType, plan, span))
                    { have.Done = true; Laid++; }
                }
                else
                {
                    var b = c.Target.Resolve(repo);
                    if (b.IsNull || !repo.IsAlive(b) || !repo.HasComponent<SimTransform>(b) || !repo.HasComponent<ObstacleShape>(b)) { _progress.Remove(e.Index); continue; }
                    var tf = repo.GetComponentRO<SimTransform>(b);
                    var shape = repo.GetComponentRO<ObstacleShape>(b);
                    var (stand, _) = BridgeLaying.RecoverStand(new Vector2(tf.Position.X, tf.Position.Y), TerrainObstacles.Yaw(tf.Rotation), shape.Length,
                                                               shape.Height > 0f ? shape.Height : span.Height, half, here);
                    if (Vector2.Distance(here, stand) > BridgeLaying.StandReachMetres) { _progress.Remove(e.Index); continue; }
                    have.Seconds += deltaTime;
                    if (have.Seconds >= MathF.Max(layer.RecoverSeconds, 0.1f)) { RemoveBridge(repo, view, b); have.Done = true; Recovered++; }
                }
                _progress[e.Index] = have;
            }
        }

        private static bool RequestBridge(EntityRepository repo, long bridgeType, in LaidDeckPlan plan, Fdp.Toolkit.Tkb.Domain.StaticObstacleDto span)
        {
            if (!repo.Bus.IsRegisteredManaged<EntityCreationRequest>()) return false;
            var terrain = EqsTerrainSight.World(repo);
            float z = terrain?.GroundHeightAt(plan.Stand.X, plan.Stand.Y) ?? 0f;
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = 0,   // the system picks
                TkbType = (TkbTypeId)bridgeType,
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = new Vector3(plan.Centre, z), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plan.Yaw) },
                    new ObstacleShape { Length = plan.Length, Width = span.Width, Height = span.Height },
                },
            });
            return true;
        }

        private static void RemoveBridge(EntityRepository repo, ISimulationView view, Entity b)
        {
            if (Fdp.Toolkit.NetworkSpawning.NetworkEntityDestroy.Request(repo, b, "Bridge recovered"))
            {
                view.GetCommandBuffer().SetLifecycleState(b, EntityLifecycle.TearDown);   // ⭐ as ObstacleRemovalSystem: out of every query (and the bake's set) from the next frame
                return;
            }
            view.GetCommandBuffer().DestroyEntity(b);
        }

        private static float HullHalf(EntityRepository repo, Entity e)
            => repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && repo.HasComponent<global::CarKinem.Core.VehicleParams>(e)
               && repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Length > 0f
                ? repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Length * 0.5f : 3f;
    }

    internal static class BridgeVectorExtensions
    {
        public static Vector2 AsXY(this Vector3 v) => new(v.X, v.Y);
    }
}
