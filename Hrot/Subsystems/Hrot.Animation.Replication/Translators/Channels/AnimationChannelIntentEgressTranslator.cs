using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.Channels;

/// <summary>
/// Egress translator: reads <see cref="AnimationChannel"/> from locally-owned entities
/// and publishes DDS intent samples (Brain -> Muscle direction).
/// Only publishes when <see cref="AnimationChannel.ActionInstanceId"/> changes.
/// Does NOT replicate Muscle-authored fields (DispatchedInstanceId, Status, State).
/// </summary>
internal sealed class AnimationChannelIntentEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/intent/AnimationChannel";

    private readonly IAnimDdsWriter<DdsAnimationChannelIntent> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, uint> _lastPublishedActionInstanceId = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    /// <summary>
    /// Incremented when an entity was checked for changes but the fine-grained
    /// filter determined ActionInstanceId had not changed (no publish occurred).
    /// </summary>
    internal long DirtyFalsePositiveCount { get; private set; }

    internal AnimationChannelIntentEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsAnimationChannelIntent>(participant, TopicNameConst), entityMap)
    {
    }

    internal AnimationChannelIntentEgressTranslator(
        IAnimDdsWriter<DdsAnimationChannelIntent> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public unsafe void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<AnimationChannel>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var channel = ref view.GetComponentRO<AnimationChannel>(entity);

            // Fine-grained dirty filter: only publish when ActionInstanceId changes.
            if (_lastPublishedActionInstanceId.TryGetValue(entity, out uint lastId)
                && lastId == channel.ActionInstanceId)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            // Build wire message — only Brain-authored intent fields.
            var ch = channel; // local copy so we can safely take pointer
            var msg = new DdsAnimationChannelIntent
            {
                EntityId = netId.Value,
                ActiveAction = ch.ActiveAction,
                ActionInstanceId = ch.ActionInstanceId,
                BehaviorInstanceId = ch.BehaviorInstanceId,
            };
            AnimationChannel* pCh = &ch;
            DdsAnimationChannelIntent* pMsg = &msg;
            Buffer.MemoryCopy(pCh->Params, pMsg->ActionParams, 32, 32);

            _writer.Write(msg);
            SentSampleCount++;
            _lastPublishedActionInstanceId[entity] = ch.ActionInstanceId;
        }
    }
}
