# BATCH-07: Compiler - Flattener & Emitter

**Effort:** 3-4 days  
**Tasks:** TASK-C06 (Flattener), TASK-C07 (Emitter)

---

## Context

Compiler pipeline progress:
```
User API → Graph ✅ → Normalize ✅ → Validate ✅ → Flatten → Emit
```

This batch: Convert normalized graph to flat arrays and emit `HsmDefinitionBlob`.

---

## Task 1: Graph Flattener (TASK-C06)

**File:** `src/Fhsm.Compiler/HsmFlattener.cs`

Convert graph nodes to flat ROM structs.

### Operations

1. **Flatten States** - StateNode → StateDef[]
2. **Flatten Transitions** - TransitionNode → TransitionDef[]
3. **Flatten Regions** - RegionNode → RegionDef[]
4. **Build Dispatch Tables** - ActionIds[], GuardIds[]
5. **Compute Transition Costs** - LCA structural distance (Architect Q6)
6. **Separate Global Transitions** - GlobalTransitionDef[] (Architect Q7)

### Implementation Skeleton

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Compiler
{
    public class HsmFlattener
    {
        public class FlattenedData
        {
            public StateDef[] States { get; set; }
            public TransitionDef[] Transitions { get; set; }
            public RegionDef[] Regions { get; set; }
            public GlobalTransitionDef[] GlobalTransitions { get; set; }
            public ushort[] ActionIds { get; set; }
            public ushort[] GuardIds { get; set; }
        }
        
        /// <summary>
        /// Flatten normalized graph to ROM arrays.
        /// </summary>
        public static FlattenedData Flatten(StateMachineGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            
            var result = new FlattenedData();
            
            // 1. Build function dispatch tables first (needed for IDs)
            var actionTable = BuildActionTable(graph);
            var guardTable = BuildGuardTable(graph);
            
            result.ActionIds = actionTable.Values.ToArray();
            result.GuardIds = guardTable.Values.ToArray();
            
            // 2. Flatten states (use FlatIndex order)
            result.States = FlattenStates(graph, actionTable);
            
            // 3. Flatten transitions (compute costs)
            result.Transitions = FlattenTransitions(graph, actionTable, guardTable);
            
            // 4. Flatten regions
            result.Regions = FlattenRegions(graph);
            
            // 5. Separate global transitions
            result.GlobalTransitions = FlattenGlobalTransitions(graph, actionTable, guardTable);
            
            return result;
        }
        
        private static Dictionary<string, ushort> BuildActionTable(StateMachineGraph graph)
        {
            // Collect all unique action names, assign IDs
            var actions = new HashSet<string>();
            
            foreach (var state in graph.States.Values)
            {
                if (state.OnEntryAction != null) actions.Add(state.OnEntryAction);
                if (state.OnExitAction != null) actions.Add(state.OnExitAction);
                if (state.ActivityAction != null) actions.Add(state.ActivityAction);
                if (state.TimerAction != null) actions.Add(state.TimerAction);
                
                foreach (var trans in state.Transitions)
                {
                    if (trans.ActionFunction != null) actions.Add(trans.ActionFunction);
                }
            }
            
            // Assign sequential IDs (sorted for determinism)
            var sorted = actions.OrderBy(a => a).ToList();
            var table = new Dictionary<string, ushort>();
            
            for (ushort i = 0; i < sorted.Count; i++)
            {
                table[sorted[i]] = i;
            }
            
            return table;
        }
        
        private static Dictionary<string, ushort> BuildGuardTable(StateMachineGraph graph)
        {
            // Similar to BuildActionTable but for guards
            var guards = new HashSet<string>();
            
            foreach (var state in graph.States.Values)
            {
                foreach (var trans in state.Transitions)
                {
                    if (trans.GuardFunction != null) guards.Add(trans.GuardFunction);
                }
            }
            
            var sorted = guards.OrderBy(g => g).ToList();
            var table = new Dictionary<string, ushort>();
            
            for (ushort i = 0; i < sorted.Count; i++)
            {
                table[sorted[i]] = i;
            }
            
            return table;
        }
        
        private static StateDef[] FlattenStates(StateMachineGraph graph, Dictionary<string, ushort> actionTable)
        {
            // Sort by FlatIndex (should already be assigned by Normalizer)
            var states = graph.States.Values.OrderBy(s => s.FlatIndex).ToArray();
            var result = new StateDef[states.Length];
            
            for (int i = 0; i < states.Length; i++)
            {
                var node = states[i];
                var def = new StateDef();
                
                // Hierarchy
                def.ParentIndex = node.Parent != null ? node.Parent.FlatIndex : (ushort)0xFFFF;
                def.FirstChildIndex = node.Children.Count > 0 ? node.Children[0].FlatIndex : (ushort)0xFFFF;
                def.NextSiblingIndex = GetNextSiblingIndex(node);
                
                // Transitions (computed later, set FirstTransitionIndex)
                // Will be filled in FlattenTransitions
                
                // Metadata
                def.Depth = node.Depth;
                def.Flags = BuildStateFlags(node);
                
                // Actions
                def.OnEntryActionId = node.OnEntryAction != null ? actionTable[node.OnEntryAction] : (ushort)0xFFFF;
                def.OnExitActionId = node.OnExitAction != null ? actionTable[node.OnExitAction] : (ushort)0xFFFF;
                def.ActivityActionId = node.ActivityAction != null ? actionTable[node.ActivityAction] : (ushort)0xFFFF;
                def.TimerActionId = node.TimerAction != null ? actionTable[node.TimerAction] : (ushort)0xFFFF;
                
                // History
                def.HistorySlotIndex = node.HistorySlotIndex;
                
                result[i] = def;
            }
            
            return result;
        }
        
        private static ushort GetNextSiblingIndex(StateNode node)
        {
            if (node.Parent == null) return 0xFFFF;
            
            var siblings = node.Parent.Children;
            int index = siblings.IndexOf(node);
            
            if (index >= 0 && index < siblings.Count - 1)
            {
                return siblings[index + 1].FlatIndex;
            }
            
            return 0xFFFF;
        }
        
        private static StateFlags BuildStateFlags(StateNode node)
        {
            StateFlags flags = StateFlags.None;
            
            if (node.IsInitial) flags |= StateFlags.IsInitial;
            if (node.IsHistory) flags |= StateFlags.IsHistory;
            if (node.IsDeepHistory) flags |= StateFlags.IsDeepHistory;
            if (node.IsParallel) flags |= StateFlags.IsParallel;
            if (node.Children.Count > 0) flags |= StateFlags.IsComposite;
            
            return flags;
        }
        
        private static TransitionDef[] FlattenTransitions(
            StateMachineGraph graph,
            Dictionary<string, ushort> actionTable,
            Dictionary<string, ushort> guardTable)
        {
            // Collect all transitions, assign FirstTransitionIndex to states
            var allTransitions = new List<TransitionNode>();
            var states = graph.States.Values.OrderBy(s => s.FlatIndex).ToArray();
            
            ushort transIndex = 0;
            foreach (var state in states)
            {
                if (state.Transitions.Count > 0)
                {
                    // Set FirstTransitionIndex in StateDef (need to update result array)
                    // This is tricky - we need to update StateDef[] from FlattenStates
                    // Better: return FirstTransitionIndex from here and update later
                    
                    foreach (var trans in state.Transitions)
                    {
                        allTransitions.Add(trans);
                    }
                }
            }
            
            // Flatten transitions
            var result = new TransitionDef[allTransitions.Count];
            
            for (int i = 0; i < allTransitions.Count; i++)
            {
                var node = allTransitions[i];
                var def = new TransitionDef();
                
                def.SourceStateIndex = node.Source.FlatIndex;
                def.TargetStateIndex = node.Target.FlatIndex;
                def.EventId = node.EventId;
                def.GuardId = node.GuardFunction != null ? guardTable[node.GuardFunction] : (ushort)0xFFFF;
                def.ActionId = node.ActionFunction != null ? actionTable[node.ActionFunction] : (ushort)0xFFFF;
                
                // Flags (include priority)
                def.Flags = BuildTransitionFlags(node);
                
                // Cost: ARCHITECT Q6 - structural only (LCA distance)
                def.Cost = ComputeTransitionCost(node.Source, node.Target);
                
                result[i] = def;
            }
            
            return result;
        }
        
        private static TransitionFlags BuildTransitionFlags(TransitionNode node)
        {
            TransitionFlags flags = TransitionFlags.None;
            
            if (node.IsInternal) flags |= TransitionFlags.IsInternal;
            
            // Encode priority (4 bits)
            flags |= (TransitionFlags)((node.Priority & 0x0F) << 8);  // Assuming priority in bits 8-11
            
            return flags;
        }
        
        private static byte ComputeTransitionCost(StateNode source, StateNode target)
        {
            // ARCHITECT Q6: Structural cost only (LCA distance)
            // Cost = steps to exit + steps to enter
            
            var lca = FindLCA(source, target);
            
            byte exitSteps = 0;
            var current = source;
            while (current != lca && current != null)
            {
                exitSteps++;
                current = current.Parent;
            }
            
            byte enterSteps = 0;
            current = target;
            while (current != lca && current != null)
            {
                enterSteps++;
                current = current.Parent;
            }
            
            return (byte)(exitSteps + enterSteps);
        }
        
        private static StateNode? FindLCA(StateNode source, StateNode target)
        {
            // Mark ancestors of source
            var ancestors = new HashSet<StateNode>();
            var current = source;
            while (current != null)
            {
                ancestors.Add(current);
                current = current.Parent;
            }
            
            // Walk target up until hit marked ancestor
            current = target;
            while (current != null)
            {
                if (ancestors.Contains(current)) return current;
                current = current.Parent;
            }
            
            return null;  // Should not happen if graph is valid
        }
        
        private static RegionDef[] FlattenRegions(StateMachineGraph graph)
        {
            // Placeholder: Regions not fully implemented yet
            return Array.Empty<RegionDef>();
        }
        
        private static GlobalTransitionDef[] FlattenGlobalTransitions(
            StateMachineGraph graph,
            Dictionary<string, ushort> actionTable,
            Dictionary<string, ushort> guardTable)
        {
            // ARCHITECT Q7: Separate table for global transitions
            var result = new GlobalTransitionDef[graph.GlobalTransitions.Count];
            
            for (int i = 0; i < graph.GlobalTransitions.Count; i++)
            {
                var node = graph.GlobalTransitions[i];
                var def = new GlobalTransitionDef();
                
                def.TargetStateIndex = node.Target.FlatIndex;
                def.EventId = node.EventId;
                def.GuardId = node.GuardFunction != null ? guardTable[node.GuardFunction] : (ushort)0xFFFF;
                def.ActionId = node.ActionFunction != null ? actionTable[node.ActionFunction] : (ushort)0xFFFF;
                def.Flags = BuildTransitionFlags(node);
                
                result[i] = def;
            }
            
            return result;
        }
    }
}
```

**Note:** You'll need to update `FlattenStates` to set `FirstTransitionIndex` and `TransitionCount` after flattening transitions. Consider a second pass.

---

## Task 2: Blob Emitter (TASK-C07)

**File:** `src/Fhsm.Compiler/HsmEmitter.cs`

Create `HsmDefinitionBlob` from flattened data.

### Operations

1. **Create Header** - Populate counts, magic, version
2. **Compute StructureHash** - Hash topology (parent/child structure)
3. **Compute ParameterHash** - Hash logic (actions, guards, events)
4. **Create Blob** - Instantiate with all arrays

### Implementation Skeleton

```csharp
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Fhsm.Kernel.Data;

