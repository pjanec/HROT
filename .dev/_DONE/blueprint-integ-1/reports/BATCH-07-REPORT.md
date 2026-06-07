# BATCH-07 Report

## Implementation Summary

### AIE-025 — Blackboard Authoring bound to active asset

**Files changed:**
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — added `AiDocumentManager.ActiveChanged` handler that updates all three per-perspective `EditorSelectionStore.ActiveAsset` fields when the active document changes.
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/BlackboardAuthoringWindowBindingTests.cs` — three new behavioral tests.

The `BlackboardAuthoringWindow` already used a pure pull model (`_store.ActiveAsset` read on each `DrawClientArea` frame). The missing link was: nothing set `_store.ActiveAsset` when `AiDocumentManager.ActiveChanged` fired. The wiring is a four-line lambda after `AiDocumentManager` is constructed in `RegisterWindows`. The "tolerate no aggregator" requirement was already satisfied by `BuildViewModel` handling `aggregationResult: null` (explicit vars only, no throw).

### AIE-026 — Save → emit → hot-reload loop

**New source files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/RegenerationScheduler.cs` — deterministic debounce scheduler with injectable clock (`Func<long>`) and flush action. `Tick()` is called once per frame from `EditorSubsystem.Update()`. `debounceTicks` defaults to 500 (ms when using `Environment.TickCount64`).
- `Hrot/Editor/Hrot.Editor.AiShared/Emit/AiAssetEmitService.cs` — thin façade wrapping a kind-specific emit delegate + optional post-emit callback (used to clear the dirty flag). Delegates to `FluentCSharpEmitterBase.WriteAtomic` for atomic file writes.

**Modified source files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — added `public void ClearDirty()` (was missing; `MarkDirty` was internal).
- `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocument.cs` — `Asset` property changed to `private set`; new `ReconcileAsset(IEditableAsset)` method updates the asset reference and clears dirty after reload.
- `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` — new `ReconcileFromCatalog(IEnumerable<IEditableAsset>)` method that matches open documents by `AssetId` and calls `ReconcileAsset` on matches; fires `ActiveChanged` when the active document is reconciled.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — wired everything:
  - AIE-025: `ActiveChanged → per-perspective store.ActiveAsset`.
  - AIE-026: `RegenerationScheduler` tick in `Update()`; `DocumentOpened` subscribes `asset.Changed → scheduler.Schedule(asset)` when dirty; `AiAssetEmitService` with `BTreeFluentEmitter`/`HsmFluentEmitter` as emit delegates; on `OnReloadCompleted` calls `_aiDocumentManager.ReconcileFromCatalog(_aiCatalogBuilder.Catalog.All)`.
  - Blueprint routing: `_blueprintQuickReloadTrigger` field (Phase 4 seam); scheduler flush action routes `AssetKind.Blueprint` through the trigger (no-op in Phase 2).

**New test files:**
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/BlackboardAuthoringWindowBindingTests.cs` (3 tests)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Emit/RegenerationSchedulerTests.cs` (9 tests — debounce, multi-asset, single-asset, same-asset dedup, timing)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Emit/BlueprintSchedulerRoutingTests.cs` (2 tests — Blueprint routed to trigger; null trigger no-throw)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Emit/RegenerationSchedulerTests.cs` also contains `ReloadReconciliationTests` (4 tests — ReconcileAsset updates ref + clears dirty; wrong AssetId is no-op; ReconcileFromCatalog fires ActiveChanged; unrelated asset is no-op)
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/SaveBTreeEmitTests.cs` (4 tests — deterministic emit, byte-identical WriteAtomic no-op, content-differs write, empty path no-op)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/SaveHsmEmitTests.cs` (4 tests — deterministic emit, byte-identical WriteAtomic no-op, content-differs write, structural content)

---

## Design Decisions

### RegenerationScheduler clock injection
Used `Func<long>` (returning monotonic tick count) rather than a `TimeProvider` abstraction or `Task.Delay`, so tests call `Tick()` synchronously with a manual counter. The `debounceTicks` param is in the same units as the provider; defaults to 500 ms with `Environment.TickCount64`. The scheduler is purely main-thread (no lock needed).

### AiAssetEmitService design
Kept the service in `Hrot.Editor.AiShared` (no direct references to BTree/HSM). The BTree/HSM type dispatch lives in the `EditorSubsystem` lambda passed as `emitDelegate`, keeping the dependency direction clean.

### Reconciliation via `ReconcileAsset`
Changed `AiDocument.Asset` from `{ get; }` to `{ get; private set; }` and added `ReconcileAsset`. This preserves the invariant that the asset identity is set at construction while allowing the post-reload swap. The document's `ViewState` (GraphView + view-state) is preserved across the reconcile — only `Asset` is replaced.

### Blueprint "light wiring"
`_blueprintQuickReloadTrigger` is a null `Action<IEditableAsset>` seam. The flush action routes `AssetKind.Blueprint` to this trigger (null = no-op); Phase 4 sets it to `QuickReloadService.TriggerAsync`. This avoids building the full Blueprint pipeline (which needs `EditorState`, `IBlueprintCompiler`, etc.) in Phase 2.

### Ordering of `OnReloadCompleted` subscribers
The existing subscriber at line ~529 calls `_aiCatalogBuilder.RefreshFromAssembly(aiAsm)`, which synchronously runs `contributor.LoadFrom()` → `AssetCatalog.Rebuild()`. The new subscriber (added in `RegisterWindows`, registered later) then calls `ReconcileFromCatalog(catalog.All)` with the already-fresh assets. Multicast delegate ordering guarantees the first subscriber fires before the second.

