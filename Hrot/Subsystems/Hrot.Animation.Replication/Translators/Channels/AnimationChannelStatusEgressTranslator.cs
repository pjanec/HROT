using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fbt;
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
/// and publishes DDS status samples (Muscle -> Brain direction).
/// Only publishes when (Status, DispatchedInstanceId) changes.
/// Does NOT replicate Brain-authored intent fields (ActiveAction, ActionInstanceId, etc.).
/// </summary>
internal sealed class AnimationChannelStatusEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/status/AnimationChannel";

    private readonly IAnimDdsWriter<DdsAnimationChannelStatus> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, (NodeStatus, uint)> _lastPublished = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    internal AnimationChannelStatusEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsAnimationChannelStatus>(participant, TopicNameConst), entityMap)
    {
    }

    internal AnimationChannelStatusEgressTranslator(
        IAnimDdsWriter<DdsAnimationChannelStatus> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<AnimationChannel>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var channel = ref view.GetComponentRO<AnimationChannel>(entity);

            // Fine-grained dirty filter: only publish when Status or DispatchedInstanceId changes.
            if (_lastPublished.TryGetValue(entity, out var last)
                && last.Item1 == channel.Status
                && last.Item2 == channel.DispatchedInstanceId)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            _writer.Write(new DdsAnimationChannelStatus
            {
                EntityId = netId.Value,
                Status = (byte)channel.Status,
                DispatchedInstanceId = channel.DispatchedInstanceId,
            });
            SentSampleCount++;
            _lastPublished[entity] = (channel.Status, channel.DispatchedInstanceId);
        }
    }
}
