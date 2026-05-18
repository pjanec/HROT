# BATCH-06: Compiler - Normalizer & Validator

**Effort:** 3-4 days  
**Phase:** Phase 2 - Compiler (CONTINUE)

---

## Context

BATCH-05 complete: Builder API creates graph. Now implement **normalization** (assign indices, compute metadata) and **validation** (structural correctness).

**Pipeline so far:**
```
User API → Graph ✅ → Normalize → Validate → Flatten → Emit
```

This batch: Normalize + Validate.

---

## Task 1: Graph Normalizer

**File:** `src/Fhsm.Compiler/HsmNormalizer.cs`

Transform graph into normalized form ready for flattening.

### Operations

1. **Assign FlatIndex** - BFS traversal, assign sequential indices
2. **Compute Depth** - Parent depth + 1
3. **Resolve Initial States** - Each composite needs initial child
4. **Assign History Slots** - CRITICAL: Sort by StableId (Architect Q3)
5. **Compute Transition Ranges** - FirstTransitionIndex, TransitionCount per state

### Implementation

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler
{
    public class HsmNormalizer
    {
        /// <summary>
        /// Normalize graph: assign indices, compute depths, resolve initial states.
        /// </summary>
        public static void Normalize(StateMachineGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            
            // 1. Assign FlatIndex (BFS for cache locality)
            AssignFlatIndices(graph);
            
            // 2. Compute depths
            ComputeDepths(graph.RootState);
            
            // 3. Resolve initial states
            ResolveInitialStates(graph);
            
            // 4. Assign history slots (CRITICAL: sort by StableId)
            AssignHistorySlots(graph);
            
            // 5. Compute transition ranges
            ComputeTransitionRanges(graph);
        }
        
        private static void AssignFlatIndices(StateMachineGraph graph)
        {
            // BFS traversal from root
            var queue = new Queue<StateNode>();
            ushort index = 0;
            
            graph.RootState.FlatIndex = index++;
            queue.Enqueue(graph.RootState);
            
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                
                foreach (var child in state.Children)
                {
                    child.FlatIndex = index++;
                    queue.Enqueue(child);
                }
            }
        }
        
        private static void ComputeDepths(StateNode node, byte depth = 0)
        {
            node.Depth = depth;
            
            foreach (var child in node.Children)
            {
                ComputeDepths(child, (byte)(depth + 1));
            }
        }
        
        private static void ResolveInitialStates(StateMachineGraph graph)
        {
            // For each composite state (has children), ensure one is marked Initial
            foreach (var state in graph.States.Values)
            {
                if (state.Children.Count > 0)
                {
                    var initial = state.Children.FirstOrDefault(c => c.IsInitial);
                    
                    if (initial == null)
                    {
                        // No explicit initial → use first child
                        state.Children[0].IsInitial = true;
                    }
                }
            }
        }
        
        private static void AssignHistorySlots(StateMachineGraph graph)
        {
            // ARCHITECT CRITICAL: Sort by StableId (Guid) for hot reload stability
            // NOT by name or declaration order
            
            var historyStates = graph.States.Values
                .Where(s => s.IsHistory)
                .OrderBy(s => s.StableId)  // CRITICAL: Stable sort
                .ToList();
            
            ushort slotIndex = 0;
            foreach (var state in historyStates)
            {
                // Store slot index in state metadata (add field to StateNode)
                // For now, we'll use a dictionary or add field later
                // Placeholder: state.HistorySlotIndex = slotIndex++;
            }
        }
        
        private static void ComputeTransitionRanges(StateMachineGraph graph)
        {
            // Will be used during flattening
            // Each state needs to know: FirstTransitionIndex, TransitionCount
            // This is computed when we flatten transitions into array
            // For now, just validate transitions exist
        }
    }
}
```

**CRITICAL:** Add `HistorySlotIndex` field to `StateNode`:

```csharp
// In StateNode.cs
public ushort HistorySlotIndex { get; set; } = 0xFFFF;  // 0xFFFF = no history
```

---

## Task 2: Graph Validator

**File:** `src/Fhsm.Compiler/HsmGraphValidator.cs`

Validate graph structural correctness. Return list of errors.

### Validation Rules (~20)

**Structural:**
1. Root state exists
2. No orphan states (all reachable from root)
3. No circular parent chains
4. State names unique
5. No self-parenting

**Transitions:**
6. All transitions have valid source
7. All transitions have valid target (not null)
8. Target state exists in graph
9. EventId registered in graph
10. No duplicate transitions (same source + event + target)

**Initial States:**
11. Each composite has exactly one initial child
12. Leaf states cannot be initial (no children to enter)

**History States:**
13. History states have valid parent
14. History parent is composite (has children)
15. Deep history only on composites with depth > 1

**Functions:**
16. OnEntry actions registered (if not null)
17. OnExit actions registered (if not null)
18. Activity actions registered (if not null)
19. Guard functions registered (if not null)
20. Transition actions registered (if not null)

**Limits:**
21. Depth <= 15 (byte limit)
22. State count <= 65535 (ushort limit)
23. Transition count <= 65535

### Implementation

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler.Graph;

namespace Fhsm.Compiler
{
    public class HsmGraphValidator
    {
        public class ValidationError
        {
            public string Message { get; set; }
            public string? StateName { get; set; }
            
            public ValidationError(string message, string? stateName = null)
            {
                Message = message;
                StateName = stateName;
            }
            
            public override string ToString() => 
                StateName != null ? $"[{StateName}] {Message}" : Message;
        }
        
        /// <summary>
        /// Validate graph. Returns list of errors (empty if valid).
        /// </summary>
        public static List<ValidationError> Validate(StateMachineGraph graph)
        {
            var errors = new List<ValidationError>();
            
            if (graph == null)
            {
                errors.Add(new ValidationError("Graph is null"));
                return errors;
            }
            
            // Run all validation rules
            ValidateStructure(graph, errors);
            ValidateTransitions(graph, errors);
            ValidateInitialStates(graph, errors);
            ValidateHistoryStates(graph, errors);
            ValidateFunctions(graph, errors);
            ValidateLimits(graph, errors);
            
            return errors;
        }
        
        private static void ValidateStructure(StateMachineGraph graph, List<ValidationError> errors)
        {
            // 1. Root exists
            if (graph.RootState == null)
            {
                errors.Add(new ValidationError("Root state is null"));
                return;
            }
            
            // 2. No orphans (all states reachable from root)
            var reachable = new HashSet<StateNode>();
            CollectReachable(graph.RootState, reachable);
            
            foreach (var state in graph.States.Values)
            {
                if (!reachable.Contains(state))
                {
                    errors.Add(new ValidationError("Orphan state (not reachable from root)", state.Name));
                }
            }
            
            // 3. No circular parent chains
            foreach (var state in graph.States.Values)
            {
                if (HasCircularParent(state))
                {
                    errors.Add(new ValidationError("Circular parent chain detected", state.Name));
                }
            }
            
            // 4. State names unique (already enforced by AddState, but check)
            var names = new HashSet<string>();
            foreach (var state in graph.States.Values)
            {
                if (!names.Add(state.Name))
                {
                    errors.Add(new ValidationError($"Duplicate state name: {state.Name}"));
                }
            }
        }
        
        private static void ValidateTransitions(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                foreach (var trans in state.Transitions)
                {
                    // Source valid
                    if (trans.Source == null)
                    {
                        errors.Add(new ValidationError("Transition has null source", state.Name));
                        continue;
                    }
                    
                    // Target valid
                    if (trans.Target == null)
                    {
                        errors.Add(new ValidationError("Transition has null target", state.Name));
                        continue;
                    }
                    
                    // Target exists in graph
                    if (!graph.States.ContainsValue(trans.Target))
                    {
                        errors.Add(new ValidationError($"Transition target '{trans.Target.Name}' not in graph", state.Name));
                    }
                    
                    // EventId registered
                    if (!graph.EventNameToId.ContainsValue(trans.EventId))
                    {
                        errors.Add(new ValidationError($"Transition uses unregistered EventId {trans.EventId}", state.Name));
                    }
                }
            }
        }
        
        private static void ValidateInitialStates(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                if (state.Children.Count > 0)
                {
                    // Composite: must have exactly one initial
                    var initialCount = state.Children.Count(c => c.IsInitial);
                    
                    if (initialCount == 0)
                    {
                        errors.Add(new ValidationError("Composite state has no initial child", state.Name));
                    }
                    else if (initialCount > 1)
                    {
                        errors.Add(new ValidationError("Composite state has multiple initial children", state.Name));
                    }
                }
                else
                {
                    // Leaf: should not be marked initial (meaningless)
                    // Actually, initial is relative to parent, so this is OK
                }
            }
        }
        
        private static void ValidateHistoryStates(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                if (state.IsHistory)
                {
                    // Must have parent
                    if (state.Parent == null)
                    {
                        errors.Add(new ValidationError("History state has no parent", state.Name));
                        continue;
                    }
                    
                    // Parent must be composite
                    if (state.Parent.Children.Count == 0)
                    {
                        errors.Add(new ValidationError("History state parent is not composite", state.Name));
                    }
                }
            }
        }
        
        private static void ValidateFunctions(StateMachineGraph graph, List<ValidationError> errors)
        {
            foreach (var state in graph.States.Values)
            {
                // Check actions
                if (state.OnEntryAction != null && !graph.RegisteredActions.Contains(state.OnEntryAction))
                {
                    errors.Add(new ValidationError($"OnEntry action '{state.OnEntryAction}' not registered", state.Name));
                }
                
                if (state.OnExitAction != null && !graph.RegisteredActions.Contains(state.OnExitAction))
                {
                    errors.Add(new ValidationError($"OnExit action '{state.OnExitAction}' not registered", state.Name));
                }
                
                if (state.ActivityAction != null && !graph.RegisteredActions.Contains(state.ActivityAction))
                {
                    errors.Add(new ValidationError($"Activity action '{state.ActivityAction}' not registered", state.Name));
                }
                
                // Check transition guards/actions
                foreach (var trans in state.Transitions)
                {
                    if (trans.GuardFunction != null && !graph.RegisteredGuards.Contains(trans.GuardFunction))
                    {
                        errors.Add(new ValidationError($"Guard '{trans.GuardFunction}' not registered", state.Name));
                    }
                    
                    if (trans.ActionFunction != null && !graph.RegisteredActions.Contains(trans.ActionFunction))
                    {
                        errors.Add(new ValidationError($"Transition action '{trans.ActionFunction}' not registered", state.Name));
                    }
                }
            }
        }
        
        private static void ValidateLimits(StateMachineGraph graph, List<ValidationError> errors)
        {
            // State count
            if (graph.States.Count > 65535)
            {
                errors.Add(new ValidationError($"State count {graph.States.Count} exceeds limit 65535"));
            }
            
            // Depth
            foreach (var state in graph.States.Values)
            {
                if (state.Depth > 15)
                {
                    errors.Add(new ValidationError($"Depth {state.Depth} exceeds limit 15", state.Name));
                }
            }
            
            // Transition count
            int totalTransitions = graph.States.Values.Sum(s => s.Transitions.Count);
            if (totalTransitions > 65535)
            {
                errors.Add(new ValidationError($"Transition count {totalTransitions} exceeds limit 65535"));
            }
        }
        
        // Helpers
        
        private static void CollectReachable(StateNode node, HashSet<StateNode> reachable)
        {
            if (!reachable.Add(node)) return;  // Already visited
            
            foreach (var child in node.Children)
            {
                CollectReachable(child, reachable);
            }
        }
        
        private static bool HasCircularParent(StateNode state)
        {
            var visited = new HashSet<StateNode>();
            var current = state;
            
            while (current != null)
            {
                if (!visited.Add(current)) return true;  // Cycle detected
                current = current.Parent;
            }
            
            return false;
        }
    }
}
```

