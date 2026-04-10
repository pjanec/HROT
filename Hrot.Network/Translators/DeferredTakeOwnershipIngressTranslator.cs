using System;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.NED.Messages;
using ModuleHost.Core.Abstractions;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// Ingress translator for the <c>DeferredTakeOwnership</c> DDS topic.
    ///
    /// <para>
    /// By placing this translator <em>first</em> in the <c>CycloneNetworkIngressSystem</c>
    /// array (before <c>EntityMasterIngressTranslator</c>), we guarantee that when both
    /// packets arrive in the same 16 ms frame, the intent is processed before the master
    /// — eliminating the "unowned creation" race condition (Rule 2: deterministic polling priority).
    /// </para>
    ///
    /// <para>
    /// Each <see cref="DeferredTakeOwnership"/> message carries an unbounded list of
    /// (descriptorTypeId, nodeId) grants covering the whole cluster.  This translator
    /// extracts only the entries whose <see cref="DescriptorOwnerEntry.NodeId"/> equals
    /// the local node ID and stores them in the <see cref="PendingAuthorityGrants"/> managed
    /// component on the ghost entity.
    /// </para>
    ///
    /// <para>This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.</para>
    /// </summary>
    public sealed class DeferredTakeOwnershipIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "DeferredTakeOwnership";

        private readonly DdsReader<DeferredTakeOwnership>? _reader;
        private readonly NetworkEntityMap                  _entityMap;
        private readonly GhostCreationSystem               _ghostCreationSystem;
        private readonly int                               _localNodeId;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => -4; // Out-of-band ordinal (must not clash with known descriptors).

        public DeferredTakeOwnershipIngressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            GhostCreationSystem  ghostCreationSystem,
            int                  localNodeId)
        {
            _reader              = participant != null
                ? new DdsReader<DeferredTakeOwnership>(participant, DdsTopicName)
                : null;
            _entityMap           = entityMap           ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem  ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _localNodeId         = localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ProcessSample(sample.Data, cmd, view);
            }
        }

        public void ScanAndPublish(ISimulationView view) { /* ingress-only */ }

        public void ApplyToEntity(Entity entity, object data, Fdp.Kernel.EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }

        // ── Private helpers ──────────────────────────────────────────────────────

        internal void ProcessSample(
            in DeferredTakeOwnership sample,
            IEntityCommandBuffer     cmd,
            ISimulationView          view)
        {
            if (sample.Grants == null || sample.Grants.Count == 0) return;

            long netId = sample.EntityId;
            var  repo  = view as Fdp.Kernel.EntityRepository;

            // Filter: does any grant in this message target us?
            bool hasGrantForUs = false;
            foreach (var grant in sample.Grants)
            {
                if (grant.NodeId == _localNodeId) { hasGrantForUs = true; break; }
            }
            if (!hasGrantForUs) return;

            Entity entity;
            if (!_entityMap.TryGetEntity(netId, out entity))
            {
                if (repo == null)
                {
                    FdpLog<DeferredTakeOwnershipIngressTranslator>.Warn(
                        "[Muscle] DeferredTakeOwnership: view is read-only, cannot create ghost for NetId={0}.", netId);
                    return;
                }

                // Ghost shell does not yet exist. Create it BEFORE EntityMaster arrives.
                // GhostPromotionSystem will not fire until EntityMaster arrives and attaches TkbIdentity.
                entity = _ghostCreationSystem.CreateGhost(repo, netId, view.Tick);
                FdpLog<DeferredTakeOwnershipIngressTranslator>.Debug(
                    "[Muscle] DeferredTakeOwnership: pre-genesis ghost created NetId={0} GrantCount={1}",
                    netId, sample.Grants.Count);
            }

            // Attach (or merge into existing) PendingAuthorityGrants managed component.
            PendingAuthorityGrants pending;
            bool alreadyHas = view.HasManagedComponent<PendingAuthorityGrants>(entity);
            if (alreadyHas)
            {
                pending = view.GetManagedComponentRO<PendingAuthorityGrants>(entity);
            }
            else
            {
                pending = new PendingAuthorityGrants { CreatorNodeId = _localNodeId };
            }

            foreach (var grant in sample.Grants)
            {
                if (grant.NodeId != _localNodeId) continue;
                pending.Merge(grant.DescriptorTypeId, grant.NodeId);
            }

            if (alreadyHas)
                cmd.SetManagedComponent(entity, pending);
            else
                cmd.AddManagedComponent(entity, pending);
        }
    }
}
