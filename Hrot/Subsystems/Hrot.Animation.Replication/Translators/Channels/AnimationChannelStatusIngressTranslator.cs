using CycloneDDS.Runtime;
using Fbt;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.Channels;

/// <summary>
/// Ingress translator: receives <see cref="DdsAnimationChannelStatus"/> from DDS
/// and writes the Brain ghost's <see cref="AnimationChannelStatus"/> (CE-513 / R-180); the Brain's request component is not touched.
/// </summary>
internal sealed class AnimationChannelStatusIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/status/AnimationChannel";

    private readonly DdsReader<DdsAnimationChannelStatus>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal AnimationChannelStatusIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsAnimationChannelStatus>(participant, TopicNameConst)
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

    /// <summary>
    /// Updates only the Muscle-authored status fields; preserves Brain-local intent fields.
    /// Exposed internal for unit testing.
    /// </summary>
    internal void ProcessSample(
        in DdsAnimationChannelStatus msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        // ⭐ CE-513 / R-180 — the report is its own component; the Brain's request is not touched.
        cmd.SetComponent(entity, new AnimationChannelStatus
        {
            Status = (NodeStatus)msg.Status,
            DispatchedInstanceId = msg.DispatchedInstanceId,
        });
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
