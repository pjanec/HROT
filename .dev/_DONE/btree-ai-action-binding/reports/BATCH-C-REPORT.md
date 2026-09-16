# BATCH-C Report

## Implementation Summary

### Task 1 — Service implementation & registry access

**Impl file:** `Hrot/Subsystems/Hrot.Editor/Adapters/EditorMissionService.cs`

`EditorMissionService` is the concrete `IMissionEditorService` used by the offline Editor. It already held a `BehaviorRegistry _registry` injected via its constructor (`EditorMissionService(FdpEventBus, EntityRepository, BehaviorRegistry)`) — no DI wiring change was needed; the registry was already there.

### Task 2 — Inclusion point

`GetAvailableBehaviors` was extended as follows:

1. The existing path (curated catalog filtered through registry) is unchanged and runs first.
2. A new private static helper `AppendEditorBTreeBehaviors(BehaviorRegistry, List<string>)` is appended immediately after:
   - Iterates `registry.GetRegisteredNames()`.
   - Skips any name already in the result (dedup; curated list comes first, preserving order).
   - Resolves each name to its `BehaviorDefinition`; skips entries whose `BrainTier != BrainTierBTree`.
   - The remaining names (editor-authored BTree behaviors) are appended.
3. The single inclusion point is the call `AppendEditorBTreeBehaviors(_registry, result)` on one line in `GetAvailableBehaviors`, with the comment on the helper method:
   > `// TODO (option c): gate by per-asset DisEntityType affinity mask instead of listing for all entity types.`

**"Editor-authored" signal used (interim):** `BrainTier == BrainTierBTree` combined with "not already in the curated list." There is no explicit `IsEditorAuthored` flag on `BehaviorDefinition`. The interim rule is correct because:
- The curated list (`BehaviorCatalog`) covers all behaviors with `[BehaviorContractAttribute]` — these are code-authored.
- All editor-authored (JSON-sourced) assets registered by generated bridge registrars have `BrainTier = BrainTierBTree` and are absent from the curated list by construction.
- The dedup ensures a behavior that somehow lands in both (e.g., a code-authored BTree behavior) never appears twice.

**Future option-c upgrade:** replace the body of `AppendEditorBTreeBehaviors` with a per-asset affinity mask check. The call site in `GetAvailableBehaviors` passes `tkbType` if needed — the method signature can be extended to accept it.

### Task 3 — End-to-end attach path verification (read-only)

Path: User selects editor BTree in Mission Editor → `MissionPanel` calls `GetAvailableBehaviors` (now includes T10) → user picks "T10_MultiAction" → `MissionPanel` publishes `AssignBehaviorEvent { BehaviorName = "T10_MultiAction", Entity = ..., JsonParams = "" }` via `CommitMissionAsync` / `SendControlCommandAsync` → `BehaviorIngressSystem.Execute` consumes `AssignBehaviorEvent`, calls `_registry.TryGetId("T10_MultiAction", out id)` (succeeds because bridge registrar registered it) → `TryGetDefinition(id, out def)` succeeds → `def.ParseParams` is checked: T10 has defaults so `ParseParams` may be null or succeed with empty input — either path proceeds → `BehaviorState.ActiveBehaviorHash = id`, `BrainTier = BrainTierBTree`, `BrainBTreeState.State = default` → `BTreeTickSystem` ticks via `def.BTreeInterpreter`.

No blockers for T10 specifically. One note: `BehaviorIngressSystem` requires the entity to have `BehaviorState` and `BrainBlackboard` components pre-added at spawn time (these are standard ECS components added by `SpawnSystem`). If T10's entity was spawned before the bridge registrar ran (i.e., on an entity that was spawned without `BTreeInterpreter`), the attach would silently no-op on the `TryGetId` miss — but at runtime the bridge registrar runs at startup before any entity is spawned, so this is not an issue.

## Design Decisions

- **No change to the curated path.** The existing `catalog.Where(n => _registry.TryGetId(n, out _))` guard (which already filtered out unregistered curated names) is left intact. The new code runs after it.
- **Private static helper.** Keeps `GetAvailableBehaviors` readable and isolates the option-c swap point.
- **`HashSet<string>` for dedup.** O(1) lookup. The curated list is typically ~5–15 items; overhead is negligible.
- **Order: curated first, then editor BTrees.** Mission Editor lists curated entries at the top (familiar to operators), editor-authored entries below. This can be changed trivially by reordering the two append steps.
- **No IAssetCatalog lookup.** Investigated whether an `IAssetCatalog` "editor-owned BTree" flag was available. No such flag exists on `BehaviorDefinition` or the registry. The `BrainTier == BrainTierBTree` + "not in curated list" signal is the cleanest available signal and is documented as interim.

## Deviations

None. Implementation follows the spec exactly.

## Test Results

Suite: `Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj`
Filter: `Stability!=Flaky&Stability!=Environment&Stability!=Broken`

```
Test Run Successful.
Total tests: 188
     Passed: 188
 Total time: 1.87 Seconds
```

New tests (confirmed individually):

```
Passed  GetAvailableBehaviors_IncludesRegisteredEditorBTree       [2 ms]
Passed  GetAvailableBehaviors_DoesNotDuplicateCuratedEntries      [37 ms]
Passed  GetAvailableBehaviors_DeadEntity_ReturnsEmpty             [5 ms]
Passed  GetAvailableBehaviors_InsurgentWithRegisteredAmbush_ReturnsAmbush [1 ms]
```

`GetAvailableBehaviors_IncludesRegisteredEditorBTree`: registers a `BrainTierBTree` behavior named "T10_MultiAction" (not in the curated list), creates a `MilitaryApc` entity, calls `GetAvailableBehaviors`, asserts the name is present. This is the primary spec test.

`GetAvailableBehaviors_DoesNotDuplicateCuratedEntries`: registers "Ambush" (BrainTierBTree) which the curated catalog also includes for Insurgents. Verifies the name appears exactly once in the result.

## Developer Insights

- The `BehaviorCatalog` is a purely static/reflection-based catalog with no extension point — correct approach was to go through the registry's `GetRegisteredNames()` rather than trying to extend the catalog.
- `GetRegisteredNames()` returns a snapshot copy (`_nameToId.Keys.ToList()`), so iterating it while the registry is read-only at runtime is safe.
- The `ExConMissionShim.GetAvailableBehaviors` (in `Hrot.ExCon`) and `MissionEditorService.GetAvailableBehaviors` (also in `Hrot.ExCon`) were NOT modified. The task spec targets the Editor path (`Hrot.Editor`). The ExCon path is a separate DDS-based remote service that does not hold a live `BehaviorRegistry`. If ExCon also needs editor BTrees, that is a separate workstream.

## Known Issues

- `ExConMissionShim` and `MissionEditorService` (ExCon variants) do not include editor-authored BTrees. These serve a different host context and are out of scope.
- After rebuild+restart of the editor app, T10_MultiAction (and any other JSON-authored BTree) will appear in the Mission Editor behavior dropdown and be attachable to any entity type.

## Suggested Commit Message

`feat(mission-editor): expose editor-authored BTree behaviors in GetAvailableBehaviors`
