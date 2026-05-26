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
/// Egress translator: publishes <see cref="StanceStatus"/> to DDS (Muscle -> Brain).
/// Dirty trigger: (Phase, CurrentStance, AckVersion).
/// TransitionProgress rides in the payload but does NOT drive the dirty trigger.
/// </summary>
internal sealed class StanceStatusEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/StanceStatus";

    private readonly IAnimDdsWriter<DdsStanceStatus> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, (StanceTransitionPhase, StanceId, uint)> _lastPublished = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    internal StanceStatusEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsStanceStatus>(participant, TopicNameConst), entityMap)
    {
    }

    internal StanceStatusEgressTranslator(
        IAnimDdsWriter<DdsStanceStatus> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<StanceStatus>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var status = ref view.GetComponentRO<StanceStatus>(entity);

            // CRITICAL: TransitionProgress does NOT drive dirty trigger.
            if (_lastPublished.TryGetValue(entity, out var last)
                && last.Item1 == status.Phase
                && last.Item2 == status.CurrentStance
                && last.Item3 == status.AckVersion)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            _writer.Write(new DdsStanceStatus
            {
                EntityId = netId.Value,
                CurrentStance = (byte)status.CurrentStance,
                Phase = (byte)status.Phase,
                TransitionProgress = status.TransitionProgress,
                AckVersion = status.AckVersion,
            });
            SentSampleCount++;
            _lastPublished[entity] = (status.Phase, status.CurrentStance, status.AckVersion);
        }
    }
}
