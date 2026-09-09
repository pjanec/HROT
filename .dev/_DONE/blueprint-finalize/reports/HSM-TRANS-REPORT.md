# HSM-TRANS Report — HSM Transition Facet Bridge

**Branch:** `blueprint-integ-1`
**Date:** 2026-06-07

---

## Goal

Extend `HsmSelectionBridgeHelper.MapSelection` so clicking a canvas transition **link** publishes
an `HsmTransitionSelection` to `ActiveSubSelection`, causing `HsmFacetDispatcher.GetFacet` to
return a `TransitionFacet` in the Inspector. FIX-A had wired state nodes only.

---

## Investigation Findings

### Link-selection mechanism

`SelectionState` (NodeEditor.Core.View) tracks selected elements by `SelectionEntry` structs.
`SelectionState.Links` yields `LinkId` values for link-kind entries — separate from `.Nodes`.
A canvas link click with no modifier replaces the selection with a single `SelectionEntry.OfLink(linkId)`.

### Canvas link → HSM transition identity

`HsmGraphModel.BuildCaches` populates `_linkCache[new LinkId(t.VisualId)]` for every
`TransitionNode`. Therefore:

```
canvas LinkId.Value == TransitionNode.VisualId
```

This means `hsmAsset.FindTransitionByVisualId(linkId.Value)` is the correct lookup, directly
analogous to `hsmAsset.FindStateByStableId(nodeId.Value)` for state nodes.

### Transition sub-selection type

`HsmTransitionSelection(Guid VisualId)` already existed in
`Hrot.Editor.AiShared/Selection/SubSelectionRecords.cs`.

### Facet dispatcher

`HsmFacetDispatcher.GetFacet` already handled `HsmTransitionSelection` via
`_mapper.GetTransitionFacet(tr.VisualId)` — **no changes required** to the dispatcher or mapper.
The transition facet mapping was already complete.

---

## Implementation

### `HsmSelectionBridgeHelper.MapSelection`

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmSelectionBridgeHelper.cs`

Return type widened from `HsmStateSelection?` to `IAssetSubSelection?`.

New logic after the existing node path:

```csharp
// --- single link (transition) ---
using var linkEnum = selection.Links.GetEnumerator();
if (linkEnum.MoveNext())
{
    var visualId = linkEnum.Current.Value;
    if (!linkEnum.MoveNext()) // no second link
    {
        if (selection.Count == 1)
        {
            var transition = hsmAsset.FindTransitionByVisualId(visualId);
            return transition is null ? null : new HsmTransitionSelection(visualId);
        }
    }
}
return null;
```

Tie-break policy (mixed node + link in same selection): the state node is preferred — the node
path returns early before the link path is reached. Multi-node selections return null immediately.
The `selection.Count == 1` guard in the link path ensures the link path only fires for an
exclusive single-link selection.

`BuildAfterDrawAction` is unchanged — it already assigns `selectionStore.ActiveSubSelection = newSel`
which accepts `IAssetSubSelection?`.

---

## Tests

**File:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmSelectionBridgeHelperTests.cs`

### Changed
- `MapSelection_LinkSelected_ReturnsNull` → **renamed** to `MapSelection_UnknownLinkId_ReturnsNull`
  (the test now correctly describes a stale/unknown link id returning null, not the general link case).

### Added (4 new tests)
| Test | Scenario |
|------|----------|
| `MapSelection_TransitionLinkSelected_ReturnsHsmTransitionSelection_WithCorrectVisualId` | Single known transition link → `HsmTransitionSelection` with correct `VisualId` |
| `MapSelection_UnknownLinkId_ReturnsNull` | Single link with random Guid not in asset → null (stale-id guard) |
| `MapSelection_MultipleLinksSelected_ReturnsNull` | Two links selected → null (multi-select) |
| `MapSelection_MixedNodeAndLink_PrefersStateNode` | One node + one link → `HsmStateSelection` (tie-break: state wins) |
| `GetCurrentFacet_ReturnsTransitionFacet_WhenTransitionSubSelectionIsWired` | End-to-end: MapSelection → `HsmTransitionSelection` → `inspector.GetCurrentFacet()` returns `TransitionFacet` with correct `SourceStateName`/`TargetStateName` |

### Updated (2 existing tests)
- `MapSelection_StateNodeSelected_ReturnsHsmStateSelection_WithCorrectStableId` — added
  `.Should().BeOfType<HsmStateSelection>()` + cast (return type is now `IAssetSubSelection?`).
- `MapSelection_AnotherState_ReturnsCorrectStableId` — same.

---

## Build / Test Results

```
dotnet build Hrot.Hsm.Editor.csproj      → 0 errors, 0 warnings
dotnet build Hrot.Hsm.Editor.Tests.csproj → 0 errors, 0 warnings

dotnet test Hrot.Hsm.Editor.Tests (all)  → 352/352 passed
dotnet test Hrot.Editor.AiShared.Tests   → 856/856 passed (re-run; 1/856 fs-race flake on first run,
                                           pre-existing per task spec — 0 new failures)
```

---

## Transition Facet Mapping — Already Existed

`HsmFacetDispatcher.GetFacet` already contained:

```csharp
HsmTransitionSelection tr => _mapper.GetTransitionFacet(tr.VisualId),
```

and `HsmFacetMapper.GetTransitionFacet` and `HsmFacetDispatcher.ApplyTransitionFacet` were both
fully implemented. This batch only needed to wire the bridge (source of `HsmTransitionSelection`)
— the downstream facet handling was already complete.

---

## Files Changed

- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmSelectionBridgeHelper.cs`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Host/HsmSelectionBridgeHelperTests.cs`

No changes to: `SubSelectionRecords.cs`, `HsmFacetDispatcher.cs`, `HsmFacetMapper.cs`,
`HsmGraphModel.cs`, `HsmAsset.cs`, or any EditorSubsystem wiring.
