using System;
using System.Runtime.InteropServices;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

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
    /// For already-known entities the translator does not emit any ECS component updates.
    ///
    /// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class EntityMasterIngressTranslator : IDescriptorTranslator
    {
        // --- Named constants (§CODE-STANDARDS §1 — no magic numbers) ---
        private const string DdsTopicName = "EntityMaster";
        private const long OrdinalValue = -2; // distinct from the FDP SST_EntityMaster ordinal (-1)

        private readonly DdsReader<EntityMaster> _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        public EntityMasterIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            FdpEventBus eventBus,
            GhostCreationSystem ghostCreationSystem)
        {
            // participant may be null in unit-test mode — PollIngress becomes a no-op
            _reader = participant is not null ? new DdsReader<EntityMaster>(participant) : null!;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return; // test mode — no DDS participant supplied
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
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

                var master = sample.Data;
                ProcessSample(in master, cmd, view);
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
            _eventBus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = networkEntityId,
                Reason = "EntityMaster disposed"
            });
        }

        internal void ProcessSample(in EntityMaster master, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = master.EntityId;

            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<EntityMasterIngressTranslator>.Warn(
                        "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                    return;
                }

                FdpLog<EntityMasterIngressTranslator>.Debug(
                    "[TRACE-IG] Ingress: EntityMaster NetID={0} -> Ghost spawn", master.EntityId);

                entity = _ghostCreationSystem.CreateGhost(repo, netId, view.Tick);
            }

            // Permanent identity component — drives GhostPromotionSystem.
            cmd.AddComponent(entity, new TkbIdentity { TkbType = master.TkbType });

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
    }
}
