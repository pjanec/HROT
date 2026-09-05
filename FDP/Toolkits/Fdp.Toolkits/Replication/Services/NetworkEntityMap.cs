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

        /// <summary>
        /// 🔴🔴 <b>Forgets every mapping — the WORLD BOUNDARY operation.</b> 📄
        /// <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11 *(<c>HN-037</c>)*.
        ///
        /// <para>⭐⭐⭐ <b>Why this had to exist before the allocator could be unified.</b> This map is a
        /// node-local index OF the world, and <c>EntityRepository.SoftClear</c> does not touch it. That was
        /// invisible while a reload allocated FRESH ids *(§11a's drift: the second load in one process got
        /// <c>1008–1015</c>)* — every id was new, so no stale entry could match. ⛔ The moment the world
        /// boundary resets the authority to 1000, the second load re-issues <c>1000–1007</c> and
        /// <c>NetworkSpawningSystem</c>'s duplicate guard *(<i>"silently drop if already spawned"</i>)</c>
        /// drops <b>every single spawn</b> — 📐 measured `2026-08-24`: 8 entities on the first load, <b>0</b>
        /// on the second, with no exception and no log line.</para>
        ///
        /// <para>⇒ ⭐⭐ <b>The drift was not merely a cosmetic divergence; it was standing in for this
        /// clear.</b> Removing it without adding this turns a visible id difference into a silently empty
        /// world — strictly worse. 📌 Stated here rather than only in the design because the next person to
        /// find this method will be wondering why a map needs a Clear at all.</para>
        ///
        /// <para>⛔ NOT for preview: preview does not clear the world, and its participant captures and
        /// restores state instead *(<see cref="CaptureState"/> / <see cref="RestoreState"/>, §4d)*.</para>
        /// </summary>
        public void Clear()
        {
            _netToEntity.Clear();
            _entityToNet.Clear();
            _graveyard.Clear();
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

        /// <summary>
        /// Moves the ids of destroyed entities into the graveyard.
        ///
        /// <para>⚠ <b>Clock corrected <c>2026-09-03</c>: this stamps <see cref="EntityRepository.SimulationTick"/>,
        /// not <c>GlobalVersion</c>.</b> The graveyard window is denominated in FRAMES
        /// (<c>graveyardDurationFrames</c>), and the repository's own rule is explicit — <i>"Frame-index /
        /// wall-tick consumers must read this, NOT GlobalVersion"</i> — because <c>BumpMemoryVersion()</c>
        /// advances <c>GlobalVersion</c> alone during a mid-tick debug burst. ⭐ The mismatch was harmless
        /// only while <see cref="PruneGraveyard"/> had no caller and the window was never evaluated; it
        /// stopped being harmless the moment <c>DisposalMonitoringSystem</c> started ticking it.</para>
        /// </summary>
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
                Unregister(netId, repo.SimulationTick);
            }
        }

    }
}
