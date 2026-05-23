# BATCH-31 -- HS-S1-19, HS-S1-22

## Tasks
- TASK-HS-S1-19: OutputLaneMask inference (with OutputLaneMaskInferenceTests)
- TASK-HS-S1-22: HSM validation rules (14 diagnostic codes, with HsmValidationTests)

## Non-negotiable rules
1. No Unicode characters in comments or string literals (ASCII only).
2. Build must succeed with 0 errors and 0 warnings.
3. All 70 existing tests must continue passing.
4. Do not modify any existing file unless required by these tasks.
5. Preserve existing comments exactly.

---

## Overview

Create 6 new source files:
- `Validation/HsmDiagnosticCode.cs` -- 14-code enum
- `Validation/HsmDiagnostic.cs` -- diagnostic record
- `Validation/HsmValidator.cs` -- validation rules implementation
- `Validation/HsmOutputLaneMaskInferrer.cs` -- lane mask inference
- `HsmValidationTests.cs` -- 12 tests
- `OutputLaneMaskInferenceTests.cs` -- 5 tests

### File locations

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/
  Validation/
    HsmDiagnosticCode.cs            <-- CREATE
    HsmDiagnostic.cs                <-- CREATE
    HsmValidator.cs                 <-- CREATE
    HsmOutputLaneMaskInferrer.cs    <-- CREATE

Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/
  HsmValidationTests.cs             <-- CREATE
  OutputLaneMaskInferenceTests.cs   <-- CREATE
```

---

## Step 1 -- Understand existing code

### 1.1 Read HsmAsset.cs

File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`

Key structures needed:
- `StateNode` fields: `IsParallel`, `IsFinal`, `IsHistory`, `IsDeepHistory`, `IsInitial`, `Children`, `OutgoingTransitions`, `Parent`, `DeferredEventIds`, `RegionNodes`
- `TransitionNode.EventId`
- `HsmAsset.AllStates`, `AllTransitions`, `AllEvents`, `AllRegions`, `RootState`

### 1.2 Check HsmActionAttribute for Lane property

File: `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs`

Verify: `public CommandLane Lane { get; set; } = CommandLane.None;`

### 1.3 Check CommandLane enum

File: `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/Enums.cs`

CommandLane: Animation=0, Navigation=1, Gameplay=2, Blackboard=3, Audio=4, VFX=5, Message=6, Count=7, None=0xFF

OutputLaneMask is a byte where bit N = 1 means CommandLane N is written by the state.
Lane.None (0xFF) does NOT set any bit (the state has no explicit lane).

### 1.4 Read the existing BTreeValidator as pattern

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Validation/BTreeValidator.cs`

Note the return type, how diagnostics are built, the pattern used.

### 1.5 Read the test setup pattern for HsmAsset

File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmGraphModelTests.cs`

You'll need the same pattern to build test assets for validation tests.

---

## Step 2 -- Create HsmDiagnosticCode.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnosticCode.cs`

### Content

```csharp
namespace Hrot.Hsm.Editor.Validation;

// Diagnostic codes for the HSM editor validator.
// See HSM_Editor_NodeEditor_Host_Design.md section 12.
public enum HsmDiagnosticCode
{
    // A composite state (with children) has no child marked IsInitial,
    // or more than one child marked IsInitial.
    CompositeWithoutInitialChild,

    // A composite state has more than one child marked IsInitial.
    MultipleInitialChildrenInSameParent,

    // A history pseudo-state's parent is not a composite state.
    HistoryOutsideComposite,

    // A final state (IsFinal=true) has one or more child states.
    FinalStateWithChildren,

    // A final state (IsFinal=true) has one or more outgoing transitions.
    FinalStateWithOutgoingTransition,

    // An action FQN referenced by a state or transition was not found in the registry.
    UnboundAction,

    // A guard FQN referenced by a transition was not found in the registry.
    UnboundGuard,

    // Two states in different parallel regions of the same composite write to
    // the same CommandLane via their OutputLaneMask.
    OutputLaneConflict,

    // A state's depth in the tree exceeds 16 (kernel byte limit).
    StateDepthExceeded,

    // A parallel composite has more regions than the allowed tier count.
    RegionCountExceedsTier,

    // Static analysis found a potential infinite microstep due to a cycle
    // of same-priority transitions reachable in one RTC tick.
    TransitionPriorityCycle,

    // A transition references an event ID that is no longer present in AllEvents.
    EventReferenceDangling,

    // An action's Lane attribute changed since the last snapshot;
    // OutputLaneMask was updated automatically.
    ActionSignatureMismatch,

    // After a hot reload, a reference in the asset points to a symbol
    // that no longer exists in the new assembly.
    DanglingReferenceAfterReload,
}
```

