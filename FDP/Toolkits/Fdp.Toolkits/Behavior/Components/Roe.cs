using System.Runtime.InteropServices;
using Fdp.Core;
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
    }

    /// <summary>⭐ <c>CE-2074</c> — reading a unit's ROE with the defaults applied, in one place.</summary>
    public static class RoeOf
    {
        /// <summary>⭐ <c>CE-2095</c> — the ReturnFire window when nothing set one: the 5 s the design's own SOP example uses
        /// (<c>DESIGN_Decision_Layer.md</c> §4.3, "was hit within 5 s"; it was <c>AimAndFireExecutor</c>'s constant).</summary>
        public const float DefaultReturnFireWindowSeconds = 5f;

        /// <summary>⭐ <c>CE-2095</c> — the unit's ReturnFire window, defaults applied.</summary>
        public static float ReturnFireWindowSeconds(ISimulationView view, Entity unit)
        {
            if (!view.HasComponent<Roe>(unit)) return DefaultReturnFireWindowSeconds;
            float w = view.GetComponentRO<Roe>(unit).ReturnFireWindowSeconds;
            return w > 0f ? w : DefaultReturnFireWindowSeconds;
        }

        /// <summary>The unit's fire rule; <see cref="RoeFire.FireAtWill"/> when it has no ROE or it is unset.</summary>
        public static RoeFire Fire(ISimulationView view, Entity unit)
        {
            if (!view.HasComponent<Roe>(unit)) return RoeFire.FireAtWill;
            var fire = view.GetComponentRO<Roe>(unit).Fire;
            return fire == RoeFire.FireUnset ? RoeFire.FireAtWill : fire;
        }

        /// <summary>The unit's reaction rule; <see cref="RoeReactions.React"/> when it has no ROE or it is unset.</summary>
        public static RoeReactions Reactions(ISimulationView view, Entity unit)
        {
            if (!view.HasComponent<Roe>(unit)) return RoeReactions.React;
            var reactions = view.GetComponentRO<Roe>(unit).Reactions;
            return reactions == RoeReactions.ReactionsUnset ? RoeReactions.React : reactions;
        }
    }
}
