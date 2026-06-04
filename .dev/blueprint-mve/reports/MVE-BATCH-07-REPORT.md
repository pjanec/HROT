# MVE-BATCH-07 Report — Hot-Reload: recompile a running blueprint, new code goes live, state reconciled

## Implementation Summary

### VERIFY-FIRST Findings

#### Finding 1 — StructureHash independence from Tick body

**Citation:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/StructureHashComputation.cs:9-17`

```csharp
public static ulong Compute(IrAsset asset)
{
    var sb = new StringBuilder();
    sb.Append(asset.Dispatch).Append(';');
    AppendFields(sb, asset.Parameters);
    AppendFields(sb, asset.WorkingState);
    AppendFields(sb, asset.Variables);
    return FnvHasher.Hash64(Encoding.UTF8.GetBytes(sb.ToString()));
}
```

`StructureHashComputation.Compute` hashes only `asset.Dispatch`, `asset.Parameters`, `asset.WorkingState`, and `asset.Variables` (names, types, offsets, sizes via `AppendFields`). **The `asset.Graphs` collection (the Tick body / IR) is entirely excluded.** Consequence: two `BlueprintDefinition` instances with identical variable layout but different `Tick` delegates produce the same `StructureHash`. A hot-reload that changes only the Tick body takes the state-preserved path (`slot.StructureHash == def.StructureHash` in `BlueprintTickSystem.cs:87`) — no `ResetSlot` / `InitDefault` is called.

This finding is also directly confirmed by the existing test `Stage6_StructureHash_StableWhenOnlyGraphBodyChanges` (`Hrot.Blueprints.Tests/Stage6Tests.cs:184-242`).

#### Finding 2 — BlueprintAssetBuilder increment expressiveness

**Citation:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Builders/BlueprintAssetBuilder.cs:231-237`

```csharp
public GraphBuilder SetVariable(string variableName, string valueExpression)
{
    var nodeId = MakeNodeId("SetVariable", _nodes.Count);
    var node = new SetVariableNode { Id = nodeId, VariableId = variableName };
    RegisterNode(node, hasExecIn: true, hasExecOut: true);
    return this;
}
```

`GraphBuilder.SetVariable` accepts a `valueExpression` string parameter but only stores `VariableId = variableName` in the `SetVariableNode`. There is no `AddNode`, `GetVariableNode`, or literal-constant node type in the builder. The `valueExpression` string is silently discarded — the compiler cannot generate a `Count++` increment from graph builder nodes alone.

**Observable chosen:** Hand-crafted `BlueprintDefinition` delegates (v1: `Count += 1` per tick; v2: `Count += 2` per tick; v3: extra field, different hash), committed via `AiHotReloadCoordinator.ApplyQuickReload`. This is the exact internal mechanism `QuickReloadService.TriggerAsync` uses (see `QuickReloadService.cs:155-161`: after compilation, `coordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging)` is called — the same call made in the tests). The hand-crafted approach gives richer observables than the v1=empty/v2=increment fallback.

