using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Behavior.Demo
{
    // ── DEMO — enum-parameter action for live AN6 enum-pin editor testing ─────────────────────
    // These types exist solely so the Blueprint editor's AN6 EnumPinEditor (enum combo) can be
    // exercised end-to-end: palette drop → Stance enum combo renders → value persists →
    // compiles to (global::Fdp.Toolkit.Behavior.Demo.DemoStance)N.
    //
    // Runtime no-op: there is no executor registered for ActionId 99 on LocomotionChannel.
    // REMOVABLE once a real enum-field param struct exists in the project.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// DEMO enum for AN6 enum-pin editor testing.
    /// Int-backed (size 4) — matches the AN2 compiler assumption for unmanaged enum pins.
    /// </summary>
    public enum DemoStance : int
    {
        Standing  = 0,
        Crouching = 1,
        Prone     = 2,
    }

    /// <summary>
    /// DEMO blittable param struct for the <c>DemoEnumAction</c> catalog entry (ActionId 99,
    /// LocomotionChannel). Contains an enum field (<see cref="DemoStance"/>) so the AN6
    /// <c>EnumPinEditor</c> combo is exercised when the node is selected on the canvas.
    /// <para>
    /// Layout: Vector3 (12) + int/DemoStance (4) + int (4) = 20 bytes — well within the
    /// 32-byte <c>BehaviorConstants.ActionParamsByteSize</c> limit.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DemoEnumActionParams
    {
        /// <summary>Dummy target position (metres, Sim Z-up).</summary>
        public Vector3 TargetPos;

        /// <summary>
        /// DEMO enum field: the pin editor should render a <c>Standing / Crouching / Prone</c> combo
        /// (AN6 <c>EnumPinEditor</c>) for this field because its TypeId is stamped with the
        /// <c>"global::"</c> prefix by <c>NodePinSchema.ReflectDataMembers</c>.
        /// </summary>
        public DemoStance Stance;

        /// <summary>Repeat count (int, plain value pin — verifies non-enum pins project normally).</summary>
        public int Repeat;
    }
}
