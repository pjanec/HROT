# FastBTree Context & External Systems

**Version:** 1.0.0  
**Date:** 2026-01-04

---

## 1. Overview

The **Context System** provides an abstraction layer between behavior tree logic and external systems (physics, pathfinding, animation, etc.). This achieves:

- **Testability:** Mock implementations for unit tests
- **Determinism:** Controlled input for golden run tests
- **Batching:** Group expensive queries for parallel processing
- **Decoupling:** BT logic independent of engine specifics

---

## 2. Core Interface

### 2.1 IAIContext

```csharp
using System.Numerics;

namespace Fbt
{
    /// <summary>
    /// Context providing external services to behavior tree nodes.
    /// Implementations: GameContext (runtime), MockContext (testing), ReplayContext (golden runs).
    /// </summary>
    public interface IAIContext
    {
        // ===== TIMING =====
        
        /// <summary>Delta time for this frame (seconds).</summary>
        float DeltaTime { get; }
        
        /// <summary>Total elapsed time (seconds).</summary>
        float Time { get; }
        
        /// <summary>Current frame number.</summary>
        int FrameCount { get; }
        
        // ===== PHYSICS QUERIES (BATCHED) =====
        
        /// <summary>
        /// Request a raycast (batched, processed at end of frame).
        /// Returns request ID for polling.
        /// </summary>
        int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask = -1);
        
        /// <summary>Get status/result of raycast request.</summary>
        RaycastResult GetRaycastResult(int requestId);
        
        /// <summary>Request a sphere overlap query (batched).</summary>
        int RequestOverlapSphere(Vector3 center, float radius, int layerMask = -1);
        
        /// <summary>Get result of overlap query.</summary>
        OverlapResult GetOverlapResult(int requestId);
        
        // ===== PATHFINDING (BATCHED) =====
        
        /// <summary>Request a path from A to B (batched).</summary>
        int RequestPath(Vector3 from, Vector3 to);
        
        /// <summary>Get status/result of path request.</summary>
        PathResult GetPathResult(int requestId);
        
        /// <summary>Cancel a pathfinding request.</summary>
        void CancelPathRequest(int requestId);
        
        // ===== RANDOM (DETERMINISTIC) =====
        
        /// <summary>Random integer in [min, max) range.</summary>
        int RandomInt(int min, int max);
        
        /// <summary>Random float in [min, max) range.</summary>
        float RandomFloat(float min, float max);
        
        // ===== ENTITY QUERIES =====
        
        /// <summary>Get entity's world position.</summary>
        Vector3 GetEntityPosition(int entityId);
        
        /// <summary>Get entity's forward direction.</summary>
        Vector3 GetEntityForward(int entityId);
        
        /// <summary>Check if entity is alive.</summary>
        bool IsEntityAlive(int entityId);
        
        /// <summary>Get distance between two entities.</summary>
        float GetEntityDistance(int entityA, int entityB);
        
        // ===== ACTIONS/COMMANDS =====
        
        /// <summary>Trigger animation on entity.</summary>
        void TriggerAnimation(int entityId, string animationName);
        
        /// <summary>Deal damage to target entity.</summary>
        void DealDamage(int targetId, float amount);
        
        /// <summary>Move entity along computed path.</summary>
        void MoveAlongPath(int entityId, int pathId, float speed);
        
        /// <summary>Stop entity movement.</summary>
        void StopMovement(int entityId);
        
        // ===== PARAMETER LOOKUP =====
        
        /// <summary>Get float parameter by index (from BehaviorTreeBlob).</summary>
        float GetFloatParam(int index);
        
        /// <summary>Get int parameter by index.</summary>
        int GetIntParam(int index);
        
        // ===== BATCH PROCESSING =====
        
        /// <summary>
        /// Called at the end of frame to process all batched requests.
        /// Game code should call this after all BTs have ticked.
        /// </summary>
        void ProcessBatch();
        
        /// <summary>Clear completed requests (housekeeping).</summary>
        void ClearCompletedRequests();
    }
}
```

---

## 3. Result Structures

### 3.1 RaycastResult

