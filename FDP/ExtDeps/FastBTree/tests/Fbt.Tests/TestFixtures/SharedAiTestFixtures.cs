using Fbt;
using Fbt.Kernel;
using Fbt.Runtime;
using System.Runtime.InteropServices;
using Xunit;

namespace Fbt.Tests.TestFixtures
{
    // ---- BHU-012 SharedAi test blackboard --------------------------------------
    // Minimal unsafe blackboard struct used by the generator tests below.
    public unsafe struct SharedAiTestBlackboard
    {
        public fixed byte Memory[64];
    }

    // ---- BHU-012 test context: must have Self and World so the generator
    //      assigns SharedAi entries to the (SharedAiTestBlackboard, SharedAiTestContext) group.
    public struct SharedAiTestContext : IAIContext
    {
        public int Self;    // Required: generator checks for this member
        public int World;   // Required: SharedAi thunks call Method(ref field, ctx.Self, ctx.World)

        public float DeltaTime    => 0;
        public float Time         => 0;
        public int   FrameCount   => 0;
        public int   RequestRaycast(System.Numerics.Vector3 o, System.Numerics.Vector3 d, float m) => 0;
        public RaycastResult GetRaycastResult(int rid) => new RaycastResult { IsReady = true };
        public int RequestPath(System.Numerics.Vector3 f, System.Numerics.Vector3 t) => 0;
        public PathResult GetPathResult(int rid) => default;
        public float GetFloatParam(int index) => 0f;
        public int   GetIntParam(int index) => 0;
    }

    // ---- BHU-012 sequential-layout DTO struct (offset = 0 for A, 4 for B) ----
    public struct SequentialDto
    {
        public int   FieldA;   // offset 0
        public float FieldB;   // offset 4
    }

    // ---- BHU-012 explicit-layout DTO struct ------------------------------------
    [StructLayout(LayoutKind.Explicit)]
    public struct ExplicitDto
    {
        [FieldOffset(12)] public float FieldAt12;
    }

    // ---- BHU-012 annotated SharedAi test actions --------------------------------
    // A 4-param action creates the (SharedAiTestBlackboard, SharedAiTestContext) group.
    public static class SharedAiTestActions
    {
        [BTreeAction]
        public static NodeStatus GroupAnchorAction(
            ref SharedAiTestBlackboard bb,
            ref BehaviorTreeState state,
            ref SharedAiTestContext ctx,
            int paramIndex)
            => NodeStatus.Success;

        // Registered as condition under compound key "SequentialCondition@4" (FieldB at offset 4)
        // Returns bool: BTree adapter wraps in ternary, HSM uses directly as guard.
        [SharedAiCondition(typeof(SequentialDto), "FieldB")]
        public static bool SequentialCondition(ref float field, int self, int world)
            => field > 0f;

        // Registered as action under compound key "ExplicitAction@12" (FieldAt12 at offset 12)
        [SharedAiAction(typeof(ExplicitDto), "FieldAt12")]
        public static NodeStatus ExplicitAction(ref float field, int self, int world)
        {
            field = 1f;
            return NodeStatus.Success;
        }
    }
}
