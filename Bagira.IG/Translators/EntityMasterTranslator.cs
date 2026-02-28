using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Bagira.BDC.SSTD;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network.Interfaces;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Ingress translator for the Bagira <c>EntityMaster</c> DDS topic.
    ///
    /// On receiving a new entity announcement it publishes a <see cref="SpawnEntityCommand"/>
    /// onto the <see cref="FdpEventBus"/> so the kernel-side <c>NetworkSpawningSystem</c>
    /// can drive the full ELM construction cycle.
    ///
    /// On disposal it publishes <see cref="DestroyEntityCommand"/> so the lifecycle module
    /// can tear the entity down cleanly.
    ///
    /// For already-known entities the translator does not emit any ECS component updates.
    ///
    /// IG is a ghost-only (read-only) node — <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class EntityMasterTranslator : IDescriptorTranslator
    {
        // --- Named constants (§CODE-STANDARDS §1 — no magic numbers) ---
        private const string DdsTopicName   = "EntityMaster";
        private const long   OrdinalValue   = -2; // distinct from the FDP SST_EntityMaster ordinal (-1)

        private readonly DdsReader<EntityMaster> _reader;
        private readonly NetworkEntityMap        _entityMap;
        private readonly FdpEventBus             _eventBus;

        public string TopicName       => DdsTopicName;
        public long   DescriptorOrdinal => OrdinalValue;

        public EntityMasterTranslator(
            DdsParticipant?  participant,
            NetworkEntityMap entityMap,
            FdpEventBus      eventBus)
        {
            // participant may be null in unit-test mode — PollIngress becomes a no-op
            _reader    = participant is not null ? new DdsReader<EntityMaster>(participant) : null!;
            _entityMap = entityMap  ?? throw new ArgumentNullException(nameof(entityMap));
            _eventBus  = eventBus   ?? throw new ArgumentNullException(nameof(eventBus));
        }

        // ── Ingress ──────────────────────────────────────────────────────────

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return; // test mode — no DDS participant supplied
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                var info = sample.Info;
                if (info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                {
                    // Disposed instance → request teardown
                    var keyData = DdsTypeSupport.FromNative<EntityMaster>(sample.NativePtr);
                    ProcessDispose(keyData.EntityId);
                    continue;
                }

                var master = sample.Data;
                ProcessSample(in master, cmd, view);
            }
        }

        // ── Egress (IG is ghost-only — nothing to publish) ───────────────────

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
                Reason    = "EntityMaster disposed"
            });
        }

        internal void ProcessSample(in EntityMaster master, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = master.EntityId;

            if (!_entityMap.TryGetEntity(netId, out _))
            {
                    FdpLog<EntityMasterTranslator>.Debug(
                        "[TRACE-IG] Ingress: EntityMaster NetID={0} -> Ghost spawn", master.EntityId);

                // New remote entity — request creation through NetworkSpawningSystem
                // InitType = None: IG is a ghost replica, not an authority node.
                // OwnerNodeId = 0: remote / no local authority — prevents IG from
                // claiming HasAuthority = true on replicated entities (TASK-IF004).
                _eventBus.PublishManaged(new SpawnEntityCommand
                {
                    NetworkId         = netId,
                    TkbType           = master.TkbType,
                    DisType           = master.DisType,
                    OwnerNodeId       = 0,
                    InitType          = ReliableInitType.None,
                    InitialComponents = new List<object>(),
                    RequestId         = Guid.Empty
                });
            }
        }
    }
}
