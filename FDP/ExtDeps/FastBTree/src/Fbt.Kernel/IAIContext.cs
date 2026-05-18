using System.Numerics;

namespace Fbt
{
    /// <summary>
    /// Context providing external services to behavior tree nodes.
    /// Allows for testability (mock implementations).
    /// </summary>
    public interface IAIContext
    {
        // ===== Time =====
        float DeltaTime { get; }
        float Time { get; }
        int FrameCount { get; }
        
        // ===== Physics Queries (Batched) =====
        int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance);
        RaycastResult GetRaycastResult(int requestId);
        
        // ===== Pathfinding (Batched) =====
        int RequestPath(Vector3 from, Vector3 to);
        PathResult GetPathResult(int requestId);
        
        // ===== Parameter Lookup =====
        float GetFloatParam(int index);
        int GetIntParam(int index);
    }
}
