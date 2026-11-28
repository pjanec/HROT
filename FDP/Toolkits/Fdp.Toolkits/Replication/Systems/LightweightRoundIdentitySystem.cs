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
    /// <para>Registered by <c>EntityCreationPack</c> on every ECS host (it holds the allocator) — ⭐ CE-11050: the editor, which has no network adapters, included, so
    /// the gateway sees its rounds; a host without an allocator assigns nothing, and its rounds stay local.</para>
    /// <para>⭐ CE-11050 — it also UNMAPS the rounds it identified once they are gone: a round lives a second or less and a fight fires thousands, and a host with no
    /// replication module (the editor) has no <c>DisposalMonitoringSystem</c> to prune the map, so the entries (and the dead ids' graveyard) would grow for the whole run.
    /// On a networked host the monitor prunes the same ids too — <c>Unregister</c> of an id already gone is a no-op, so the two do not disagree.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class LightweightRoundIdentitySystem : IEcsModuleSystem
    {
        private readonly INetworkIdAllocator? _ids;
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeId _localNodeId;
        [Fdp.Core.FrameScratch("scratch — cleared at the top of each run and filled from this frame's world")]
        private readonly List<Entity> _pending = new();
        [Fdp.Core.WorldDerived("the rounds this system identified and has not yet unmapped — each one is a live world entity with a NetworkIdentity; the map's own RebuildFromWorld covers a restore")]
        private readonly List<(int Id, Entity Round)> _assigned = new();
        private EntityRepository? _queryWorld;
        private EntityQuery? _query;

        public LightweightRoundIdentitySystem(INetworkIdAllocator? ids, NetworkEntityMap entityMap, int localNodeId)
        {
            _ids = ids;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = (Fdp.Core.NodeId)(localNodeId);
        }

        /// <summary>How many ids the allocator handed out that the map still held for another entity (CE-5061) — skipped, never registered twice.</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter — read by tests and diagnostics only")]
        public int IdCollisions { get; private set; }

        private const int MaxIdRetries = 64;

        /// <summary>Rounds given an identity in the last tick — the rail's counter.</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter — read by tests and diagnostics only")]
        public int Assigned { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            Assigned = 0;
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (_ids == null || view is not EntityRepository world || !world.IsComponentTypeRegistered<LightweightRound>()) return;
            if (!ReferenceEquals(world, _queryWorld))
            {
                _queryWorld = world;
                _assigned.Clear();   // handles of another world mean nothing in this one (and its map is its own)
                _query = world.Query().With<LightweightRound>().Without<NetworkIdentity>().Build();
            }
            Unmap(world);
            _pending.Clear();
            foreach (var e in _query!) _pending.Add(e);
            foreach (var e in _pending)
            {
                // ⭐ CE-5061 (unreal-ig H-6: the editor process ended after ~23 min of live stechovice-battle, "NetworkId 6263 already registered") — the allocator handed out an
                //   id the map still holds for ANOTHER entity (it was reissued: a world boundary resets the id authority while restored rounds keep theirs). A round must never
                //   end the process: take the next id, count it, say it once. ⛔ The reissue itself is the open defect (CE-5061), not fixed here.
                int id = _ids.AllocateId();
                for (int tries = 0; _entityMap.TryGetEntity(id, out var holder) && holder != e && tries < MaxIdRetries; tries++)
                {
                    if (IdCollisions++ == 0)
                        Console.WriteLine($"[LightweightRoundIdentity] CE-5061: id {id} is still mapped to entity {holder} - the allocator reissued it; the round takes the next id (said once)");
                    id = _ids.AllocateId();
                }
                if (_entityMap.TryGetEntity(id, out var still) && still != e) continue;   // no free id after the retries: the round stays local (drawn by this node only)
                world.AddComponent(e, new NetworkIdentity(id));
                world.AddComponent(e, new NetworkAuthority(_localNodeId, _localNodeId));
                _entityMap.Register(id, e);
                _assigned.Add((id, e));
                Assigned++;
            }
        }

        /// <summary>⭐ CE-11050 — a round that is gone leaves the map (and its id the graveyard, aged here too): no per-frame allocation, swap-remove over a field list.</summary>
        private void Unmap(EntityRepository world)
        {
            bool any = false;
            for (int i = _assigned.Count - 1; i >= 0; i--)
            {
                var (id, round) = _assigned[i];
                if (world.IsAlive(round)) continue;
                _entityMap.Unregister(id, world.SimulationTick);
                int last = _assigned.Count - 1;
                _assigned[i] = _assigned[last];
                _assigned.RemoveAt(last);
                any = true;
            }
            if (any) _entityMap.PruneGraveyard(world.SimulationTick);
        }
    }
}
