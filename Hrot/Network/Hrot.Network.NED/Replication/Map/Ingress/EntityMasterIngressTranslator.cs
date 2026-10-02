using System;
using System.Runtime.InteropServices;
using Hrot.NED.Descriptors;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core.Logging;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Hrot <c>EntityMaster</c> DDS topic.
    ///
    /// On receiving a new entity announcement it ensures a ghost exists and attaches
    /// a <see cref="TkbIdentity"/> component so the kernel-side ghost promotion pipeline
    /// can drive the ELM construction cycle.
    ///
    /// On disposal it publishes <see cref="DestroyEntityCommand"/> so the lifecycle module
    /// can tear the entity down cleanly.
    ///
    /// For already-known entities the translator emits no ECS component updates, except to fill an unknown primary owner.
    ///
    /// ⭐ <b>CE-517 — the primary owner is the sample's WRITER.</b> Spec (<c>BDC_NED_SST_Descriptor_Rules.md</c>):
    /// <i>"Ownership is determined by the most recent writer"</i>; <c>EntityMaster</c> carries no owner field. Every
    /// production participant enables CycloneDDS sender tracking with <c>AppInstanceId</c> = its node id
    /// (<c>BUG2-DESIGN.md</c> §1.2), so the reader resolves the writer of each sample to a node.
    /// ⚠ <b>Why a retry by publication handle exists:</b> CycloneDDS.NET 0.3.2 maps a writer's handle to its participant
    /// synchronously (subscription-matched listener), but moves an arrived identity sample into its lookup only from an
    /// async loop (<c>SenderRegistry.MonitorIdentitiesAsync</c>, a thread-pool continuation). Under thread-pool starvation
    /// an identity that has ARRIVED is not yet LOOKED UP, so a sample delivered in that window has no sender — measured:
    /// once in four full <c>Hrot.IG.Tests</c> runs, and in a starved-pool probe. Such a ghost keeps -1 and is resolved by
    /// its writer's handle on a later poll. ⭐ Remove the retry once the library drains pending identities on a miss.
    /// A KNOWN owner is never overwritten here: a master move reaches every node as an <c>OwnershipUpdate</c> through
    /// <c>OwnershipApplier</c>, and an old writer's late sample must not flip it back.
    ///
    /// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class EntityMasterIngressTranslator : IDescriptorTranslator
    {
        // --- Named constants (§CODE-STANDARDS §1 — no magic numbers) ---
        private const string DdsTopicName = "EntityMaster";
        private const long OrdinalValue = (long)EDescriptorType.dtEntityMaster;

        private readonly DdsReader<EntityMaster> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly SenderRegistry? _senders;

        /// <summary>CE-517: resolves a writer's publication handle to its node id, or null while its identity is unknown.</summary>
        private Func<long, int?>? _resolveWriter;

        /// <summary>CE-517: ghosts whose owner is still unknown, keyed by network id → the writer's publication handle.</summary>
        private readonly Dictionary<long, long> _ownerUnresolved = new();
        private readonly List<long> _resolvedScratch = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityMasterIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            long localNodeId,
            FdpEventBus eventBus,
            GhostCreationSystem ghostCreationSystem)
        {
            // participant may be null in unit-test mode — PollIngress becomes a no-op
            _reader = participant is not null ? new DdsReader<EntityMaster>(participant) : null!;
            // CE-517: resolve each sample's writer to a node (a participant without sender tracking yields none).
            _senders = participant?.SenderRegistry;
            if (_senders != null)
            {
                _reader.EnableSenderTracking(_senders);
                var senders = _senders;
                _resolveWriter = handle => senders.TryGetIdentity(handle, out var id) ? id.AppInstanceId : null;
            }
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return; // test mode — no DDS participant supplied
            RetryUnresolvedOwners(cmd, view);
            using var loan = _reader.Take();
            int index = -1;
            foreach (var sample in loan)
            {
                index++;
                var info = sample.Info;
                if (info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                {
                    // Disposed instance → request teardown.
                    // NOTE: dispose notifications have IsValid == false (no data payload,
                    // only key fields are valid). Must check instance state BEFORE IsValid.
                    var keyData = DdsTypeSupport.FromNative<EntityMaster>(sample.NativePtr);
                    ProcessDispose(keyData.EntityId);
                    continue;
                }

                // For alive instances, skip samples with no valid data payload.
                if (!sample.IsValid)
                    continue;

                ReceivedSampleCount++;
                var master = sample.Data;
                int writer = loan.GetSender(index) is { } id ? id.AppInstanceId : UnknownOwner;
                ProcessSample(in master, cmd, view, writer, info.PublicationHandle);
            }
        }

        // ── Egress (ingress-only translator — nothing to publish) ────────────

        public void ScanAndPublish(ISimulationView view) { }

        // ── Ghost promotion helper ────────────────────────────────────────────

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Dispose(long networkEntityId) { /* IG does not write EntityMaster */ }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Publishes a <see cref="DestroyEntityCommand"/> for a disposed DDS instance.
        /// Extracted as <c>internal</c> so tests can verify the teardown path without DDS.
        /// </summary>
        internal void ProcessDispose(long networkEntityId)
        {
            _ownerUnresolved.Remove(networkEntityId);
            _eventBus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = networkEntityId,
                Reason = "EntityMaster disposed",
                IsRemote = true,
            });
        }

        private const int UnknownOwner = -1;

        /// <param name="writerNodeId">CE-517: the node that wrote this sample (sender identity), or -1 when unknown.</param>
        /// <param name="publicationHandle">The writer's handle, to resolve an unknown writer on a later poll.</param>
        internal void ProcessSample(in EntityMaster master, IEntityCommandBuffer cmd, ISimulationView view,
                                    int writerNodeId = UnknownOwner, long publicationHandle = 0)
        {
            long netId = master.EntityId;

            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<EntityMasterIngressTranslator>.Warn(
                    "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                    return;
                }

                FdpLog<EntityMasterIngressTranslator>.Debug(
                    "[Node-{0}] Ingress: EntityMaster NetID={1} -> Ghost spawn", _localNodeId, master.EntityId);
                entity = _ghostCreationSystem.CreateGhost(repo, netId, view.Tick);
            }

            // Permanent identity component — drives GhostPromotionSystem.
            cmd.AddComponent(entity, new TkbIdentity { TkbType = master.TkbType });

            // Only set NetworkAuthority for entities that do not have it yet (new ghosts).
            // Entities that the local node created itself already have NetworkAuthority with the
            // correct PrimaryOwnerId. DDS loopback or re-announcements of locally-owned entities
            // must not overwrite it with the unknown-owner sentinel (-1), which would silently
            // clear authority and prevent the egress translators from publishing.
            if (!view.HasComponent<NetworkAuthority>(entity))
            {
                cmd.AddComponent(entity, new NetworkAuthority
                {
                    PrimaryOwnerId = writerNodeId,      // CE-517: the writer; -1 until its identity resolves
                    LocalNodeId = (int)_localNodeId
                });
                if (writerNodeId == UnknownOwner) RememberUnresolved(netId, publicationHandle);

                // Reliable-init barrier (CE-283, §3a.2): the creator marked this entity WaitForAcks,
                // so this peer must report its Active status once the ghost finishes local
                // construction. Tag only genuine remote ghosts (the branch that creates the
                // ghost's NetworkAuthority), never a locally-owned entity seen via DDS loopback.
                if ((master.Flags & (ulong)EntityMasterFlags.WaitForAcks) != 0
                    && !view.HasComponent<ReportLifecycleOnActive>(entity))
                {
                    cmd.AddComponent(entity, new ReportLifecycleOnActive());
                }
            }
            else if (view.GetComponentRO<NetworkAuthority>(entity).PrimaryOwnerId == UnknownOwner)
            {
                // CE-517: an owner still unknown is filled from the writer; a known one is never overwritten (see class doc).
                if (writerNodeId != UnknownOwner)
                {
                    cmd.SetComponent(entity, new NetworkAuthority { PrimaryOwnerId = writerNodeId, LocalNodeId = (int)_localNodeId });
                    _ownerUnresolved.Remove(netId);
                }
                else RememberUnresolved(netId, publicationHandle);
            }

            // Reconstruct DISEntityType.Value from the 8 named DisTypeStruct fields.
            // FieldOffset layout (little-endian): Extra[0], Specific[1], Subcategory[2],
            // Category[3], Country[4-5], Domain[6], Kind[7].
            ulong disValue
                = ((ulong)master.DisType.Kind        << 56)
                | ((ulong)master.DisType.Domain      << 48)
                | ((ulong)master.DisType.Country     << 32)
                | ((ulong)master.DisType.Category    << 24)
                | ((ulong)master.DisType.Subcategory << 16)
                | ((ulong)master.DisType.Specific    <<  8)
                |  (ulong)master.DisType.Extra;

            // Store DIS entity type natively in the entity header.
            if (view is EntityRepository repoForDis)
                repoForDis.SetDisType(entity, new DISEntityType { Value = disValue });
        }

        /// <summary>Test seam: stands in for the sender registry (a live participant cannot force a late identity).</summary>
        internal Func<long, int?>? WriterResolverForTests { set => _resolveWriter = value; }

        private void RememberUnresolved(long netId, long publicationHandle)
        {
            if (_resolveWriter != null && publicationHandle != 0) _ownerUnresolved[netId] = publicationHandle;
        }

        /// <summary>CE-517: a ghost created before its writer's identity arrived learns its owner once it does.</summary>
        internal void RetryUnresolvedOwners(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_resolveWriter == null || _ownerUnresolved.Count == 0) return;
            _resolvedScratch.Clear();
            foreach (var (netId, handle) in _ownerUnresolved)
            {
                if (!_entityMap.TryGetEntity(netId, out var entity)) { _resolvedScratch.Add(netId); continue; }
                if (!view.HasComponent<NetworkAuthority>(entity)) continue;     // its ghost's record lands next playback
                if (_resolveWriter(handle) is not int writer) continue;
                _resolvedScratch.Add(netId);
                if (view.GetComponentRO<NetworkAuthority>(entity).PrimaryOwnerId == UnknownOwner)
                    cmd.SetComponent(entity, new NetworkAuthority { PrimaryOwnerId = writer, LocalNodeId = (int)_localNodeId });
            }
            foreach (long netId in _resolvedScratch) _ownerUnresolved.Remove(netId);
        }
    }
}
