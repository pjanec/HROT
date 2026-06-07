using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;

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

    // ── DEMO — [SharedAiAction] non-channel action for AN7/AN8b palette + pin testing ─────────
    // DEMO-ACTIONS batch: this method is discovered by ActionSchemaExporter (which reflects all
    // loaded assemblies for [SharedAiAction]) and surfaced by IBehaviorActionCatalog as a
    // Blueprint-valid non-channel action (ActionHosting.Shared → BehaviorActionHosts.Blueprint).
    // The palette generator (AN7) then registers it as "Action:{FQN}", and NodePinSchema projects
    // DemoSharedActionParams fields as data-IN pins (including the DemoStance enum field, stamped
    // "global::" per AN6).
    //
    // NOTE: DO NOT add this action to any committed .bp.json blueprint that passes through the
    // MSBuild generator — the AiPrimitive(BlueprintCall) lowering path emits #error until AN8b.
    // The point is discovery + palette appearance + pin projection.
    //
    // REMOVABLE once a real [SharedAiAction] with an enum-field DTO exists in the project.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// DEMO blittable params struct for <see cref="DemoSharedActions.AlertNearbyUnits"/>.
    /// <para>
    /// This is the type passed as <c>ref TDto</c> to the action method.
    /// <c>ActionSchemaExporter.ExtractFirstRefParamType</c> extracts it from the first
    /// <c>ref</c> parameter and stores it as <c>ActionSchemaEntry.DtoType</c>, which
    /// <c>BehaviorActionCatalog</c> exposes as <c>BehaviorActionEntry.ParamsTypeFqn</c>, and
    /// <c>NodePinSchema.ReflectDataMembers</c> reflects to project data-IN pins.
    /// </para>
    /// <para>
    /// Layout: float (4) + int/DemoStance (4) + int (4) = 12 bytes — well within the
    /// 32-byte <c>BehaviorConstants.ActionParamsByteSize</c> limit.
    /// </para>
    /// <para>
    /// Contains a <see cref="DemoStance"/> enum field so that AN6 enum pins are exercised on
    /// a <b>non-channel</b> action (AN7 path) in addition to the channel-command demo.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DemoSharedActionParams
    {
        /// <summary>Alert radius in metres.</summary>
        public float AlertRadius;

        /// <summary>
        /// DEMO enum field: the pin editor renders a <c>Standing / Crouching / Prone</c>
        /// combo (AN6 <c>EnumPinEditor</c>) because <c>NodePinSchema.ReflectDataMembers</c>
        /// stamps this field's TypeId with <c>"global::"</c>.
        /// </summary>
        public DemoStance PostureHint;

        /// <summary>Maximum number of units to alert (int, plain value pin).</summary>
        public int MaxUnits;
    }

    /// <summary>
    /// DEMO parent blackboard-slot container used as the <c>DtoType</c> argument to
    /// <c>[SharedAiAction]</c>.  The source generator validates that the <c>ref</c> param
    /// type of the annotated method matches the type of <c>Params</c> field here.
    /// This struct is never used at runtime; it exists solely as the attribute argument.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DemoBlackboardSlot
    {
        /// <summary>The projected field — type must match the action method's <c>ref</c> param.</summary>
        public DemoSharedActionParams Params;
    }

    /// <summary>
    /// DEMO static action host for DEMO-ACTIONS batch.
    /// Provides one <c>[SharedAiAction]</c> method discovered by <c>ActionSchemaExporter</c>
    /// and surfaced by <c>IBehaviorActionCatalog</c> as a Blueprint-valid non-channel action.
    /// </summary>
    public static class DemoSharedActions
    {
        /// <summary>
        /// DEMO [SharedAiAction] — alerts nearby units within <c>p.AlertRadius</c> metres,
        /// using <c>p.PostureHint</c> to filter by posture and limited to <c>p.MaxUnits</c>.
        /// <para>
        /// This method exists solely for palette + pin projection testing (AN7/AN8b).
        /// The body is a no-op; the method does nothing at runtime.
        /// </para>
        /// <para>
        /// Attribute: <c>[SharedAiAction(typeof(DemoBlackboardSlot), "Params")]</c> where
        /// <c>DemoBlackboardSlot.Params</c> is of type <c>DemoSharedActionParams</c> — the
        /// analyzer validates <c>ref DemoSharedActionParams</c> matches the field type.
        /// The first <c>ref</c> param type (<see cref="DemoSharedActionParams"/>) is extracted
        /// by <c>ActionSchemaExporter.ExtractFirstRefParamType</c> and becomes the catalog's
        /// <c>ParamsTypeFqn</c>, which <c>NodePinSchema</c> reflects to produce data-IN pins.
        /// Hosting: <c>ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared</c>
        /// → <c>BehaviorActionHosts.Blueprint</c> via <c>BehaviorActionCatalog.MapHosting</c>.
        /// </para>
        /// <para>
        /// DO NOT add to a committed blueprint asset — will <c>#error</c> until AN8b implements
        /// the non-channel <c>AiPrimitive(BlueprintCall)</c> lowering path.
        /// </para>
        /// </summary>
        [SharedAiAction(typeof(DemoBlackboardSlot), nameof(DemoBlackboardSlot.Params))]
        public static NodeStatus AlertNearbyUnits(
            ref DemoSharedActionParams p,
            Entity self,
            EntityRepository world)
        {
            // DEMO no-op body — palette + pin projection only.
            _ = p;
            _ = self;
            _ = world;
            return NodeStatus.Success;
        }
    }
}
