using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.Channels;

/// <summary>
/// Egress translator: reads <see cref="LookAtChannelStatus"/> (the Muscle's report, CE-513 / R-180) from locally-owned entities
/// and publishes DDS status samples (Muscle -> Brain direction).
/// Only publishes when (Status, DispatchedInstanceId) changes.
/// </summary>
internal sealed class LookAtChannelStatusEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/status/LookAtChannel";

    private readonly IAnimDdsWriter<DdsLookAtChannelStatus> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, (NodeStatus, uint)> _lastPublished = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    internal LookAtChannelStatusEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsLookAtChannelStatus>(participant, TopicNameConst), entityMap)
    {
    }

    internal LookAtChannelStatusEgressTranslator(
        IAnimDdsWriter<DdsLookAtChannelStatus> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<LookAtChannelStatus>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var report = ref view.GetComponentRO<LookAtChannelStatus>(entity);

            if (_lastPublished.TryGetValue(entity, out var last)
                && last.Item1 == report.Status
                && last.Item2 == report.DispatchedInstanceId)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            _writer.Write(new DdsLookAtChannelStatus
            {
                EntityId = netId.Value,
                Status = (byte)report.Status,
                DispatchedInstanceId = report.DispatchedInstanceId,
            });
            SentSampleCount++;
            _lastPublished[entity] = (report.Status, report.DispatchedInstanceId);
        }
    }
}
