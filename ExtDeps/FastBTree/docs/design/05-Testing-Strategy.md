# FastBTree Testing Strategy

**Version:** 1.0.0  
**Date:** 2026-01-04

---

## 1. Overview

The testing strategy covers three tiers:

1. **Unit Tests** - Individual node logic and system components
2. **Integration Tests** - Full tree execution scenarios
3. **Golden Run Tests** - Deterministic replay for regression detection

**Framework:** xUnit  
**Coverage Goal:** 100% for core logic  
**Test Location:** `tests/Fbt.Tests`

---

## 2. Test Structure

### 2.1 Project Organization

```
tests/
├── Fbt.Tests/
│   ├── Unit/
│   │   ├── DataStructuresTests.cs
│   │   ├── InterpreterTests.cs
│   │   ├── SerializationTests.cs
│   │   └── ContextTests.cs
│   ├── Integration/
│   │   ├── TreeExecutionTests.cs
│   │   ├── ObserverAbortTests.cs
│   │   ├── AsyncOperationsTests.cs
│   │   └── HotReloadTests.cs
│   ├── GoldenRun/
│   │   ├── CombatScenarioTests.cs
│   │   ├── PatrolScenarioTests.cs
│   │   └── Recordings/
│   │       ├── combat_basic_001.json
│   │       └── patrol_loop_001.json
│   └── TestFixtures/
│       ├── MockContext.cs
│       ├── TestBlackboards.cs
│       └── TestActions.cs
```

---

## 3. Unit Tests

### 3.1 Data Structures

```csharp
using Xunit;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class DataStructuresTests
    {
        [Fact]
        public void BehaviorTreeState_ShouldBe64Bytes()
        {
            // Arrange & Act
            var size = Unsafe.SizeOf<BehaviorTreeState>();
            
            // Assert
            Assert.Equal(64, size);
        }
        
        [Fact]
        public void NodeDefinition_ShouldBe8Bytes()
        {
            var size = Unsafe.SizeOf<NodeDefinition>();
            Assert.Equal(8, size);
        }
        
        [Fact]
        public void BehaviorTreeState_Reset_ClearsAllState()
        {
            // Arrange
            var state = new BehaviorTreeState
            {
                RunningNodeIndex = 5,
                TreeVersion = 10
            };
            state.NodeIndexStack[0] = 3;
            state.LocalRegisters[0] = 42;
            
            // Act
            state.Reset();
            
            // Assert
            Assert.Equal(0, state.RunningNodeIndex);
            Assert.Equal(11, state.TreeVersion); // Incremented
            Assert.Equal(0, state.NodeIndexStack[0]);
            Assert.Equal(0, state.LocalRegisters[0]);
        }
        
        [Fact]
        public void AsyncToken_PackUnpack_RoundTrips()
        {
            // Arrange
            var original = new AsyncToken(12345, 67);
            
            // Act
            ulong packed = original.Pack();
            var unpacked = AsyncToken.Unpack(packed);
            
            // Assert
            Assert.Equal(original.RequestID, unpacked.RequestID);
            Assert.Equal(original.Version, unpacked.Version);
        }
        
        [Fact]
        public void AsyncToken_IsValid_ReturnsFalseForOldVersion()
        {
            var token = new AsyncToken(100, 5);
            
            Assert.True(token.IsValid(5));   // Current version
            Assert.False(token.IsValid(6));  // Newer version (token is stale)
        }
    }
}
```

### 3.2 Interpreter Tests