---

## Task 3: Update StateNode

**File:** `src/Fhsm.Compiler/Graph/StateNode.cs`

Add `HistorySlotIndex` field:

```csharp
// Add after FlatIndex
public ushort HistorySlotIndex { get; set; } = 0xFFFF;  // 0xFFFF = no history
```

---

## Task 4: Tests

**File:** `tests/Fhsm.Tests/Compiler/NormalizerValidatorTests.cs`

Minimum 25 tests covering normalization and validation.

### Normalizer Tests (10)

1. FlatIndex assigned in BFS order
2. Root gets index 0
3. Depth computed correctly (root=0, child=1, grandchild=2)
4. Initial state resolved (first child if none marked)
5. Initial state preserved (if explicitly marked)
6. History slots assigned
7. History slots sorted by StableId (not name)
8. Multiple history states get sequential slots
9. Non-history states get 0xFFFF
10. Normalization doesn't throw on valid graph

### Validator Tests (15)

11. Valid graph passes (no errors)
12. Orphan state detected
13. Circular parent chain detected
14. Null transition target detected
15. Unregistered event detected
16. Composite without initial detected
17. Multiple initial children detected
18. History without parent detected
19. Unregistered OnEntry action detected
20. Unregistered OnExit action detected
21. Unregistered Activity action detected
22. Unregistered Guard detected
23. Unregistered transition action detected
24. Depth limit exceeded detected
25. State count limit (mock with 65536 states - may skip if too slow)

