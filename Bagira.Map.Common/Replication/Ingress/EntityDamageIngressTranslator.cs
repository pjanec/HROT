using System;
using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Kernel.Logging;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.Map.Common.Replication.Ingress
{
    /// <summary>
    /// Ingress translator for the Bagira <c>EntityDamage</c> DDS topic.
    ///
    /// Converts DDS damage into the IG-internal <see cref="IgHealthState"/> component.
    /// This translator is ingress-only; <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class EntityDamageIngressTranslator : CycloneTranslator<EntityDamage, EntityDamage>
    {
        private const string DdsTopicName = "EntityDamage";
        private const long OrdinalValue = 30;

        private readonly GhostCreationSystem _ghostCreationSystem;

        public EntityDamageIngressTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
        }

        protected override void Decode(in EntityDamage data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
            {
                var repo = view as EntityRepository;
                if (repo == null)
                {
                    FdpLog<EntityDamageIngressTranslator>.Warn(
                        "[IG] Cannot create ghost for NetID {0}: view is read-only.", netId);
                    return;
                }

                entity = _ghostCreationSystem.CreateGhost(repo, netId);
            }

            cmd.SetComponent(entity, new IgHealthState { Damage = data.Damage });
        }

        public override void ScanAndPublish(ISimulationView view) { }

        public override void ApplyToEntity(Entity entity, object data, EntityRepository repo)
        {
            if (data is not EntityDamage damage)
                return;

            repo.SetComponent(entity, new IgHealthState { Damage = damage.Damage });
        }
    }
}