namespace Fhsm.Compiler
{
    public class HsmEmitter
    {
        /// <summary>
        /// Emit HsmDefinitionBlob from flattened data.
        /// </summary>
        public static HsmDefinitionBlob Emit(HsmFlattener.FlattenedData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            
            var header = new HsmDefinitionHeader();
            
            // Magic & version
            header.Magic = HsmDefinitionHeader.MagicNumber;
            header.FormatVersion = 1;
            
            // Counts
            header.StateCount = (ushort)data.States.Length;
            header.TransitionCount = (ushort)data.Transitions.Length;
            header.RegionCount = (ushort)data.Regions.Length;
            header.GlobalTransitionCount = (ushort)data.GlobalTransitions.Length;
            header.EventDefinitionCount = 0;  // Not tracked yet
            header.ActionCount = (ushort)data.ActionIds.Length;
            
            // Hashes
            header.StructureHash = ComputeStructureHash(data);
            header.ParameterHash = ComputeParameterHash(data);
            
            // Create blob
            var blob = new HsmDefinitionBlob();
            blob.Header = header;
            blob.States = data.States;
            blob.Transitions = data.Transitions;
            blob.Regions = data.Regions;
            blob.GlobalTransitions = data.GlobalTransitions;
            // Note: ActionIds/GuardIds not in current blob - need to add
            
            return blob;
        }
        