### Task 1 — Hot-reload proof tests

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintHotReloadMveTests.cs`

All three tests use `BlueprintTestFixture` (real `BlueprintTickSystem`) + a `AiHotReloadCoordinator` sharing `fixture.Registry`. Hot-reload is performed via `coordinator.ApplyQuickReload(alc, behStaging, bpStaging)` — the identical call path `QuickReloadService.TriggerAsync` invokes. Single blueprint identity (`AssetGuid = 07000007-0000-0000-0000-000000000001`), single entity, no re-attach across reload.

#### Test 1a — `HotReload_BehaviorChange_StatePreserved_SameStructureHash`

- v1 registered (Count += 1/tick, `StructureHash = HashAB`)
- Entity attached, 3 frames pumped → `Count = 3`
- Hot-reload to v2 (Count += 2/tick, **same `StructureHash = HashAB`**)
- Immediately after reload: `Count == 3` (state preserved — not reset to 0)
- 4 more frames pumped → `Count = 3 + 8 = 11`
- **Assert: `Count == 11`**
  - If hard-reset occurred: 0 + 8 = 8 (wrong)
  - If v1 still running: 3 + 4 = 7 (wrong)

#### Test 1b — `HotReload_StructuralChange_HardResets_State`

- v1 registered (Count += 1/tick, `HashAB`)
- Entity attached, 5 frames → `Count = 5`
- Hot-reload to v3 (extra `Extra:int` field, `HashC ≠ HashAB`)
- 1 frame pumped: `BlueprintTickSystem.TickTier_1024:87-99` detects `slot.StructureHash != def.StructureHash` → `ResetSlot + InitDefault` → Count zeroed, then v3 tick (+1) → `Count = 1`
- **Assert: `Count == 1`** (hard reset happened; pre-reload value 5 is gone)
- 1 more frame → **Assert: `Count == 2`** (confirms continuous running post-reset)

#### Test 1c — `HotReload_CaptureLiveState_ReturnsPostReloadCount`

- v1 registered, 3 frames → `Count = 3`
- `BlueprintDebugSession` constructed against `fixture.Registry` + `fixture.View`
- `DebugMap` registered for `Count:int` at offset 16 (after `BlueprintLatentCursor` header)
- Hot-reload to v2 (same `HashAB`), DebugMap re-registered (mirrors `QuickReloadService.cs:159-161`)
- 4 more frames → `Count = 11`
- `session.CaptureLiveState(entity, AssetGuid)` called (MVE-06 07-A non-pause-gated API)
- **Assert: `snapshot.FieldValues["Count"] == 11`**
  - If DebugMap was stale: `FieldValues` would be empty (null stateLayout)
  - If state was reset: Count would be 8

### Task 2 — Editor trigger confirmation

The "Compile / Reload Blueprint" toolbar action is already wired end-to-end from MVE-BATCH-05. Confirmed code path:

1. `EditorSubsystem.cs:1873-1893` (RegisterWindows): registers toolbar entry "Compile / Reload Blueprint" via `CaptureWindowRegistrar`; callback invokes `_blueprintQuickReloadTrigger?.Invoke(_aiDocumentManager.Active.Asset)`
2. `EditorSubsystem.cs:2102-2111`: `_blueprintQuickReloadTrigger` calls `quickReloadService.TriggerAsync(bpAsset).GetAwaiter().GetResult()`
3. `QuickReloadService.TriggerAsync` compiles the in-memory asset → ALC → registrars → staging → `coordinator.ApplyQuickReload` → `BlueprintRegistry.CommitStaging` (atomic swap)
4. `BlueprintTickSystem` re-resolves `_registry.TryGetById(slot.BlueprintId, out def)` every tick per slot (line 85) — so the new definition is picked up on the very next tick after `CommitStaging`

**Current status text** (`EditorSubsystem.cs:2112-2113`):
```csharp
_blueprintCompileStatus = result.Succeeded
    ? $"Compiled in {result.DurationMs}ms"
    : $"Compile failed: {result.ErrorMessage}";
