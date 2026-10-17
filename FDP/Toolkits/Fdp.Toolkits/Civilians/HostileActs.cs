using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 1 (CIV-4, CV-4 as amended by AW-16; docs/DESIGN_Civilians_And_Crowd_Control.md §5i) — a HOSTILE ACT (a blow,
    /// later a throw, a shot at the forces) and the side flip it causes: once, sticky for the exercise. The flip is the ONE write path
    /// (§7.1a): <see cref="EntityWriteRouter"/> sets the <c>Affiliation</c> attribute to Opposing — Direct where this node owns his
    /// <see cref="EntityInfo"/> (CGF, for a scenario cast), a request to the owner otherwise — and the dirty-marked record reaches every node.
    /// <para>⏳ The act's replicated KIND is backend's B-16; until it lands the act is stamped brain-locally in
    /// <see cref="CivilianState.HostileActAt"/>.</para>
    /// </summary>
    public static class HostileActs
    {
        /// <summary>Records <paramref name="actor"/>'s hostile act and flips him Hostile. True the first time (the flip was asked for);
        /// false when he had already committed one, was already Hostile, or is gone.</summary>
        public static bool Commit(EntityRepository repo, Entity actor)
        {
            if (!repo.IsAlive(actor)) return false;
            if (repo.IsComponentTypeRegistered<CivilianState>() && repo.HasComponent<CivilianState>(actor))
            {
                ref var mind = ref repo.GetComponentRW<CivilianState>(actor);
                if (mind.HostileActAt > 0) return false;
                double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;
                mind.HostileActAt = now > 0 ? now : 1e-6;
            }
            if (repo.HasComponent<EntityInfo>(actor) && repo.GetComponentRO<EntityInfo>(actor).ForceId == ForceId.Hostile) return false;
            EntityWriteRouter.For(repo).Write(actor, new[]
            {
                new EntityAttributeChange
                {
                    AttributeId = AttributeIds.Affiliation,
                    Value       = AttributeValue.FromInt((int)ForceIdentifier.Opposing),
                },
            });
            return true;
        }

        /// <summary>True once <paramref name="actor"/> committed a hostile act (this node's brain-local stamp).</summary>
        public static bool Committed(EntityRepository repo, Entity actor)
            => repo.IsComponentTypeRegistered<CivilianState>() && repo.HasComponent<CivilianState>(actor)
               && repo.GetComponentRO<CivilianState>(actor).HostileActAt > 0;
    }
}
