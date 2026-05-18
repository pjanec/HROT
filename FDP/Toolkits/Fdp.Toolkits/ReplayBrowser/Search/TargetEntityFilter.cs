using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    public sealed class TargetEntityFilter
    {
        public bool UseNetworkId { get; set; }
        public int Index { get; set; }
        public int Generation { get; set; }
        public long NetworkId { get; set; }

        public bool Passes(EntityRepository repo, Entity entity)
        {
            if (entity.IsNull) return false;

            if (UseNetworkId)
            {
                int networkIdentityTypeId = ComponentTypeRegistry.GetId(typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity));
                if (networkIdentityTypeId < 0 || !repo.HasComponentByTypeId(entity, networkIdentityTypeId)) return false;

                ref readonly var netId = ref repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity);
                return netId.Value == NetworkId;
            }

            return entity.Index == Index && entity.Generation == Generation;
        }
    }
}
