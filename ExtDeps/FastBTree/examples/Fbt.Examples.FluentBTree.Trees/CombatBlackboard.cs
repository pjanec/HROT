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

    public struct CombatContext : IAIContext
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
    }
}
