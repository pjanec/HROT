using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.SideBuffers;

/// <summary>
/// Ingress translator: receives <see cref="DdsMontageQueue"/> from DDS
/// and updates the Muscle ghost's <see cref="AnimationMontageQueue"/> component.
/// Writes Count valid entries and zeros the remainder.
/// Does NOT touch ObservedQueueVersion (Muscle-internal executor state).
/// </summary>
internal sealed class AnimationMontageQueueIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/MontageQueue";

    private readonly DdsReader<DdsMontageQueue>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal AnimationMontageQueueIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsMontageQueue>(participant, TopicNameConst)
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
    /// Updates QueueVersion and Count; writes Count entry bytes and zeros the tail.
    /// Exposed internal for unit testing.
    /// </summary>
    internal unsafe void ProcessSample(
        in DdsMontageQueue msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        // Preserve ObservedQueueVersion (Muscle executor state) if present.
        AnimationMontageQueue updated = view.HasComponent<AnimationMontageQueue>(entity)
            ? view.GetComponentRO<AnimationMontageQueue>(entity)
            : default;

        updated.QueueVersion = msg.QueueVersion;
        byte count = msg.Count > 8 ? (byte)8 : msg.Count;
        updated.Count = count;

        var m = msg;
        AnimationMontageQueue* pUpd = &updated;
        DdsMontageQueue* pMsg = &m;

        int entrySize = System.Runtime.InteropServices.Marshal.SizeOf<MontageQueueEntry>();
        int validBytes = count * entrySize;
        int tailBytes = 128 - validBytes;

        // Copy valid entries from wire message.
        Buffer.MemoryCopy(pMsg->EntriesData, pUpd->EntriesData, 128, validBytes);
        // Zero the tail to avoid stale data.
        if (tailBytes > 0)
        {
            new System.Span<byte>(pUpd->EntriesData + validBytes, tailBytes).Clear();
        }

        cmd.SetComponent(entity, updated);
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
