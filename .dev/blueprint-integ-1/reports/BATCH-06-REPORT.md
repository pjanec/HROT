# BATCH-06 Report

## Implementation Summary

### Corrective Task 0 — BTree link projection (P1 from BATCH-05)

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs`

Added `BTreeParentChildLink : ILinkModel` (same file) — a deterministic link adapter whose `Id` is XOR-derived from the child's `VisualId` (constant `0xCC…01`/`…05`), `FromPin = child.OutputPinId`, `ToPin = parent.InputPinId`. `BuildCaches()` now populates `_linkCache: Dictionary<LinkId, BTreeParentChildLink>` by iterating every node's `ChildVisualIds`. `Links` exposes `_linkCache.Values`; `FindLink(id)` does a dictionary lookup. The cache rebuilds on every `Changed` event (unchanged rebuild path).

Rewrote `BTreeDocumentFactory_Build_GraphView_ExposesProjectedLinks`: given `RootSequence2Actions()` (4 nodes, 3 edges), it asserts exact count = 3, every `link.FromPin == child.OutputPinId`, `link.ToPin == parent.InputPinId`, and `FindLink(link.Id) != null`. Added dedicated `BTreeGraphModelTests.cs` (8 tests) covering empty tree, exact counts, sorted ids, all-findable, FindLink-unknown=null.

### Task 1 — Inspector facet dispatch (AIE-023)

**New files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Inspector/IFacetDispatcher.cs` — `GetFacet(IAssetSubSelection) → object?` + `ApplyFacet(IAssetSubSelection, object)`. Lives in AiShared; subsystems implement it — correct dependency direction.
- `Hrot/Editor/Hrot.Editor.AiShared/Inspector/IPickerListSource.cs` — headless-testable item-list interface.
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreeFacetMapper.cs` — `IFacetDispatcher` impl mapping `BTreeNodeSelection` → `BTree*Facet` structs and applying back (all KernelTypes: Action/Condition/Wait/Sequence/Selector/ObserverSelector/Parallel/Root/Subtree).
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Inspector/HsmFacetDispatcher.cs` — `IFacetDispatcher` impl wrapping `HsmFacetMapper` for read + new apply logic for State/Transition/Region/Event/GlobalTransition.

Updated `InspectorWindow`:
- New optional `IFacetDispatcher? facetDispatcher` ctor param (default null — existing callers unaffected).
- `SetFacetDispatcher(dispatcher)` runtime setter.
- `internal GetCurrentFacet()` / `internal CommitCurrentFacet(object)` headless-testable seams.
- `DrawClientArea()` gained a `GetCurrentContext()==IntPtr.Zero`-gated facet section.

Tests in `Hrot.BTree.Editor.Tests/Inspector/BTreeFacetMapperTests.cs` (7 tests) and `Hrot.Hsm.Editor.Tests/Inspector/HsmFacetDispatcherTests.cs` (6 tests) covering all required scenarios including `Inspector_Commit_AppliesToAsset_AndMarksDirty` and `Inspector_NoSubSelection_FallsBackToAssetProperties`.

### Task 2 — Custom StructEdit field pickers + PickerRegistry.Get fix (AIE-024 + DEBT-003)

