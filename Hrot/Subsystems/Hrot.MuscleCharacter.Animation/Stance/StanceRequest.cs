using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.MuscleCharacter.Animation.Stance
{
    /// <summary>
    /// ⭐ <c>CE-2121</c> — the Brain's one way to ask for a body stance: writes <see cref="StanceIntent"/> and bumps its
    /// <c>Version</c> ONLY when the target changes, so a behaviour may call it every tick. The Muscle
    /// (<see cref="Systems.StanceTransitionSystem"/>) performs it and reports in <see cref="StanceStatus"/>; the Brain does not
    /// wait for that (🔒 user <c>2026-10-07</c>, leans approved). 📄 docs/DESIGN_Decision_Layer.md §3.3g.
    /// </summary>
    public static class StanceRequest
    {
        /// <summary>Asks for <paramref name="stance"/> over <paramref name="blendSeconds"/>. False when the unit cannot carry a
        /// stance (no <see cref="StanceIntent"/> — no animation definition, or the type is not registered on this node).</summary>
        public static bool Set(EntityRepository world, Entity self, StanceId stance, float blendSeconds)
        {
            if (!world.IsComponentTypeRegistered<StanceIntent>() || !world.HasComponent<StanceIntent>(self)) return false;
            ref var intent = ref world.GetComponentRW<StanceIntent>(self);
            if (intent.TargetStance == stance) return true;
            intent.TargetStance = stance;
            intent.BlendTime = blendSeconds;
            intent.Version++;
            return true;
        }
    }
}
