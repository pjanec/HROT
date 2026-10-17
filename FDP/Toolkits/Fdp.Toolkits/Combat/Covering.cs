using Fdp.Core;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2241</c> ① (CV-13a demo; docs/DESIGN_Civilians_And_Crowd_Control.md §5h.3) — who COVERS a man: the nearest forces person
    /// (<c>Friend</c>, <see cref="Armament.IsCombatant"/>, still in the fight) whose <see cref="WeaponPosture"/> is raised (not
    /// <see cref="WeaponPostureKind.AtEase"/>) and names him as its <see cref="WeaponPosture.Target"/>. Reads the replicated posture
    /// (WL-6's one writer, <see cref="WeaponPostureOf"/>), so it answers the same on every node; a soldier who HOLDS at raised hands
    /// (<c>HoldSurrender</c> ⇒ Ready, the target kept) still covers him.
    /// </summary>
    public static class Covering
    {
        /// <summary>How far a covering rifle is noticed (m).</summary>
        public const float ReachMetres = 100f;

        /// <summary>The nearest forces person within <paramref name="reach"/> covering <paramref name="target"/>, or <see cref="Entity.Null"/>.</summary>
        public static Entity By(EntityRepository world, Entity target, float reach = ReachMetres)
        {
            if (target.IsNull || !world.IsAlive(target) || !world.HasComponent<SimTransform>(target)
                || !world.IsComponentTypeRegistered<WeaponPosture>() || !world.IsComponentTypeRegistered<EntityInfo>()) return Entity.Null;
            var me = world.GetComponentRO<SimTransform>(target).Position;
            float best = reach * reach;
            var found = Entity.Null;
            foreach (var e in world.Query().With<WeaponPosture>().With<EntityInfo>().With<SimTransform>().Build())
            {
                if (e == target) continue;
                ref readonly var posture = ref world.GetComponentRO<WeaponPosture>(e);
                if (posture.Kind == WeaponPostureKind.AtEase || posture.Target != target) continue;
                if (world.GetComponentRO<EntityInfo>(e).ForceId != ForceId.Friend || !Armament.IsCombatant(world, e)) continue;
                if (world.HasComponent<Health>(e) && world.GetComponentRO<Health>(e).Current <= 0f) continue;
                if (Fdp.Toolkit.Perception.ThreatDanger.IsOutOfTheFight(world, e)) continue;
                var at = world.GetComponentRO<SimTransform>(e).Position;
                float d2 = (at.X - me.X) * (at.X - me.X) + (at.Y - me.Y) * (at.Y - me.Y);
                if (d2 >= best) continue;
                best = d2;
                found = e;
            }
            return found;
        }
    }
}