        private static uint ComputeStructureHash(HsmFlattener.FlattenedData data)
        {
            // Hash topology: state count, parent indices, depths
            // This should be stable across renames (uses indices, not names)
            
            using var sha = SHA256.Create();
            var builder = new StringBuilder();
            
            builder.Append(data.States.Length);
            
            foreach (var state in data.States)
            {
                builder.Append(state.ParentIndex);
                builder.Append(state.Depth);
                builder.Append(state.FirstChildIndex);
                builder.Append(state.NextSiblingIndex);
            }
            
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = sha.ComputeHash(bytes);
            
            // Take first 4 bytes as uint
            return BitConverter.ToUInt32(hash, 0);
        }
        
        private static uint ComputeParameterHash(HsmFlattener.FlattenedData data)
        {
            // Hash logic: action IDs, guard IDs, event IDs
            // This changes when logic changes
            
            using var sha = SHA256.Create();
            var builder = new StringBuilder();
            
            foreach (var state in data.States)
            {
                builder.Append(state.OnEntryActionId);
                builder.Append(state.OnExitActionId);
                builder.Append(state.ActivityActionId);
                builder.Append(state.TimerActionId);
            }
            
            foreach (var trans in data.Transitions)
            {
                builder.Append(trans.EventId);
                builder.Append(trans.GuardId);
                builder.Append(trans.ActionId);
            }
            
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = sha.ComputeHash(bytes);
            
            return BitConverter.ToUInt32(hash, 0);
        }
    }
}
```

---

## Task 3: Update HsmDefinitionBlob (TASK-D09 Fix)

**File:** `src/Fhsm.Kernel/Data/HsmDefinitionBlob.cs`

Fix issues from BATCH-04 review:

1. Make class `sealed`
2. Make arrays `private readonly`
3. Add `ActionIds[]` and `GuardIds[]` arrays
4. Expose only `ReadOnlySpan<T>` properties

```csharp
public sealed class HsmDefinitionBlob  // Add sealed
{
    public HsmDefinitionHeader Header { get; set; }
    
