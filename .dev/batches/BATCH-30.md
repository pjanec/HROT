# BATCH-30 -- HS-S1-18, HS-S1-20, HS-S1-21

## Tasks
- TASK-HS-S1-18: Action / Guard pickers (HSM) -- picker attribute stubs
- TASK-HS-S1-20: HSM facet structs (5 structs)
- TASK-HS-S1-21: Inspector dispatch + LCA computation (with LcaComputationTests)

## Non-negotiable rules
1. No Unicode characters in comments or string literals (ASCII only).
2. Build must succeed with 0 errors and 0 warnings.
3. All 62 existing tests must continue passing.
4. Do not modify any existing file unless required by these tasks.
5. Preserve existing comments exactly.

---

## Overview

Create 8 new source files:
- 5 picker attribute files (trivial marker attributes)
- 1 facets file (5 inspector facet structs)
- 1 sub-selections file (2 new selection records)
- 1 facet mapper file (with FindLca helper)

And 1 new test file with LCA tests.

### File locations

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/
  Inspector/
    HsmActionPickerAttribute.cs      <-- CREATE
    HsmGuardPickerAttribute.cs       <-- CREATE
    HsmEventPickerAttribute.cs       <-- CREATE
    HsmStateSelectorAttribute.cs     <-- CREATE
    HsmSyncGroupPickerAttribute.cs   <-- CREATE
    HsmFacets.cs                     <-- CREATE
    HsmSubSelections.cs              <-- CREATE
    HsmFacetMapper.cs                <-- CREATE

Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/
  HsmLcaTests.cs                     <-- CREATE
```

---

## Step 1 -- Understand existing code before writing

### 1.1 Read HsmAsset.cs to understand StateNode hierarchy

File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`

Key facts:
- `StateNode.Parent` is `StateNode?`; null only for RootState itself
- `HsmAsset.RootState` is a synthetic root; top-level states have Parent = RootState
- `StateNode.Children` is a List<StateNode>
- LCA computation: walk from a state to RootState to build the ancestor path, then find
  the last common element of two such paths.

### 1.2 Read BTreeFacets.cs as pattern

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreeFacets.cs`

Check the `using StructEdit.Core.Attributes;` import pattern.

### 1.3 Read BehaviorHashPickerAttribute.cs as pattern

File: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BehaviorHashPickerAttribute.cs`

### 1.4 Read SubSelectionRecords.cs to see existing selection records

File: `Hrot/Editor/Hrot.Editor.AiShared/Selection/SubSelectionRecords.cs`

The file already has:
- `HsmStateSelection(Guid StableId)`
- `HsmTransitionSelection(Guid VisualId)`
- `HsmRegionSelection(Guid StableId, int RegionIndex)`

We need to add two more records for HS-S1-21.
IMPORTANT: Do NOT modify SubSelectionRecords.cs -- add the new records in
`Hrot.Hsm.Editor/Inspector/HsmSubSelections.cs` instead.

### 1.5 Read the existing test pattern for building HsmAsset

File: `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmGraphModelTests.cs`

See how the test builds a minimal HsmAsset with a state hierarchy.
You will need the same pattern for LCA tests.

### 1.6 Read EventFacet spec from TASK-DETAIL

The spec mentions EventPriority in the EventFacet.
Check: `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/Data/Enums.cs`
Look for: `EventPriority` (byte enum: Low=0, Normal=1, Interrupt=2)

---

## Step 2 -- Create picker attribute files

All five files follow the same pattern. Each is a marker attribute
with `[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`.

Namespace for all: `Hrot.Hsm.Editor.Inspector`

### 2.1 HsmActionPickerAttribute.cs

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmActionPickerAttribute.cs
```

```csharp
namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM action picker.
// Populated from HsmActionDispatcher.AllActions.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmActionPickerAttribute : Attribute { }
```

### 2.2 HsmGuardPickerAttribute.cs

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmGuardPickerAttribute.cs
```

```csharp
namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM guard picker.
// Populated from HsmActionDispatcher.AllGuards.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmGuardPickerAttribute : Attribute { }
```

### 2.3 HsmEventPickerAttribute.cs

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmEventPickerAttribute.cs
```

```csharp
namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM event picker.
// Populated from the current asset's event list.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmEventPickerAttribute : Attribute { }
```

### 2.4 HsmStateSelectorAttribute.cs

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmStateSelectorAttribute.cs
```

