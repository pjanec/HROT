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
        private readonly Dictionary<long, bool> _present = new();   // ⭐ CE-3329 W1 — this frame's networked ids → owned here?
        private readonly List<long> _gone = new();
        private int _worldEpoch;   // ⭐ CE-2101 — tracked ids belong to ONE world (WorldEpoch)
        private readonly Dictionary<IDescriptorTranslator, SystemProfileData> _translatorProfileData = new();

        public IReadOnlyList<IDescriptorTranslator> Translators => _translators;

        /// <summary>⭐ CE-3329 W1 — ids disposed by the sweep (absent from the world without a DestructionOrder). Diagnostics, rails.</summary>
        public int SweptDisposes { get; private set; }

        /// <summary>⭐ CE-3329 W1 — ids forgotten because another node owns them now (a hand-over): never disposed.</summary>
        public int ForgottenHandedOver { get; private set; }
        
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
            // ⭐ D4 — Ended, not Moved: an in-place restore (a dry-run exit, a checkpoint restore) replaces the world but does
            //   not END it — the survivors are still this node's. ⭐ CE-3329 W1: what did NOT survive is retracted by the sweep below.
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Ended(view, ref _worldEpoch))
            {
                foreach (var netId in _trackedEntities.Keys.ToList())
                    DisposeAll(netId);
                if (_trackedEntities.Count > 0)
                    FdpLog<CycloneNetworkCleanupSystem>.Info("World wiped: deleted {0} owned entities on the wire.", _trackedEntities.Count);
                _trackedEntities.Clear();
            }

            // ⭐⭐⭐ 1. CE-3329 PW — §12b W1: a MARK-AND-SWEEP BY NETWORK ID over every id this node published.
            //   📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12b. A world replaced IN PLACE (a replay seek's keyframe, a checkpoint
            //   restore, a dry-run exit, a review's entry / exit) raises no DestructionOrder, so an entity alive before the swap and
            //   absent after kept its DDS instances — TransientLocal handed it to every late joiner and an external IG kept drawing it
            //   (CE-3331). ⇒ every frame: an id published here and ABSENT from the world ⇒ DisposeAll; present as a GHOST (another
            //   node owns it — a hand-over flips authority in place) ⇒ forget, no dispose; present and owned ⇒ keep (its entity handle
            //   refreshed — a keyframe re-creates entities under new handles). Network ids are never reused within one world
            //   (IRestorableIdAllocator), so the diff is exact. ⭐ Subsumes LR-2's lightweight-round sweep (R-271): a burst round is
            //   simply absent. ⛔ SUPERSEDED: replay-and-modules §3.10.4 "clear the tracking on a seek" (forgets without disposing).
            _present.Clear();
            var query = view.Query()
                .WithLifecycle(EntityLifecycle.All)
                .With<NetworkIdentity>()
                .With<NetworkAuthority>()
                .Build();

            var repo = view as EntityRepository;
            foreach (var entity in query)
            {
                // ⭐ an entity TEARING DOWN counts as absent: whichever sees it first — the sweep, or its DestructionOrder below —
                //   retracts it, once; it is never re-tracked while it lingers. A seek that brings it back alive tracks it again.
                if (repo != null && repo.GetLifecycleState(entity) == EntityLifecycle.TearDown) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                bool owned = view.GetComponentRO<NetworkAuthority>(entity).HasAuthority;   // DB-MOD1-03 / CE-281
                _present[netId] = owned;
                if (owned) _trackedEntities[netId] = entity;   // new ⇒ tracked; known ⇒ the handle refreshed
            }

            if (_trackedEntities.Count > 0)
            {
                _gone.Clear();
                foreach (var netId in _trackedEntities.Keys)
                    if (!_present.TryGetValue(netId, out bool owned) || !owned) _gone.Add(netId);
                foreach (long netId in _gone)
                {
                    _trackedEntities.Remove(netId);
                    if (_present.ContainsKey(netId)) { ForgottenHandedOver++; continue; }   // a ghost now: its owner publishes it
                    SweptDisposes++;
                    DisposeAll(netId);
                }
                if (_gone.Count > 0)
                    FdpLog<CycloneNetworkCleanupSystem>.Debug("Sweep: {0} id(s) left this node's world.", _gone.Count);
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
