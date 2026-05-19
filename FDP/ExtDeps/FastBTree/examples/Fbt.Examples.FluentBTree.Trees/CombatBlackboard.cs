using System.Runtime.InteropServices;
using Fbt;
using System.Numerics;

namespace Fbt.Examples.FluentBTree
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CombatBlackboard
    {
        public int AmmoCount;
        [MarshalAs(UnmanagedType.U1)]
        public bool ThreatVisible;
        // Padding: 3 bytes to align EngagementRange at offset 8
        public byte _pad0, _pad1, _pad2;
        public float EngagementRange;
    }

    public struct CombatContext : IAIContext, ITreeTracer
    {
        public float DeltaTime { get; set; }
        public float Time { get; set; }
        public int FrameCount { get; set; }

        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new RaycastResult { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new PathResult { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 0f;
        public int GetIntParam(int index) => 0;

        // ITreeTracer (no-op — examples do not record traces).
        public void TraceNodeEvaluated(int nodeIndex, NodeStatus status) { }
        public void TraceScopePushed(ushort newStackDepth) { }
        public void TraceScopePopped(ushort newStackDepth) { }
        public void TraceWaitStarted(int nodeIndex, float duration) { }
        public void TraceWaitCompleted(int nodeIndex, float duration) { }
    }
}
