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
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;

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
        private readonly Dictionary<long, Entity> _trackedLightweight = new();   // LR-2 — the subset with no lifecycle
        private uint _clearGeneration;   // ⭐ CE-3329 W1 — EntityRepository.ClearGeneration at the last sweep
        private uint _lastRunVersion;    // GlobalVersion at the last run — the delta query's horizon
        private EntityQuery? _query;
        private EntityRepository? _queryWorld;
        private int _worldEpoch;   // ⭐ CE-2101 — tracked ids belong to ONE world (WorldEpoch)
        private readonly Dictionary<IDescriptorTranslator, SystemProfileData> _translatorProfileData = new();

        public IReadOnlyList<IDescriptorTranslator> Translators => _translators;

        /// <summary>⭐ CE-3329 W1 — ids disposed by the sweep (absent after an in-place world replacement). Diagnostics, rails.</summary>
        public int SweptDisposes { get; private set; }

        /// <summary>⭐ CE-3329 W1 — ids forgotten because another node owns them now (a hand-over): never disposed.</summary>
        public int ForgottenHandedOver { get; private set; }

        /// <summary>⭐ CE-3329 W1 — how many full sweeps ran: one per in-place world replacement, never one per frame.</summary>
        public int Sweeps { get; private set; }

        /// <summary>⭐ CE-4079 ② — descriptor instances this node's writers let go of because the grantee confirmed (F7). Diagnostics, rails.</summary>
        public int ReleasedHandedOver { get; private set; }
        
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
            ReleaseHandedOver(view);

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
                _trackedLightweight.Clear();
            }

            // ⭐⭐⭐ 1. CE-3329 PW — §12b W1, CHANGE-DRIVEN: nothing here walks the world on an ordinary frame.
            //   📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12b.
            //   ① the world was replaced IN PLACE (EntityRepository.ClearGeneration moved: a replay seek's keyframe, a checkpoint
            //     restore, a dry-run exit, a review's entry / exit) — those raise no DestructionOrder, so ONE mark-and-sweep by
            //     network id retracts what did not survive (CE-3331: TransientLocal kept handing it to late joiners and the IG).
            //   ② otherwise only entities whose NetworkIdentity / NetworkAuthority chunk changed since the last run are looked at
            //     (QueryDelta skips unchanged chunks): a new owned id is tracked, an id handed over to another node is forgotten.
            //   ③ an ordinary end is announced: a DestructionOrder (step 2), or LR-2's lightweight round (step 1b — it has no lifecycle).
            //   ⛔ SUPERSEDED (same CE, first build): the sweep ran EVERY frame over every networked entity.
            //   ⛔ SUPERSEDED: replay-and-modules §3.10.4 "clear the tracking on a seek" (forgets without disposing).
            var repo = view as EntityRepository;
            if (repo == null || repo.ClearGeneration != _clearGeneration || repo.GlobalVersion < _lastRunVersion)
            {
                if (repo != null) _clearGeneration = repo.ClearGeneration;
                Sweep(view, repo);
            }
            else
            {
                Discover(view, repo);
            }
            if (repo != null) _lastRunVersion = repo.GlobalVersion;

            // ⭐⭐ 1b. LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — a LIGHTWEIGHT round has no lifecycle, so no DestructionOrder
            //   ever announces its end: it is simply destroyed when it bursts or stops. ⇒ a tracked lightweight round that is no
            //   longer alive gets its descriptors disposed here (EntityMaster ⇒ every viewer destroys its copy). Only the live rounds
            //   are visited.
            if (_trackedLightweight.Count > 0)
            {
                _gone.Clear();
                foreach (var (netId, round) in _trackedLightweight)
                    if (!view.IsAlive(round)) _gone.Add(netId);
                foreach (long netId in _gone)
                {
                    _trackedLightweight.Remove(netId);
                    if (_trackedEntities.Remove(netId)) DisposeAll(netId);
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

        private EntityQuery BuildQuery(ISimulationView view) => view.Query()
            .WithLifecycle(EntityLifecycle.All)
            .With<NetworkIdentity>()
            .With<NetworkAuthority>()
            .Build();

        /// <summary>
        /// ⭐ CE-3329 W1 ② — the entities whose networking components changed since the last run (unchanged chunks skipped):
        /// a new owned id is tracked, a known one's handle refreshed, one handed over to another node forgotten (its owner
        /// publishes it now — never disposed).
        /// </summary>
        private void Discover(ISimulationView view, EntityRepository repo)
        {
            if (!ReferenceEquals(_queryWorld, repo)) { _query = BuildQuery(view); _queryWorld = repo; }
            // ⚠ ">= last run": a change stamped later in the frame the last run saw carries that same version
            uint since = _lastRunVersion == 0 ? 0 : _lastRunVersion - 1;
            foreach (var entity in repo.QueryDelta(_query!, since))
                Observe(view, repo, entity);
        }

        /// <summary>
        /// ⭐⭐ CE-3329 W1 ① — once per in-place world replacement: a MARK-AND-SWEEP BY NETWORK ID. An id published here and
        /// ABSENT ⇒ DisposeAll; present as a GHOST (another node owns it) ⇒ forget, no dispose; present and owned ⇒ keep, its
        /// handle refreshed. Network ids are never reused within one world (IRestorableIdAllocator), so the diff is exact.
        /// </summary>
        private void Sweep(ISimulationView view, EntityRepository? repo)
        {
            Sweeps++;
            _present.Clear();
            _trackedLightweight.Clear();   // re-learnt below: a round that did not survive is simply absent
            foreach (var entity in BuildQuery(view))
            {
                if (IsTearingDown(repo, entity)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
                bool owned = view.GetComponentRO<NetworkAuthority>(entity).HasAuthority;   // DB-MOD1-03 / CE-281
                _present[netId] = owned;
                if (owned) Track(view, entity, netId);
            }

            if (_trackedEntities.Count == 0) return;
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

        /// <summary>
        /// ⭐⭐ <c>CE-4079</c> ② — the creator's grant was CONFIRMED by the grantee (<see cref="DescriptorHandedOver"/>, raised by
        /// <c>OwnershipRecomputeSystem</c>): the translators of that descriptor let go of the instance — an UNREGISTER, never a dispose
        /// (every descriptor topic sets <c>AutodisposeUnregisteredInstances = false</c>), so readers keep the grantee's value and the
        /// grantee is the SOLE writer: its crash then shows on the descriptor as <c>NotAliveNoWriters</c>, which is the spec's partial-owner
        /// mechanism. 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.3 (F7). ⚠ Not held by the replay egress hold: it is a retraction
        /// like the disposes below, and the grantee already writes.
        /// </summary>
        private void ReleaseHandedOver(ISimulationView view)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<DescriptorHandedOver>()) return;
            foreach (var handedOver in view.ReadEvents<DescriptorHandedOver>())
            {
                var (typeId, instanceId) = OwnershipExtensions.UnpackKey(handedOver.PackedKey);
                foreach (var translator in _translators)
                {
                    if (translator.DescriptorOrdinal != typeId) continue;
                    try
                    {
                        translator.ReleaseInstance(handedOver.NetworkId.Value, instanceId);
                        ReleasedHandedOver++;
                    }
                    catch (Exception ex)
                    {
                        FdpLog<CycloneNetworkCleanupSystem>.Warn(
                            "Translator {0} failed to release entity {1} (descriptor {2}): {3}",
                            translator.GetType().Name, handedOver.NetworkId.Value, typeId, ex.Message);
                    }
                }
            }
        }

        private void Observe(ISimulationView view, EntityRepository repo, Entity entity)
        {
            if (IsTearingDown(repo, entity)) return;
            long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (view.GetComponentRO<NetworkAuthority>(entity).HasAuthority) Track(view, entity, netId);
            else if (_trackedEntities.Remove(netId)) { _trackedLightweight.Remove(netId); ForgottenHandedOver++; }
        }

        private void Track(ISimulationView view, Entity entity, long netId)
        {
            _trackedEntities[netId] = entity;   // new ⇒ tracked; known ⇒ the handle refreshed
            if (view.HasComponent<LightweightRound>(entity)) _trackedLightweight[netId] = entity;
        }

        // ⭐ an entity TEARING DOWN counts as absent: whichever sees it first — the sweep, or its DestructionOrder — retracts it,
        //   once; it is never re-tracked while it lingers. A seek that brings it back alive tracks it again.
        private static bool IsTearingDown(EntityRepository? repo, Entity entity)
            => repo != null && repo.GetLifecycleState(entity) == EntityLifecycle.TearDown;

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
