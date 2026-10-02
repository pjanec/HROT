using System;
using System.Globalization;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// ⭐ <b><c>CE-484</c> — a behaviour fault leaves the node that raised it.</b> 📄
    /// <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §4c W4.
    ///
    /// <para>On the <c>WeaponFireNotificationEgressTranslator</c> precedent: drains the local
    /// <see cref="BehaviorFaultNotification"/> events and writes one <see cref="BehaviorFaultReport"/> per fault, with the
    /// entity's network id and the behaviour's registered name. Installed by <c>CognitiveTranslatorPack</c> (Brain /
    /// AllInOne — where behaviours run). A fault on an entity with no network id is skipped: it never left the node, and the
    /// local tab already shows it (<see cref="BehaviorFault.Raise"/>).</para>
    /// </summary>
    public sealed class BehaviorFaultEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "BehaviorFault";

        private readonly IDdsWriter<BehaviorFaultReport> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly BehaviorRegistry? _behaviorRegistry;
        private readonly long _localNodeId;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtBehaviorFault;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public BehaviorFaultEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap,
                                             BehaviorRegistry? behaviorRegistry, long localNodeId)
            : this(new DdsWriterAdapter<BehaviorFaultReport>(participant, DdsTopicName), entityMap, behaviorRegistry, localNodeId)
        {
        }

        /// <summary>Testable constructor — accepts an injected writer.</summary>
        internal BehaviorFaultEgressTranslator(IDdsWriter<BehaviorFaultReport> writer, NetworkEntityMap entityMap,
                                               BehaviorRegistry? behaviorRegistry, long localNodeId)
        {
            _writer           = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap        = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _behaviorRegistry = behaviorRegistry;
            _localNodeId      = localNodeId;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            foreach (var fault in view.ReadManagedEvents<BehaviorFaultNotification>())
            {
                if (!_entityMap.TryGetNetworkId(fault.Entity, out long netId)) continue;
                _writer.Write(new BehaviorFaultReport
                {
                    EntityId     = netId,
                    OriginNodeId = _localNodeId,
                    BehaviorName = NameOf(_behaviorRegistry, fault.BehaviorHash),
                    BehaviorHash = fault.BehaviorHash,
                    InstanceId   = fault.InstanceId,
                    Code         = (int)fault.Code,
                    Message      = fault.Message ?? string.Empty,
                    SimTime      = fault.SimTime,
                });
                SentSampleCount++;
            }
        }

        /// <summary>The behaviour's registered name, else its hash as <c>#XXXXXXXX</c>.</summary>
        internal static string NameOf(BehaviorRegistry? registry, int hash)
            => registry != null && registry.TryGetName(hash, out var name) ? name
               : "#" + hash.ToString("X8", CultureInfo.InvariantCulture);

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// ⭐ <b><c>CE-484</c> — a behaviour fault raised on another node reaches this node's operator.</b> 📄
    /// <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §4c W5.
    ///
    /// <para>Turns each <see cref="BehaviorFaultReport"/> into a row of <see cref="BehaviorFaultLog.Shared"/> — the Message
    /// Log's "Behaviour faults" tab. Installed by <c>SharedTranslatorPack</c> (every role: any node may host an operator
    /// window). Skips samples this node published (its own <see cref="BehaviorFault.Raise"/> already reported them);
    /// <see cref="BehaviorFaultLog.Report"/> drops the remaining duplicates (several nodes in one process).</para>
    /// </summary>
    public sealed class BehaviorFaultIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "BehaviorFault";

        private readonly DdsReader<BehaviorFaultReport>? _reader;
        private readonly BehaviorFaultLog _log;
        private readonly long _localNodeId;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtBehaviorFault;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>Production constructor. A <c>null</c> participant makes <see cref="PollIngress"/> a no-op (tests).</summary>
        public BehaviorFaultIngressTranslator(DdsParticipant? participant, long localNodeId, BehaviorFaultLog? log = null)
        {
            _reader      = participant is not null ? new DdsReader<BehaviorFaultReport>(participant, DdsTopicName) : null;
            _localNodeId = localNodeId;
            _log         = log ?? BehaviorFaultLog.Shared;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                ProcessSample(in data);
            }
        }

        /// <summary>One received report. Returns true when it added a row. Exposed for tests (no DDS stack needed).</summary>
        internal bool ProcessSample(in BehaviorFaultReport report)
        {
            ReceivedSampleCount++;
            if (report.OriginNodeId == _localNodeId) return false;   // our own fault — Raise already reported it
            return _log.Report(
                new BehaviorFaultLog.FaultKey(report.EntityId, report.InstanceId, report.BehaviorHash),
                string.IsNullOrEmpty(report.BehaviorName) ? "#" + report.BehaviorHash.ToString("X8", CultureInfo.InvariantCulture)
                                                          : report.BehaviorName,
                $"entity {report.EntityId} (node {report.OriginNodeId})",
                report.Code,
                report.Message ?? string.Empty);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
