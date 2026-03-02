using System;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Bagira <c>MapEntitySymbol</c> DDS topic.
    ///
    /// Applies per-entity visual overrides via <see cref="IgSymbolOverride"/>.
    /// IG is a ghost-only node, so <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class MapEntitySymbolIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "MapEntitySymbol";
        private const long OrdinalValue = 40;

        private readonly DdsReader<MapEntitySymbol>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _mapGroupId;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;

        public MapEntitySymbolIngressTranslator(
            DdsParticipant? participant,
            NetworkEntityMap entityMap,
            int mapGroupId,
            GhostCreationSystem ghostCreationSystem)
        {
            _reader = participant is not null ? new DdsReader<MapEntitySymbol>(participant) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _mapGroupId = mapGroupId;
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid)
                    continue;

                if (sample.Info.InstanceState != DdsInstanceState.Alive)
                    continue;

                ProcessSample(sample.Data, cmd, view as EntityRepository);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not MapEntitySymbol symbol)
                return;

            if (!ShouldApply(symbol.MapGroupId))
                return;

            repo.SetManagedComponent(entity, new IgSymbolOverride
            {
                StyleSetId = string.IsNullOrEmpty(symbol.StyleSetId) ? null : symbol.StyleSetId,
                TextureOverride = null
            });
        }

        public void Dispose(long networkEntityId) { }

        internal void ProcessSample(in MapEntitySymbol data, IEntityCommandBuffer cmd, EntityRepository? repo)
        {
            if (!ShouldApply(data.MapGroupId))
                return;

            long netId = data.EntityId;
            if (!_entityMap.TryGetEntity(netId, out var entity))
            {
                if (repo == null)
                {
                    FdpLog<MapEntitySymbolIngressTranslator>.Warn(
                        "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            cmd.SetManagedComponent(entity, new IgSymbolOverride
            {
                StyleSetId = string.IsNullOrEmpty(data.StyleSetId) ? null : data.StyleSetId,
                TextureOverride = null
            });
        }

        private bool ShouldApply(int mapGroupId)
        {
            return mapGroupId == 0 || mapGroupId == _mapGroupId;
        }
    }
}
