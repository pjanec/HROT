# BATCH-21: Fix FailSafe Test + All P3 Polish Tasks (TASK-G13-G20)

**Batch Number:** BATCH-21  
**Tasks:** Fix FailSafeTest + TASK-G13, G14, G15, G16, G17, G18, G19, G20  
**Phase:** Polish & Complete (Final Batch)  
**Estimated Effort:** 10-12 hours  
**Priority:** P3 (Low Priority) + Critical Test Fix  
**Dependencies:** BATCH-20 break fix applied

---

## 📋 Onboarding

**Required Reading:**
1. **Task Definitions:** `.dev-workstream/GAP-TASKS.md` - TASK-G13 through G20
2. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Sections 1.3, 2.1, 2.3, 2.7, 4.4, 5.2, 5.3

**Source Code:** `src/Fhsm.Kernel/`, `src/Fhsm.Compiler/`, `tests/Fhsm.Tests/`

**Report:** `.dev-workstream/reports/BATCH-21-REPORT.md`

---

## Context

This batch completes all remaining P3 tasks and fixes the FailSafeTest that breaks after the RTC loop fix. After this batch, the HSM implementation will be feature-complete per the design document.

---

## ✅ Tasks

### Task 0: Fix FailSafe Test (CRITICAL - Do First)

**File:** `tests/Fhsm.Tests/Kernel/FailSafeTests.cs`

**Problem:** Current test creates State 0 ↔ State 1 ping-pong, which is NOT infinite with proper RTC semantics (break after transition).

**Solution:** Create actual infinite loop using **self-loop** (State 0 → State 0).

**New Test Implementation:**

```csharp
private HsmDefinitionBlob CreateInfiniteLoopBlob()
{
    // Single state with self-loop transition
    var states = new StateDef[1];
    states[0] = new StateDef 
    { 
        ParentIndex = 0xFFFF, 
        FirstTransitionIndex = 0, 
        TransitionCount = 1,
        Depth = 0
    };

    var transitions = new TransitionDef[1];
    transitions[0] = new TransitionDef 
    { 
        SourceStateIndex = 0, 
        TargetStateIndex = 0,  // Self-loop! State 0 -> State 0
        EventId = 10,
        GuardId = 0,  // No guard = always fires
        ActionId = 0,
        Flags = 0
    };

    var header = new HsmDefinitionHeader();
    header.StructureHash = 0x12345678;
    header.StateCount = 1;
    header.TransitionCount = 1;
    header.RegionCount = 1;

    return new HsmDefinitionBlob(
        header,
        states,
        transitions,
        Array.Empty<RegionDef>(),
        Array.Empty<GlobalTransitionDef>(),
        Array.Empty<ushort>(),
        Array.Empty<ushort>()
    );
}

[Fact]
public void InfiniteLoop_Detected_And_Stops()
{
    var blob = CreateInfiniteLoopBlob();
    var instances = new HsmInstance64[1];
    instances[0].Header.MachineId = 0x12345678;
    instances[0].Header.Phase = InstancePhase.RTC;
    instances[0].Header.Flags = InstanceFlags.DebugTrace;
    instances[0].Header.RngState = 123;
    instances[0].ActiveLeafIds[0] = 0; // Start at State 0
    
    int context = 0;
    
    // Queue event 10 (triggers self-loop)
    HsmEventQueue.Enqueue(
        (void*)Unsafe.AsPointer(ref instances[0]), 
        sizeof(HsmInstance64), 
        new HsmEvent { Id = 10, Priority = EventPriority.Normal }
    );
    
    var traceBuffer = new HsmTraceBuffer(65536);
    traceBuffer.FilterLevel = TraceLevel.All;
    HsmKernelCore.SetTraceBuffer(traceBuffer);
    
    try 
    {
        HsmKernel.Update(blob, ref instances[0], context, 0.016f);
    }
    finally
    {
        HsmKernelCore.SetTraceBuffer(null);
    }
    
    // 1. Check Safe State (0xFFFF)
    Assert.Equal(0xFFFF, instances[0].ActiveLeafIds[0]);
    
    // 2. Check Phase Reset to Idle
    Assert.Equal(InstancePhase.Idle, instances[0].Header.Phase);
    
    // 3. Check Trace Error
    var data = traceBuffer.GetTraceData();
    Assert.True(data.Length > 0, "Trace buffer is empty");
    
    bool foundError = false;
    foreach (var record in traceBuffer.GetRecords())
    {
        if (record.OpCode == TraceOpCode.Error)
        {
            foundError = true;
            // ErrorCode should be 1 (RTC Loop)
            break;
        }
    }
    
    Assert.True(foundError, "Error record not found in trace");
}
```

