# MVE-BATCH-05 Report — Compile-on-Demand: close the live blueprint loop

## Implementation Summary

### Task 1 — QuickReloadService wired into EditorSubsystem

**File changed:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

**Fields added** (lines ~294–308):
```csharp
private Action? _blueprintCompileCallback;
private string  _blueprintCompileStatus = string.Empty;
```

**`_blueprintQuickReloadTrigger = null` replaced** (line ~1990) with real wiring:
- Constructs a `Fdp.Toolkit.Behavior.AiHotReloadCoordinator(behaviorRegistry, blueprintRegistry, opts)` — the lightweight FDP variant (not the file-watching `Hrot.Editor` variant; see Registry-sharing proof below).
- Constructs a `Hrot.Blueprints.Editor.Reload.QuickReloadService(catalog, state, console, compiler, coordinator, session)`.
- Sets `_blueprintQuickReloadTrigger` to a lambda that: resolves the active `BlueprintAsset` from `_aiDocumentManager.Active.ViewState as AiCanvasContext`, calls `TriggerAsync(bpAsset).GetAwaiter().GetResult()`, and writes the result to `_blueprintCompileStatus`.

**Toolbar action** (lines ~1830–1860 in `RegisterWindows`):
- Uses the same `CaptureWindowRegistrar` / `RegisterToolbarEntry` pattern as Run (MVE-03) and Save (MVE-04).
- Label: `"Compile / Reload Blueprint"`.
- Callback invokes `_blueprintQuickReloadTrigger?.Invoke(active.Asset)` after resolving the active document.
- `_blueprintCompileCallback` is captured via `GetToolbarCallback`.

**DrawUI** (lines ~1486–1512):
- Gates on `ImGui.GetCurrentContext() != Zero` (headless-safe).
- Opens `ImGui.Begin("Blueprint Compile")`, renders button + status line.

**Internal test accessors added:**
```csharp
internal Action? BlueprintCompileCallback => _blueprintCompileCallback;
internal string  BlueprintCompileStatus   => _blueprintCompileStatus;
internal Fdp.Toolkit.Blueprints.BlueprintRegistry BlueprintRegistry => _blueprintRegistry;
```

### Task 2 — Headless compile→register→run integration tests

**New file:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintCompileOnDemandMveTests.cs`

Five tests in class `BlueprintCompileOnDemandMveTests [Collection("DebugProbe")]`:

| Test | What it proves |
|---|---|
| `QuickReload_InstanceBlueprint_RegistersIntoSharedRegistry` | `TriggerAsync` on an in-memory Instance asset → `TryGetById` returns true in shared registry |
| `QuickReload_RegisteredBlueprint_AttachAndPump_CounterAdvancesN(1/3/5)` (Theory) | Coordinator staging path → `AttachBlueprint` succeeds → `PumpFrames(N)` → `TickCount == N` |
| `QuickReload_FullPipeline_CompiledBlueprint_AttachesAndRunsOnEntity` | Full `TriggerAsync` → `TryGetById` → attach → 3 pump frames → slot exists + `StateSize > 0` |

### Task 3 — Run auto-compile decision

**Decision: two-click flow (Compile + Run separate actions).**

Rationale:
1. `RunBlueprintOnEntityCommand.Execute` is a `static` method in `Hrot.Blueprints.Editor` — it has no reference to `QuickReloadService` (which lives in the same assembly but via `EditorSubsystem`'s composition, not a direct dependency).
2. Wiring compile-on-Run would require threading `QuickReloadService` or a compile delegate through `Execute`, which adds async complexity to a previously synchronous call path.
3. The `NotRegistered` branch already returns `"Compile / register the blueprint first."` — clear one-sentence guidance.
4. The two toolbar buttons (`Compile / Reload Blueprint` and `Run Blueprint on Selected Entity`) form a natural two-step workflow and match user expectations (compile → run = standard IDE flow).

---

## Design Decisions

### Registry-instance sharing (the crux)

**Proof (file:line):**

`Hrot.Editor/EditorSubsystem.cs:535`:
```csharp
_aiCoordinator = new AiHotReloadCoordinator(
    aiAssemblyDir, "Hrot.AI.Behaviors.dll",
    _world!, _behaviorRegistry!,
    _blueprintRegistry,            // ← line 538
    ...);
