using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Patching;

namespace Fdp.Toolkit.Civilians
{
    /// <summary>
    /// ⭐ <c>CE-3238</c> (civilians B-16, AW-22 — R-2028; docs/DESIGN_Civilians_And_Crowd_Control.md §7.16 HA-1) — how grave a man's hostile
    /// act was. Ordered: an act only ever UPGRADES (<see cref="NonLethal"/> → <see cref="Lethal"/>), never down.
    /// </summary>
    public enum HostileActKind : byte
    {
        None = 0,
        /// <summary>A blow, a thrown stone, a gas canister thrown back — it hurts, it does not kill.</summary>
        NonLethal = 1,
        /// <summary>A shot, a grenade, a charge — it can kill.</summary>
        Lethal = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3238</c> (B-16, HA-1) — a man's HOSTILE ACT: when he first committed one and the gravest kind so far. On the man, Brain group,
    /// replicated as <c>dtHostileAct</c> (145) and recorded — so every node (a soldier's brain on another CGF, the map, a replay) knows he
    /// acted, not only his own brain. Absent ⇒ no act. Written only by <see cref="HostileActs.Commit"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.HostileAct)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct HostileAct
    {
        /// <summary>Sim time (s) of his FIRST act — an upgrade keeps it.</summary>
        public double SinceSimTime;
        /// <summary>The gravest act so far.</summary>
        public HostileActKind Kind;
    }

    /// <summary>
    /// ⭐ <c>CE-2238</c> part 1 (CIV-4, CV-4 as amended by AW-16; docs/DESIGN_Civilians_And_Crowd_Control.md §5i) — a HOSTILE ACT (a blow,
    /// a throw, a shot at the forces) and the side flip it causes: once, sticky for the exercise. The flip is the ONE write path
    /// (§7.1a): <see cref="EntityWriteRouter"/> sets the <c>Affiliation</c> attribute to Opposing — Direct where this node owns his
    /// <see cref="EntityInfo"/> (CGF, for a scenario cast), a request to the owner otherwise — and the dirty-marked record reaches every node.
    /// <para>⭐ <c>CE-3238</c> (B-16, HA-2) — the act itself is the replicated <see cref="HostileAct"/> (its kind upgrades, never down);
    /// <see cref="CivilianState.HostileActAt"/> is kept in step for the brain's own reads.</para>
    /// </summary>
    public static class HostileActs
    {
        /// <summary>
        /// Records <paramref name="actor"/>'s hostile act of <paramref name="kind"/> and flips him Hostile, on the node that owns his brain.
        /// The first act stamps the time; a graver one later UPGRADES the kind (the time stays); a lesser one changes nothing. The side flips
        /// once — never when he is Hostile already. True when anything changed (the act recorded or upgraded, or the flip asked for); false
        /// when he is gone, <paramref name="kind"/> is None, or this act is no graver than his last. <paramref name="kind"/> defaults to
        /// <see cref="HostileActKind.NonLethal"/> — a blow, a stone.
        /// </summary>
        public static bool Commit(EntityRepository repo, Entity actor, HostileActKind kind = HostileActKind.NonLethal)
        {
            if (!repo.IsAlive(actor) || kind == HostileActKind.None) return false;
            // ⭐ CE-2238 part 3 (AW-22, §5h.6 KC-2/KC-5) — a LETHAL act makes him a known combatant to every unit that witnessed it, EVERY
            //   time (a stone-thrower who later fires is marked even though his first act stamped him already)
            if (kind == HostileActKind.Lethal) Fdp.Toolkit.Combat.KnownCombatants.MarkWitnesses(repo, actor);
            bool first = !Committed(repo, actor);
            bool changed = false;
            double now = repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : repo.SimulationTime;
            if (now <= 0) now = 1e-6;
            if (repo.IsComponentTypeRegistered<HostileAct>())
            {
                if (!repo.HasComponent<HostileAct>(actor))
                {
                    repo.AddComponent(actor, new HostileAct { SinceSimTime = now, Kind = kind });
                    changed = true;
                }
                else
                {
                    ref var act = ref repo.GetComponentRW<HostileAct>(actor);
                    if (kind > act.Kind) { act.Kind = kind; changed = true; }   // ⭐ HA-2 — upgrade, never down; the time stays
                }
            }
            if (repo.IsComponentTypeRegistered<CivilianState>() && repo.HasComponent<CivilianState>(actor))
            {
                ref var mind = ref repo.GetComponentRW<CivilianState>(actor);
                if (mind.HostileActAt <= 0) { mind.HostileActAt = now; changed = true; }
            }
            if (!first) return changed;
            if (repo.HasComponent<EntityInfo>(actor) && repo.GetComponentRO<EntityInfo>(actor).ForceId == ForceId.Hostile) return changed;
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

        /// <summary>The gravest hostile act <paramref name="actor"/> has committed, as this node knows it (his brain's own record or the
        /// replicated one); None when none.</summary>
        public static HostileActKind Of(ISimulationView view, Entity actor)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<HostileAct>()) return HostileActKind.None;
            return view.IsAlive(actor) && view.HasComponent<HostileAct>(actor) ? view.GetComponentRO<HostileAct>(actor).Kind : HostileActKind.None;
        }

        /// <summary>True once <paramref name="actor"/> committed a hostile act — on any node that sees his <see cref="HostileAct"/>, or on his
        /// brain's node by its own stamp.</summary>
        public static bool Committed(EntityRepository repo, Entity actor)
            => Of(repo, actor) != HostileActKind.None
               || repo.IsComponentTypeRegistered<CivilianState>() && repo.HasComponent<CivilianState>(actor)
                  && repo.GetComponentRO<CivilianState>(actor).HostileActAt > 0;
    }
}