---

## Implementation Notes

### History Slot Sorting (CRITICAL)

**Architect Decision Q3:** MUST sort by StableId (Guid), NOT by name or declaration order.

**Why:** If user renames state "OldName" → "NewName", StableId stays same, slot index stays same, hot reload preserves history data.

**Test:** Create two history states "B_History" and "A_History". Verify "B" gets lower slot (if StableId is lower), NOT "A".

### BFS vs DFS

Use BFS for FlatIndex assignment → better cache locality. States at same depth level are sequential in memory.

### Validation Error Accumulation

Collect ALL errors, don't stop at first. User sees complete list.

### Function Registration

Builder API should auto-register functions when used? Or require explicit registration? Current spec: explicit registration. Validation catches missing registrations.

---

## Success Criteria

- [ ] HsmNormalizer implemented
- [ ] FlatIndex assigned (BFS)
- [ ] Depth computed
- [ ] Initial states resolved
- [ ] History slots assigned (sorted by StableId)
- [ ] HsmGraphValidator implemented
- [ ] 20+ validation rules
- [ ] StateNode.HistorySlotIndex added
- [ ] 25+ tests, all passing
- [ ] Report submitted

---

## Reference

- **Design:** `docs/design/HSM-Implementation-Design.md` Section 2.2 (Normalization), 2.3 (Validation)
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` Q3 (History Slot Stability)
- **Task Def:** `.dev-workstream/TASK-DEFINITIONS.md` BATCH-06

**Report to:** `.dev-workstream/reports/BATCH-06-REPORT.md`