    private readonly StateDef[] _states;
    private readonly TransitionDef[] _transitions;
    private readonly RegionDef[] _regions;
    private readonly GlobalTransitionDef[] _globalTransitions;
    private readonly ushort[] _actionIds;  // Add
    private readonly ushort[] _guardIds;   // Add
    
    public HsmDefinitionBlob(
        HsmDefinitionHeader header,
        StateDef[] states,
        TransitionDef[] transitions,
        RegionDef[] regions,
        GlobalTransitionDef[] globalTransitions,
        ushort[] actionIds,
        ushort[] guardIds)
    {
        Header = header;
        _states = states ?? Array.Empty<StateDef>();
        _transitions = transitions ?? Array.Empty<TransitionDef>();
        _regions = regions ?? Array.Empty<RegionDef>();
        _globalTransitions = globalTransitions ?? Array.Empty<GlobalTransitionDef>();
        _actionIds = actionIds ?? Array.Empty<ushort>();
        _guardIds = guardIds ?? Array.Empty<ushort>();
    }
    
    // Span accessors only (remove array properties)
    public ReadOnlySpan<StateDef> States => _states;
    public ReadOnlySpan<TransitionDef> Transitions => _transitions;
    public ReadOnlySpan<RegionDef> Regions => _regions;
    public ReadOnlySpan<GlobalTransitionDef> GlobalTransitions => _globalTransitions;
    public ReadOnlySpan<ushort> ActionIds => _actionIds;
    public ReadOnlySpan<ushort> GuardIds => _guardIds;
    