```csharp
namespace Fbt.Tests.Unit
{
    public class InterpreterTests
    {
        [Fact]
        public void Sequence_AllSucceed_ReturnsSuccess()
        {
            // Arrange
            var blob = CreateSimpleSequenceBlob();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, CreateTestRegistry());
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            // Act
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert
            Assert.Equal(NodeStatus.Success, result);
        }
        
        [Fact]
        public void Sequence_FirstChildFails_ReturnsFailureImmediately()
        {
            // Arrange
            var blob = CreateSequenceWithFailingFirstChild();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, CreateTestRegistry());
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            // Act
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert
            Assert.Equal(NodeStatus.Failure, result);
            // Second child should not execute
            Assert.Equal(1, ctx.ActionCallCount); // Only first action called
        }
        
        [Fact]
        public void Selector_FirstSucceeds_SkipsRest()
        {
            // Arrange
            var blob = CreateSelectorWithSucceedingFirstChild();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, CreateTestRegistry());
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            // Act
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(1, ctx.ActionCallCount);
        }
        
        [Fact]
        public void RunningNode_ResumesOnNextTick()
        {
            // Arrange
            var blob = CreateTreeWithWaitAction();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, CreateTestRegistry());
            var bb = new TestBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new MockContext { SimulatedDeltaTime = 0.5f };
            
            // Act - Frame 1
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert - Still running
            Assert.Equal(NodeStatus.Running, result1);
            Assert.NotEqual(0, state.RunningNodeIndex); // Saved running node
            
            // Act - Frame 2 (wait completes)
            ctx.SimulatedDeltaTime = 1.0f;
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert - Completed
            Assert.Equal(NodeStatus.Success, result2);
            Assert.Equal(0, state.RunningNodeIndex); // Cleared
        }
        
        // Helper methods to create test blobs
        private BehaviorTreeBlob CreateSimpleSequenceBlob()
        {
            return new BehaviorTreeBlob
            {
                TreeName = "TestSequence",
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Sequence, ChildCount = 2, SubtreeOffset = 3 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 0, SubtreeOffset = 1 },
                    new NodeDefinition { Type = NodeType.Action, PayloadIndex = 1, SubtreeOffset = 1 }
                },
                MethodNames = new[] { "ActionSuccess", "ActionSuccess" }
            };
        }
    }
}
```

### 3.3 Serialization Tests

```csharp
namespace Fbt.Tests.Unit
{
    public class SerializationTests
    {
        [Fact]
        public void JsonCompiler_ParsesSimpleTree()
        {
            // Arrange
            string json = @"{
                ""treeName"": ""Test"",
                ""version"": 1,
                ""root"": {
                    ""type"": ""Sequence"",
                    ""children"": [
                        { ""type"": ""Action"", ""method"": ""TestAction"" }
                    ]
                }
            }";
            
            // Act
            var blob = TreeCompiler.CompileFromJson(json);
            
            // Assert
            Assert.NotNull(blob);
            Assert.Equal("Test", blob.TreeName);
            Assert.Equal(2, blob.Nodes.Length); // Sequence + Action
            Assert.Single(blob.MethodNames);
            Assert.Equal("TestAction", blob.MethodNames[0]);
        }
        
        [Fact]
        public void BinarySerializer_RoundTrips()
        {
            // Arrange
            var original = CreateTestBlob();
            var stream = new MemoryStream();
            
            // Act - Save
            BinaryTreeSerializer.Save(stream, original);
            
            stream.Position = 0;
            
            // Act - Load
            var loaded = BinaryTreeSerializer.Load(stream);
            
            // Assert
            Assert.Equal(original.TreeName, loaded.TreeName);
            Assert.Equal(original.StructureHash, loaded.StructureHash);
            Assert.Equal(original.Nodes.Length, loaded.Nodes.Length);
            
            for (int i = 0; i < original.Nodes.Length; i++)
            {
                Assert.Equal(original.Nodes[i].Type, loaded.Nodes[i].Type);
                Assert.Equal(original.Nodes[i].ChildCount, loaded.Nodes[i].ChildCount);
            }
        }
        
        [Fact]
        public void StructureHash_ChangeDetected()
        {
            // Arrange
            var blob1 = CreateTestBlob();
            var blob2 = CreateTestBlob();
            
            // Modify structure
            blob2.Nodes = blob2.Nodes.Append(new NodeDefinition { Type = NodeType.Action }).ToArray();
            blob2.StructureHash = TreeCompiler.CalculateStructureHash(blob2.Nodes);
            
            // Assert
            Assert.NotEqual(blob1.StructureHash, blob2.StructureHash);
        }
    }
}
```

---

## 4. Integration Tests

### 4.1 Full Tree Execution

