using System;
using System.Collections.Generic;
using System.Numerics;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.BDC.Common;
using Hrot.BDC.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC world position translator.
    /// Egress: writes BDC_WorldPos for locally-owned entities.
    /// Ingress: updates SimTransform on ghost entities from incoming BDC_WorldPos samples.
    /// </summary>
    internal sealed class BdcWorldPosTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsWriter<BdcWorldPos>? _writer;
        private readonly DdsReader<BdcWorldPos>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;

        public string TopicName => "BDC_WorldPos";
        // BDC WorldPos ordinal
        public long DescriptorOrdinal => (long)BdcDescriptorType.WorldPos;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Bidirectional;

        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.SimTransform };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public BdcWorldPosTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId)
        {
            _entityMap    = entityMap;
            _localNodeId  = localNodeId;
            _writer       = new DdsWriter<BdcWorldPos>(participant, "BDC_WorldPos");
            _reader       = new DdsReader<BdcWorldPos>(participant);
        }

        /// <summary>
        /// The sample's simulation-time stamp (<see cref="BdcWorldPos.SimTime"/>), complaining loudly if it looks unstamped — the NED
        /// stack's translator has the same guard, for the same reason (<c>docs/DESIGN_Dead_Reckoning.md</c> rule R6). ⭐ AQ107 D: the
        /// stamp is a plain double, so "unstamped" can only be recognised as 0 while the cluster clock is well past 0.
        /// </summary>
        private double CheckSimStamp(double simTime, ISimulationView view, long entityId)
        {
            if (simTime > 0.0)
                return simTime;

            double simNow = Fdp.Toolkit.Time.SimClock.Of(view).TotalTime;
            if (simNow > 1.0)
            {
                FdpLog<BdcWorldPosTranslator>.Warn(
                    "[BDC Node-{0}] BDC_WorldPos for entity {1} arrived with SimTime={2} while the cluster clock reads {3:F1} s. " +
                    "Dead reckoning cannot extrapolate it and will hold the raw sample. " +
                    "The publisher is not stamping — fix it there (docs/DESIGN_Dead_Reckoning.md R6).",
                    _localNodeId, entityId, simTime, simNow);
            }

            return simTime;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            // Sampled once per scan so every entity in this frame carries the same stamp.
            double simNowSeconds = Fdp.Toolkit.Time.SimClock.Of(view).TotalTime;

            var query = view.Query()
                .With<NetworkIdentity>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.Active)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);

                // ⭐ AQ107 F — the pose is COPIED onto the wire in the local ENU frame (BdcWorldPosCodec), like NED's WorldPos.
                //   Velocity is what dead reckoning extrapolates ALONG; publishing zeros made every receiver hold the last sample
                //   still until the next one arrived, which is the stutter DR exists to remove (CE-211).
                Vector3 linear = Vector3.Zero, angular = Vector3.Zero;
                if (view.HasComponent<SimVelocity>(entity))
                {
                    ref readonly var simVel = ref view.GetComponentRO<SimVelocity>(entity);
                    linear  = simVel.Linear;
                    angular = simVel.Angular;
                }

                // Cluster-synced SIMULATION time, never wall time — docs/DESIGN_Dead_Reckoning.md rule R4.
                _writer!.Write(BdcWorldPosCodec.Encode(netId.Value, simNowSeconds, simTf.Position, simTf.Rotation, linear, angular));

                SentSampleCount++;
            }
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (sample.Info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                    continue;

                if (!sample.IsValid)
                    continue;

                ReceivedSampleCount++;
                var msg = sample.Data;

                if (!_entityMap.TryGetEntity(msg.EntityId, out var entity))
                    continue;

                // Guard: skip locally-owned entities to avoid loopback overwrite
                bool isLocallyOwned = view.HasComponent<NetworkAuthority>(entity)
                                      && view.GetComponentRO<NetworkAuthority>(entity).HasAuthority;
                if (isLocallyOwned)
                    continue;

                BdcWorldPosCodec.Decode(in msg, out var position, out var rotation, out var linear, out _);

                // The sample and its stamp — the anchor dead reckoning extrapolates FROM. Until
                // CE-211 this translator wrote SimTransform only, so on a BDC node the DR system
                // registered at BdcReplicationModule:87 matched zero entities: its query requires
                // NetworkTransform + NetworkVelocity and nothing here supplied either. The
                // registration was real and the mechanism was inert.
                cmd.SetComponent(entity, new NetworkTransform
                {
                    LastPosition = position,
                    LastRotation = rotation,
                    SimStamp     = CheckSimStamp(msg.SimTime, view, msg.EntityId),
                });
                cmd.SetComponent(entity, new NetworkVelocity { Value = linear });

                cmd.SetComponent(entity, new SimTransform { Position = position, Rotation = rotation });

                FdpLog<BdcWorldPosTranslator>.Debug(
                    "[BDC Node-{0}] Ingress: BDC_WorldPos EntityId={1}", _localNodeId, msg.EntityId);
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not BdcWorldPos msg) return;

            BdcWorldPosCodec.Decode(in msg, out var position, out var rotation, out _, out _);

            repo.SetComponent(entity, new SimTransform { Position = position, Rotation = rotation });
        }

        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new BdcWorldPos { EntityId = networkEntityId });
        }
    }
}
