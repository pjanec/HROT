using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐ CE-2135 P4 (R-2004 — docs/DESIGN_Orders_Params_Entity_Classes.md §13.10 P4-d/P4-g) — the params DTOs the sample / test AiPrimitive
    //   blueprints name. A blueprint declares no Parameters: these fields ARE its Parameters, in this order, with these defaults
    //   (the values its Parameters carried). ⭐ CE-3194 (R-3002): a default is a field INITIALISER (+ a parameterless constructor), never
    //   `[ParamsDto(DefaultJson)]` (retired); an all-zero default needs neither. The generated `{Name}_{Id:X8}_Bp+Params` still mirrors them field for field, so the
    //   BTree / HSM hosts that bind it by name are unchanged.

    /// <summary><c>ChannelMoveAndWaitDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct ChannelMoveAndWaitDemoParams { public float BaselineX; public float BaselineY; }

    /// <summary><c>GateConditionDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct GateConditionDemoParams { public float Threshold; }

    /// <summary><c>ParamDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct ParamDemoParams { public int Threshold; public bool FlagA; public bool FlagB; }

    /// <summary><c>HsmDriveActivity</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmDriveActivityParams { public float DestX; public float DestY; }

    /// <summary><c>HsmFireActivity</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmFireActivityParams { public long TargetNetworkId; }

    /// <summary><c>HsmGuardDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmGuardDemoParams { public bool EngageTarget; }

    /// <summary>
    /// ⭐ <c>CE-3190</c> — <c>HsmChannelE2E</c>'s params: one field per hosted blueprint, typed as that blueprint's own named DTO.
    /// Each state / guard binds <c>Params.&lt;field&gt;</c>; the hosted blueprint reads the bytes as its generated <c>Params</c>,
    /// whose layout IS its named DTO's (the blueprint's Parameters are that DTO's fields, R-2004).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmChannelE2EParams
    {
        public HsmDriveActivityParams Drive;
        public HsmFireActivityParams Fire;
        public HsmGuardDemoParams Guard;
    }

    /// <summary><c>HsmTwoRegionParamsDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmTwoRegionParamsDemoParams { public int Value; }

    /// <summary><c>T40Resolver</c>'s params — the authored contract <c>T40_BehaviorResolverAsset</c> publishes (P4-e).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct T40ResolverParams { public float HalfSpeed = 1.5f; public T40ResolverParams() { } }

    /// <summary><c>HsmResolverDemoResolver</c>'s params — the authored contract <c>HsmResolverDemo</c> publishes (P4-e).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmResolverDemoParams { public float HalfSpeed = 1.5f; public HsmResolverDemoParams() { } }

    // ⭐ CE-2135 P4 ⑥ (R-2004) — the legacy BTree/HSM test fixtures' params DTOs: each asset names one (B3a form) instead of declaring
    //   Role=Input variables; the generated `<Asset>_Blackboard` JSON mirror is no longer their contract.

    /// <summary><c>T09_BlackboardManaged</c>'s params — the four values it used to declare as Input variables.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct T09Params
    {
        /// <summary>Max distance to engage the target.</summary>
        public float AttackRange;
        /// <summary>Patrol anchor the agent returns to.</summary>
        public global::System.Numerics.Vector3 HomePosition;
        /// <summary>Number of patrol cycles before idling.</summary>
        public int PatrolLoops;
        /// <summary>Set true when an enemy is spotted.</summary>
        public bool IsAlerted;
    }

    /// <summary><c>T20_MultiStateful</c>'s params — the three node params it bound as variables, at the same offsets (0 / 4 / 8);
    /// its nodes bind <c>Params.cursorA</c> / <c>Params.cursorB</c> / <c>Params.counter</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct T20Params
    {
        public DemoCounterNodes.DemoCursorParams cursorA = new() { Limit = 3 };
        public DemoCounterNodes.DemoCursorParams cursorB = new() { Limit = 5 };
        public DemoCounterNodes.DemoCounterParams counter = new() { Threshold = 1000 };
        public T20Params() { }
    }
}
