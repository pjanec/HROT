using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3224</c> (civilians B-6, CV-13, R-2024; docs/DESIGN_Civilians_And_Crowd_Control.md §7.7) — what a person is doing with his
    /// arms. ⛔ Not a hit effect (<see cref="EffectState"/>: what was done TO him) and not a stance (locomotion): his own act, chosen by
    /// his behaviour.
    /// </summary>
    public enum GestureKind : byte
    {
        None = 0,
        HandsUp = 1,
        FistRaised = 2,
        HoldSign = 3,
        Waving = 4,
        Throwing = 5,
        /// <summary>⭐ <c>CE-2239</c> part 3 (B2, §5k EL-3) — a security-force WARNING: the audible cue of the escalation ladder ("drop
        /// it!"), shown for a couple of seconds by the warner.</summary>
        Warning = 6,
        /// <summary>⭐ <c>CE-2368</c> (AQ103 I, R-2074) — IDLE: a relaxed man smoking. ⭐ The idle kinds (from <see cref="IdleSmoking"/> on) are
        /// not an act of his behaviour but his MOOD shown — written by <c>RestlessSystem</c> while he is idle and calm, for the IG to play an
        /// idle animation; any act or threat replaces them at once.</summary>
        IdleSmoking = 7,
        /// <summary>⭐ <c>CE-2368</c> — IDLE: stepping from one leg to the other.</summary>
        IdleShiftWeight = 8,
        /// <summary>⭐ <c>CE-2368</c> — IDLE: a stretch (arms, back, neck).</summary>
        IdleStretch = 9,
        /// <summary>⭐ <c>CE-2368</c> — IDLE: looking about, unhurried (hands on the weapon or in the pockets).</summary>
        IdleLookAbout = 10,
    }

    /// <summary>
    /// ⭐ <c>CE-3224</c> — a person's current gesture, written by his behaviour on the brain owner, replicated as <c>dtGesture</c> (143) and
    /// recorded like any component. Absent ⇒ <see cref="GestureKind.None"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Gesture)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Gesture
    {
        public GestureKind Kind;
    }

    /// <summary>⭐ <c>CE-3224</c> — the one writer and the one reader of <see cref="Gesture"/>.</summary>
    public static class Gestures
    {
        /// <summary>Sets <paramref name="unit"/>'s gesture; call it on the node that owns his brain (and so <c>dtGesture</c>). Setting None on
        /// a unit that never gestured adds nothing. False when the type is not registered or the unit is gone.</summary>
        public static bool Set(EntityRepository repo, Entity unit, GestureKind kind)
        {
            if (!repo.IsAlive(unit) || !repo.IsComponentTypeRegistered<Gesture>()) return false;
            if (!repo.HasComponent<Gesture>(unit))
            {
                if (kind == GestureKind.None) return true;
                repo.AddComponent(unit, new Gesture { Kind = kind });
                return true;
            }
            repo.GetComponentRW<Gesture>(unit).Kind = kind;
            return true;
        }

        /// <summary>⭐ <c>CE-2368</c> — <paramref name="kind"/> is an IDLE mood (smoking, shifting weight…), not an act of his behaviour.</summary>
        public static bool IsIdle(GestureKind kind) => kind >= GestureKind.IdleSmoking && kind <= GestureKind.IdleLookAbout;

        /// <summary>The unit's gesture on this node (the owner's or the replicated one); None when it has none.</summary>
        public static GestureKind Of(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Gesture>()) return GestureKind.None;
            return view.IsAlive(unit) && view.HasComponent<Gesture>(unit) ? view.GetComponentRO<Gesture>(unit).Kind : GestureKind.None;
        }
    }
}
