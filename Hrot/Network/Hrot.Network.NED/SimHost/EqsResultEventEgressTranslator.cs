using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Spatial.Eqs.Topics;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// Muscle-side egress translator: reads <see cref="EqsResultEvent"/>s from the local bus,
    /// dereferences the <see cref="EqsResultPool"/> handle to build a
    /// <see cref="List{EqsResultEntry}"/> payload, and publishes <see cref="EqsResultTopic"/>
    /// to the Brain node via DDS.
    /// </summary>
    public sealed class EqsResultEventEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EqsResult";
        private readonly DdsWriter<EqsResultTopic>? _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtEqsResult;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EqsResultEventEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (entityMap == null) throw new ArgumentNullException(nameof(entityMap));
            _writer    = new DdsWriter<EqsResultTopic>(participant, DdsTopicName);
            _entityMap = entityMap;
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            if (view is not EntityRepository repo) return;

            var events = view.ReadEvents<EqsResultEvent>();
            if (events.IsEmpty) return;

            for (int ei = 0; ei < events.Length; ei++)
            {
                ref readonly var evt = ref events[ei];

                // Skip local-only results (ParentNetworkId == 0): they are not replicated via DDS.
                if (evt.ParentNetworkId == 0) continue;

                // ⭐ S6 (F-12): only the node that owns this sensor's RESULT instance publishes it — the Perception group's
                //   record, (dtEqsResult, part), falling back to the group's (dtEqsResult, 0) and then the primary owner.
                //   Every Muscle node builds carriers and solves; without this gate two of them would both publish.
                //   📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S6.
                if (_entityMap.TryGetEntity(evt.ParentNetworkId, out var parent) && view.IsAlive(parent) &&
                    !Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(
                        view, parent, Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, evt.LocalChildIndex)))
                    continue;

                // Build the managed DDS payload from the unmanaged pool slice.
                // EntryCount == 0 (Phase 1 stub) is valid: publish an empty result.
                var entries = new List<EqsResultEntry>(evt.EntryCount);
                if (evt.EntryCount > 0)
                {
                    if (!repo.HasSingletonUnmanaged<EqsResultPool>()) continue;
                    ref readonly var pool = ref repo.GetSingletonUnmanaged<EqsResultPool>();

                    for (int i = 0; i < evt.EntryCount; i++)
                    {
                        ref readonly var r = ref pool.Results[evt.ResultHandle + i];

                        // For entity-shaped results translate local EntityId -> NetworkId.
                        // EntityId = 0 means positional candidate (no translation needed).
                        // ⭐ A target with no network id cannot be named on the wire — DROP it, as the
                        // area-query egress does. Sending it as 0 would make the Brain read it as a
                        // POSITIONAL candidate at the target's position.
                        long resolvedNetId = 0L;
                        if (r.EntityId != 0L && r.EntityId != -1L)
                        {
                            var targetEntity = new Entity((ulong)r.EntityId);
                            if (!_entityMap.TryGetNetworkId(targetEntity, out resolvedNetId)) continue;
                        }

                        entries.Add(new EqsResultEntry
                        {
                            EntityId       = resolvedNetId,
                            PositionX      = r.PositionX,
                            PositionY      = r.PositionY,
                            PositionZ      = r.PositionZ,
                            Score          = r.Score,
                            Flags          = (ushort)r.Flags,
                            FlagsMeaningful = (ushort)r.FlagsMeaningful,
                            Stance         = r.Stance,   // ⭐ CE-3135
                        });
                    }
                }

                _writer.Write(new EqsResultTopic
                {
                    ParentNetworkId = evt.ParentNetworkId,
                    LocalChildIndex = evt.LocalChildIndex,
                    Epoch           = evt.Epoch,
                    RefreshTick     = evt.RefreshTick,
                    Results         = entries,
                });

                SentSampleCount++;
            }
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}