---

## Step 3 -- Create HsmDiagnostic.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnostic.cs`

### Content

```csharp
using System;
using System.Collections.Generic;

namespace Hrot.Hsm.Editor.Validation;

// Severity level matching the shared diagnostic convention.
public enum HsmDiagnosticSeverity { Info, Warning, Error }

// A single diagnostic produced by the HsmValidator.
// TargetStableIds: the states (or transitions, via VisualId) implicated by this diagnostic.
public sealed record HsmDiagnostic(
    HsmDiagnosticCode Code,
    HsmDiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<Guid> TargetStableIds);
```

---

## Step 4 -- Create HsmValidator.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs`

### Requirements
- `public sealed class HsmValidator`
- Constructor: `public HsmValidator()` (stateless validator)
- `public IReadOnlyList<HsmDiagnostic> Validate(HsmAsset asset)`

### Rules to implement (first 8 produce real diagnostics; last 6 return empty for now)

Implement the following rules. Each rule adds zero or more `HsmDiagnostic` items to a list.

**1. CompositeWithoutInitialChild / MultipleInitialChildrenInSameParent**

For each state `s` in `asset.AllStates` where `s.Children.Count > 0`:
- Count children with `IsInitial = true`.
- If count == 0: add diagnostic `CompositeWithoutInitialChild` (Error) targeting `s.StableId`.
- If count > 1: add diagnostic `MultipleInitialChildrenInSameParent` (Error) targeting `s.StableId`.

**2. HistoryOutsideComposite**

For each state `s` in `asset.AllStates` where `s.IsHistory || s.IsDeepHistory`:
- If `s.Parent == null || s.Parent == asset.RootState || s.Parent.Children.Count == 0`:
  add diagnostic `HistoryOutsideComposite` (Warning) targeting `s.StableId`.

Actually: History is meaningful if parent is a composite (has children). If the parent
has no other children (or is root), it's misplaced. Simpler: flag if parent is null
or parent.Children.Count <= 1 (only the history state itself, no real children to track).

**3. FinalStateWithChildren**

For each state `s` where `s.IsFinal && s.Children.Count > 0`:
- Add diagnostic `FinalStateWithChildren` (Error) targeting `s.StableId`.

**4. FinalStateWithOutgoingTransition**

For each state `s` where `s.IsFinal && s.OutgoingTransitions.Count > 0`:
- Add diagnostic `FinalStateWithOutgoingTransition` (Error) targeting `s.StableId`.

**5. StateDepthExceeded**

For each state `s` in `asset.AllStates`:
- Compute depth by walking `s.Parent` chain (stop at RootState / null).
  depth(RootState) = 0; depth(top-level state) = 1.
- If depth > 16: add `StateDepthExceeded` (Error) targeting `s.StableId`.

**6. EventReferenceDangling**

For each transition `t` in `asset.AllTransitions` where `t.EventId != 0`:
- Check: `asset.FindEventById(t.EventId) == null`
- If true: add `EventReferenceDangling` (Error) targeting `new Guid[]{ t.VisualId }`.

For each global transition `g` in `asset.AllGlobalTransitions` where `g.EventId != 0`:
- Same check.

**7. OutputLaneConflict**

For each state `s` in `asset.AllStates` where `s.IsParallel && s.RegionNodes.Count >= 2`:
- Collect all states in each region by filtering `s.Children` by `RegionIndex`.
- For each region R, compute `mask_R = OR of all children's OutputLaneMask`.
  (children means direct children only for simplicity in Slice 1)
- For each pair of distinct regions (R1, R2): if `mask_R1 & mask_R2 != 0`:
  add `OutputLaneConflict` (Warning) targeting the conflicting states.

**8. HistoryOutsideComposite (refined)**

