using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Animation.Replication.Translators.Descriptors;

/// <summary>
/// Ingress translator: receives <see cref="DdsStanceStatus"/> from DDS
/// and updates the Brain ghost's <see cref="StanceStatus"/> component.
/// All fields are Muscle-authored so the full component is replaced.
/// </summary>
internal sealed class StanceStatusIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/StanceStatus";

    private readonly DdsReader<DdsStanceStatus>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal StanceStatusIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsStanceStatus>(participant, TopicNameConst)
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
        in DdsStanceStatus msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        cmd.SetComponent(entity, new StanceStatus
        {
            CurrentStance = (StanceId)msg.CurrentStance,
            Phase = (StanceTransitionPhase)msg.Phase,
            TransitionProgress = msg.TransitionProgress,
            AckVersion = msg.AckVersion,
        });
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
