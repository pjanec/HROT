using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Services
{
    [ComponentId(GlobalComponentIds.NetworkEntityMap)]
    public class NetworkEntityMap
    {
        private readonly Dictionary<long, Entity> _netToEntity = new();
        private readonly Dictionary<Entity, long> _entityToNet = new();
        
        internal struct GraveyardEntry
        {
            public long NetworkId;
            public uint DeathFrame;
        }

        private readonly List<GraveyardEntry> _graveyard = new();
        private readonly uint _graveyardDurationFrames;

        /// <summary>
        /// Raised immediately after a new (netId, entity) pair is registered.
        /// Subscribers can use this to drain deferred retry queues keyed by network ID
        /// without having to poll on every simulation tick.
        /// </summary>
        public event Action<long, Entity>? EntityRegistered;

        public NetworkEntityMap(uint graveyardDurationFrames = 60)
        {
            _graveyardDurationFrames = graveyardDurationFrames;
        }

        public IReadOnlyDictionary<long, Entity> Entries => _netToEntity;

        // ── Preview dry-run capture / restore ─────────────────────────────────

        /// <summary>
        /// ⭐⭐ An opaque snapshot of this map's whole state. ⛔ Only <see cref="RestoreState"/> reads it.
        /// </summary>
        public sealed class State
        {
            internal Dictionary<long, Entity> NetToEntity = new();
            internal Dictionary<Entity, long> EntityToNet = new();
            internal List<GraveyardEntry>     Graveyard   = new();
        }

        /// <summary>
        /// ⭐⭐⭐ <b>Captures the map so a preview can put it back.</b>
        /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §2b.
        ///
        /// <para>⛔⛔ <b>Why the MAP and not just the allocator.</b> 📐 <see cref="Register"/> THROWS on a
        /// duplicate id, and a preview's rewind *(<c>liveRepo.SyncFrom(snapshot)</c>)* does not touch this
        /// map — so entries from preview 1 survive into preview 2. ⚠ Today the id allocator's DRIFT is the
        /// only thing that stops preview 2 colliding; ⇒ the moment ids repeat exactly, they collide.
        /// ⭐ These two ship together or not at all.</para>
        ///
        /// <para>⚠ <b>The <c>EntityRegistered</c> event is deliberately NOT captured</b> — subscriptions are
        /// wiring, not state, and a preview does not re-wire anything. ⛔ Restoring them would double-fire
        /// on the next registration.</para>
        /// </summary>
        public State CaptureState() => new State
        {
            NetToEntity = new Dictionary<long, Entity>(_netToEntity),
            EntityToNet = new Dictionary<Entity, long>(_entityToNet),
            Graveyard   = new List<GraveyardEntry>(_graveyard),
        };

        /// <summary>
        /// ⭐ Puts back a <see cref="CaptureState"/> snapshot.
        /// <para>⛔ Replaces rather than merges: a preview's entries must be GONE, and a merge would keep
        /// exactly the ones that cause <see cref="Register"/> to throw.</para>
        /// </summary>
        public void RestoreState(State state)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));

            _netToEntity.Clear();
            foreach (var kv in state.NetToEntity) _netToEntity[kv.Key] = kv.Value;

            _entityToNet.Clear();
            foreach (var kv in state.EntityToNet) _entityToNet[kv.Key] = kv.Value;

            _graveyard.Clear();
            _graveyard.AddRange(state.Graveyard);
        }

        public void Register(long netId, Entity entity)
        {
            if (_netToEntity.ContainsKey(netId))
                 throw new InvalidOperationException($"NetworkId {netId} already registered");
            
            _netToEntity[netId] = entity;
            _entityToNet[entity] = netId;
            
            // Remove from graveyard if ID is reused
            _graveyard.RemoveAll(g => g.NetworkId == netId);

            EntityRegistered?.Invoke(netId, entity);
        }

        public void Unregister(long netId, uint currentFrame)
        {
            if (_netToEntity.TryGetValue(netId, out var entity))
            {
                _netToEntity.Remove(netId);
                _entityToNet.Remove(entity);
                
                AddToGraveyard(netId, currentFrame);
            }
        }

        public bool TryGetEntity(long netId, out Entity entity)
        {
            return _netToEntity.TryGetValue(netId, out entity);
        }
        
        public bool TryGetNetworkId(Entity entity, out long netId)
        {
            return _entityToNet.TryGetValue(entity, out netId);
        }

        public bool IsGraveyard(long id)
        {
             foreach(var entry in _graveyard)
             {
                 if (entry.NetworkId == id) return true;
             }
             return false;
        }

        private void AddToGraveyard(long id, uint currentFrame)
        {
            _graveyard.Add(new GraveyardEntry { NetworkId = id, DeathFrame = currentFrame });
        }

        public void PruneGraveyard(uint currentFrame)
        {
             _graveyard.RemoveAll(e => (currentFrame - e.DeathFrame) > _graveyardDurationFrames);
        }

        public void PruneDeadEntities(EntityRepository repo)
        {
            var toRemove = new List<long>();
            foreach (var kvp in _netToEntity)
            {
                if (!repo.IsAlive(kvp.Value))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var netId in toRemove)
            {
                Unregister(netId, repo.GlobalVersion);
            }
        }

    }
}