**Key Change:** State 0 → State 0 (self-loop) creates true infinite loop within single RTC cycle.

---

### Task 1: CommandLane Enum (TASK-G13)

**File:** `src/Fhsm.Kernel/Data/Enums.cs` (UPDATE)

```csharp
/// <summary>
/// Command buffer lanes for prioritization.
/// </summary>
public enum CommandLane : byte
{
    Animation = 0,
    Navigation = 1,
    Gameplay = 2,
    Blackboard = 3,
    Audio = 4,
    VFX = 5,
    Message = 6,
    Count = 7
}
```

**File:** `src/Fhsm.Kernel/Data/CommandPage.cs` (UPDATE)

Add lane tracking to command writer:

```csharp
public unsafe ref struct HsmCommandWriter
{
    // ... existing fields ...
    private CommandLane _currentLane;
    
    public HsmCommandWriter(byte* buffer, int capacity, CommandLane lane = CommandLane.Gameplay)
    {
        // ... existing init ...
        _currentLane = lane;
    }
    
    public void SetLane(CommandLane lane)
    {
        _currentLane = lane;
    }
    
    public CommandLane CurrentLane => _currentLane;
}
```

**Note:** Full lane reservation logic is complex (P4). This adds the enum for future use.

---

### Task 2: JSON Input Parser (TASK-G14)

**File:** `src/Fhsm.Compiler/IO/JsonStateMachineParser.cs` (NEW)

```csharp
using System;
using System.Text.Json;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler.IO
{
    /// <summary>
    /// Parses JSON state machine definitions into StateMachineGraph.
    /// </summary>
    public class JsonStateMachineParser
    {
        public StateMachineGraph Parse(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var graph = new StateMachineGraph(root.GetProperty("name").GetString() ?? "Unnamed");
            
            // Parse states
            if (root.TryGetProperty("states", out var states))
            {
                foreach (var state in states.EnumerateArray())
                {
                    ParseState(state, graph, null);
                }
            }
            
            // Parse transitions
            if (root.TryGetProperty("transitions", out var transitions))
            {
                foreach (var transition in transitions.EnumerateArray())
                {
                    ParseTransition(transition, graph);
                }
            }
            
            return graph;
        }
        
        private void ParseState(JsonElement stateJson, StateMachineGraph graph, StateNode? parent)
        {
            var name = stateJson.GetProperty("name").GetString() ?? "Unnamed";
            var state = graph.AddState(name, parent);
            
            // Optional: entry/exit actions
            if (stateJson.TryGetProperty("onEntry", out var entry))
            {
                state.EntryActionId = (ushort)entry.GetInt32();
            }
            
            if (stateJson.TryGetProperty("onExit", out var exit))
            {
                state.ExitActionId = (ushort)exit.GetInt32();
            }
            
            // Recursive: nested states
            if (stateJson.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    ParseState(child, graph, state);
                }
            }
        }
        
        private void ParseTransition(JsonElement transJson, StateMachineGraph graph)
        {
            var source = transJson.GetProperty("source").GetString();
            var target = transJson.GetProperty("target").GetString();
            var eventId = (ushort)transJson.GetProperty("event").GetInt32();
            
            var sourceState = graph.FindStateByName(source);
            var targetState = graph.FindStateByName(target);
            
            if (sourceState == null || targetState == null)
                throw new InvalidOperationException($"Invalid transition: {source} -> {target}");
            
            var transition = new TransitionNode
            {
                Source = sourceState,
                Target = targetState,
                EventId = eventId
            };
            
            if (transJson.TryGetProperty("guard", out var guard))
            {
                transition.GuardId = (ushort)guard.GetInt32();
            }
            
            if (transJson.TryGetProperty("action", out var action))
            {
                transition.ActionId = (ushort)action.GetInt32();
            }
            
            sourceState.Transitions.Add(transition);
        }
    }
}
```

