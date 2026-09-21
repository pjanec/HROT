using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>P2</c> — THE FIRST DTO-BOUND HSM ACTION THIS REPO HAS EVER SHIPPED WITH A
    /// LIVE DISPATCH PATH.</b> 📄 <c>PLAN_Occurrence_Storage_Build.md</c> "THE PATH" <c>P2</c>;
    /// closes <c>BP-297</c>.
    ///
    /// <para>🔴🔴 <b>Why it did not exist before, in <c>BP-297</c>'s own words:</b> <i>"the pre-written
    /// rail cannot be made to fail before the change — <c>HsmOrthogonalRegions</c>' two regions both
    /// run <c>StubIdle</c>, whose body is EMPTY ⇒ there are no bytes to collide."</i> ⛔ Every existing
    /// DTO-bound <c>[SharedAi*]</c> method lives in <c>Fdp.Toolkits</c>, which does not run
    /// <c>HsmActionGenerator</c> — so <b>zero DTO-bound HSM thunks existed in any binary</b> and the
    /// collision had no subject.</para>
    ///
    /// <para>⭐⭐ <b>Authoring this IS the trigger, not a cost.</b> The moment it exists,
    /// <c>NoNewDtoBoundHsmAction_ExistsInAGeneratorBearingAssembly</c> reddens — deliberately, and by
    /// its own message: <i>"This is NOT a ban — it means the change now needs <c>E3</c>."</i> ⇒ the
    /// subject and the fix are ONE slice, which is why they land in one commit.</para>
    ///
    /// <para>⭐ <b>Two actions on ONE slot struct, at DIFFERENT field offsets</b>, so the compound key
    /// <c>fqn@offset</c> is exercised at a non-zero offset too — all four previously-baselined
    /// entries sat at <c>@0</c>, which would have left the offset half of the identity unproven.</para>
    /// </summary>
    public static class HsmTwoRegionCuratedNodes
    {
        /// <summary>
        /// ⭐ The params DTO one region's action reads. Blittable and tiny — what is under test is
        /// WHICH occurrence's bytes the thunk reaches, not what the body computes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CuratedRegionParams
        {
            /// <summary>Seeded from the bound blackboard variable; the rail compares this.</summary>
            public int Value;
        }

        /// <summary>
        /// ⭐⭐ The SLOT struct — the layout declaration <c>[SharedAiAction]</c> names. The attribute's
        /// field offset is this field's position INSIDE this struct, never a blackboard address,
        /// which is why the compound key survives the params move with only its anchor changed.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct CuratedRegionSlot
        {
            /// <summary>Bound by <see cref="Action_ReadRegionParams"/> — offset 0.</summary>
            public CuratedRegionParams Params;

            /// <summary>Bound by <see cref="Action_ReadSecondParams"/> — a NON-ZERO offset.</summary>
            public CuratedRegionParams Second;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The subject.</b> A hand-authored HSM action that takes its params DTO BY REFERENCE
        /// and never looks for it — 🔒 the user's ruling, <c>2026-09-21</c>: <i>"Action should get the
        /// param dto reference, not actively looking for it."</i> ⭐ The generated thunk does the
        /// occurrence lookup; this body only reads what it is handed.
        /// </summary>
        [SharedAiAction(typeof(CuratedRegionSlot), nameof(CuratedRegionSlot.Params))]
        public static NodeStatus Action_ReadRegionParams(
            ref CuratedRegionParams dto, Entity self, EntityRepository world)
        {
            // ⛔ Deliberately side-effect free: two parallel regions running THIS action must be
            //    distinguishable by their params alone, so the body must not write anywhere shared.
            return dto.Value != 0 ? NodeStatus.Success : NodeStatus.Failure;
        }

        /// <summary>
        /// ⭐ The same shape at a NON-ZERO field offset, so <c>fqn@offset</c> is proven to
        /// discriminate. ⚠ Without this every shipped compound key would still be <c>@0</c> and the
        /// offset half of the identity would be untested.
        /// </summary>
        [SharedAiAction(typeof(CuratedRegionSlot), nameof(CuratedRegionSlot.Second))]
        public static NodeStatus Action_ReadSecondParams(
            ref CuratedRegionParams dto, Entity self, EntityRepository world)
        {
            return dto.Value != 0 ? NodeStatus.Success : NodeStatus.Failure;
        }
    }
}