```

No change was made. The text "Compiled in Xms" is accurate for both first-compile and re-compile scenarios. Adding "hot-reloaded" would be misleading on first compile (no running instances to hot-reload yet). The proof and documentation are the deliverable here — no code change needed.

### Task 3 — DEBT-MVE-003 upgraded to P1

**Entry added to** `.dev/blueprint-integ-1/DEBT-TRACKER.md` as `DEBT-MVE-003` (P1 / production blocker).

**Root cause confirmed** (all citations verified against source):

1. **`BlueprintRegistry.CommitStaging` full-replace** (`BlueprintRegistry.cs:117-138`):
   ```csharp
   var byIdDict = staging.Definitions.ToDictionary(kv => kv.Key, kv => kv.Value);
   ```
   Creates a brand-new dictionary from only the staging buffer entries. Any blueprint previously registered by build-time source generators or prior QuickReloads that is NOT in this staging buffer is **silently dropped**.

2. **`CSharpEmitter.EmitRegistrarClass(asset)` single-asset scope** (`CSharpEmitter.cs:128`):
   Takes a single `IrAsset` parameter and emits one registrar class for that asset only. One Roslyn compile → one assembly → one asset's definitions in the staging buffer.

3. **`QuickReloadService.TriggerAsync` single-assembly registrar invocation** (`QuickReloadService.cs:120-157`):
   Scans only `assembly.GetTypes()` from the newly compiled assembly for `[BlueprintRegistrar]` types. Invokes them into a single `BlueprintRegistryStaging` that contains only the recompiled asset.

4. **`AiHotReloadCoordinator` single `_currentAlc`** (`AiHotReloadCoordinator.cs:188-190`):
   ```csharp
   var oldAlc = _currentAlc;
   _currentAlc = newAlc;
   oldAlc?.Unload();
   ```
   On each reload, the PREVIOUS assembly is unloaded. With >1 editor-compiled blueprint, reloading blueprint A unloads the ALC that contains B's (and C's, D's…) `Tick` and `InitDefault` delegates.

**Consequence with >1 editor-compiled blueprint:**
- Quick-reload of blueprint A: (a) wipes definitions for B, C, … from the registry (full-replace with a 1-entry staging buffer), AND (b) after the NEXT reload of A, unloads the ALC holding B/C's delegates → dangling function pointers → crash/access violation on next tick of B or C.
- Invisible in all single-blueprint MVE tests (MVE-01 through MVE-07).

**Fix sketch (architectural — do NOT implement here):**

Option 1 — Carry-forward staging + per-asset ALC tracking:
- Before `CommitStaging`, seed the staging buffer with all current definitions EXCEPT the recompiled id: `foreach (id, def) in _current.ById where id != recompiledId → staging.Add(id, def)` — preserves all sibling definitions across the commit.
- Replace `_currentAlc` (single field) with `Dictionary<int, AssemblyLoadContext> _alcByBlueprintId` — track ALC per-asset; on reload of A, unload only `_alcByBlueprintId[aId]` (not siblings' ALCs).
- Risk: carry-forward copies all live definitions (including code-defined ones) into the staging dict; this is correct behavior since `CommitStaging` fully replaces. Must handle null-ALC (code-defined) definitions gracefully.

Option 2 — Merge-commit registry mode + multi-ALC retention:
- Add `MergeStaging(BlueprintRegistryStaging)` to `BlueprintRegistry` that upserts (replaces existing entries by id, keeps others); `CommitStaging` keeps its full-replace semantics for full-rebuild scenarios.
- Change `AiHotReloadCoordinator.ApplyQuickReload` to call `MergeStaging` for quick-reload and `CommitStaging` for full-rebuild.
- Retain all ALCs until explicitly released (ref-counted or explicit lifecycle); `_currentAlc` becomes a list.
- More invasive but cleaner semantics.

**Priority: P1 / production blocker for multi-blueprint editor use.** The fix must be implemented before the Blueprint editor is used with more than one compiled blueprint simultaneously.

---

## Design Decisions

1. **Hand-crafted definitions over Roslyn-compiled** for the hot-reload tests — the `GraphBuilder` cannot express a Count increment (Finding 2 above); using `AiHotReloadCoordinator.ApplyQuickReload` with hand-crafted delegates tests the SAME code path that `QuickReloadService.TriggerAsync` takes internally. The behavior-change observable (+1→+2 per tick) is richer than the empty-Tick→increment fallback since it proves state continuity non-trivially (Count = 11 uniquely identifies both rate-change AND state-preservation).

2. **DebugMap manually registered in 1c** — mirrors what `QuickReloadService.cs:159-161` does on each compile. Re-registering after the v2 reload is necessary (a new DebugMap instance, as QuickReloadService would produce from a new compile result) and proves the re-registration path works correctly.

3. **`BlueprintTestFixture` with external coordinator** — using the fixture's tick substrate with a separate coordinator that shares `fixture.Registry` follows the established `BlueprintCompileOnDemandMveTests` pattern (see `QuickReload_RegisteredBlueprint_AttachAndPump_CounterAdvancesN`). No new harness was needed.

## Deviations

None. All tasks implemented per spec. The fallback observable (v1 empty Tick → v2 increment) was replaced by a richer observable (v1 +1/tick → v2 +2/tick) as explicitly permitted by the instructions ("preferred: v1 increments by 1 → v2 by 2").

## Test Results

### New hot-reload tests
```
Passed Hrot.Blueprints.Tests.Runtime.BlueprintHotReloadMveTests.HotReload_BehaviorChange_StatePreserved_SameStructureHash [5 ms]
Passed Hrot.Blueprints.Tests.Runtime.BlueprintHotReloadMveTests.HotReload_StructuralChange_HardResets_State [85 ms]
Passed Hrot.Blueprints.Tests.Runtime.BlueprintHotReloadMveTests.HotReload_CaptureLiveState_ReturnsPostReloadCount [18 ms]
Total: 3 / Passed: 3 / Failed: 0
```

### Hrot.Blueprints.Tests (full suite)
```
Total: 1173 / Passed: 1155 / Failed: 10 / Skipped: 8
```
The 10 failures are all pre-existing DEBT-006 (6 golden-emit tests + 2 snapshot demos + 1 allocation-free perf + 1 condition-summary attachment). Zero new failures introduced by this batch.

The `WhenNode_ConditionMet_Under200ns_perTick` perf test failed once during the full-suite run (11 total) due to concurrent build CPU load (DEBT-014 flaky benchmark); passes green in isolation:
```
dotnet test --filter "FullyQualifiedName~WhenNode_ConditionMet_Under200ns"
Passed [1 s]
```

### EditorSubsystemBoot filter
```
Total: 10 / Passed: 10 / Failed: 0
```

### Hrot.Editor.AiShared.Tests
```
Total: 761 / Passed: 761 / Failed: 0
```

### Golden/snapshot confirmation
Zero golden or snapshot files were touched by this batch. The 8 emit-golden/snapshot failures are all pre-existing DEBT-006 (same error messages, same stack traces as BATCH-06). No codegen changes were made.

### Full solution build
```
dotnet build IOS-IG-SimHost.sln
Build succeeded. 0 Error(s)
```
Touched projects (Hrot.Blueprints.Tests only — test file added; no production code changed): 0 new warnings.

---

## Developer Insights

- **StructureHash truly ignores Tick body** — this is already tested by `Stage6_StructureHash_StableWhenOnlyGraphBodyChanges` and confirmed by reading `StructureHashComputation.cs`. The 1a test exploits this guarantee correctly.

- **`coordinator.ApplyQuickReload` IS the QuickReloadService hot-reload mechanism** — `QuickReloadService.TriggerAsync` is a compile-then-stage wrapper; the actual hot-swap is `coordinator.ApplyQuickReload`. Testing via the coordinator directly (without the Roslyn compile step) proves the identical runtime path while avoiding the need to express a Count increment in the compiler's graph IR.

- **DEBT-MVE-003 is a silent production bomb** — the single-blueprint MVE tests mask this completely. In production, the moment a second blueprint is compiled by the editor (e.g. compiling both a "MoveToAndFire" and a "PatrolRoute" blueprint), the first reload of either will wipe the other from the registry, and the second reload will dangle the other's delegates. This needs to be fixed before the editor ships multi-blueprint authoring.

- **DebugMap re-registration on reload is the correct pattern** — `QuickReloadService.cs:159-161` re-registers the DebugMap on every compile, which means after each hot-reload the inspector stays correct. Test 1c validates this explicitly.

## Known Issues

- **DEBT-MVE-003** (P1, documented above) — multi-blueprint hot-reload correctness blocker.
- **DEBT-MVE-002** (P2, deferred) — compiled blueprints' field names are only readable via DebugMap (07-B route from MVE-06); `BlueprintDefinition.StateFields` is not populated by the compiler.
- **DEBT-MVE-001** (P2) — `[UpdateBefore]` ordering not honored inside `TogglableSimulationGroup`; blueprints that issue channel commands in the same frame may fire after dispatchers.

## MVE Lifecycle Status

This batch closes the MVE hot-reload slice and, with it, the complete 5-stage blueprint lifecycle:

| Stage | Status |
|-------|--------|
| Load / Author | DONE (MVE-01 run proof) |
| Compile | DONE (MVE-05 compile-on-demand) |
| Run / Debug | DONE (MVE-01 to MVE-06) |
| Save | DONE (MVE-03/04) |
| Hot-reload | DONE (this batch) |

**Remaining tracked debt gates production readiness:**
- DEBT-MVE-003 (P1): multi-blueprint robustness — must fix before editor ships more than one compiled blueprint at a time.
- DEBT-MVE-002 (P2): compiler-emitted StateFields for self-describing runtime state.
- DEBT-MVE-001 (P2): UpdateBefore ordering inside TogglableSimulationGroup.

## Suggested Commit Message

```
feat(blueprint-mve): hot-reload proof — behavior change + state preserved, structural hard reset, CaptureLiveState post-reload (MVE-BATCH-07)