**Add helper to StateMachineGraph:**

**File:** `src/Fhsm.Compiler/Graph/StateMachineGraph.cs` (UPDATE)

```csharp
public StateNode? FindStateByName(string name)
{
    return FindStateRecursive(Root, name);
}

private StateNode? FindStateRecursive(StateNode node, string name)
{
    if (node.Name == name) return node;
    
    foreach (var child in node.Children)
    {
        var found = FindStateRecursive(child, name);
        if (found != null) return found;
    }
    
    return null;
}
```

---

### Task 3: JSON Parser Tests

**File:** `tests/Fhsm.Tests/Compiler/JsonParserTests.cs` (NEW)

```csharp
[Fact]
public void Parse_Simple_StateMachine()
{
    var json = @"
    {
        ""name"": ""TestMachine"",
        ""states"": [
            { ""name"": ""Idle"" },
            { ""name"": ""Active"" }
        ],
        ""transitions"": [
            { ""source"": ""Idle"", ""target"": ""Active"", ""event"": 1 }
        ]
    }";
    
    var parser = new JsonStateMachineParser();
    var graph = parser.Parse(json);
    
    Assert.Equal("TestMachine", graph.Name);
    Assert.Equal(2, graph.Root.Children.Count);
    
    var idle = graph.FindStateByName("Idle");
    Assert.NotNull(idle);
    Assert.Single(idle.Transitions);
}

[Fact]
public void Parse_Nested_States()
{
    var json = @"
    {
        ""name"": ""TestMachine"",
        ""states"": [
            { 
                ""name"": ""Parent"",
                ""children"": [
                    { ""name"": ""Child1"" },
                    { ""name"": ""Child2"" }
                ]
            }
        ]
    }";
    
    var parser = new JsonStateMachineParser();
    var graph = parser.Parse(json);
    
    var parent = graph.FindStateByName("Parent");
    Assert.NotNull(parent);
    Assert.Equal(2, parent.Children.Count);
}
```

---

### Task 4: Slot Conflict Validation (TASK-G15)

**File:** `src/Fhsm.Compiler/HsmGraphValidator.cs` (UPDATE)

```csharp
/// <summary>
/// Validate timer/history slots don't conflict in orthogonal regions.
/// </summary>
private void ValidateSlotConflicts(StateMachineGraph graph)
{
    // For each state with orthogonal regions
    foreach (var state in graph.GetAllStates())
    {
        if (state.Children.Count < 2) continue; // No orthogonal regions
        
        // Build exclusion graph: which slots are used in which region
        var timerSlots = new Dictionary<int, List<string>>();
        var historySlots = new Dictionary<int, List<string>>();
        
        foreach (var region in state.Children)
        {
            CollectSlotUsage(region, timerSlots, historySlots);
        }
        
        // Check for conflicts (same slot in multiple regions)
        foreach (var kvp in timerSlots)
        {
            if (kvp.Value.Count > 1)
            {
                _errors.Add(new ValidationError
                {
                    Message = $"Timer slot {kvp.Key} used in multiple regions: {string.Join(", ", kvp.Value)}",
                    Severity = ErrorSeverity.Error
                });
            }
        }
        
        foreach (var kvp in historySlots)
        {
            if (kvp.Value.Count > 1)
            {
                _errors.Add(new ValidationError
                {
                    Message = $"History slot {kvp.Key} used in multiple regions: {string.Join(", ", kvp.Value)}",
                    Severity = ErrorSeverity.Error
                });
            }
        }
    }
}

private void CollectSlotUsage(
    StateNode region, 
    Dictionary<int, List<string>> timerSlots,
    Dictionary<int, List<string>> historySlots)
{
    // Recursively collect timer/history slot usage
    if (region.TimerSlotIndex >= 0)
    {
        if (!timerSlots.ContainsKey(region.TimerSlotIndex))
            timerSlots[region.TimerSlotIndex] = new List<string>();
        timerSlots[region.TimerSlotIndex].Add(region.Name);
    }
    
    if (region.HistorySlotIndex >= 0)
    {
        if (!historySlots.ContainsKey(region.HistorySlotIndex))
            historySlots[region.HistorySlotIndex] = new List<string>();
        historySlots[region.HistorySlotIndex].Add(region.Name);
    }
    
    foreach (var child in region.Children)
    {
        CollectSlotUsage(child, timerSlots, historySlots);
    }
}
```