```csharp
namespace Fbt
{
    public struct RaycastResult
    {
        /// <summary>Is the result ready? (False if still processing)</summary>
        public bool IsReady;
        
        /// <summary>Did the raycast hit something?</summary>
        public bool Hit;
        
        /// <summary>Entity ID of hit object (0 if terrain/static).</summary>
        public int HitEntityId;
        
        /// <summary>World position of hit point.</summary>
        public Vector3 HitPoint;
        
        /// <summary>Surface normal at hit point.</summary>
        public Vector3 HitNormal;
        
        /// <summary>Distance to hit point.</summary>
        public float Distance;
    }
}
```

### 3.2 PathResult

```csharp
namespace Fbt
{
    public struct PathResult
    {
        /// <summary>Is the path computation complete?</summary>
        public bool IsReady;
        
        /// <summary>Was a valid path found?</summary>
        public bool Success;
        
        /// <summary>Handle to the computed path (for movement).</summary>
        public int PathId;
        
        /// <summary>Path length (meters).</summary>
        public float PathLength;
        
        /// <summary>Is the path blocked/invalid now?</summary>
        public bool IsBlocked;
    }
}
```

### 3.3 OverlapResult

```csharp
namespace Fbt
{
    public unsafe struct OverlapResult
    {
        /// <summary>Is the query complete?</summary>
        public bool IsReady;
        
        /// <summary>Number of entities found.</summary>
        public int HitCount;
        
        /// <summary>Entity IDs of overlapping entities (max 16).</summary>
        public fixed int HitEntityIds[16];
        
        /// <summary>Helper to get hit entity by index.</summary>
        public int GetHitEntity(int index)
        {
            if (index < 0 || index >= HitCount || index >= 16)
                return 0;
            return HitEntityIds[index];
        }
    }
}
```

---

## 4. Context Implementations

### 4.1 GameContext (Runtime)

