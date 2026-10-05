using System.Runtime.InteropServices;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>⭐ <c>CE-2080</c> — params of <see cref="SopConditions.SensedWithin"/>: which sensing change, how recently.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SopSenseParams
    {
        /// <summary>The change to look for (Hit, FirstThreat, Acquired, …).</summary>
        public SensorChange Kind;
        /// <summary>How recent, in sim seconds.</summary>
        public float Seconds;
    }

    /// <summary>⭐ <c>CE-2080</c> — params of the ROE conditions: the fire rule to compare against.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SopRoeParams
    {
        /// <summary>The threshold — ordered most to least restrictive (HoldFire &lt; ReturnFire &lt; FireAtWill).</summary>
        public RoeFire Fire;
    }

    /// <summary>⭐ <c>CE-2104</c> — params of <see cref="SopConditions.InContact"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SopContactParams
    {
        /// <summary>How long the mode outlasts the contact: still true this many sim seconds after <c>AllClear</c>
        /// (0 = ends with the contact).</summary>
        public float LingerSeconds;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2080</c> — the conditions an SOP's rows ask (R-199, R-200): what the unit sensed lately, and what its ROE
    /// allows. Shared C# conditions, so any BTree / HSM asset binds them. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.3–§4.4.
    /// <para>They read STATE, not events: <see cref="RecentSenses"/> (written from <c>SensorChangedEvent</c>, which lives one
    /// frame) and <see cref="Roe"/> through <see cref="RoeOf"/> (defaults applied).</para>
    /// </summary>
    public static class SopConditions
    {
        /// <summary>The unit sensed a change of <see cref="SopSenseParams.Kind"/> within the last
        /// <see cref="SopSenseParams.Seconds"/> — "was hit within 5 s".</summary>
        [SharedAiCondition]
        public static bool SensedWithin(ref SopSenseParams p, Entity self, EntityRepository world)
            => RecentSensesOf.Within(world, self, p.Kind, p.Seconds);

        /// <summary>⭐ <see cref="SensedWithin"/> AND sensed since the unit's TASK SLOT began its current run
        /// (<see cref="BehaviorState.RunSince"/>) — so an event that already caused a reaction does not fire it again once the
        /// reaction ends and the task restarts. ⭐ The row a recipe uses.</summary>
        [SharedAiCondition]
        public static bool SensedFresh(ref SopSenseParams p, Entity self, EntityRepository world)
        {
            double since = world.HasComponent<BehaviorState>(self) ? world.GetComponentRO<BehaviorState>(self).RunSince : double.NegativeInfinity;
            return RecentSensesOf.WithinSince(world, self, p.Kind, p.Seconds, since);
        }

        /// <summary>
        /// ⭐ <c>CE-2104</c> — the unit is IN CONTACT: its threat memory holds a contact (seen or heard), or it emptied less than
        /// <see cref="SopContactParams.LingerSeconds"/> ago. The "alerted" mode of any SOP, DERIVED from state the perception
        /// already keeps — on with <c>FirstThreat</c>, off with <c>AllClear</c> (+ linger) — so there is no flag to latch, save
        /// or replay. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.7 (CE-2104).
        /// </summary>
        [SharedAiCondition]
        public static bool InContact(ref SopContactParams p, Entity self, EntityRepository world)
            => (world.HasComponent<TargetMemory>(self) && world.GetComponentRO<TargetMemory>(self).Count > 0)
               || (p.LingerSeconds > 0f && RecentSensesOf.Within(world, self, SensorChange.AllClear, p.LingerSeconds));

        /// <summary>The unit's ROE fire rule is at least as permissive as <see cref="SopRoeParams.Fire"/> — "may return fire".</summary>
        [SharedAiCondition]
        public static bool RoeFireAtLeast(ref SopRoeParams p, Entity self, EntityRepository world)
            => RoeOf.Fire(world, self) >= p.Fire;

        /// <summary>The unit's ROE fire rule is at most as permissive as <see cref="SopRoeParams.Fire"/> — "must hold fire".</summary>
        [SharedAiCondition]
        public static bool RoeFireAtMost(ref SopRoeParams p, Entity self, EntityRepository world)
            => RoeOf.Fire(world, self) <= p.Fire;
    }
}
