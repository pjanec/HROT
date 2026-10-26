using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// ⭐ LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — <b>a visible round gets its network identity, and nothing else:</b> every
    /// <see cref="LightweightRound"/> entity without a <see cref="NetworkIdentity"/> (one the fire path just tagged) gets an id from the
    /// node's pooled allocator, LOCAL ownership (<see cref="NetworkAuthority"/>, owner = this node) and its place in the
    /// <see cref="NetworkEntityMap"/>. That is all the existing egress needs to publish <c>EntityMaster</c> (with the Lightweight flag)
    /// and <c>WorldPos</c> — no lifecycle, no construction, no acks.
    /// <para>⛔ Network-agnostic (R-134): it knows ids, owners and the map, never DDS. The combat code knows none of it.</para>
    /// <para>Registered by <c>EntityCreationPack</c> on every ECS host (it holds the allocator); a host without one assigns nothing, and
    /// its rounds stay local.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class LightweightRoundIdentitySystem : IEcsModuleSystem
    {
        private readonly INetworkIdAllocator? _ids;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;
        private readonly List<Entity> _pending = new();
        private EntityRepository? _queryWorld;
        private EntityQuery? _query;

        public LightweightRoundIdentitySystem(INetworkIdAllocator? ids, NetworkEntityMap entityMap, int localNodeId)
        {
            _ids = ids;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        /// <summary>Rounds given an identity in the last tick — the rail's counter.</summary>
        public int Assigned { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            Assigned = 0;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (_ids == null || view is not EntityRepository world || !world.IsComponentTypeRegistered<LightweightRound>()) return;
            if (!ReferenceEquals(world, _queryWorld))
            {
                _queryWorld = world;
                _query = world.Query().With<LightweightRound>().Without<NetworkIdentity>().Build();
            }
            _pending.Clear();
            foreach (var e in _query!) _pending.Add(e);
            foreach (var e in _pending)
            {
                long id = _ids.AllocateId();
                world.AddComponent(e, new NetworkIdentity(id));
                world.AddComponent(e, new NetworkAuthority(_localNodeId, _localNodeId));
                _entityMap.Register(id, e);
                Assigned++;
            }
        }
    }
}
