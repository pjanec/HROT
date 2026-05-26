using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.Animation.Replication.Translators.Descriptors;

/// <summary>
/// Egress translator: publishes <see cref="StanceIntent"/> to DDS (Brain -> Muscle).
/// Dirty trigger: (TargetStance, BlendTime, Version).
/// </summary>
internal sealed class StanceIntentEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/StanceIntent";

    private readonly IAnimDdsWriter<DdsStanceIntent> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, (StanceId, float, uint)> _lastPublished = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    internal StanceIntentEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsStanceIntent>(participant, TopicNameConst), entityMap)
    {
    }

    internal StanceIntentEgressTranslator(
        IAnimDdsWriter<DdsStanceIntent> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<StanceIntent>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var intent = ref view.GetComponentRO<StanceIntent>(entity);

            if (_lastPublished.TryGetValue(entity, out var last)
                && last.Item1 == intent.TargetStance
                && last.Item2 == intent.BlendTime
                && last.Item3 == intent.Version)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            _writer.Write(new DdsStanceIntent
            {
                EntityId = netId.Value,
                TargetStance = (byte)intent.TargetStance,
                BlendTime = intent.BlendTime,
                Version = intent.Version,
            });
            SentSampleCount++;
            _lastPublished[entity] = (intent.TargetStance, intent.BlendTime, intent.Version);
        }
    }
}