```csharp
namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM state selector.
// Populated from the current asset's AllStates list.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmStateSelectorAttribute : Attribute { }
```

### 2.5 HsmSyncGroupPickerAttribute.cs

```
Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmSyncGroupPickerAttribute.cs
```

```csharp
namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM sync-group picker.
// Populated from all distinct SyncGroupIds in the current asset's transitions.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmSyncGroupPickerAttribute : Attribute { }
```

---

## Step 3 -- Create HsmFacets.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacets.cs`

### Imports
```csharp
using System.Collections.Generic;
using Fhsm.Kernel.Data;
using StructEdit.Core.Attributes;
```

### Namespace
`Hrot.Hsm.Editor.Inspector`

### Content

Implement all 5 facet structs. Copy the spec from
`HSM_Editor_NodeEditor_Host_Design.md` section 11.1 closely.

Key notes:
- Use `using StructEdit.Core.Attributes;` (transitively available via Hrot.Editor.AiShared -> Fdp.Presentation -> StructEdit.Core).
- Use `using Fhsm.Kernel.Data;` for `StateFlags` and `EventPriority`.
- `[EditDisplayName("...")]` uses `EditDisplayNameAttribute` from StructEdit.Core.Attributes.
- `[EditReadOnly]` uses `EditReadOnlyAttribute` from StructEdit.Core.Attributes.
- `[EditRange(0, 255)]` uses `EditRangeAttribute` from StructEdit.Core.Attributes.
- `[HsmActionPicker]`, `[HsmGuardPicker]`, `[HsmEventPicker]`, `[HsmStateSelector]`,
  `[HsmSyncGroupPicker]` use the attributes from Step 2.

```csharp
// Inspector facet struct for a StateNode. Shown when a state is selected.
public struct StateFacet { ... }

// Inspector facet struct for a TransitionNode. Shown when a transition is selected.
public struct TransitionFacet { ... }

// Inspector facet struct for a RegionNode. Shown when a region is selected.
public struct RegionFacet { ... }

// Inspector facet struct for an EventDefinition. Shown when an event row is selected.
public struct EventFacet { ... }

// Inspector facet struct for a GlobalTransitionNode. Shown when a global is selected.
public struct GlobalTransitionFacet { ... }
```

For `StateFacet.Flags`, use type `StateFlags` (from Fhsm.Kernel.Data). For
`EventFacet.Priority`, use type `EventPriority` (from Fhsm.Kernel.Data).

For `TransitionFacet`, the `Kind` field uses `TransitionKind` (from Hrot.Hsm.Editor.Model
namespace -- already defined in HsmAsset.cs).

For `StateFacet.DeferredEventIds`, use `List<ushort>`.

---

## Step 4 -- Create HsmSubSelections.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmSubSelections.cs`

### Content

```csharp
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Hsm.Editor.Inspector;

// Sub-selection record for when an event row in the events table is selected.
public sealed record HsmEventSelection(ushort EventId) : IAssetSubSelection;

// Sub-selection record for when a global transition chip is selected.
public sealed record HsmGlobalTransitionSelection(Guid VisualId) : IAssetSubSelection;
```

---

## Step 5 -- Create HsmFacetMapper.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacetMapper.cs`

### Requirements
- `public sealed class HsmFacetMapper`
- Constructor: `public HsmFacetMapper(HsmAsset asset)`
- Store in `private readonly HsmAsset _asset;`

Methods (populate facets from the asset):

```csharp
public StateFacet GetStateFacet(Guid stableId);
public TransitionFacet GetTransitionFacet(Guid visualId);
public RegionFacet GetRegionFacet(Guid parentStableId, int regionIndex);
public EventFacet GetEventFacet(ushort eventId);
public GlobalTransitionFacet GetGlobalTransitionFacet(Guid visualId);
```

Each method looks up the relevant node, fills in the facet struct, and returns it.
For fields the node doesn't know (like LcaStateName), compute them on the spot.

The `GetTransitionFacet` method must compute LCA via `FindLca(t.Source, t.Target)` and
populate `LcaStateName` and `LcaCost` (= depth(a) + depth(b) - 2 * depth(lca)).

### FindLca helper (public, for testing)