**DEBT-003 fix:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerRegistry.cs` line 46 returned `null` instead of `typed.Source`. Fixed by adding `IPickerSource<TItem> Source => _source` to `PickerSourceAdapter<TItem>` and returning it. 5 tests in `NodeEditor.UI.Tests/Picker/PickerRegistryTests.cs` verify registered source is returned, null for missing, null for type mismatch, multi-registration, re-registration.

**BTree pickers** (`BTreePickerDrawers.cs`):
- `BehaviorHashPickerDrawer` — `GetItems()` returns `BehaviorRegistry.GetRegisteredNames()` sorted.
- `BlackboardFieldPickerDrawer` — `GetItems()` returns active asset BB variable names sorted.
- `CompositeStringDrawer` — dispatches by `EditNodeMetadata.CustomAttributes` attribute type; falls through to plain-text when no marker attribute matches.

**HSM pickers** (`HsmPickerDrawers.cs`):
- `HsmActionPickerDrawer` — collects distinct action function names from states + transitions.
- `HsmGuardPickerDrawer` — guard functions from transitions + global transitions.
- `HsmStateSelectorDrawer` — state names excluding `__`-prefixed compiler-internal pseudo-roots.
- `HsmEventPickerDrawer` — event names from `AllEvents`.
- `HsmSyncGroupPickerDrawer` — sync group IDs from transitions.
- Shared `HsmPickerHelper.RenderCombo` internal static.

All drawers' `DrawInput` are guarded with `ImGui.GetCurrentContext()==IntPtr.Zero`. Logic (`GetItems`) is headless-testable via `IPickerListSource`.

Tests: `BTreePickerDrawerTests.cs` (8 tests), `HsmPickerDrawerTests.cs` (6 tests).

### Task 3 — HsmGlobalsStrip (AIE-027)

**New file:** `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Windows/HsmGlobalsStrip.cs` (complete rewrite of stub).

Architecture:
- `IHsmGlobalsCommandDispatcher` — seam for testing (`RemoveGlobalTransition(Guid)`).
- `HsmGlobalsStripLogic` — pure logic: `GetChipLabels()`, `OnChipClicked(int)`, `OnChipRemoved(int)`.
- `HsmGlobalsStrip` — ImGui-guarded render (chip per global transition, context menu edit/remove).
- `DefaultHsmGlobalsCommandDispatcher` — production implementation calling `HsmAsset.RemoveGlobalTransition`.

**`HsmAsset` changes** (`Model/HsmAsset.cs`):
- Added `_allGlobalTransitionsList: List<GlobalTransitionNode>` backing field (mirrors `_allRegionsList`).
- Added `internal bool RemoveGlobalTransition(Guid visualId)`.

Tests: `HsmGlobalsStripTests.cs` (8 tests) covering chips count, chip labels, click→sub-selection, out-of-range safety, remove→dispatcher, DefaultDispatcher integration.

---

## Design Decisions

1. **`IFacetDispatcher` in AiShared (not in subsystem editors):** Keeps `InspectorWindow` ignorant of BTree/HSM types. Only the implementation classes reference the subsystem models — correct direction.
2. **`BTreeFacetMapper` vs `HsmFacetDispatcher` naming:** BTree has no existing mapper → new class named `BTreeFacetMapper`. HSM had `HsmFacetMapper` (read-only) → new `HsmFacetDispatcher` wraps it and adds apply logic.
3. **`IPickerListSource` in AiShared/Inspector:** Makes the headless-testable item list accessible to both BTree and HSM test assemblies without cross-subsystem coupling.
4. **`HsmStateSelectorDrawer` filters `__`-prefixed names:** The HSM compiler emits an internal `__Root` state in the blob that ends up in `AllStates`. Filtering by `s.Parent != null` was insufficient (compiler root has Parent = synthetic root, which is non-null). The `!Name.StartsWith("__")` guard cleanly excludes all compiler-internal states.
5. **`HsmGlobalsStripLogic` seam:** Separates logic from ImGui; the strip's Render() is only called in a real frame. Tests exercise `HsmGlobalsStripLogic` directly via fakes.
6. **`DefaultHsmGlobalsCommandDispatcher` uses direct asset mutation:** `HsmCommandSink` has no `RemoveGlobalTransition` command. Rather than add a new `GraphCommand` subtype (large change, out of scope), the dispatcher mutates the asset backing list directly via the new `internal RemoveGlobalTransition` method. The command-sink path is left as a DEBT item comment.

---

## Deviations

| What | Why | Benefit | Risk |
|---|---|---|---|
| `HsmFacetDispatcher` wraps `HsmFacetMapper` instead of extending it | `HsmFacetMapper` was read-only; adding apply mutations to it would break separation of concerns | Clean; mapper stays read-only | Extra class — minor maintenance overhead |
| `BTreeFacetMapper` new class (not reusing anything) | No existing BTree facet mapper existed | Adds AIE-023 for BTree correctly | None |
| `HsmGlobalsStrip` uses `HsmAsset.RemoveGlobalTransition` (not GraphCommand) | `GraphCommand` has no global-transition removal variant; adding one is out of scope | Simpler; avoids cross-cutting graph command proliferation | Not undoable via command history — noted as DEBT |
| `HsmStateSelectorDrawer` filters `!Name.StartsWith("__")` | `AllStates` includes compiler-internal `__Root` state | Picker shows only user-visible states | May accidentally hide a user state with `__` prefix (edge case; convention disallows this) |

---

## Test Results

```
Hrot.Editor.AiShared.Tests:          677 / 0 fail
Hrot.BTree.Editor.Tests:             350 / 0 fail  (was 327 pre-batch)
Hrot.Hsm.Editor.Tests:               298 / 0 fail  (was 278 pre-batch)
NodeEditor.UI.Tests:                  40 / 0 fail   (was 35 pre-batch; +5 PickerRegistry tests)
EditorSubsystemBoot filter:           10 / 0 fail
Hrot.Blueprints.Tests:               889 / 10 fail  (10 = pre-existing DEBT-006; 0 new)
```

**New tests added:**
- `BTreeGraphModelTests` — 8 tests (link projection)
- `BTreeDocumentFactoryTests._ExposesProjectedLinks` — strengthened (was NotBeNull-only)
- `BTreeFacetMapperTests` — 7 tests (AIE-023)
- `HsmFacetDispatcherTests` — 6 tests (AIE-023)
- `BTreePickerDrawerTests` — 8 tests (AIE-024)
- `HsmPickerDrawerTests` — 6 tests (AIE-024)
- `PickerRegistryTests` — 5 tests (DEBT-003)
- `HsmGlobalsStripTests` — 8 tests (AIE-027)

---

## Developer Insights

1. **`AllStates` includes compiler-internal root state.** The HSM compiler emits an `__Root` state at `FlatIndex==0` that IS in `AllStates` (it's a real blob state) — not the synthetic `__root__` state at `FlatIndex==0xFFFF`. This is a footgun for any code iterating `AllStates` expecting only user states. The `HsmAssetProjector.GraphModel_Nodes_contains_all_states` test counts all states including `__Root`. Worth a clarifying doc comment on `AllStates`.
2. **`PickerRegistry.Get` was dead code.** The `null` return at line 46 was clearly an unfinished stub — the entire body was `return null`. With the fix, the DEBT-003 picker path is now functional.
3. **`BTreeFacetMapper.ApplyFacet` sets `action.ExpressionTargetField` directly.** BTreeActionPayload fields are public mutable — no setter pattern. This is acceptable for the in-memory editor model.
4. **`HsmFacetDispatcher.ApplyTransitionFacet` rewires by name.** `t.Target = FindStateByName(...)` — if the user changes the target name to something that no longer exists, the target is silently left unchanged. A validation pass (out of scope for this batch) should catch this.

---

## Known Issues

- `DefaultHsmGlobalsCommandDispatcher.RemoveGlobalTransition` does not go through `HsmCommandSink` and is not undoable via command history. Recorded as a DEBT item for when `GraphCommand` gains a `RemoveGlobalTransition` variant.
- `HsmFacetDispatcher.ApplyStateFacet` updates `s.DeferredEventIds` in-place. The event IDs in the facet come as `List<ushort>` from the facet struct; this mutates the state node directly without triggering `_asset.Changed` until `MarkDirty()` is called (which it is). No behavioral issue but relies on the Changed event being sufficient.
- BTree/HSM `IImGuiFieldDrawer.DrawInput` implementations all contain `// Full StructEdit rendering would go here` placeholder. The AIE-024 scope is registration + picker list; the full StructEdit form render is deferred to the StructEdit integration phase (referenced in design as per-picker ImGui widgets). The headless picker list functionality is complete and tested.

