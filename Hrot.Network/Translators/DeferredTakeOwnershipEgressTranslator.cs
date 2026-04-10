using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning.Events;
using Hrot.NED.Messages;
using ModuleHost.Core.Abstractions;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// Egress-only translator that bridges the <see cref="DeferredTakeOwnershipCommand"/>
    /// event-bus event to the <c>DeferredTakeOwnership</c> DDS topic.
    ///
    /// <para>
    /// This translator is installed on the Brain (CGF) node only.  It polls the bus
    /// during the <c>Export</c> phase and publishes one <see cref="DeferredTakeOwnership"/>
    /// sample per pending command, satisfying <b>Rule 1</b>: the pre-genesis routing table
    /// always hits the wire <em>before</em> <c>EntityMaster</c> because the translator
    /// array places this before <see cref="Hrot.Map.Common.Replication.Egress.EntityMasterEgressTranslator"/>.
    /// </para>
    ///
    /// <para>
    /// Each command carries an unbounded list of (descriptorTypeId, nodeId) grants,
    /// so one DDS sample covers the entire cluster's routing table for a single entity.
    /// </para>
    /// </summary>
    public sealed class DeferredTakeOwnershipEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<DeferredTakeOwnership>? _writer;

        public string TopicName         => "DeferredTakeOwnership";
        public long   DescriptorOrdinal => -3; // Out-of-band — not a standard descriptor ordinal.

        public DeferredTakeOwnershipEgressTranslator(DdsParticipant? participant)
        {
            _writer = participant != null
                ? new DdsWriter<DeferredTakeOwnership>(participant, "DeferredTakeOwnership")
                : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { /* egress-only */ }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer == null) return;

            foreach (var cmd in view.ConsumeManagedEvents<DeferredTakeOwnershipCommand>())
            {
                if (cmd.Grants.Count == 0) continue;

                // Convert DescriptorGrant (bus-layer) → DescriptorOwnerEntry (wire-layer).
                var wireGrants = new List<DescriptorOwnerEntry>(cmd.Grants.Count);
                foreach (var g in cmd.Grants)
                    wireGrants.Add(new DescriptorOwnerEntry { DescriptorTypeId = g.DescriptorTypeId, NodeId = g.NodeId });

                _writer.Write(new DeferredTakeOwnership
                {
                    EntityId = cmd.NetworkId,
                    Grants   = wireGrants,
                });

                FdpLog<DeferredTakeOwnershipEgressTranslator>.Debug(
                    "[CGF] DeferredTakeOwnership egress: EntityId={0} Grants={1}",
                    cmd.NetworkId, cmd.Grants.Count);
            }
        }

        public void ApplyToEntity(Entity entity, object data, Fdp.Kernel.EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}
