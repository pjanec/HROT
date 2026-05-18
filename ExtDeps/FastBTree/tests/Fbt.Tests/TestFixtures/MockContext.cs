using System.Numerics;
using Fbt;

namespace Fbt.Tests.TestFixtures
{
    public struct MockContext : IAIContext
    {
        // BHU-012: Self and World allow the BTree source generator to assign SharedAi entries
        // to the (TestBlackboard, MockContext) group. Types are int here (test stand-in for
        // Entity/EntityRepository in production).
        public int Self;
        public int World;

        public float DeltaTime { get; set; }
        public int CallCount;
        public int ActionCallCount; // Used in tests
        public int PathRequestCount; // Used in tests
        public int AnimationTriggerCount; // Used in tests
        public bool NextEntityAlive; // Used in tests
        public float NextEntityDistance; // Used in tests
        public float SimulatedDeltaTime; // Used in tests
        
        // IAIContext implementation
        // IAIContext implementation
        public float Time { get; set; }
        public int FrameCount { get; set; }
        
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance)
            => 0;
        
        public RaycastResult GetRaycastResult(int requestId)
            => new RaycastResult { IsReady = true };
        
        public int RequestPath(Vector3 from, Vector3 to)
        {
            PathRequestCount++;
            return 0;
        }
        
        public PathResult GetPathResult(int requestId)
            => new PathResult { IsReady = true, Success = true };
        
        public float GetFloatParam(int index) => 1.0f;
        public int GetIntParam(int index) => 1;
    }
}
