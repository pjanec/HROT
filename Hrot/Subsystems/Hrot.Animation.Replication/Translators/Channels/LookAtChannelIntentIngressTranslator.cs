using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.Animation.Replication.Translators.Channels;

/// <summary>
/// Ingress translator: receives <see cref="DdsLookAtChannelIntent"/> from DDS
/// and updates only the Brain-authored intent fields on the Muscle ghost's
/// <see cref="LookAtChannel"/> component.
/// Muscle-local fields (DispatchedInstanceId, Status, State) are preserved.
/// </summary>
internal sealed class LookAtChannelIntentIngressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/intent/LookAtChannel";

    private readonly DdsReader<DdsLookAtChannelIntent>? _reader;
    private readonly NetworkEntityMap _entityMap;

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Ingress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }

    internal LookAtChannelIntentIngressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
    {
        _reader = participant is not null
            ? new DdsReader<DdsLookAtChannelIntent>(participant, TopicNameConst)
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
    /// Updates only the intent fields; preserves Muscle-local fields via read-modify-write.
    /// For LookAtEntity actions, remaps TargetEntityId from network ID to local entity ID via
    /// NetworkEntityMap. If the target is not in the map, the channel is left unchanged.
    /// Exposed internal for unit testing.
    /// </summary>
    internal unsafe void ProcessSample(
        in DdsLookAtChannelIntent msg, IEntityCommandBuffer cmd, ISimulationView view)
    {
        if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) return;

        LookAtChannel updated = view.HasComponent<LookAtChannel>(entity)
            ? view.GetComponentRO<LookAtChannel>(entity)
            : default;

        updated.ActiveAction = msg.ActiveAction;
        updated.ActionInstanceId = msg.ActionInstanceId;
        updated.BehaviorInstanceId = msg.BehaviorInstanceId;

        var m = msg;
        LookAtChannel* pUpd = &updated;
        DdsLookAtChannelIntent* pMsg = &m;

        if (msg.ActiveAction == LookAtActionIds.LookAtEntity)
        {
            // Remap TargetEntityId from network entity ID to local entity ID (DD-2 §2.3).
            // Copy params first so we can modify TargetEntityId in place.
            Buffer.MemoryCopy(pMsg->ActionParams, pUpd->Params, 32, 32);

            // pUpd->Params is already a fixed buffer inside the struct pointer; cast directly.
            LookAtEntityParams* p = (LookAtEntityParams*)pUpd->Params;
            long networkTargetId = (long)p->TargetEntityId;
            if (!_entityMap.TryGetEntity(networkTargetId, out var localTarget))
            {
                // Target entity not yet known on this node — keep channel unchanged.
                return;
            }
            p->TargetEntityId = (uint)localTarget.Index;
        }
        else
        {
            Buffer.MemoryCopy(pMsg->ActionParams, pUpd->Params, 32, 32);
        }

        cmd.SetComponent(entity, updated);
        ReceivedSampleCount++;
    }

    public void ScanAndPublish(ISimulationView view) { }
}