```csharp
namespace Fbt.Tests.Integration
{
    public class TreeExecutionTests
    {
        [Fact]
        public void PatrolCombat_TransitionsCorrectly()
        {
            // Arrange - Load full tree from JSON
            string json = File.ReadAllText("TestAssets/patrol_combat.json");
            var blob = TreeCompiler.CompileFromJson(json);
            var interpreter = new Interpreter<OrcBlackboard, MockContext>(blob, CreateOrcActions());
            
            var bb = new OrcBlackboard { Health = 100, TargetEntityId = 0 };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            // Act - Tick 1: No target, should patrol
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert - Started patrol
            Assert.Equal(NodeStatus.Running, result1);
            Assert.Equal(1, ctx.PathRequestCount); // Requested path to patrol point
            
            // Act - Tick 2: Target appears!
            bb.TargetEntityId = 42;
            ctx.NextEntityAlive = true;
            ctx.NextEntityDistance = 3.0f;
            
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert - Switched to combat
            Assert.Equal(NodeStatus.Running, result2);
            Assert.Equal(1, ctx.AnimationTriggerCount); // Triggered attack animation
        }
        
        [Fact]
        public void ObserverAbort_InterruptsRunningNode()
        {
            // Arrange
            var blob = CreateObserverTree();
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, CreateTestRegistry());
            
            var bb = new TestBlackboard { Priority = false };
            var state = new BehaviorTreeState();
            var ctx = new MockContext();
            
            // Act - Tick 1: Start low-priority task
            interpreter.Tick(ref bb, ref state, ref ctx);
            Assert.NotEqual(0, state.RunningNodeIndex);
            
            uint oldVersion = state.TreeVersion;
            
            // Act - Tick 2: Observer condition becomes true
            bb.Priority = true;
            var result = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert - Aborted!
            Assert.Equal(NodeStatus.Success, result);
            Assert.Equal(oldVersion + 1, state.TreeVersion); // Version incremented
        }
    }
}
```

### 4.2 Async Operations

```csharp
namespace Fbt.Tests.Integration
{
    public class AsyncOperationsTests
    {
        [Fact]
        public void PathfindingRequest_HandlesMultiFrameOperation()
        {
            // Arrange
            var blob = CreatePathfindingTree();
            var interpreter = new Interpreter<OrcBlackboard, GameContext>(blob, CreateOrcActions());
            
            var bb = new OrcBlackboard { Position = Vector3.Zero };
            var state = new BehaviorTreeState();
            var ctx = new GameContext(...);
            
            // Act - Frame 1: Issue request
            var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Assert
            Assert.Equal(NodeStatus.Running, result1);
            Assert.NotEqual(0ul, state.AsyncHandles[0]); // Stored handle
            
            // Act - Frame 2: Still processing
            ctx.ProcessBatch(); // Simulate batch processing
            var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
            
            // Path ready
            Assert.Equal(NodeStatus.Success, result2);
            Assert.Equal(0ul, state.AsyncHandles[0]); // Cleared
        }
        
        [Fact]
        public void ZombieRequest_InvalidatedOnAbort()
        {
            // Arrange
            var bb = new OrcBlackboard();
            var state = new BehaviorTreeState();
            state.TreeVersion = 5;
            
            // Store old async handle
            state.AsyncHandles[0] = new AsyncToken(999, 5).Pack();
            
            // Act - Tree aborts (version increments)
            state.TreeVersion++;
            
            // Act - Node checks validity
            var token = AsyncToken.Unpack(state.AsyncHandles[0]);
            bool valid = token.IsValid(state.TreeVersion);
            
            // Assert - Token is invalid
            Assert.False(valid);
        }
    }
}
```

---

## 5. Golden Run Tests

### 5.1 Recording Structure

```csharp
namespace Fbt.Testing
{
    [Serializable]
    public class GoldenRunRecording
    {
        public string TreeName;
        public string Description;
        public TestBlackboard InitialBlackboard;
        public BehaviorTreeState InitialState;
        public List<FrameRecord> Frames = new();
    }
    
    [Serializable]
    public class FrameRecord
    {
        public float DeltaTime;
        
        // Recorded query results (FIFO queues)
        public Queue<bool> RaycastResults = new();
        public Queue<int> RandomIntResults = new();
        public Queue<float> RandomFloatResults = new();
        public Queue<PathResult> PathResults = new();
        
        // Expected state after frame
        public ushort ExpectedRunningNode;
        public NodeStatus ExpectedResult;
    }
}
```

### 5.2 Recorder Implementation

```csharp
namespace Fbt.Testing
{
    public class GoldenRunRecorder
    {
        private GoldenRunRecording _recording;
        private bool _isRecording;
        
        public void StartRecording(string treeName, TestBlackboard bb, BehaviorTreeState state)
        {
            _recording = new GoldenRunRecording
            {
                TreeName = treeName,
                InitialBlackboard = bb,
                InitialState = state
            };
            _isRecording = true;
        }
        
        public void RecordFrame(
            float deltaTime,
            RecordingContext context,
            ushort runningNode,
            NodeStatus result)
        {
            if (!_isRecording) return;
            
            var frame = new FrameRecord
            {
                DeltaTime = deltaTime,
                RaycastResults = new Queue<bool>(context.RecordedRaycasts),
                RandomIntResults = new Queue<int>(context.RecordedRandomInts),
                PathResults = new Queue<PathResult>(context.RecordedPaths),
                ExpectedRunningNode = runningNode,
                ExpectedResult = result
            };
            
            _recording.Frames.Add(frame);
        }
        
        public void SaveRecording(string path)
        {
            var json = JsonSerializer.Serialize(_recording, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(path, json);
            _isRecording = false;
        }
    }
}
```

