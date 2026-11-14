using Fdp.Core;
using Fdp.Toolkit.Combat.Components;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ <c>Q94 A-3</c> (R-3018) — a stand-in for the muscle in a brain-only test world: every round a weapon has ASKED and not yet had
    /// answered is answered FIRED (<see cref="ShotLedger.Answer"/>), so a test that drives an executor and then counts the ammunition sees
    /// what the brain sees once the real muscle's notifications arrive. A test of the asking itself (reserved, not yet spent; a refusal)
    /// does not call it.
    /// </summary>
    internal static class TestMuscle
    {
        public static void FireAll(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<WeaponState>()) return;
            foreach (var e in world.Query().With<WeaponState>().Build())
            {
                ref var w = ref world.GetComponentRW<WeaponState>(e);
                while (ShotLedger.Pending(w) > 0)
                    ShotLedger.Answer(world, e, ref w, unchecked(w.LastAnsweredId + 1), FireRefusal.None);
            }
        }
    }
}
