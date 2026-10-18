using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>⭐ <c>CE-2074</c> (R-200) — the weapons-control half of a unit's ROE, ordered from most to least restrictive so
    /// a condition can ask "ROE ≥ ReturnFire". <see cref="FireUnset"/> = no ROE given ⇒ <see cref="FireAtWill"/>.</summary>
    public enum RoeFire : byte
    {
        /// <summary>No ROE given — reads as <see cref="FireAtWill"/> (today's behaviour). ⚠ Named, not <c>Unset</c>: the DDS
        /// IDL puts every enum member of a module in ONE scope, so two <c>Unset</c>s collide.</summary>
        FireUnset = 0,
        /// <summary>Never fire.</summary>
        HoldFire = 1,
        /// <summary>Fire only when fired upon — was hit / shot at within the last few seconds.</summary>
        ReturnFire = 2,
        /// <summary>Fire at any hostile.</summary>
        FireAtWill = 3,
    }

    /// <summary>⭐ <c>CE-2239</c> part 1 (CV-8, AW-18; docs/DESIGN_Civilians_And_Crowd_Control.md §5j FL-1) — the FORCE LEVEL: the most force
    /// the unit's weapons may use, ordered from most to least restrictive. <see cref="ForceUnset"/> = no level given ⇒ <see cref="Lethal"/>
    /// (today's behaviour). Self-defence lifts it (AW-18, <c>ForceLevels.Permits</c>).</summary>
    public enum RoeForceLevel : byte
    {
        /// <summary>No level given — reads as <see cref="Lethal"/>. ⚠ Named, not <c>Unset</c> (the IDL's one enum scope).</summary>
        ForceUnset = 0,
        /// <summary>Warn only: no round at all.</summary>
        Warn = 1,
        /// <summary>Non-lethal rounds only (baton rounds, stun, gas) and the baton.</summary>
        NonLethal = 2,
        /// <summary>Any weapon.</summary>
        Lethal = 3,
    }

    /// <summary>⭐ <c>CE-2074</c> (R-199 ②, R-200) — may the unit's SOP interrupt its task with a reaction?
    /// <see cref="ReactionsUnset"/> ⇒ <see cref="React"/>.</summary>
    public enum RoeReactions : byte
    {
        /// <summary>No ROE given — reads as <see cref="React"/>.</summary>
        ReactionsUnset = 0,
        /// <summary>The order forbids reactions: the task is never paused.</summary>
        StayOnTask = 1,
        /// <summary>Reactions may pause the task.</summary>
        React = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2074</c> (R-200) — a unit's RULES OF ENGAGEMENT: unit state that persists across tasks until an order
    /// changes it; the TKB gives the default. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.4.
    /// <para>Read by the SOP's conditions, by the gate (<see cref="Reactions"/>, <c>CE-2078</c>) and — enforced once — by
    /// the fire executor (<see cref="Fire"/>, <c>CE-2075</c>). Changed only through <c>SetRoeEvent</c>, gated by
    /// <see cref="SetBy"/> exactly as a behaviour is by its origin.</para>
    /// <para>⚠ <c>NoScenario</c>: the scenario saves it through the AI snapshot (<c>CE-3042</c>, R-192) only when it
    /// differs from the TKB default — never as raw component state.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Roe)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Roe
    {
        /// <summary>Weapons control.</summary>
        public RoeFire Fire;
        /// <summary>May reactions pause the task?</summary>
        public RoeReactions Reactions;
        /// <summary>Who set it last — a lower origin cannot change it (<see cref="BehaviorOriginRank"/>).
        /// <see cref="BehaviorOrigin.Unmarked"/> = the TKB default, which anything may change.</summary>
        public BehaviorOrigin SetBy;
        /// <summary>⭐ <c>CE-2095</c> — how long after being hit (or near-missed) a <see cref="RoeFire.ReturnFire"/> unit may shoot
        /// back, in seconds; <c>0</c> = unset ⇒ <see cref="RoeOf.DefaultReturnFireWindowSeconds"/>. Last, so the earlier fields
        /// keep their offsets.</summary>
        public float ReturnFireWindowSeconds;
        /// <summary>⭐ <c>CE-2239</c> part 1 (CV-8, §5j FL-1) — the most force the unit may use; <see cref="RoeForceLevel.ForceUnset"/> ⇒
        /// <see cref="RoeForceLevel.Lethal"/>. Last, so the earlier fields keep their offsets.</summary>
        public RoeForceLevel ForceLevel;
    }

    /// <summary>⭐ <c>CE-2074</c> — reading a unit's ROE with the defaults applied, in one place.
    /// <para>⭐⭐ <c>CE-2289</c> (PC-6, R-2046) — and UP THE CHAIN OF COMMAND, per field: a value an ORDER set on the man himself wins;
    /// else the nearest commander (<see cref="UnitSubordinate.Commander"/>, walked up to <see cref="MaxChainDepth"/> levels) whose
    /// field an order set; else the man's own TKB default; else the built-in default. ⇒ one <c>SetRoeEvent</c> on a squad or a
    /// platoon reaches every man under it. ⚠ "Set by an order" is the whole component's <see cref="Roe.SetBy"/>: once an order
    /// touched the man's ROE, every field he holds (a TKB default included) counts as his own. ⚠ The commander's <see cref="Roe"/>
    /// must be on the man's node — it lives on the commander's brain node (<c>CE-2299</c>).
    /// 📄 <c>docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md</c> §10 PC-6.</para></summary>
    public static class RoeOf
    {
        /// <summary>⭐ <c>CE-2095</c> — the ReturnFire window when nothing set one: the 5 s the design's own SOP example uses
        /// (<c>DESIGN_Decision_Layer.md</c> §4.3, "was hit within 5 s"; it was <c>AimAndFireExecutor</c>'s constant).</summary>
        public const float DefaultReturnFireWindowSeconds = 5f;

        /// <summary>⭐ <c>CE-2289</c> — how many commanders up the chain is read (a team → squad → platoon → company …); also ends a
        /// cycle in a mis-authored hierarchy.</summary>
        public const int MaxChainDepth = 8;

        /// <summary>⭐ <c>CE-2095</c> — the unit's ReturnFire window, defaults applied.</summary>
        public static float ReturnFireWindowSeconds(ISimulationView view, Entity unit)
            => Decide(view, unit, static r => r.ReturnFireWindowSeconds > 0f, out var roe) ? roe.ReturnFireWindowSeconds : DefaultReturnFireWindowSeconds;

        /// <summary>The unit's fire rule; <see cref="RoeFire.FireAtWill"/> when nothing in its chain set one.</summary>
        public static RoeFire Fire(ISimulationView view, Entity unit)
            => Decide(view, unit, static r => r.Fire != RoeFire.FireUnset, out var roe) ? roe.Fire : RoeFire.FireAtWill;

        /// <summary>⭐ <c>CE-2239</c> part 1 — the unit's force level; <see cref="RoeForceLevel.Lethal"/> when nothing in its chain set one.</summary>
        public static RoeForceLevel ForceLevel(ISimulationView view, Entity unit)
            => Decide(view, unit, static r => r.ForceLevel != RoeForceLevel.ForceUnset, out var roe) ? roe.ForceLevel : RoeForceLevel.Lethal;

        /// <summary>The unit's reaction rule; <see cref="RoeReactions.React"/> when nothing in its chain set one.</summary>
        public static RoeReactions Reactions(ISimulationView view, Entity unit)
            => Decide(view, unit, static r => r.Reactions != RoeReactions.ReactionsUnset, out var roe) ? roe.Reactions : RoeReactions.React;

        /// <summary>⭐ <c>CE-2289</c> — whose ROE decides one field (see the class remarks). <c>false</c> = nobody set it.</summary>
        private static bool Decide(ISimulationView view, Entity unit, Func<Roe, bool> isSet, out Roe source)
        {
            bool own = TryRoe(view, unit, out source) && isSet(source);
            if (own && source.SetBy != BehaviorOrigin.Unmarked) return true;   // an order on the man himself
            var tkb = source;

            var commander = CommanderOf(view, unit);
            for (int depth = 0; depth < MaxChainDepth && !commander.IsNull; depth++)
            {
                if (TryRoe(view, commander, out source) && source.SetBy != BehaviorOrigin.Unmarked && isSet(source)) return true;
                commander = CommanderOf(view, commander);
            }
            source = tkb;
            return own;                                                       // the man's TKB default, or nothing
        }

        private static bool TryRoe(ISimulationView view, Entity e, out Roe roe)
        {
            if (view.HasComponent<Roe>(e)) { roe = view.GetComponentRO<Roe>(e); return true; }
            roe = default;
            return false;
        }

        private static Entity CommanderOf(ISimulationView view, Entity e)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<UnitSubordinate>()) return Entity.Null;
            if (!view.HasComponent<UnitSubordinate>(e)) return Entity.Null;
            var commander = view.GetComponentRO<UnitSubordinate>(e).Commander;
            return !commander.IsNull && commander != e && view.IsAlive(commander) ? commander : Entity.Null;
        }
    }
}