A history state's parent must be a composite with more than one other child.
For simplicity, just check: parent must be non-null, not RootState, and have Children.Count > 1
(otherwise there's nothing to track history for).

Actually let's simplify: just check that the parent is a composite (Children.Count > 1 means
at least 2 children including the history state, so there's something to track).

**Stub rules (return no diagnostics):**
- UnboundAction: skip (no action registry yet)
- UnboundGuard: skip
- RegionCountExceedsTier: skip
- TransitionPriorityCycle: skip
- ActionSignatureMismatch: skip
- DanglingReferenceAfterReload: skip

### Imports
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Hsm.Editor.Model;
```

---

## Step 5 -- Create HsmOutputLaneMaskInferrer.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmOutputLaneMaskInferrer.cs`

### Requirements

The inferrer builds an `OutputLaneMask` for each state from the action FQN strings.
It requires a dictionary mapping action FQNs to their declared `CommandLane`.

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Validation;

// Infers OutputLaneMask for each StateNode from action FQN -> CommandLane mappings.
// The mappings are built by reflecting against the loaded assembly.
public sealed class HsmOutputLaneMaskInferrer
{
    // Reflects all types in the given assemblies and builds a dictionary
    // from action FQN (full method name) to CommandLane.
    // Only methods with [HsmAction] attribute are included.
    // Methods with Lane = CommandLane.None are excluded (contribute no bits).
    public static IReadOnlyDictionary<string, CommandLane> BuildLaneDictionary(
        IEnumerable<Assembly> assemblies)
    {
        var dict = new Dictionary<string, CommandLane>(StringComparer.Ordinal);
        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<HsmActionAttribute>();
                    if (attr == null) continue;
                    if (attr.Lane == CommandLane.None) continue;
                    // Use the full qualified name as the FQN key.
                    var fqn = type.FullName + "." + method.Name;
                    dict[fqn] = attr.Lane;
                }
            }
        }
        return dict;
    }

    // Computes OutputLaneMask for a single state using the pre-built lane dictionary.
    // Considers OnEntry, OnExit, Activity, and Timer actions.
    // Returns a byte where bit N = 1 when CommandLane N is used.
    public static byte ComputeMask(StateNode state,
        IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        byte mask = 0;
        mask |= LaneBit(state.OnEntryAction, laneMap);
        mask |= LaneBit(state.OnExitAction, laneMap);
        mask |= LaneBit(state.ActivityAction, laneMap);
        mask |= LaneBit(state.TimerAction, laneMap);
        return mask;
    }

    // Returns the bit contribution of a single action FQN.
    private static byte LaneBit(string? fqn, IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        if (fqn == null) return 0;
        if (!laneMap.TryGetValue(fqn, out var lane)) return 0;
        if ((byte)lane >= (byte)CommandLane.Count) return 0;   // None or unknown
        return (byte)(1 << (byte)lane);
    }

    // Applies inferred OutputLaneMask to all states in the asset.
    public static void ApplyToAsset(HsmAsset asset,
        IReadOnlyDictionary<string, CommandLane> laneMap)
    {
        foreach (var s in asset.AllStates)
            s.OutputLaneMask = ComputeMask(s, laneMap);
    }
}
```

---

## Step 6 -- Create HsmValidationTests.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmValidationTests.cs`

### Imports
```csharp
using System;
using System.Linq;
using FluentAssertions;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;
```

### Test setup

Before writing tests, read HsmGraphModelTests.cs to see how minimal HsmAssets are built.
You'll need to create simple state hierarchies.

For each test:
1. Build a minimal HsmAsset (using the same helper/pattern as HsmGraphModelTests).
2. Create a `HsmValidator` instance.
3. Call `Validate(asset)` and check the result.

IMPORTANT: Look at the HsmGraphModelTests.cs helper to understand what StateNode fields
must be set for a valid state. You'll also need `StateNode.OutgoingTransitions`.

### Tests (12 tests):

1. **`Valid_asset_produces_no_diagnostics`**
   - Asset: Root -> [A (initial), B], A has IsInitial=true
   - Expected: 0 diagnostics

2. **`Composite_without_initial_child_produces_error`**
   - Asset: Root -> Composite (IsComposite=true, no child with IsInitial)
   - Add a child that is NOT marked initial
   - Expected: 1 diagnostic, Code = CompositeWithoutInitialChild, Severity = Error

3. **`Multiple_initial_children_produces_error`**
   - Asset: Root -> Composite -> [A (initial), B (initial)]
   - Expected: 1 diagnostic, Code = MultipleInitialChildrenInSameParent, Severity = Error

4. **`Final_state_with_outgoing_transition_produces_error`**
   - Asset: Root -> [A (IsFinal), B], A has an outgoing transition to B
   - Expected: at least 1 diagnostic, Code = FinalStateWithOutgoingTransition

5. **`Final_state_with_children_produces_error`**
   - Asset: Root -> FinalComposite (IsFinal + Children.Count > 0)
   - Expected: 1 diagnostic, Code = FinalStateWithChildren