```csharp
// Finds the least common ancestor of two states in the tree.
// The LCA is the deepest state that is an ancestor of both a and b (inclusive).
public StateNode FindLca(StateNode a, StateNode b)
{
    var aPath = AncestorPathFromRoot(a);
    var bPath = AncestorPathFromRoot(b);
    StateNode lca = _asset.RootState;
    for (int i = 0; i < Math.Min(aPath.Count, bPath.Count); i++)
    {
        if (aPath[i] == bPath[i]) lca = aPath[i];
        else break;
    }
    return lca;
}

// Returns the path from RootState down to the given state (inclusive at both ends).
// E.g. for a leaf state in RootState -> A -> B -> Leaf, returns [RootState, A, B, Leaf].
private List<StateNode> AncestorPathFromRoot(StateNode state)
{
    var path = new List<StateNode>();
    var current = state;
    while (current != null)
    {
        path.Add(current);
        current = current.Parent;
    }
    path.Reverse();
    return path;
}
```

### Stub implementations for GetXxxFacet methods

For now, populate the obvious string/value fields from the node.
For fields that need complex computation (OutputLanesSummary, DeferredByStatesSummary),
use a simple placeholder like `""` or `"(not computed)"`.

Example for GetStateFacet:
```csharp
public StateFacet GetStateFacet(Guid stableId)
{
    var s = _asset.FindStateByStableId(stableId)
        ?? throw new KeyNotFoundException($"State {stableId} not found");
    return new StateFacet
    {
        Name                      = s.Name,
        OnEntryAction             = s.OnEntryAction,
        OnExitAction              = s.OnExitAction,
        ActivityAction            = s.ActivityAction,
        TimerAction               = s.TimerAction,
        Flags                     = BuildStateFlags(s),
        DeferredEventIds          = new List<ushort>(s.DeferredEventIds),
        OutputLanesSummary        = "",         // populated by HS-S1-19
        Comment                   = s.Comment,
        IsBreakpoint              = s.IsBreakpoint,
        StableId                  = s.StableId.ToString(),
        IncomingTransitionCount   = _asset.AllTransitions.Count(t => t.Target == s),
        OutgoingTransitionCount   = s.OutgoingTransitions.Count,
    };
}
```

Helper BuildStateFlags:
```csharp
private static StateFlags BuildStateFlags(StateNode s)
{
    var f = StateFlags.None;
    if (s.Children.Count > 0) f |= StateFlags.IsComposite;
    if (s.IsHistory)          f |= StateFlags.IsHistory;
    if (s.IsDeepHistory)      f |= StateFlags.IsDeepHistory;
    if (s.IsParallel)         f |= StateFlags.IsParallel;
    if (s.OnEntryAction != null) f |= StateFlags.HasOnEntry;
    if (s.OnExitAction  != null) f |= StateFlags.HasOnExit;
    if (s.ActivityAction != null) f |= StateFlags.HasOnUpdate;
    if (s.IsInitial)          f |= StateFlags.IsInitial;
    if (s.IsFinal)            f |= StateFlags.IsFinal;
    return f;
}
```

For GetTransitionFacet, compute LCA:
```csharp
public TransitionFacet GetTransitionFacet(Guid visualId)
{
    var t = _asset.FindTransitionByVisualId(visualId)
        ?? throw new KeyNotFoundException($"Transition {visualId} not found");
    var lca     = FindLca(t.Source, t.Target);
    var lcaCost = (ushort)(DepthOf(t.Source) + DepthOf(t.Target) - 2 * DepthOf(lca));
    return new TransitionFacet
    {
        SourceStateName = t.Source.Name,
        TargetStateName = t.Target.Name,
        EventId         = t.EventId,
        GuardFunction   = t.GuardFunction,
        ActionFunction  = t.ActionFunction,
        Priority        = t.Priority,
        Kind            = t.Kind,
        SyncGroupId     = t.SyncGroupId,
        Comment         = t.Comment,
        IsBreakpoint    = t.IsBreakpoint,
        VisualId        = t.VisualId.ToString(),
        LcaStateName    = lca.Name,
        LcaCost         = lcaCost,
    };
}

// Returns the depth of a state in the tree (RootState = 0, top-level = 1, ...).
private int DepthOf(StateNode s)
{
    int depth = 0;
    var cur = s;
    while (cur.Parent != null) { depth++; cur = cur.Parent; }
    return depth;
}
```

