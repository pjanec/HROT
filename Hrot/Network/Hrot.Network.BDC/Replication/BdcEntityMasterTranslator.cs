using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.BDC.Messages;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.BDC.Replication
{
    /// <summary>
    /// BDC entity lifecycle translator.
    /// Egress: writes BDC_EntityMaster for locally-owned entities.
    /// Ingress: creates ghost entities from incoming BDC_EntityMaster samples.
    /// </summary>
    internal sealed class BdcEntityMasterTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<BdcEntityMaster>? _writer;
        private readonly DdsReader<BdcEntityMaster>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly long _localNodeId;
        private readonly FdpEventBus _eventBus;
        private readonly GhostCreationSystem _ghostCreation;
        private readonly HashSet<long> _publishedNetIds = new();

        public string TopicName => "BDC_EntityMaster";
        // BDC ordinal space starts at 1000 to avoid collisions with NED
        public long DescriptorOrdinal => (long)BdcDescriptorType.EntityMaster;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Bidirectional;

        private static readonly IReadOnlyList<int> _targetIds =
            new int[] { GlobalComponentIds.NetworkIdentity, GlobalComponentIds.TkbIdentity };
        public IReadOnlyList<int> TargetComponentIds => _targetIds;

        public BdcEntityMasterTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            long localNodeId,
            FdpEventBus eventBus,
            GhostCreationSystem ghostCreation)
        {
            _entityMap     = entityMap;
            _localNodeId   = localNodeId;
            _eventBus      = eventBus;
            _ghostCreation = ghostCreation;
            _writer        = new DdsWriter<BdcEntityMaster>(participant, "BDC_EntityMaster");
            _reader        = new DdsReader<BdcEntityMaster>(participant);
        }

        public void ScanAndPublish(ISimulationView view)
        {
            var query = view.Query()
                .With<NetworkIdentity>()
                .With<TkbIdentity>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            long packedKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(DescriptorOrdinal, 0);

            foreach (var entity in query)
            {
                if (!view.HasAuthority(entity, packedKey))
                    continue;

                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);

                if (_publishedNetIds.Contains(netId.Value))
                    continue;

                ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);

                _writer!.Write(new BdcEntityMaster
                {
                    EntityId = (int)netId.Value,
                    TkbType  = tkb.TkbType,
                    Diskind  = 1, // Platform
                });

                SentSampleCount++;
                _publishedNetIds.Add(netId.Value);
                FdpLog<BdcEntityMasterTranslator>.Debug(
                    "[BDC Node-{0}] Egress: BDC_EntityMaster EntityId={1}", _localNodeId, netId.Value);
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader == null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                var info = sample.Info;
                if (info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                {
                    // Disposed instance — key fields still valid, data payload may not be.
                    var keyData = DdsTypeSupport.FromNative<BdcEntityMaster>(sample.NativePtr);
                    ProcessDispose(keyData.EntityId);
                    continue;
                }

                if (!sample.IsValid)
                    continue;

                ReceivedSampleCount++;
                var master = sample.Data;
                ProcessSample(in master, cmd, view);
            }
        }

        private void ProcessSample(in BdcEntityMaster master, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = master.EntityId;
            if (netId == 0) return;
            if (_entityMap.TryGetEntity(netId, out _)) return;

            var repo = view as EntityRepository;
            if (repo == null)
            {
                FdpLog<BdcEntityMasterTranslator>.Warn(
                    "[BDC Node-{0}] Cannot create ghost for NetID {1}: view is read-only.",
                    _localNodeId, netId);
                return;
            }

            _ghostCreation.CreateGhost(repo, netId, view.Tick);
            FdpLog<BdcEntityMasterTranslator>.Debug(
                "[BDC Node-{0}] Ingress: ghost for EntityId={1}", _localNodeId, netId);
        }

        private void ProcessDispose(long networkEntityId)
        {
            _eventBus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = networkEntityId,
                Reason = "BDC_EntityMaster disposed",
                IsRemote = true,
            });
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            if (_writer != null && _publishedNetIds.Contains(networkEntityId))
            {
                _writer.DisposeInstance(new BdcEntityMaster { EntityId = (int)networkEntityId });
                _publishedNetIds.Remove(networkEntityId);
            }
        }
    }
}