### 5.3 Golden Run Test

```csharp
namespace Fbt.Tests.GoldenRun
{
    public class CombatScenarioTests
    {
        [Fact]
        public void GoldenRun_CombatBasic_MatchesRecording()
        {
            // Arrange - Load recording
            string json = File.ReadAllText("TestAssets/Recordings/combat_basic_001.json");
            var recording = JsonSerializer.Deserialize<GoldenRunRecording>(json);
            
            // Load tree
            var blob = TreeManager.GetTree(recording.TreeName);
            var interpreter = new Interpreter<TestBlackboard, ReplayContext>(blob, CreateTestRegistry());
            
            // Initialize state from recording
            var bb = recording.InitialBlackboard;
            var state = recording.InitialState;
            
            float simTime = 0;
            
            // Act - Replay all frames
            foreach (var frame in recording.Frames)
            {
                var replayCtx = new ReplayContext(frame, simTime);
                var result = interpreter.Tick(ref bb, ref state, ref replayCtx);
                
                // Assert - Frame matches
                Assert.Equal(frame.ExpectedResult, result);
                Assert.Equal(frame.ExpectedRunningNode, state.RunningNodeIndex);
                
                // Verify all queries were consumed
                Assert.Empty(replayCtx.RemainingRaycasts);
                
                simTime += frame.DeltaTime;
            }
        }
        
        [Fact]
        public void GoldenRun_PatrolLoop_Deterministic()
        {
            // Similar structure, different scenario
            // Tests repeating behavior, looping, timers
        }
    }
}
```

---

## 6. Test Fixtures

### 6.1 Test Blackboards

```csharp
namespace Fbt.Tests.TestFixtures
{
    public struct TestBlackboard
    {
        public int Counter;
        public bool Flag;
        public float Timer;
        public bool Priority;
    }
    
    public struct OrcBlackboard
    {
        public int SelfEntityId;
        public int TargetEntityId;
        public Vector3 Position;
        public float Health;
        public float AggroRange;
    }
}
```

### 6.2 Test Actions

```csharp
namespace Fbt.Tests.TestFixtures
{
    public static class TestActions
    {
        public static NodeStatus ActionSuccess(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            ctx.ActionCallCount++;
            return NodeStatus.Success;
        }
        
        public static NodeStatus ActionFailure(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            ctx.ActionCallCount++;
            return NodeStatus.Failure;
        }
        
        public static NodeStatus WaitFrames(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            ref int counter = ref state.LocalRegisters[0];
            
            if (counter == 0)
                counter = ctx.GetIntParam(paramIndex);
            
            counter--;
            
            if (counter <= 0)
            {
                counter = 0;
                return NodeStatus.Success;
            }
            
            return NodeStatus.Running;
        }
        
        public static NodeStatus ConditionFlag(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            return bb.Flag ? NodeStatus.Success : NodeStatus.Failure;
        }
    }
}
```

---

## 7. Coverage Targets

### 7.1 Critical Paths (100% Required)

- ✅ Data structure initialization and reset
- ✅ All node types (Sequence, Selector, Action, etc.)
- ✅ Resume logic (skip already-processed children)
- ✅ Observer abort interruption
- ✅ Async token validation
- ✅ Hot reload hash checking
- ✅ Binary serialization round-trip

### 7.2 Edge Cases

- Stack overflow (max depth exceeded)
- Invalid node indices
- Null/empty trees
- Corrupt binary files
- Hash collisions
- Async handle overflow (too many concurrent operations)

---

## 8. CI/CD Integration

```yaml
# .github/workflows/test.yml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Run Unit Tests
        run: dotnet test tests/Fbt.Tests --filter Category=Unit --logger trx
      
      - name: Run Integration Tests
        run: dotnet test tests/Fbt.Tests --filter Category=Integration --logger trx
      
      - name: Run Golden Run Tests
        run: dotnet test tests/Fbt.Tests --filter Category=GoldenRun --logger trx
      
      - name: Coverage Report
        run: dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
      
      - name: Upload Coverage
        uses: codecov/codecov-action@v3
```

---

**Next Document:** `06-Demo-Application.md`