For GetRegionFacet, EventFacet, GlobalTransitionFacet, write similar stubs.

For EventFacet.DeferredByStatesSummary, compute it from AllStates where
s.DeferredEventIds.Contains(eventId) and join names with ", ":
```csharp
DeferredByStatesSummary = string.Join(", ", _asset.AllStates
    .Where(s => s.DeferredEventIds.Contains(eventId.EventId))
    .Select(s => s.Name)),
```

### Imports for HsmFacetMapper.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Model;
```

---

## Step 6 -- Create HsmLcaTests.cs

### Location
`Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/HsmLcaTests.cs`

### Requirements
- `using Hrot.Hsm.Editor.Inspector;`
- `using Hrot.Hsm.Editor.Model;`
- `using FluentAssertions;`
- `using Xunit;`
- Namespace: `Hrot.Hsm.Editor.Tests`
- Class: `public sealed class HsmLcaTests`

### How to build a minimal HsmAsset with a state tree

Look at how HsmGraphModelTests.cs builds assets (it uses a factory helper or
direct construction). Use the same pattern.

The simplest approach: look for a static helper in the test class that builds an asset.
Copy or reuse the same pattern.

If you can't find a clean factory, use the internal constructor of HsmAsset directly
(it is accessible via InternalsVisibleTo). You'll need at minimum:
- A RootState (synthetic, no parent)
- A few child states for testing

Pattern from reading HsmAsset.cs:
- `new StateNode(name)` creates a node with a fresh StableId
- Set `Parent` manually
- Add to parent's `Children` list
- Call `new HsmAsset(...)` with all the necessary lists

### Tests (8 tests):

1. **`FindLca_two_siblings_returns_parent`**
   - Tree: Root -> A -> [B, C]
   - FindLca(B, C) should be A

2. **`FindLca_state_with_itself_returns_same`**
   - FindLca(A, A) should be A

3. **`FindLca_ancestor_and_descendant_returns_ancestor`**
   - Tree: Root -> A -> B -> C
   - FindLca(A, C) should be A

4. **`FindLca_states_in_different_subtrees`**
   - Tree: Root -> [A, X -> [Y, Z]]
   - FindLca(A, Y) should be RootState (the synthetic root)

5. **`FindLca_deep_tree`**
   - Tree: Root -> A -> B -> C -> D, Root -> A -> B -> E
   - FindLca(D, E) should be B

6. **`FindLca_direct_parent_child`**
   - Tree: Root -> A -> B
   - FindLca(A, B) should be A (B's parent is A = the LCA)

7. **`FindLca_top_level_siblings`**
   - Tree: Root -> [X, Y]
   - FindLca(X, Y) should be RootState

8. **`DepthOf_computed_correctly_via_lca_cost`**
   - For Tree Root -> A -> B -> C, test that GetTransitionFacet gives correct LcaCost
   - Create a transition from B to C, call GetTransitionFacet, verify LcaStateName = "A" or "B"
     (LCA of B and C is B since B is ancestor of C), LcaCost = depth(B) + depth(C) - 2*depth(B) = 1

   Actually: FindLca(B, C) -- C's path from root: [Root, A, B, C], B's path: [Root, A, B]
   Common prefix: Root, A, B -> LCA = B. LcaCost = depth(B) + depth(C) - 2*depth(B) = 2 + 3 - 4 = 1.

---

## Step 7 -- Build and test

```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj
```

Expected: 0 errors, 0 warnings, 70 tests passing (62 existing + 8 new).

---

## Completion checklist

- [ ] 5 picker attribute files created in Inspector/
- [ ] `Inspector/HsmFacets.cs` created with 5 facet structs
- [ ] `Inspector/HsmSubSelections.cs` created with 2 new selection records
- [ ] `Inspector/HsmFacetMapper.cs` created with FindLca + GetXxxFacet stubs
- [ ] `HsmLcaTests.cs` created with 8 LCA tests
- [ ] Build: 0 errors, 0 warnings
- [ ] Tests: all 70 pass
- [ ] `git add -A && git commit -m "BATCH-30: HS-S1-18/20/21 - picker attrs, HSM facets, sub-selections, facet mapper with LCA (70 tests)"`
