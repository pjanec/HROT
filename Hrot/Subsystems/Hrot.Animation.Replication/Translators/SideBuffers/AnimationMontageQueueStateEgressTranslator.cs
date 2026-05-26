using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.SideBuffers;

/// <summary>
/// Egress translator: publishes <see cref="AnimationMontageQueueState"/> to DDS (Muscle -> Brain).
/// Dirty trigger: (CurrentEntryIndex, InBlendOutWindow).
/// EntryElapsedSeconds rides in the payload but does NOT drive the dirty trigger.
/// ObservedQueueVersion is NOT replicated (Muscle executor internal state).
/// </summary>
internal sealed class AnimationMontageQueueStateEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/MontageQueueState";

    private readonly IAnimDdsWriter<DdsMontageQueueState> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, (byte, bool)> _lastPublished = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    internal AnimationMontageQueueStateEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsMontageQueueState>(participant, TopicNameConst), entityMap)
    {
    }

    internal AnimationMontageQueueStateEgressTranslator(
        IAnimDdsWriter<DdsMontageQueueState> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<AnimationMontageQueueState>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var state = ref view.GetComponentRO<AnimationMontageQueueState>(entity);

            // CRITICAL: EntryElapsedSeconds does NOT drive dirty trigger.
            if (_lastPublished.TryGetValue(entity, out var last)
                && last.Item1 == state.CurrentEntryIndex
                && last.Item2 == state.InBlendOutWindow)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            _writer.Write(new DdsMontageQueueState
            {
                EntityId = netId.Value,
                CurrentEntryIndex = state.CurrentEntryIndex,
                InBlendOutWindow = state.InBlendOutWindow ? (byte)1 : (byte)0,
                EntryElapsedSeconds = state.EntryElapsedSeconds,
            });
            SentSampleCount++;
            _lastPublished[entity] = (state.CurrentEntryIndex, state.InBlendOutWindow);
        }
    }
}