---

## Suggested Commit Message

```
feat(editor): BATCH-06 — BTree link projection + inspector facet dispatch + pickers + HsmGlobalsStrip

- Corrective Task 0: BTreeGraphModel projects parent→child edges as ILinkModel wires
  (child.OutputPinId→parent.InputPinId); FindLink implemented; cache rebuilt on Changed.
  Strengthened ExposesProjectedLinks test (exact count + pin ids).
- AIE-023: IFacetDispatcher seam in AiShared; BTreeFacetMapper + HsmFacetDispatcher
  implement it; InspectorWindow wired with headless GetCurrentFacet/CommitCurrentFacet seams.
- AIE-024: BTreePickerDrawers (BehaviorHash, BlackboardField, CompositeString) +
  HsmPickerDrawers (Action, Guard, State, Event, SyncGroup) + IPickerListSource headless interface.
  DEBT-003 fixed: PickerRegistry.Get<TItem> now returns the registered source.
- AIE-027: HsmGlobalsStrip finished (chip-per-global-transition, click→HsmGlobalTransitionSelection,
  context-menu remove dispatched via IHsmGlobalsCommandDispatcher); HsmAsset.RemoveGlobalTransition added.
Tests: AiShared 677, BTree 350, HSM 298, NodeEditor.UI 40, EditorSubsystemBoot 10/10, Blueprints 889/10 (DEBT-006).
```
