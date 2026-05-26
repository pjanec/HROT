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
/// Egress translator: reads <see cref="LookAtChannel"/> from locally-owned entities
/// and publishes DDS intent samples (Brain -> Muscle direction).
/// Only publishes when <see cref="LookAtChannel.ActionInstanceId"/> changes.
/// Does NOT replicate Muscle-authored fields (DispatchedInstanceId, Status, State).
/// </summary>
internal sealed class LookAtChannelIntentEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/intent/LookAtChannel";

    private readonly IAnimDdsWriter<DdsLookAtChannelIntent> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, uint> _lastPublishedActionInstanceId = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    internal LookAtChannelIntentEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsLookAtChannelIntent>(participant, TopicNameConst), entityMap)
    {
    }

    internal LookAtChannelIntentEgressTranslator(
        IAnimDdsWriter<DdsLookAtChannelIntent> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public unsafe void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<LookAtChannel>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var channel = ref view.GetComponentRO<LookAtChannel>(entity);

            if (_lastPublishedActionInstanceId.TryGetValue(entity, out uint lastId)
                && lastId == channel.ActionInstanceId)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            var ch = channel;
            var msg = new DdsLookAtChannelIntent
            {
                EntityId = netId.Value,
                ActiveAction = ch.ActiveAction,
                ActionInstanceId = ch.ActionInstanceId,
                BehaviorInstanceId = ch.BehaviorInstanceId,
            };
            LookAtChannel* pCh = &ch;
            DdsLookAtChannelIntent* pMsg = &msg;
            Buffer.MemoryCopy(pCh->Params, pMsg->ActionParams, 32, 32);

            _writer.Write(msg);
            SentSampleCount++;
            _lastPublishedActionInstanceId[entity] = ch.ActionInstanceId;
        }
    }
}