    // Keep indexed accessors
    public ref readonly StateDef GetState(int index) { /* ... */ }
    public ref readonly TransitionDef GetTransition(int index) { /* ... */ }
}
```

---

## Task 4: Tests

**File:** `tests/Fhsm.Tests/Compiler/FlattenerEmitterTests.cs`

Minimum 20 tests covering flattening and emission.

### Flattener Tests (12)

1. States flattened in FlatIndex order
2. ParentIndex correct
3. FirstChildIndex correct
4. NextSiblingIndex correct
5. Action IDs mapped correctly
6. Guard IDs mapped correctly
7. Transition source/target indices correct
8. Transition cost computed (LCA distance)
9. StateFlags built correctly
10. TransitionFlags include priority
11. Global transitions separated
12. Empty graph handled

### Emitter Tests (8)

13. Header magic number correct
14. Header counts correct
15. StructureHash computed
16. ParameterHash computed
17. StructureHash stable across renames (use same structure, different names)
18. ParameterHash changes when actions change
19. Blob created with all arrays
20. Blob validates with HsmValidator

---

## Implementation Notes

### FirstTransitionIndex Update

After flattening transitions, you need to update `StateDef.FirstTransitionIndex` and `TransitionCount`. Consider:

```csharp
// After flattening transitions
ushort transIndex = 0;
foreach (var state in states.OrderBy(s => s.FlatIndex))
{
    if (state.Transitions.Count > 0)
    {
        result.States[state.FlatIndex].FirstTransitionIndex = transIndex;
        result.States[state.FlatIndex].TransitionCount = (ushort)state.Transitions.Count;
        transIndex += (ushort)state.Transitions.Count;
    }
}
```

### Hash Stability

**StructureHash:** Should NOT change if you rename states. Only topology matters.

**ParameterHash:** Should change if you modify actions/guards/events.

Test this explicitly.

### Dispatch Table Sorting

Sort action/guard names alphabetically before assigning IDs for determinism.

---

## Success Criteria

- [ ] TASK-C06: HsmFlattener implemented
- [ ] States flattened (hierarchy preserved)
- [ ] Transitions flattened (costs computed)
- [ ] Dispatch tables built (ActionIds, GuardIds)
- [ ] Global transitions separated
- [ ] TASK-C07: HsmEmitter implemented
- [ ] Header populated
- [ ] StructureHash computed
- [ ] ParameterHash computed
- [ ] TASK-D09: HsmDefinitionBlob fixed (sealed, private arrays, spans only)
- [ ] 20+ tests, all passing
- [ ] Report submitted

---

## Reference

- **Design:** `docs/design/HSM-Implementation-Design.md` §2.4 (Flattening), §2.5 (Emission)
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` Q6 (Cost), Q7 (Global Table)
- **Tasks:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) TASK-C06, TASK-C07, TASK-D09

**Report to:** `.dev-workstream/reports/BATCH-07-REPORT.md`