```

`Hrot.Editor/AiHotReloadCoordinator.cs:332` — `_blueprintRegistry` is stored as `private readonly BlueprintRegistry _blueprintRegistry` and used in `ApplyQuickReload`:
```csharp
_blueprintRegistry.CommitStaging(blueprintStaging);
```

For `QuickReloadService`, a **new** `Fdp.Toolkit.Behavior.AiHotReloadCoordinator` is constructed in `RegisterWindows` with the SAME `_blueprintRegistry` field:
```csharp
var qrsCoordinator = new Fdp.Toolkit.Behavior.AiHotReloadCoordinator(
    _behaviorRegistry!,
    _blueprintRegistry,     // ← SAME instance as passed to _aiCoordinator (line 538)
    new AiHotReloadCoordinatorOptions());
```

`FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs:174` — `ApplyQuickReload` calls:
```csharp
_blueprintRegistry.CommitStaging(blueprintStaging);
```

Therefore: `QuickReloadService.TriggerAsync(asset)` → `coordinator.ApplyQuickReload(...)` → `CommitStaging(...)` writes into the EXACT `_blueprintRegistry` instance that `BlueprintTickSystem` resolves definitions from. No mismatch.

### Why `Fdp.Toolkit.Behavior.AiHotReloadCoordinator` and not `Hrot.Editor.AiHotReloadCoordinator`

`QuickReloadService` (in `Hrot.Blueprints.Editor`) imports `Fdp.Toolkit.Behavior` and requires `Fdp.Toolkit.Behavior.AiHotReloadCoordinator`. The `Hrot.Editor.AiHotReloadCoordinator` is a different class (file-watching, full dependency graph). A new lightweight FDP coordinator is constructed with shared registries — same semantic result.

### In-memory vs disk compile

`QuickReloadService.TriggerAsync(BlueprintAsset asset)` compiles from the **in-memory** `BlueprintAsset` object directly. It uses `_editorState.GetInMemoryAsset()` for sibling signatures but the edited asset itself is passed by reference. **No disk save is required before compiling.** The user can compile unsaved work; Save (MVE-04) is separate and optional beforehand.

### Async handling

`TriggerAsync` returns `Task.FromResult(...)` — it is synchronous internally. Calling `.GetAwaiter().GetResult()` is safe (no thread pool blocking). This matches the pattern used in the existing test suite.

---

## Deviations

| Item | What | Why | Risk |
|---|---|---|---|
| `StateFields` not populated in compiled blueprint | The compiler does NOT emit `StateFields` into the generated registrar (confirmed in `Snapshots/Emit/InstanceCounter.cs.txt`). `TryGetField<int>("Count")` fails on compiled blueprints. | Compiler limitation, not introduced by this batch. | Low: `StateFields` are not needed for tick/attach; they are only used by the debug watch panel. A future compiler enhancement would add them. |
| Test `QuickReload_FullPipeline_*` asserts `StateSize > 0` not `Count == N` | Cannot assert `Count == N` when Tick body is empty (Entry→Return). | Compiler doesn't generate increment nodes without an explicit graph; acknowledged in test comment. | Low: the attach+pump path is proven. The counter proof is in `QuickReload_RegisteredBlueprint_AttachAndPump_CounterAdvancesN` via FakeInstanceBp staging. |

---

## Test Results

### New tests (`BlueprintCompileOnDemandMveTests`) — all 5 pass
```
Passed  QuickReload_InstanceBlueprint_RegistersIntoSharedRegistry           [262ms]
Passed  QuickReload_RegisteredBlueprint_AttachAndPump_CounterAdvancesN(1)   [12ms]
Passed  QuickReload_RegisteredBlueprint_AttachAndPump_CounterAdvancesN(3)   [11ms]
Passed  QuickReload_RegisteredBlueprint_AttachAndPump_CounterAdvancesN(5)   [10ms]
Passed  QuickReload_FullPipeline_CompiledBlueprint_AttachesAndRunsOnEntity  [1321ms]
```

### `Hrot.Blueprints.Tests` full suite
```
Failed:   10  (all pre-existing DEBT-006: golden-file snapshots, allocation-free, condition-summary)
Passed: 1152
Skipped:    8
Total:   1170
```
The 10 failures are identical to those present before this batch. No new failures.

### `Hrot.Editor.AiShared.Tests`
```
Passed: 761, Failed: 0, Skipped: 0
```

### `Hrot.ClusterRunner.Integration.Tests --filter EditorSubsystemBoot`
```
Passed: 10, Failed: 0  (QuickReloadService now constructed at composition; boot unaffected)
```

### `dotnet build IOS-IG-SimHost.sln`
```
0 Errors, 18 Warnings (all pre-existing; 0 new warnings in touched projects)
```

---

## Developer Insights

### `StateFields` gap
The compiler does not populate `BlueprintDefinition.StateFields` in the generated registrar. This means `BlueprintStateView.TryGetField` doesn't work for compiled blueprints — only for manually-written definitions (`FakeInstanceBp`, `CounterDemoBlueprint`). This is DEBT worth tracking for MVE-07/debug-observe, where the watch panel needs field access.

### `AssemblyLoadContext` is not `IDisposable`
`System.Runtime.Loader.AssemblyLoadContext` does not implement `IDisposable` in .NET 8; it has `Unload()`. The `using var alc = new AssemblyLoadContext(...)` pattern causes CS1674. Changed to `var alc = new AssemblyLoadContext(...)`.

### `Fdp.Toolkit.Behavior.AiHotReloadCoordinatorOptions` default ctor
The FDP `AiHotReloadCoordinatorOptions` is a record with no required properties. `new AiHotReloadCoordinatorOptions()` works and leaves all optional settings at defaults (no file-watching configured, since QuickReloadService doesn't need it).

### Compile button placement
The Compile button ("Blueprint Compile" ImGui window) is rendered in `DrawUI`, which is only called in non-headless mode. In headless tests, `_blueprintCompileCallback` can still be invoked directly via the test accessor. This mirrors the Run (MVE-03) and Save (MVE-04) button placement exactly.

---

## Known Issues

1. **`StateFields` gap (DEBT)**: Compiled blueprints have empty `StateFields` so field read-back via `TryGetField` doesn't work. The test documents this. The debug-observe MVE-07 batch will need to address this in the compiler.
2. **One-time QuickReload overhead**: `TriggerAsync` on a non-trivial blueprint can take ~1–2 seconds (Roslyn compile). This is expected; UI shows the duration in the compile status line.
3. **`_blueprintQuickReloadTrigger` is set in `RegisterWindows`** (not `Initialize`). The `RegenerationScheduler` is also built in `RegisterWindows`, so the scheduling path and the trigger are both set up in the same method — consistent. However, if `RegisterWindows` is not called (some test paths), the trigger remains null. This is by design and safe.

---

## Next Steps

### MVE-06 — hot-reload (verify end-to-end file-watch)
The `Hrot.Editor.AiHotReloadCoordinator` watches `Hrot.AI.Behaviors.dll` for changes. MVE-06 would trigger a file change while a blueprint instance is running and verify the live instance picks up the change (soft-reload preserves state where hash unchanged). The `OnReloadCompleted` callback in `EditorSubsystem` already reconciles open documents.

### MVE-07 — debug-observe
Wire `BlueprintDebugSession` to a running instance. Requires `Attach(entity, blueprintId)` on the debug session, then polling the probe sink for variable values. The current batch wires `_blueprintDebugSession` into `QuickReloadService` (optional `session` param) so the debug map is registered on compile. The watch panel should show values if `StateFields` is populated — see DEBT above.

### Compiler `StateFields` enhancement
The generated registrar should populate `StateFields` from `VarIds` so that `TryGetField<int>("Count")` works on compiled blueprints without a separately-defined `FakeInstanceBp`. This would unblock the `Count == N` assertion in a compiled Tick that has an increment graph.

---

## Suggested Commit Message

```
feat: MVE-05 compile-on-demand — wire QuickReloadService into EditorSubsystem + compile→register→run test
```

**Details:**
- Replace `_blueprintQuickReloadTrigger = null` with real `QuickReloadService.TriggerAsync` wiring; compiled blueprint lands in SAME `_blueprintRegistry` the kernel ticks (shared via `Fdp.Toolkit.Behavior.AiHotReloadCoordinator` holding same reference).
- Add "Compile / Reload Blueprint" toolbar button (CaptureWindowRegistrar + DrawUI, same pattern as Run/Save).
- Compile from in-memory asset — no disk save required.
- 5 new tests in `BlueprintCompileOnDemandMveTests`: compile→register, register→attach→run (Count==N), full pipeline.
- Build 0 errors; Hrot.Blueprints.Tests 10 pre-existing DEBT-006 unchanged; EditorSubsystemBoot 10/10; AiShared.Tests 761/761.
