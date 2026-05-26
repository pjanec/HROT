using CycloneDDS.Runtime;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.Channels;

/// <summary>
/// Ingress translator: receives <see cref="DdsLookAtChannelStatus"/> from DDS
/// and updates only the Muscle-authored status fields on the Brain ghost's
/// <see cref="LookAtChannel"/> component.
/// </summary>
internal sealed class LookAtChannelStatusIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/status/LookAtChannel";

    private readonly DdsReader<DdsLookAtChannelStatus>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal LookAtChannelStatusIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsLookAtChannelStatus>(participant, TopicNameConst)
            : null;
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (_reader is null) return;

        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            ProcessSample(sample.Data, cmd, view);
        }
    }

    internal void ProcessSample(
        in DdsLookAtChannelStatus msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        LookAtChannel updated = view.HasComponent<LookAtChannel>(entity)
            ? view.GetComponentRO<LookAtChannel>(entity)
            : default;

        updated.Status = (NodeStatus)msg.Status;
        updated.DispatchedInstanceId = msg.DispatchedInstanceId;

        cmd.SetComponent(entity, updated);
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