---

## Deviations

| What | Why | Benefit | Risk |
|------|-----|---------|------|
| `HsmAsset.ClearDirty()` added (new public method) | `IsDirty`'s setter is `internal`; `EditorSubsystem` (different assembly) needed to clear it after emit | Clean API; mirrors `BehaviorTreeAsset.ClearDirty()` | Low — additive change with doc |
| `AiDocument.Asset` changed to `private set` | `ReconcileAsset` needs to update the field after hot-reload | Enables reconcile without breaking encapsulation | Minimal — constructor still sets it; only `ReconcileAsset` mutates it |
| Blueprint QuickReload: Phase 4 seam instead of full construction | `QuickReloadService` requires `IAssetCatalog` (Blueprint-specific), `EditorState`, `IBlueprintCompiler` — none wired yet | Avoids dependency bloat; Phase 4 fills the seam | Blueprints won't auto-save until Phase 4 wires the trigger |

---

## Test Results

All suites run with `--no-build` after full incremental rebuild.

| Suite | Passed | Failed | Total | Notes |
|-------|--------|--------|-------|-------|
| `Hrot.Editor.AiShared.Tests` | **692** | 0 | 692 | +15 new (3 AIE-025 + 9 scheduler + 4 reconcile + 2 blueprint routing); prev 677 |
| `Hrot.BTree.Editor.Tests` | **354** | 0 | 354 | +4 new (BTree emit determinism); prev 350 |
| `Hrot.Hsm.Editor.Tests` | **302** | 0 | 302 | +4 new (HSM emit determinism); prev 298 |
| `EditorSubsystemBoot` filter | **10** | 0 | 10 | unchanged |
| `Hrot.Blueprints.Tests` | 889 | **10** | 907 | same 10 pre-existing DEBT-006 failures; no new failures |

### Named tests (batch-required)

- `RegenerationScheduler_DebouncesBurst_IntoSingleSave` — **PASS**: 5 rapid schedules → 1 flush after debounce window; 0 flushes before.
- `Save_BTree_EmitsDeterministicCSharp_ByteIdentical_OnNoChange` — **PASS**: two emits of unchanged asset produce identical strings; `WriteAtomic` returns `false` (no-op).
- `Save_Hsm_EmitsDeterministicCSharp_ByteIdentical_OnNoChange` — **PASS**: same for HSM.
- `Reload_ReconcilesModel_ByStableId` — **PASS**: `ReconcileAsset` replaces asset ref, clears dirty; wrong AssetId is no-op; `ReconcileFromCatalog` fires `ActiveChanged` on reconcile.

---

## Developer Insights

- **HsmAsset lacked `ClearDirty`**: `BehaviorTreeAsset.ClearDirty()` existed but `HsmAsset` only had an `internal MarkDirty()`. Added `public ClearDirty()` to match the pattern.
- **`HsmBuilder.State()` name uniqueness**: The `HsmBuilder` throws on duplicate state names across the whole builder session, not just within one call chain. Initial test called `builder.State("Idle")` once at line 34 and again implicitly via `.On("Tick")`. Fixed by capturing the return value.
- **Parallel build race (CycloneDDS)**: `dotnet build` with multiple project args triggers a CycloneDDS IDL copy race. Always use `--no-build` for test runs after separate build steps.
- **`AiDocument.Asset` readonly**: Changing to `private set` was the minimal correct approach. An alternative would be a separate `IReconciledDocument` interface, but that adds complexity with no benefit here.
- **Blueprint seam vs. inline construction**: Attempting to wire `QuickReloadService` inline failed because `BlueprintContributorCatalogAdapter`, `NullOutputConsole`, and `DefaultBlueprintCompiler` don't exist as production types. The seam approach is cleaner and defers correctly to Phase 4.

## Known Issues

- **Blueprint QuickReload is a no-op in Phase 2**: `_blueprintQuickReloadTrigger` is always null until Phase 4 wires it. Dirty Blueprint files will not auto-save. This is by design (Phase 4 concern).
- **Atomic write uses string equality, not byte equality**: `FluentCSharpEmitterBase.WriteAtomic` reads the file as a string (`File.ReadAllText`) and compares with `==`. For the C# source files involved, this is sufficient; it would fail for binary files but none are emitted here.
- **`RegenerationScheduler.Tick()` is not called in headless mode** for tests that bypass `DrawUI` (e.g., integration tests). The scheduler won't auto-flush in headless but can be ticked manually or tested directly.

## Suggested Commit Message

```
feat(editor): Blackboard binding + save→emit→hot-reload loop (BATCH-07)

AIE-025: AiDocumentManager.ActiveChanged → per-perspective EditorSelectionStore.ActiveAsset;
BlackboardAuthoringWindow now shows the active asset's schema via existing pull model.

AIE-026: RegenerationScheduler (injectable clock/flush; debounces bursts → single save);
AiAssetEmitService (BTreeFluentEmitter/HsmFluentEmitter + FluentCSharpEmitterBase.WriteAtomic);
HsmAsset.ClearDirty() added; AiDocument.ReconcileAsset + AiDocumentManager.ReconcileFromCatalog
for post-reload reconciliation by VisualId/StableId; Blueprint dirty → QuickReloadService seam
(Phase 4 fills _blueprintQuickReloadTrigger). EditorSubsystem.Update() ticks the scheduler.

Tests: AiShared 692/692 (+15), BTree 354/354 (+4), HSM 302/302 (+4),
EditorSubsystemBoot 10/10, Blueprints 889/10 (DEBT-006, no new).
```
