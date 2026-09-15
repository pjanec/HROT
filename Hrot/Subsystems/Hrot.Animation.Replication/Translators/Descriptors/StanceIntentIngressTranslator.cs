using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Animation.Replication.Translators.Descriptors;

/// <summary>
/// Ingress translator: receives <see cref="DdsStanceIntent"/> from DDS
/// and updates the Muscle ghost's <see cref="StanceIntent"/> component.
/// All fields are Brain-authored so the full component is replaced.
/// </summary>
internal sealed class StanceIntentIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/StanceIntent";

    private readonly DdsReader<DdsStanceIntent>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal StanceIntentIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsStanceIntent>(participant, TopicNameConst)
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
        in DdsStanceIntent msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        cmd.SetComponent(entity, new StanceIntent
        {
            TargetStance = (StanceId)msg.TargetStance,
            BlendTime = msg.BlendTime,
            Version = msg.Version,
        });
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