---

### Task 5: LinkerTableEntry Struct (TASK-G16)

**File:** `src/Fhsm.Kernel/Data/LinkerTableEntry.cs` (NEW)

```csharp
using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Entry in the linker table mapping function IDs to addresses.
    /// Size: 16 bytes (cache-friendly)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct LinkerTableEntry
    {
        /// <summary>FNV-1a hash of function name</summary>
        [FieldOffset(0)] public ushort FunctionId;
        
        /// <summary>Reserved for alignment</summary>
        [FieldOffset(2)] public ushort Reserved;
        
        /// <summary>Function pointer (64-bit)</summary>
        [FieldOffset(8)] public long FunctionPointer;
    }
}
```

**File:** `src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs` (UPDATE)

Replace raw `ushort[]` arrays with `LinkerTableEntry[]`:

```csharp
private readonly LinkerTableEntry[] _actionTable;
private readonly LinkerTableEntry[] _guardTable;

public HsmDefinitionBlob(
    HsmDefinitionHeader header,
    StateDef[] states,
    TransitionDef[] transitions,
    RegionDef[] regions,
    GlobalTransitionDef[] globalTransitions,
    LinkerTableEntry[] actionTable,
    LinkerTableEntry[] guardTable)
{
    // ... existing init ...
    _actionTable = actionTable;
    _guardTable = guardTable;
}

public ReadOnlySpan<LinkerTableEntry> ActionTable => _actionTable.AsSpan();
public ReadOnlySpan<LinkerTableEntry> GuardTable => _guardTable.AsSpan();
```

**Note:** This requires updating `HsmFlattener` and `HsmEmitter` to generate `LinkerTableEntry[]` instead of `ushort[]`.

---

### Task 6: XxHash64 Implementation (TASK-G17)

**File:** `src/Fhsm.Compiler/Hashing/XxHash64.cs` (NEW)

```csharp
using System;

namespace Fhsm.Compiler.Hashing
{
    /// <summary>
    /// XxHash64 implementation for structure/parameter hashing.
    /// Faster and better avalanche than SHA256.
    /// </summary>
    public static class XxHash64
    {
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 = 1609587929392839161UL;
        private const ulong Prime4 = 9650029242287828579UL;
        private const ulong Prime5 = 2870177450012600261UL;
        
        public static ulong ComputeHash(ReadOnlySpan<byte> data, ulong seed = 0)
        {
            ulong hash;
            int remaining = data.Length;
            int offset = 0;
            
            if (remaining >= 32)
            {
                ulong v1 = seed + Prime1 + Prime2;
                ulong v2 = seed + Prime2;
                ulong v3 = seed;
                ulong v4 = seed - Prime1;
                
                do
                {
                    v1 = Round(v1, ReadUInt64(data, offset)); offset += 8;
                    v2 = Round(v2, ReadUInt64(data, offset)); offset += 8;
                    v3 = Round(v3, ReadUInt64(data, offset)); offset += 8;
                    v4 = Round(v4, ReadUInt64(data, offset)); offset += 8;
                    remaining -= 32;
                } while (remaining >= 32);
                
                hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                hash = MergeRound(hash, v1);
                hash = MergeRound(hash, v2);
                hash = MergeRound(hash, v3);
                hash = MergeRound(hash, v4);
            }
            else
            {
                hash = seed + Prime5;
            }
            
            hash += (ulong)data.Length;
            
            while (remaining >= 8)
            {
                hash ^= Round(0, ReadUInt64(data, offset));
                hash = RotateLeft(hash, 27) * Prime1 + Prime4;
                offset += 8;
                remaining -= 8;
            }
            
            if (remaining >= 4)
            {
                hash ^= ReadUInt32(data, offset) * Prime1;
                hash = RotateLeft(hash, 23) * Prime2 + Prime3;
                offset += 4;
                remaining -= 4;
            }
            
            while (remaining > 0)
            {
                hash ^= data[offset] * Prime5;
                hash = RotateLeft(hash, 11) * Prime1;
                offset++;
                remaining--;
            }
            
            // Avalanche
            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;
            
            return hash;
        }
        
        private static ulong Round(ulong acc, ulong input)
        {
            acc += input * Prime2;
            acc = RotateLeft(acc, 31);
            acc *= Prime1;
            return acc;
        }
        
        private static ulong MergeRound(ulong acc, ulong val)
        {
            val = Round(0, val);
            acc ^= val;
            acc = acc * Prime1 + Prime4;
            return acc;
        }
        
        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }
        
        private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset)
        {
            return BitConverter.ToUInt64(data.Slice(offset, 8));
        }
        
        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        {
            return BitConverter.ToUInt32(data.Slice(offset, 4));
        }
    }
}
```