```csharp
using System.Numerics;
using System.Collections.Generic;

namespace Fbt.Runtime
{
    /// <summary>
    /// Production context that interfaces with real game systems.
    /// </summary>
    public struct GameContext : IAIContext
    {
        // === State ===
        private float _deltaTime;
        private float _totalTime;
        private int _frameCount;
        
        // === External System References ===
        // (In real implementation, these would be injected)
        private IPhysicsWorld _physics;
        private IPathfindingSystem _pathfinding;
        private IEntityManager _entities;
        private IAnimationSystem _animations;
        private BehaviorTreeBlob _currentBlob; // For parameter lookup
        
        // === Batching ===
        private Dictionary<int, RaycastRequest> _pendingRaycasts;
        private Dictionary<int, RaycastResult> _raycastResults;
        private Dictionary<int, PathRequest> _pendingPaths;
        private Dictionary<int, PathResult> _pathResults;
        
        private int _nextRequestId;
        
        // === Initialization ===
        
        public GameContext(
            IPhysicsWorld physics,
            IPathfindingSystem pathfinding,
            IEntityManager entities,
            IAnimationSystem animations)
        {
            _physics = physics;
            _pathfinding = pathfinding;
            _entities = entities;
            _animations = animations;
            
            _pendingRaycasts = new Dictionary<int, RaycastRequest>();
            _raycastResults = new Dictionary<int, RaycastResult>();
            _pendingPaths = new Dictionary<int, PathRequest>();
            _pathResults = new Dictionary<int, PathResult>();
            
            _nextRequestId = 1;
            _deltaTime = 0;
            _totalTime = 0;
            _frameCount = 0;
            _currentBlob = null;
        }
        
        public void BeginFrame(float deltaTime, BehaviorTreeBlob blob)
        {
            _deltaTime = deltaTime;
            _totalTime += deltaTime;
            _frameCount++;
            _currentBlob = blob;
        }
        
        // === IAIContext Implementation ===
        
        public float DeltaTime => _deltaTime;
        public float Time => _totalTime;
        public int FrameCount => _frameCount;
        
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask = -1)
        {
            int id = _nextRequestId++;
            _pendingRaycasts[id] = new RaycastRequest
            {
                Origin = origin,
                Direction = direction,
                MaxDistance = maxDistance,
                LayerMask = layerMask
            };
            return id;
        }
        
        public RaycastResult GetRaycastResult(int requestId)
        {
            if (_raycastResults.TryGetValue(requestId, out var result))
                return result;
            
            // Not ready yet
            return new RaycastResult { IsReady = false };
        }
        
        public int RequestPath(Vector3 from, Vector3 to)
        {
            int id = _nextRequestId++;
            _pendingPaths[id] = new PathRequest { From = from, To = to };
            return id;
        }
        
        public PathResult GetPathResult(int requestId)
        {
            if (_pathResults.TryGetValue(requestId, out var result))
                return result;
            
            return new PathResult { IsReady = false };
        }
        
        public void ProcessBatch()
        {
            // === Process Raycasts ===
            foreach (var kvp in _pendingRaycasts)
            {
                var req = kvp.Value;
                var hit = _physics.Raycast(req.Origin, req.Direction, req.MaxDistance, req.LayerMask);
                
                _raycastResults[kvp.Key] = new RaycastResult
                {
                    IsReady = true,
                    Hit = hit.Hit,
                    HitEntityId = hit.EntityId,
                    HitPoint = hit.Point,
                    HitNormal = hit.Normal,
                    Distance = hit.Distance
                };
            }
            _pendingRaycasts.Clear();
            
            // === Process Paths ===
            foreach (var kvp in _pendingPaths)
            {
                var req = kvp.Value;
                var path = _pathfinding.FindPath(req.From, req.To);
                
                _pathResults[kvp.Key] = new PathResult
                {
                    IsReady = true,
                    Success = path != null,
                    PathId = path?.Id ?? 0,
                    PathLength = path?.Length ?? 0
                };
            }
            _pendingPaths.Clear();
        }
        
        public void ClearCompletedRequests()
        {
            // Keep results for N frames for late polling
            // (Implementation detail - could use timestamp)
        }
        
        // === Entity Queries ===
        
        public Vector3 GetEntityPosition(int entityId)
            => _entities.GetPosition(entityId);
        
        public bool IsEntityAlive(int entityId)
            => _entities.IsAlive(entityId);
        
        // === Actions ===
        
        public void TriggerAnimation(int entityId, string animationName)
            => _animations.Play(entityId, animationName);
        
        public void DealDamage(int targetId, float amount)
            => _entities.ApplyDamage(targetId, amount);
        
        // === Parameters ===
        
        public float GetFloatParam(int index)
            => _currentBlob.FloatParams[index];
        
        public int GetIntParam(int index)
            => _currentBlob.IntParams[index];
        
        // ... (other methods)
    }
    
    // === Helper Structures ===
    
    internal struct RaycastRequest
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float MaxDistance;
        public int LayerMask;
    }
    
    internal struct PathRequest
    {
        public Vector3 From;
        public Vector3 To;
    }
}
```

### 4.2 MockContext (Testing)

```csharp
namespace Fbt.Testing
{
    /// <summary>
    /// Mock context for unit tests.
    /// All queries return pre-programmed results.
    /// </summary>
    public struct MockContext : IAIContext
    {
        // === Simulated Time ===
        public float SimulatedDeltaTime;
        public float SimulatedTime;
        public int SimulatedFrameCount;
        
        // === Pre-programmed Results ===
        public bool NextRaycastHit;
        public Vector3 NextRaycastHitPoint;
        public bool NextPathSuccess;
        
        // === Call Tracking ===
        public int RaycastCallCount;
        public int PathRequestCount;
        public int AnimationTriggerCount;
        
        // === IAIContext Implementation ===
        
        public float DeltaTime => SimulatedDeltaTime;
        public float Time => SimulatedTime;
        public int FrameCount => SimulatedFrameCount;
        
        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask = -1)
        {
            RaycastCallCount++;
            return RaycastCallCount; // Use count as fake ID
        }
        
        public RaycastResult GetRaycastResult(int requestId)
        {
            // Immediately ready (no batching in tests)
            return new RaycastResult
            {
                IsReady = true,
                Hit = NextRaycastHit,
                HitPoint = NextRaycastHitPoint,
                HitNormal = Vector3.UnitY
            };
        }
        
        public int RequestPath(Vector3 from, Vector3 to)
        {
            PathRequestCount++;
            return PathRequestCount;
        }
        
        public PathResult GetPathResult(int requestId)
        {
            return new PathResult
            {
                IsReady = true,
                Success = NextPathSuccess,
                PathId = requestId
            };
        }
        
        public void TriggerAnimation(int entityId, string animationName)
        {
            AnimationTriggerCount++;
        }
        
        // ... (other methods return defaults or tracking)
        
        public void ProcessBatch()
        {
            // No-op in tests (results immediate)
        }
    }
}
```