BlueprintHotReloadMveTests (3 tests): through the real BlueprintTickSystem + AiHotReloadCoordinator
staging path (same mechanism as QuickReloadService.TriggerAsync):
  1a: v1(+1/tick) → hot-reload v2(+2/tick, same StructureHash) → Count=3+8=11 (state preserved)
  1b: v1 5 frames → hot-reload v3(extra field, diff hash) → hard reset → Count=2 after 2 ticks
  1c: CaptureLiveState(entity, assetId) after v2 reload returns Count=11 (DebugMap re-registered)

VERIFY-FIRST confirmed: StructureHashComputation.cs:9-17 hashes only variable layout (no Graphs);
GraphBuilder.SetVariable stores VariableId only (valueExpression discarded) — no compiler-side
increment expressible from builder nodes.

Task 2: editor "Compile / Reload Blueprint" toolbar already drives QuickReloadService.TriggerAsync
(EditorSubsystem.cs:2111) — no change needed. Task 3: DEBT-MVE-003 upgraded to P1 / production
blocker in DEBT-TRACKER.md with confirmed root cause + fix sketch (carry-forward staging +
per-asset ALC; or merge-commit + multi-ALC).

Build 0 errors; Blueprints 1155/10 (DEBT-006 unchanged); EditorSubsystemBoot 10/10;
AiShared 761/0. Zero golden/codegen change. Closes MVE lifecycle.
```
