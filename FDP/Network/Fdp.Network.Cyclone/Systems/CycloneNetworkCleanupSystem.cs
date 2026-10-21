using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Interfaces;
using Fdp.Network.Cyclone.Topics;
using Fdp.Core.Logging;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Network.Cyclone.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    [TickMode(TickPolicy.EveryFrame)]
    public class CycloneNetworkCleanupSystem : IEcsModuleSystem
    {
        private readonly Fdp.Interfaces.IDescriptorTranslator[] _translators;
        private readonly Dictionary<long, Entity> _trackedEntities = new();
        private readonly Dictionary<long, Entity> _trackedLightweight = new();   // ⭐ LR-2 — rounds with no lifecycle (R-271)
        private readonly List<long> _goneLightweight = new();
        private int _worldEpoch;   // ⭐ CE-2101 — tracked ids belong to ONE world (WorldEpoch)
        private readonly Dictionary<IDescriptorTranslator, SystemProfileData> _translatorProfileData = new();

        public IReadOnlyList<IDescriptorTranslator> Translators => _translators;
        
        public CycloneNetworkCleanupSystem(IEnumerable<Fdp.Interfaces.IDescriptorTranslator> translators)
        {
            _translators = translators?.ToArray()
                ?? throw new ArgumentNullException(nameof(translators));

            foreach (var translator in _translators)
            {
                var translatorName = $"{translator.TopicName} [{translator.DescriptorOrdinal}]";
                _translatorProfileData[translator] = new SystemProfileData(translatorName);
            }
        }

        public SystemProfileData? GetTranslatorProfileData(IDescriptorTranslator translator)
            => _translatorProfileData.TryGetValue(translator, out var data) ? data : null;

        public void Execute(ISimulationView view, float dt)
        {
            // ⭐⭐ CE-4000 F2 / R-4019 — the world was wiped (entering Idle, or a load's world boundary) without a
            //   DestructionOrder: DELETE on the wire every entity this node owned, then forget them — so a late joiner never
            //   receives a dead run (EntityMaster is TransientLocal). ⛔ SUPERSEDED (CE-2101): "forget, no Dispose — the next
            //   world reuses the ids". The deletes now go out when the cluster ENTERS Idle, a load cycle before any id is
            //   reused, and a receiver ignores a delete from a node that is not the entity's owner.
            //   📄 docs/DESIGN_Cluster_Load_Phase.md §8.2.
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch))
            {
                foreach (var netId in _trackedEntities.Keys.ToList())
                    DisposeAll(netId);
                if (_trackedEntities.Count > 0)
                    FdpLog<CycloneNetworkCleanupSystem>.Info("World wiped: deleted {0} owned entities on the wire.", _trackedEntities.Count);
                _trackedEntities.Clear();
                _trackedLightweight.Clear();
            }

            // 1. Scan for new entities to track (all lifecycle states — entities may be in
            //    Constructing, Active, or TearDown when they first need to be tracked).
            var query = view.Query()
                .WithLifecycle(EntityLifecycle.All)
                .With<NetworkIdentity>()
                .With<NetworkAuthority>()
                .Build();

            foreach (var entity in query)
            {
                 // CE-281: repointed from the retired NetworkOwnership to NetworkAuthority (identical shape).
                 // The query now also matches ghosts (NetworkAuthority-only), but the HasAuthority guard
                 // below drops them exactly as before (ghost PrimaryOwnerId != LocalNodeId).
                 ref readonly var ownership = ref view.GetComponentRO<NetworkAuthority>(entity);
                 if (!ownership.HasAuthority) continue; // DB-MOD1-03: replaced PrimaryOwnerId != LocalNodeId
                 
                 ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(entity);
                 long netId = identity.Value;
                 
                 if (!_trackedEntities.ContainsKey(netId))
                 {
                     _trackedEntities[netId] = entity;
                     if (view.HasComponent<LightweightRound>(entity)) _trackedLightweight[netId] = entity;
                 }
            }

            // ⭐⭐ 1b. LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — a LIGHTWEIGHT round has no lifecycle, so no DestructionOrder
            //   ever announces its end: it is simply destroyed when it bursts or stops. ⇒ a tracked lightweight round that is no
            //   longer alive gets its descriptors disposed here (EntityMaster ⇒ every viewer destroys its copy). Without this, every
            //   viewer would keep every round it ever saw.
            if (_trackedLightweight.Count > 0)
            {
                _goneLightweight.Clear();
                foreach (var (netId, round) in _trackedLightweight)
                    if (!view.IsAlive(round)) _goneLightweight.Add(netId);
                foreach (long netId in _goneLightweight)
                {
                    _trackedLightweight.Remove(netId);
                    _trackedEntities.Remove(netId);
                    DisposeAll(netId);
                }
            }

            // 2. Dispose translators for entities entering TearDown via DestructionOrder.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
            {
                if (!view.HasComponent<NetworkIdentity>(evt.Entity))
                    continue;

                ref readonly var identity = ref view.GetComponentRO<NetworkIdentity>(evt.Entity);
                long netId = identity.Value;

                if (_trackedEntities.Remove(netId))
                {
                    FdpLog<CycloneNetworkCleanupSystem>.Info(
                        "Detected entity destruction {0}, sending dispose.",
                        netId);

                    DisposeAll(netId);
                }
            }
        }

        /// <summary>Disposes <paramref name="netId"/> on every translator (writes "entity deleted" to the wire), timing each.</summary>
        private void DisposeAll(long netId)
        {
            foreach (var translator in _translators)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    translator.Dispose(netId);
                }
                catch (Exception ex)
                {
                    FdpLog<CycloneNetworkCleanupSystem>.Error(
                        "Translator {0} failed to dispose entity {1}: {2}",
                        translator.GetType().Name,
                        netId,
                        ex.Message);
                }
                finally
                {
                    sw.Stop();
                    if (_translatorProfileData.TryGetValue(translator, out var profile))
                    {
                        profile.RecordExecution(sw.Elapsed.TotalMilliseconds);
                    }
                }
            }
        }
    }
}
