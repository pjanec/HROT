using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐ CE-2135 P4 (R-2004 — docs/DESIGN_Orders_Params_Entity_Classes.md §13.10 P4-d/P4-g) — the params DTOs the sample / test AiPrimitive
    //   blueprints name. A blueprint declares no Parameters: these fields ARE its Parameters, in this order, with these defaults
    //   (the values its Parameters carried). The generated `{Name}_{Id:X8}_Bp+Params` still mirrors them field for field, so the
    //   BTree / HSM hosts that bind it by name are unchanged.

    /// <summary><c>ChannelMoveAndWaitDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto(DefaultJson = "{\"BaselineX\":0,\"BaselineY\":0}")]
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
    [ParamsDto(DefaultJson = "{\"DestX\":0,\"DestY\":0}")]
    public struct HsmDriveActivityParams { public float DestX; public float DestY; }

    /// <summary><c>HsmFireActivity</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto(DefaultJson = "{\"TargetNetworkId\":0}")]
    public struct HsmFireActivityParams { public long TargetNetworkId; }

    /// <summary><c>HsmGuardDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto(DefaultJson = "{\"EngageTarget\":false}")]
    public struct HsmGuardDemoParams { public bool EngageTarget; }

    /// <summary><c>HsmTwoRegionParamsDemo</c>'s params.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto]
    public struct HsmTwoRegionParamsDemoParams { public int Value; }

    /// <summary><c>T40Resolver</c>'s params — the authored contract <c>T40_BehaviorResolverAsset</c> publishes (P4-e).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto(DefaultJson = "{\"HalfSpeed\":1.5}")]
    public struct T40ResolverParams { public float HalfSpeed; }

    /// <summary><c>HsmResolverDemoResolver</c>'s params — the authored contract <c>HsmResolverDemo</c> publishes (P4-e).</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ParamsDto(DefaultJson = "{\"HalfSpeed\":1.5}")]
    public struct HsmResolverDemoParams { public float HalfSpeed; }

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
    [ParamsDto(DefaultJson = "{\"cursorA\":{\"Limit\":3},\"cursorB\":{\"Limit\":5},\"counter\":{\"Counter\":0,\"Threshold\":1000}}")]
    public struct T20Params
    {
        public DemoCounterNodes.DemoCursorParams cursorA;
        public DemoCounterNodes.DemoCursorParams cursorB;
        public DemoCounterNodes.DemoCounterParams counter;
    }
}
