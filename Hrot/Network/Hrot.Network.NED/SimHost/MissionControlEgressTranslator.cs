using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Events;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ Edit framework F6 (📄 docs/UX/DESIGN_Edit_Framework.md §3, CE-4277) — a mission plan commit (a local <see cref="MissionControlIntent"/>) for a unit
    /// whose BRAIN another node owns goes to that node as a <see cref="MissionControlRequest"/> (its ingress on the brain republishes it, marked remote). The
    /// SAME rule as <c>TacticalIntentEgressTranslator</c>: on every node, forward exactly what this node does not own. ⛔ Was: processed locally, writing the
    /// replica's plan (no egress existed). The owner's answer comes back as <c>EntityEditOutcome</c> (and its <c>MissionControlAck</c>).
    /// </summary>
    public sealed class MissionControlEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<MissionControlRequest> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<MissionControlRequest>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtMissionControlRequest;
        public long ReceivedSampleCount => 0;
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public MissionControlEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<MissionControlRequest>(participant), entityMap) { }

        internal MissionControlEgressTranslator(IDdsWriter<MissionControlRequest> writer, NetworkEntityMap entityMap)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegisteredManaged<MissionControlIntent>()) return;
            foreach (var intent in repo.ReadManagedEvents<MissionControlIntent>())
            {
                if (intent is null || intent.IsRemote) continue;
                if (!_entityMap.TryGetEntity(intent.TargetEntityId, out var entity) || !repo.IsAlive(entity)) continue;
                if (Hrot.Common.Systems.MissionControlExecutionSystem.OwnsBrain(repo, entity)) continue;   // applied here
                _writer.Write(ToRequest(intent));
                SentSampleCount++;
            }
        }

        /// <summary>The neutral intent as the wire request (the ONE mapping the gateway uses, <c>NedTranslationHelper</c>), keeping the user's request id.</summary>
        internal static MissionControlRequest ToRequest(MissionControlIntent intent)
        {
            var request = Hrot.Network.NED.ExCon.NedTranslationHelper.ToMissionControlRequest(new Hrot.Core.Network.MissionControlCommand
            {
                EntityId = intent.TargetEntityId,
                CommandType = intent.Payload.CommandType,
                Plan = intent.Payload.FullMissionData,
                TaskId = intent.Payload.TargetTaskId,
                BaseVersion = intent.BaseVersion,
            });
            request.RequestId = intent.RequestId;
            return request;
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
