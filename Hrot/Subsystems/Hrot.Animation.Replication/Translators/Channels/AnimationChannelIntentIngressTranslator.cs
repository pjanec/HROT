using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.Channels;

/// <summary>
/// Ingress translator: receives <see cref="DdsAnimationChannelIntent"/> from DDS
/// and updates only the Brain-authored intent fields on the Muscle ghost's
/// <see cref="AnimationChannel"/> component.
/// Muscle-local fields (DispatchedInstanceId, Status, State) are preserved via read-modify-write.
/// </summary>
internal sealed class AnimationChannelIntentIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/intent/AnimationChannel";

    private readonly DdsReader<DdsAnimationChannelIntent>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal AnimationChannelIntentIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        // participant may be null in unit-test mode.
        _reader = participant is not null
            ? new DdsReader<DdsAnimationChannelIntent>(participant, TopicNameConst)
            : null;
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (_reader is null) return; // test mode — use ProcessSample directly

        using var loan = _reader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            ProcessSample(sample.Data, cmd, view);
        }
    }

    /// <summary>
    /// Updates only the intent fields; preserves Muscle-local fields via read-modify-write.
    /// Exposed internal for unit testing without a live DDS reader.
    /// </summary>
    internal unsafe void ProcessSample(
        in DdsAnimationChannelIntent msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        // Read existing component to preserve Muscle-local fields.
        AnimationChannel updated = view.HasComponent<AnimationChannel>(entity)
            ? view.GetComponentRO<AnimationChannel>(entity)
            : default;

        // Overwrite only Brain-authored intent fields.
        updated.ActiveAction = msg.ActiveAction;
        updated.ActionInstanceId = msg.ActionInstanceId;
        updated.BehaviorInstanceId = msg.BehaviorInstanceId;

        // Copy ActionParams (32 bytes).
        var m = msg;
        AnimationChannel* pUpd = &updated;
        DdsAnimationChannelIntent* pMsg = &m;
        Buffer.MemoryCopy(pMsg->ActionParams, pUpd->Params, 32, 32);

        // DispatchedInstanceId, Status, State are NOT touched.
        cmd.SetComponent(entity, updated);
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
