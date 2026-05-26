using System.Collections.Generic;
using System.Runtime.InteropServices;
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
/// Egress translator: publishes <see cref="AnimationMontageQueue"/> to DDS (Brain -> Muscle).
/// Dirty trigger: QueueVersion change only.
///
/// <para><b>Serialization contract (DD-2 §4.2):</b> only the live entries (Count * 16 bytes) are
/// copied into the DDS wire struct; the tail entries beyond Count are left as zeros. The DDS
/// transport still sends the full fixed-size <see cref="DdsMontageQueue"/> frame (DDS requires a
/// deterministic struct size), but the logical payload byte count is <c>12 + 16 * Count</c>
/// (EntityId(8) + QueueVersion(4) = 12 header bytes, plus 16 bytes per live entry).</para>
/// </summary>
internal sealed class AnimationMontageQueueEgressTranslator : INetworkTranslator
{
    private const string TopicNameConst = "hrot/anim/MontageQueue";

    private readonly IAnimDdsWriter<DdsMontageQueue> _writer;
    private readonly NetworkEntityMap _entityMap;
    private readonly Dictionary<Entity, uint> _lastPublishedQueueVersion = new();

    public string TopicName => TopicNameConst;
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount { get; private set; }
    public long SentSampleCount { get; private set; }
    internal long DirtyFalsePositiveCount { get; private set; }

    /// <summary>
    /// Logical payload byte count for a queue with the given entry count (DD-2 §4.2).
    /// Accounts for EntityId(8) + QueueVersion(4) header bytes plus 16 bytes per live entry.
    /// Note: the DDS wire frame is always the full fixed-size <see cref="DdsMontageQueue"/> struct;
    /// this method expresses the minimal meaningful bytes, not the over-the-wire frame size.
    /// </summary>
    internal static int LogicalPayloadBytes(byte count) => 12 + count * 16;

    internal AnimationMontageQueueEgressTranslator(
        DdsParticipant participant, NetworkEntityMap entityMap)
        : this(new DdsLiveWriter<DdsMontageQueue>(participant, TopicNameConst), entityMap)
    {
    }

    internal AnimationMontageQueueEgressTranslator(
        IAnimDdsWriter<DdsMontageQueue> writer, NetworkEntityMap entityMap)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
    }

    public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

    public unsafe void ScanAndPublish(ISimulationView view)
    {
        var query = view.Query()
            .With<AnimationMontageQueue>()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            if (!view.HasAuthority(entity)) continue;

            ref readonly var queue = ref view.GetComponentRO<AnimationMontageQueue>(entity);

            if (_lastPublishedQueueVersion.TryGetValue(entity, out uint lastVer)
                && lastVer == queue.QueueVersion)
            {
                DirtyFalsePositiveCount++;
                continue;
            }

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

            var q = queue; // local copy
            byte count = q.Count > 8 ? (byte)8 : q.Count;
            var msg = new DdsMontageQueue
            {
                EntityId = netId.Value,
                QueueVersion = q.QueueVersion,
                Count = count,
            };
            // Copy only the live entries (Count * 16 bytes). The tail of EntriesData in msg
            // stays zero-initialized (DDS fixed-size framing; see LogicalPayloadBytes).
            int validBytes = count * System.Runtime.InteropServices.Marshal.SizeOf<MontageQueueEntry>();
            AnimationMontageQueue* pQ = &q;
            DdsMontageQueue* pMsg = &msg;
            if (validBytes > 0)
                Buffer.MemoryCopy(pQ->EntriesData, pMsg->EntriesData, 128, validBytes);

            _writer.Write(msg);
            SentSampleCount++;
            _lastPublishedQueueVersion[entity] = q.QueueVersion;
        }
    }
}
