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
    // Tracks the ActionParams blob per entity (4 x ulong covers the 32-byte fixed array).
    private readonly Dictionary<Entity, (ulong p0, ulong p1, ulong p2, ulong p3)> _lastPublishedActionParams = new();

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

            // Fine-grained dirty filter: only publish when ActionInstanceId or ActionParams blob changes.
            var ch = channel; // local copy so we can safely take pointer
            (ulong p0, ulong p1, ulong p2, ulong p3) currentParams;
            unsafe
            {
                // ch is a local value-type variable (stack-allocated); no fixed statement needed.
                ulong* u = (ulong*)ch.Params;
                currentParams = (u[0], u[1], u[2], u[3]);
            }
            if (_lastPublishedActionInstanceId.TryGetValue(entity, out uint lastId)
                && lastId == channel.ActionInstanceId
                && _lastPublishedActionParams.TryGetValue(entity, out var lastParams)
                && lastParams == currentParams)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

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
            _lastPublishedActionParams[entity] = currentParams;
        }
    }
}
