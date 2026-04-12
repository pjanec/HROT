using System;
using Fdp.Interfaces;
using Fdp.Kernel;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Common.Events;
using CycloneDDS.Runtime;
using ModuleHost.Core.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Polls <see cref="MissionControlRequest"/> DDS samples and publishes
    /// <see cref="MissionControlIntent"/> events on the ECS bus for
    /// <c>MissionControlExecutionSystem</c> to process.
    ///
    /// <para>
    /// <b>Responsibilities (PACK-P001):</b>
    /// <list type="bullet">
    ///   <item>Reads DDS wire messages â€” the only class that does so for mission control.</item>
    ///   <item>Constructs a <see cref="MissionControlIntent"/> carrying the strongly-typed
    ///         <see cref="MissionCommandUnion"/> payload.</item>
    ///   <item>Uses <see cref="FdpEventBus.PublishManaged{T}"/> because
    ///         <see cref="MissionControlIntent"/> is a managed class (not unmanaged).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Not responsible for:</b> retry logic, entity resolution, version checking â€”
    /// all handled by <c>MissionControlExecutionSystem</c>.
    /// </para>
    /// </summary>
    public sealed class MissionControlIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MissionControlRequest";

        private readonly DdsReader<MissionControlRequest>? _reader;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => 90;

        /// <summary>Production constructor â€” creates a live DDS reader.</summary>
        public MissionControlIngressTranslator(DdsParticipant participant)
        {
            _reader = new DdsReader<MissionControlRequest>(participant);
        }

        /// <summary>
        /// Internal test constructor â€” accepts a pre-built reader stub so tests run
        /// without a live DDS stack.
        /// </summary>
        internal MissionControlIngressTranslator(DdsReader<MissionControlRequest> reader)
        {
            _reader = reader;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            var repo = view as EntityRepository;
            if (repo is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                var req = sample.Data;
                repo.Bus.PublishManaged(new MissionControlIntent
                {
                    RequestId      = req.RequestId,
                    TargetEntityId = req.TargetEntityId,
                    BaseVersion    = req.BaseVersion,
                    Payload        = req.Payload,
                });
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