###4.3 ReplayContext (Golden Runs)

```csharp
namespace Fbt.Testing
{
    /// <summary>
    /// Context that replays recorded results from a golden run.
    /// Ensures deterministic behavior.
    /// </summary>
    public struct ReplayContext : IAIContext
    {
        private FrameRecord _currentFrame;
        private float _simulatedTime;
        
        public ReplayContext(FrameRecord frame, float time)
        {
            _currentFrame = frame;
            _simulatedTime = time;
        }
        
        public float DeltaTime => _currentFrame.DeltaTime;
        public float Time => _simulatedTime;
        
        public RaycastResult GetRaycastResult(int requestId)
        {
            // Replay recorded result (FIFO from frame data)
            if (_currentFrame.RaycastResults.Count == 0)
                throw new Exception("Desync: Logic requested raycast not in recording!");
            
            bool hit = _currentFrame.RaycastResults.Dequeue();
            return new RaycastResult { IsReady = true, Hit = hit };
        }
        
        // ... (similar for path queries)
    }
}
```

---

## 5. Batching System Design

### 5.1 Execution Flow

```
┌─────────────────────────────────────────────────┐
│           Game Update Loop                      │
└─────────────────────────────────────────────────┘
                    │
      ┌─────────────┴──────────────┐
      │                            │
      ↓                            ↓
┌──────────┐              ┌─────────────┐
│ Physics  │              │ AI Systems  │
│ Fixed    │              │ (Variable)  │
│ Update   │              │             │
└──────────┘              └─────────────┘
                                  │
                ┌─────────────────┴──────────────────┐
                │                                    │
                ↓                                    ↓
     ┌────────────────────┐             ┌─────────────────┐
     │ For Each Entity:   │             │ Batch Process   │
     │   Tick BT          │             │ All Requests    │
     │   (Issues queries) │──────────→  │ (End of Frame)  │
     └────────────────────┘             └─────────────────┘
                                                 │
                                                 ↓
                                      ┌──────────────────────┐
                                      │ Results Available    │
                                      │ (Next Frame Poll)    │
                                      └──────────────────────┘
```

### 5.2 Usage Example

```csharp
// In your main game loop:

public void Update(float deltaTime)
{
    var context = new GameContext(...);
    context.BeginFrame(deltaTime, treeBlob);
    
    // === Tick all AI entities ===
    foreach (var entity in aiEntities)
    {
        ref var bb = ref entity.GetBlackboard();
        ref var state = ref entity.GetBehaviorState();
        
        treeRunner.Tick(ref bb, ref state, ref context);
        // ^ Issues batched requests
    }
    
    // === Process batch (parallel if desired) ===
    context.ProcessBatch();
    // ^ All raycasts/paths computed here
    
    // === Next frame, entities poll results ===
}
```

---

## 6. Testing Strategy

### 6.1 Unit Test Example

```csharp
[Test]
public void Action_Attack_FailsWhenTargetDead()
{
    // Arrange
    var bb = new OrcBlackboard { TargetEntityId = 42 };
    var state = new BehaviorTreeState();
    var ctx = new MockContext
    {
        NextEntityAlive = false // Target is dead
    };
    
    // Act
    var result = CombatActions.Attack(ref bb, ref state, ref ctx, 0);
    
    // Assert
    Assert.AreEqual(NodeStatus.Failure, result);
}
```

---

**Next Document:** `04-Serialization.md`
