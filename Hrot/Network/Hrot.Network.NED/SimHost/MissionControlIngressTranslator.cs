using System;
using Fdp.Interfaces;
using Fdp.Kernel;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.NED.Descriptors;
using Hrot.Common.Events;
using Hrot.Core.Mission;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Abstractions;
using Fdp.Interfaces;

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
                    Payload        = MapToNeutralPayload(req.Payload),
                });
            }
        }

        private static MissionCommandPayload MapToNeutralPayload(MissionCommandUnion dds)
        {
            var payload = new MissionCommandPayload
            {
                CommandType  = (Hrot.Core.Mission.eMissionCommandType)(int)dds._d,
                TargetTaskId = dds._d == Hrot.NED.Messages.eMissionCommandType.CMD_JUMP_TO_TASK
                               ? dds.TargetTaskId
                               : Guid.Empty,
            };

            if (dds._d == Hrot.NED.Messages.eMissionCommandType.CMD_REPLACE_MISSION)
            {
                payload.FullMissionData = new Hrot.Core.Mission.MissionPlan
                {
                    ActiveTaskId = dds.FullMissionData.ActiveTaskId,
                    Tasks = dds.FullMissionData.Tasks?.ConvertAll(MapToNeutralTask) ?? new(),
                };
            }

            return payload;
        }

        private static Hrot.Core.Mission.MissionTask MapToNeutralTask(Hrot.NED.Descriptors.MissionTask dds)
            => new Hrot.Core.Mission.MissionTask
            {
                TaskId          = dds.TaskId,
                ExecutingEngine = dds.ExecutingEngine ?? string.Empty,
                BehaviorId      = dds.BehaviorId      ?? string.Empty,
                BehaviorParams  = dds.BehaviorParams  ?? string.Empty,
                State           = (Hrot.Core.Mission.eTaskState)(int)dds.State,
                Triggers        = dds.Triggers?.ConvertAll(t => new Hrot.Core.Mission.MissionTrigger
                                  { Type = t.Type ?? string.Empty, Params = t.Params ?? string.Empty })
                                  ?? new(),
            };

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