**File:** `src/Fhsm.Compiler/HsmEmitter.cs` (UPDATE)

Replace SHA256 with XxHash64:

```csharp
// BEFORE:
using System.Security.Cryptography;
var hash = SHA256.HashData(buffer);

// AFTER:
using Fhsm.Compiler.Hashing;
var hash = XxHash64.ComputeHash(buffer);
```

---

### Task 7: Debug Metadata Export (TASK-G18)

**File:** `src/Fhsm.Compiler/HsmEmitter.cs` (UPDATE)

Add debug sidecar export:

```csharp
public static void EmitWithDebug(
    HsmDefinitionBlob blob,
    MachineMetadata metadata,
    string outputPath)
{
    // Export main blob (existing logic)
    var blobBytes = SerializeBlob(blob);
    File.WriteAllBytes(outputPath, blobBytes);
    
    // Export debug sidecar
    var debugPath = outputPath + ".debug";
    var debugJson = SerializeMetadata(metadata);
    File.WriteAllText(debugPath, debugJson);
}

private static string SerializeMetadata(MachineMetadata metadata)
{
    var sb = new StringBuilder();
    sb.AppendLine("{");
    
    // State names
    sb.AppendLine("  \"states\": {");
    foreach (var kvp in metadata.StateNames)
    {
        sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\",");
    }
    sb.AppendLine("  },");
    
    // Event names
    sb.AppendLine("  \"events\": {");
    foreach (var kvp in metadata.EventNames)
    {
        sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\",");
    }
    sb.AppendLine("  },");
    
    // Action names
    sb.AppendLine("  \"actions\": {");
    foreach (var kvp in metadata.ActionNames)
    {
        sb.AppendLine($"    \"{kvp.Key}\": \"{kvp.Value}\",");
    }
    sb.AppendLine("  }");
    
    sb.AppendLine("}");
    return sb.ToString();
}
```

---

### Task 8: Full Orthogonal Region Support (TASK-G19)

**File:** `src/Fhsm.Kernel/Data/StateDef.cs` (UPDATE)

