using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-6005</c> (city battle O-2; 📄 docs/DESIGN_Peek_And_Fire.md §9.10) — a HEAVY vehicle's wreck blocks the street: when a vehicle whose
    /// type carries <see cref="WreckObstacleDto"/> is destroyed (<c>Health.Current ≤ 0</c>, the death <c>WreckSpawnSystem</c> reads), the node that
    /// owns its Health (<see cref="HealthOwnership.AppliesHere"/>) asks for ONE static obstacle of that type on the hulk — the hulk's footprint
    /// (<c>VehicleParams</c> length × width, else its collider circle) × its hull height, along its heading — through the ONE creation request
    /// (<see cref="EntityCreationRequest"/>, the ELM: multi-frame, acked; the obstacle type is held <c>Constructing</c> until its bake commits,
    /// R-243). The hulk STAYS (dead bodies never vanish, CE-267 revert); the obstacle is a separate entity sitting on it, removable like any
    /// obstacle (CE-6003: a charge, a blade).
    /// <para>⭐ ONCE per death: only a CROSSING makes one — this system saw the vehicle alive, then dead (the <c>WreckSpawnSystem</c> rule, VE-P). A
    /// vehicle first seen already dead (a scenario saved with a wreck — its obstacle, if not cleared, was saved too; a late joiner; a Health claim
    /// that moved to this node) makes none, so a cleared wreck is never rebuilt and a reload never doubles it. Off during a replay (the recording
    /// holds the obstacle); a reload (a new world epoch) forgets.</para>
    /// <para>⭐ Parallel contract (CLAUDE.md "BEHAVIOUR CODE RUNS IN PARALLEL"): a serial system; the hulk changes nothing, the new entity is born
    /// through the ELM; no statics, no randomness, no clock. Its only state is a per-vehicle "was alive" map, grown on a vehicle's first sight
    /// (0 B per steady frame). In <c>CoreInfrastructureCapabilities.HealthApplication</c>, after <c>ObstacleRemovalSystem</c>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class WreckObstacleSystem : IEcsModuleSystem
    {
        private readonly Dictionary<Entity, bool> _wasAlive = new();
        private readonly Dictionary<long, long> _wreckTypeOf = new();   // vehicle TKB type → its wreck obstacle type (0 = none)
        private EntityRepository? _for;
        private EntityQuery? _query;
        private int _epoch;

        /// <summary>Wreck obstacles this node asked for (rails, diagnostics).</summary>
        public long Requested { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Health>() || !repo.IsComponentTypeRegistered<TkbIdentity>()) return;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) { _wasAlive.Clear(); return; }
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _epoch) | !ReferenceEquals(repo, _for) || _query == null)
            {
                _for = repo;
                _query = repo.Query().With<Health>().With<TkbIdentity>().With<SimTransform>().Build();
                _wasAlive.Clear();
                _wreckTypeOf.Clear();
            }
            if (!repo.HasSingletonManaged<ITkbDatabase>() || repo.GetSingletonManaged<ITkbDatabase>() is not { } tkb) return;
            foreach (var e in _query)
            {
                long wreckType = WreckTypeOf(tkb, repo.GetComponentRO<TkbIdentity>(e).TkbType);
                if (wreckType == 0) continue;
                bool alive = repo.GetComponentRO<Health>(e).Current > 0f;
                bool crossed = !alive && _wasAlive.TryGetValue(e, out bool was) && was;
                _wasAlive[e] = alive;
                if (!crossed || !HealthOwnership.AppliesHere(repo, e)) continue;
                if (Request(repo, e, wreckType)) Requested++;
            }
        }

        private long WreckTypeOf(ITkbDatabase tkb, long type)
        {
            if (_wreckTypeOf.TryGetValue(type, out long w)) return w;
            w = tkb.TryGetByType(type, out var t) && t.GetDescriptor<WreckObstacleDto>() is { ObstacleType: > 0 } d ? d.ObstacleType : 0;
            _wreckTypeOf[type] = w;
            return w;
        }

        /// <summary>The obstacle's box on <paramref name="hulk"/>: <c>VehicleParams</c> length × width, else the square inside its collider circle;
        /// its hull height, else the wreck type's own.</summary>
        internal static ObstacleShape ShapeOf(EntityRepository repo, Entity hulk, StaticObstacleDto? wreck)
        {
            float length = wreck?.Length ?? 0f, width = wreck?.Width ?? 0f, height = wreck?.Height ?? 0f;
            if (repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>() && repo.HasComponent<global::CarKinem.Core.VehicleParams>(hulk)
                && repo.GetComponentRO<global::CarKinem.Core.VehicleParams>(hulk) is { Length: > 0f, Width: > 0f } p)
            { length = p.Length; width = p.Width; }
            else if (PhysicsColliderReaders.Radius(repo, hulk) is > 0f and var r)
                length = width = r * MathF.Sqrt(2f);
            float h = PhysicsColliderReaders.HullHeight(repo, hulk);
            if (h > 0f) height = h;
            return new ObstacleShape { Length = length, Width = width, Height = height };
        }

        private static bool Request(EntityRepository repo, Entity hulk, long wreckType)
        {
            if (!repo.Bus.IsRegisteredManaged<EntityCreationRequest>()) return false;
            var tkb = repo.GetSingletonManaged<ITkbDatabase>();
            var dto = tkb != null && tkb.TryGetByType(wreckType, out var t) ? t.GetDescriptor<StaticObstacleDto>() : null;
            ref readonly var tf = ref repo.GetComponentRO<SimTransform>(hulk);
            repo.Bus.PublishManaged(new EntityCreationRequest
            {
                RequestId = Guid.NewGuid(),
                OwnerAppInstanceId = 0,   // the system picks
                TkbType = (TkbTypeId)(wreckType),
                InitialComponents = new List<object>
                {
                    new SimTransform { Position = tf.Position, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, TerrainObstacles.Yaw(tf.Rotation)) },
                    ShapeOf(repo, hulk, dto),
                },
            });
            return true;
        }
    }
}
