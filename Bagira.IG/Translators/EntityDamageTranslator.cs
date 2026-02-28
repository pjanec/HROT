using Bagira.BDC.SSTD;
using Bagira.IG.Components;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Translators;

namespace Bagira.IG.Translators
{
    /// <summary>
    /// Ingress translator for the Bagira <c>EntityDamage</c> DDS topic.
    ///
    /// Converts DDS damage into the IG-internal <see cref="IgHealthState"/> component.
    /// IG is a ghost-only node, so <see cref="ScanAndPublish"/> is a no-op.
    /// </summary>
    public class EntityDamageTranslator : CycloneTranslator<EntityDamage, EntityDamage>
    {
        private const string DdsTopicName = "EntityDamage";
        private const long   OrdinalValue = 30;

        public EntityDamageTranslator(
            DdsParticipant participant,
            NetworkEntityMap entityMap)
            : base(participant, DdsTopicName, OrdinalValue, entityMap)
        {
        }

        protected override void Decode(in EntityDamage data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            long netId = data.EntityId;
            if (!EntityMap.TryGetEntity(netId, out var entity))
                return;

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