Add OutputLaneMask:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct StateDef
{
    // ... existing fields ...
    
    /// <summary>Output lanes this state writes to (for conflict detection)</summary>
    [FieldOffset(28)] public byte OutputLaneMask;
}
```

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)

Add region arbitration in ExecuteTransition:

```csharp
private static void ExecuteTransition(...)
{
    // ... existing logic ...
    
    // NEW: Region arbitration for orthogonal regions
    if (definition.Header.RegionCount > 1)
    {
        // Check for output lane conflicts
        byte combinedMask = 0;
        for (int i = 0; i < regionCount; i++)
        {
            var state = definition.States[activeLeafIds[i]];
            if ((combinedMask & state.OutputLaneMask) != 0)
            {
                // Conflict! Region arbitration needed
                // For now: First region wins
                continue;
            }
            combinedMask |= state.OutputLaneMask;
        }
    }
    
    // ... rest of logic ...
}
```

---

### Task 9: Deep History Support (TASK-G20)

**File:** `src/Fhsm.Kernel/HsmKernelCore.cs` (UPDATE)

Enhance history restore for deep history:

```csharp
private static void RestoreHistory(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    ushort stateIndex,
    bool isDeep)  // NEW parameter
{
    var state = definition.States[stateIndex];
    if (state.HistorySlotIndex < 0) return;
    
    ushort savedStateId = GetHistorySlot(instancePtr, instanceSize, state.HistorySlotIndex);
    
    if (savedStateId == 0xFFFF) return; // No history
    
    // Validate saved state is descendant
    if (!IsAncestor(definition, stateIndex, savedStateId))
    {
        return; // Invalid history
    }
    
    // NEW: Deep history restoration
    if (isDeep)
    {
        // Restore entire subtree
        RestoreDeepHistory(definition, instancePtr, instanceSize, savedStateId);
    }
    else
    {
        // Shallow: Restore only immediate child
        SetActiveLeafId(instancePtr, instanceSize, 0, savedStateId);
    }
}

private static void RestoreDeepHistory(
    HsmDefinitionBlob definition,
    byte* instancePtr,
    int instanceSize,
    ushort stateIndex)
{
    var state = definition.States[stateIndex];
    
    // Recursively restore children
    for (ushort i = 0; i < definition.Header.StateCount; i++)
    {
        var child = definition.States[i];
        if (child.ParentIndex == stateIndex)
        {
            if (child.HistorySlotIndex >= 0)
            {
                RestoreHistory(definition, instancePtr, instanceSize, i, true);
            }
        }
    }
}
```

---

## 🧪 Testing Requirements

**Minimum Tests:**
- FailSafeTests: 1 test (fixed)
- CommandLaneTests: 2 tests (enum, setter)
- JsonParserTests: 3 tests
- SlotConflictTests: 2 tests
- LinkerTableTests: 2 tests (skip if refactor too large)
- XxHash64Tests: 2 tests (correctness, collision)
- DebugExportTests: 1 test (skip if manual verification)
- OrthogonalRegionTests: 2 tests
- DeepHistoryTests: 2 tests

**Total: ~17 new tests**

**Quality Standards:**
- All 216 existing tests must pass
- New tests validate core logic
- Integration tests for complex features (orthogonal, deep history)

---

## 🎯 Success Criteria

**Functionality:**
- [ ] FailSafeTest fixed (self-loop)
- [ ] CommandLane enum added
- [ ] JSON parser working
- [ ] Slot conflict validation
- [ ] LinkerTableEntry struct (optional if too complex)
- [ ] XxHash64 hashing
- [ ] Debug metadata export
- [ ] Orthogonal region arbitration
- [ ] Deep history support
- [ ] All 8 P3 tasks complete

**Tests:**
- [ ] All 216+ tests pass
- [ ] 17 new tests pass
- [ ] **Target: ~233 tests passing**

**Report:**
- [ ] Report submitted with all sections
- [ ] Document troubles, weak points, design decisions

---

## 📚 Reference Materials

**Task Definitions:** `.dev-workstream/GAP-TASKS.md` (lines 472-561)  
**Design Document:** `docs/design/HSM-Implementation-Design.md`

---

## ⚠️ Common Pitfalls

**Pitfall 1: FailSafe test - don't use ping-pong**
- Self-loop (0→0) is correct infinite loop
- Ping-pong (0↔1) breaks with RTC semantics

**Pitfall 2: LinkerTableEntry refactor is large**
- If too complex, document and defer to v2.0
- Focus on other P3 tasks first

**Pitfall 3: Deep history is recursive**
- Need to restore entire subtree
- Validate all ancestors

---

**Note:** P3 tasks are polish/advanced features. Some (like LinkerTableEntry refactor) may be deferred if too complex. Prioritize FailSafe fix + simpler tasks first.

Good luck! This is the final implementation batch. 🚀
