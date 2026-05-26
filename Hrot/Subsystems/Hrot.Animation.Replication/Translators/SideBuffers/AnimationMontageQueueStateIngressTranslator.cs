using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.SideBuffers;

/// <summary>
/// Ingress translator: receives <see cref="DdsMontageQueueState"/> from DDS
/// and updates the Brain ghost's <see cref="AnimationMontageQueueState"/> component.
/// Does NOT replicate ObservedQueueVersion (Muscle executor internal).
/// </summary>
internal sealed class AnimationMontageQueueStateIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/MontageQueueState";

    private readonly DdsReader<DdsMontageQueueState>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal AnimationMontageQueueStateIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsMontageQueueState>(participant, TopicNameConst)
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
    /// Sets CurrentEntryIndex, InBlendOutWindow, EntryElapsedSeconds.
    /// Preserves ObservedQueueVersion (not replicated).
    /// Exposed internal for unit testing.
    /// </summary>
    internal void ProcessSample(
        in DdsMontageQueueState msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        // Preserve ObservedQueueVersion (Muscle executor internal).
        AnimationMontageQueueState updated = view.HasComponent<AnimationMontageQueueState>(entity)
            ? view.GetComponentRO<AnimationMontageQueueState>(entity)
            : default;

        updated.CurrentEntryIndex = msg.CurrentEntryIndex;
        updated.InBlendOutWindow = msg.InBlendOutWindow != 0;
        updated.EntryElapsedSeconds = msg.EntryElapsedSeconds;
        // ObservedQueueVersion is NOT overwritten.

        cmd.SetComponent(entity, updated);
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