6. **`History_outside_composite_produces_warning`**
   - Asset: Root -> [H (IsHistory=true, lone child)]
   - Parent of H (= Root) has only H as child, so no composite context to track
   - Expected: 1 diagnostic, Code = HistoryOutsideComposite, Severity = Warning

7. **`State_depth_exceeded_produces_error`**
   - Build a chain 17 levels deep: Root -> S1 -> S2 -> ... -> S17
   - Expected: diagnostic Code = StateDepthExceeded on S17 (depth = 17 > 16)

8. **`Event_reference_dangling_for_transition`**
   - Asset with a transition referencing EventId = 99, but AllEvents has no event with Id 99
   - Expected: diagnostic Code = EventReferenceDangling

9. **`Valid_composite_with_single_initial_child_no_diagnostics`**
   - Asset: Root -> Composite -> [Init (IsInitial), Other]
   - Expected: 0 diagnostics

10. **`Multiple_diagnostics_for_multiple_violations`**
    - Asset with 2 composites, each missing initial child
    - Expected: 2 diagnostics, both CompositeWithoutInitialChild

11. **`OutputLaneConflict_in_parallel_state_produces_warning`**
    - Asset: Root -> Parallel (IsParallel, 2 regions)
    - Region 0: child CA with OutputLaneMask = 0x01 (Animation)
    - Region 1: child CB with OutputLaneMask = 0x01 (Animation)
    - Expected: diagnostic Code = OutputLaneConflict, Severity = Warning

12. **`No_conflict_when_parallel_regions_have_disjoint_lanes`**
    - Asset: Root -> Parallel, Region 0: child with mask=0x01, Region 1: child with mask=0x02
    - Expected: 0 OutputLaneConflict diagnostics

---

## Step 7 -- Create OutputLaneMaskInferenceTests.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/OutputLaneMaskInferenceTests.cs`

### Imports
```csharp
using System.Collections.Generic;
using System.Reflection;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Validation;
using Xunit;
```

### Test helper: inline action class

Define a private static class inside the test class to hold test actions:
```csharp
private static class TestActions
{
    [HsmAction(Lane = CommandLane.Animation)]
    public static void AnimAction() { }

    [HsmAction(Lane = CommandLane.Navigation)]
    public static void NavAction() { }

    [HsmAction(Lane = CommandLane.None)]
    public static void NoLaneAction() { }

    [HsmAction]  // Lane defaults to None
    public static void DefaultLaneAction() { }
}
```

Use `typeof(TestActions).Assembly` to get the assembly for `BuildLaneDictionary`.

The FQN key for `AnimAction` would be `typeof(TestActions).FullName + ".AnimAction"`.
In the test, compute the expected FQN and verify.

### Tests (5 tests):

1. **`BuildLaneDictionary_includes_animation_lane_method`**
   - Build dictionary from test assembly
   - Key for AnimAction should map to `CommandLane.Animation`

2. **`BuildLaneDictionary_excludes_none_lane_methods`**
   - NoLaneAction and DefaultLaneAction should NOT be in the dictionary

3. **`ComputeMask_single_animation_action_returns_bit0`**
   - StateNode with `OnEntryAction = FQN_of_AnimAction`
   - Expected mask = 0x01 (bit 0 = Animation)

4. **`ComputeMask_two_actions_different_lanes_returns_or`**
   - StateNode with `OnEntryAction = AnimAction FQN`, `ActivityAction = NavAction FQN`
   - Expected mask = 0x03 (bit 0 + bit 1)

5. **`ComputeMask_no_actions_returns_zero`**
   - StateNode with all action fields null
   - Expected mask = 0x00

---

## Step 8 -- Build and test

```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
```

Expected: 0 errors, 0 warnings, 87 tests passing (70 existing + 12 validation + 5 lane mask).

---

## Completion checklist

- [ ] `Validation/HsmDiagnosticCode.cs` created (14 codes)
- [ ] `Validation/HsmDiagnostic.cs` created
- [ ] `Validation/HsmValidator.cs` created (8 implemented rules + 6 stubs)
- [ ] `Validation/HsmOutputLaneMaskInferrer.cs` created
- [ ] `HsmValidationTests.cs` created (12 tests)
- [ ] `OutputLaneMaskInferenceTests.cs` created (5 tests)
- [ ] Build: 0 errors, 0 warnings
- [ ] Tests: all 87 pass
- [ ] `git add -A && git commit -m "BATCH-31: HS-S1-19/22 - OutputLaneMask inferrer, HSM validator with 8 rules (87 tests)"`
