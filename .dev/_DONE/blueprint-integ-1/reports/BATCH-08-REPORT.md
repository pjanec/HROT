# BATCH-08 Report
**Tasks:** AIE-030, AIE-031, AIE-032   **Phase:** 3 (Debug)

---

## Implementation Summary

### AIE-030 — DebugSessionRegistry + AiTracerCoordinator + session factories

**Files changed:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Three new fields added (lines ~264–266):
- `_aiTracerCoordinator` — `AiTracerCoordinator`
- `_btreeDebugSession`   — `Hrot.BTree.Editor.Debug.BTreeDebugSession`
- `_hsmDebugSession`     — `Hrot.Hsm.Editor.Debug.HsmDebugSession`

In `Initialize()`, before `AiAssetCatalogBuilder` construction:
1. `AiTracerCoordinator` is instantiated.
2. `BTreeDebugSession(_aiTracerCoordinator)` and `HsmDebugSession(_aiTracerCoordinator)` are created and stored.
3. `BTreeAssetContributor` is constructed with `_btreeDebugSession` — so every `LoadFrom`/`RegisterBlob` call automatically invokes `SetDebugMetadata(blob.DebugMetadata, assetId)` on the session, wiring node-index→VisualId symbolication.

In `RegisterWindows()`, after `debugRegistry` is created and before the registrars are built:
- `debugRegistry.RegisterSessionFactory<BTreeDebugSession>(() => _btreeDebugSession)` — factory returns the pre-built singleton.
- `debugRegistry.RegisterSessionFactory<HsmDebugSession>(() => _hsmDebugSession)` — same pattern.

### AIE-031 — RuntimeInspector panes per perspective

**Files changed:** `EditorSubsystem.cs`

Immediately after `_btreeRegistrar.RegisterWindows(...)` / `_hsmRegistrar.RegisterWindows(...)`:

```csharp
var btreePane = new BTreeRuntimeInspectorPane();
btreePane.SetSession(_btreeDebugSession);
_btreeRegistrar.RuntimeInspector.RegisterPane(btreePane);

var hsmPane = new HsmRuntimeInspectorPane();
hsmPane.SetSession(_hsmDebugSession);
_hsmRegistrar.RuntimeInspector.RegisterPane(hsmPane);
```

The `PerspectiveWorkspaceRegistrar.RuntimeInspector` property exposes the `RuntimeInspectorWindow` instance that was registered in `RegisterWindows()`; `RegisterPane(IRuntimeInspectorPane)` adds it to the window's internal pane list.

### AIE-032 — TraceTimeline lane providers per perspective

**Files changed:** `EditorSubsystem.cs`

Immediately after the pane registration above:

```csharp
_btreeRegistrar.TraceTimeline.RegisterProvider(new BTreeTraceLaneProvider());
_hsmRegistrar.TraceTimeline.RegisterProvider(new HsmTraceLaneProvider());
```

Uses `PerspectiveWorkspaceRegistrar.TraceTimeline.RegisterProvider(ITraceLaneProvider)`.

---

## Design Decisions

1. **Sessions created in `Initialize()`, factories registered in `RegisterWindows()`** — `AiTracerCoordinator` and the two sessions need to exist before `BTreeAssetContributor` is built (so metadata wiring is live for the initial `TriggerInitialLoad()`). The factory registration in `RegisterWindows()` is correct because `DebugSessionRegistry` is a local built there; no timing issue.

2. **Singleton factory pattern** — The factory lambdas capture and return the pre-built session instances rather than `new`-ing on each `TryAcquireSession`. This matches the design intent: one editor session per kind, created at start, reused across debug attach/detach cycles.

3. **`HsmAssetContributor` not modified** — The HSM contributor has no debug-session ctor parameter (the HSM session uses `SetMetadata(Guid, MachineMetadata)`, called from `HsmDebugSession.Update()` directly). The BTree contributor already has the `BTreeDebugSession?` ctor pattern, which is the production mechanism for metadata wiring. No changes to `HsmAssetContributor` were needed or appropriate for this batch; HSM symbolication is driven by the session's own metadata (set via `Update()`).

4. **Tests in the correct assemblies** — `Aie030DebugSessionRegistryIntegrationTests` is in `Hrot.Editor.AiShared.Tests` because it exercises the shared `DebugSessionRegistry` API (which that project already references BTree/HSM editor assemblies for). BTree/HSM inspector pane tests and trace timeline tests are placed in their respective `*.Editor.Tests` projects.

5. **Headless-safe tests** — No test calls `ImGui.*` or `Draw()`. All assertions operate on snapshot data, lane descriptors, and registration counts — all pure-logic, no GPU context required.

---

## Deviations

**None.** All class names, method names, and wiring patterns follow the code exactly. The design named `RegisterPane`/`RegisterProvider` and that is what the code uses. No invented APIs.

---

## Real Class Names and APIs Used

| Concept | Actual class / method |
|---|---|
| Shared debug coordinator | `AiTracerCoordinator` (no-op base; overridable `BeginObservingAssetImpl`/`EndObservingAssetImpl`) |
| Shared debug registry | `DebugSessionRegistry.RegisterSessionFactory<T>(Func<T>)`, `TryAcquireSession<T>(out T?)`, `ReleaseSession(IAiDebugSession)` |
| BTree session | `BTreeDebugSession(AiTracerCoordinator? coordinator = null)` ctor; `SetDebugMetadata(NodeDebugMetadata[]?, Guid)` for symbolication; `Update(EntityRepository, Entity)` for ECS polling; `GetCurrentStateSnapshot()` → `BehaviorTreeStateSnapshot` |
| HSM session | `HsmDebugSession(AiTracerCoordinator? coordinator = null)` ctor; `SetMetadata(Guid, MachineMetadata?)` for symbolication; `Update(EntityRepository, Entity)`; `GetCurrentStateSnapshot()` → `HsmInstanceSnapshot` |
| BTree contributor | `BTreeAssetContributor(BTreeDebugSession? debugSession = null)` — calls `_debugSession?.SetDebugMetadata(blob.DebugMetadata, assetId)` on every `LoadFrom`/`RegisterBlob` |
| BTree inspector pane | `BTreeRuntimeInspectorPane : IRuntimeInspectorPane`; `SetSession(IBTreeDebugSession?)` |
| HSM inspector pane | `HsmRuntimeInspectorPane : IRuntimeInspectorPane`; `SetSession(IHsmDebugSession?)` |
| RuntimeInspectorWindow | `RegisterPane(IRuntimeInspectorPane)` — adds to `_panes` list; `RegisteredPaneCount` (internal) |
| BTree trace lane provider | `BTreeTraceLaneProvider : ITraceLaneProvider`; lanes: `bt.nodes` (Lifecycle\|Decisions), `bt.stack` (Lifecycle), `bt.async` (Async), `bt.errors` (Errors) |
| HSM trace lane provider | `HsmTraceLaneProvider : ITraceLaneProvider`; lanes: `hsm.states` (Lifecycle), `hsm.events/actions/guards/timers` (Decisions), `hsm.conflicts` (Errors) |
| TraceTimelineWindow | `RegisterProvider(ITraceLaneProvider)` — adds to `_providers` list; `RegisteredProviderCount` (internal) |
| Registrar access | `PerspectiveWorkspaceRegistrar.RuntimeInspector` → `RuntimeInspectorWindow`; `.TraceTimeline` → `TraceTimelineWindow` |

---

## How Sessions Bind to World/Kernel/Time

`BTreeDebugSession` and `HsmDebugSession` are given `_aiTracerCoordinator` at construction. The coordinator's `RequestPause`/`RequestContinue`/`RequestStepOneTick` delegates to the kernel's time controller (currently no-op base class — the production subclass wiring is BATCH-09's canvas overlay + breakpoint work). The sessions themselves poll the ECS world via `Update(EntityRepository, Entity)` — called once per frame by the future debug-tick system (BATCH-09). The `_world`, `_kernel`, and `_timeController` are accessible as fields of `EditorSubsystem`; the coordinator and sessions are passed them via the coordinator's virtual methods, not directly. This keeps sessions decoupled from ECS details.

## How Debug-Metadata Symbolication Was Verified

`BTreeAssetContributor` is constructed with `_btreeDebugSession` as its `debugSession` arg. Its private `RegisterBlobCore` method ends with `_debugSession?.SetDebugMetadata(blob.DebugMetadata, assetId)`. This fires on every `LoadFrom` (assembly scan) and every `RegisterBlob` call.

Test `Contributor_WiresDebugMetadata_IntoSession` (in `Aie030DebugSessionRegistryIntegrationTests`):
1. Constructs a `BTreeDebugSession` + `BTreeAssetContributor(session)`.
2. Calls `contributor.RegisterBlob(blob, ...)` with one-entry metadata (VisualId at index 0).
3. Drives `session.Update(world, entity)` with an ECS entity whose `RunningNodeIndex == 0`.
4. Asserts `snapshot.RunningElementId == expectedVisualId` — proves the metadata was wired and symbolication ran end-to-end through the production path.

---

## Test Results

```
Hrot.Editor.AiShared.Tests       Passed:  695 / 695  (+3 new)
Hrot.BTree.Editor.Tests          Passed:  367 / 367  (+13 new)
Hrot.Hsm.Editor.Tests            Passed:  318 / 318  (+16 new)
EditorSubsystemBoot filter       Passed:   10 /  10  (unchanged)
Hrot.Blueprints.Tests            Failed:   10 / 907  (pre-existing DEBT-006, no new failures)
```

New tests added:
- `Aie030DebugSessionRegistryIntegrationTests` (3 tests in AiShared.Tests): registry acquire BTree/HSM, contributor symbolication round-trip
- `BTreeRuntimeInspectorPaneTests` (5 tests in BTree.Editor.Tests): running node id, stack, deep-stack, null-session, TargetKind
- `Aie032BTreeTraceTimelineTests` (8 tests in BTree.Editor.Tests): 4 lane ids, lane levels, kind, uniqueness, display names
- `HsmRuntimeInspectorPaneTests` (5 tests in Hsm.Editor.Tests): active config (leaf ids + phase), null, event queue, TargetKind, single-leaf
- `Aie032HsmTraceTimelineTests` (9 tests in Hsm.Editor.Tests): 6 lane ids, per-lane levels, kind, uniqueness, display names

---

## Developer Insights

1. **MSB3492 transient build errors** — The first `dotnet build` after multiple concurrent builds fails with "Could not read existing file...AssemblyInfoInputs.cache" errors. These are file-locking artifacts on Windows; they self-heal on a second build. The test-runner's `--no-build` flag avoids them after the first sequential build.

2. **`TrySymbolicateIndex` is `internal`** — This method on `BTreeDebugSession` is not accessible outside `Hrot.BTree.Editor.Tests`. The `AiShared.Tests` integration test uses the public ECS `Update()` + `GetCurrentStateSnapshot()` round-trip instead — which is the stronger, more production-faithful assertion anyway.

3. **`TraceLevel` ambiguity in `Hrot.Hsm.Editor.Tests`** — `Fhsm.Kernel.Data.TraceLevel` and `Hrot.Editor.AiShared.Debug.TraceLevel` are both in scope. The stub's `BeginObservingAsset` must use the fully-qualified `Hrot.Editor.AiShared.Debug.TraceLevel` type to match the interface.

4. **`HsmAssetContributor` has no debug-session slot** — Unlike `BTreeAssetContributor`, the HSM contributor does not accept a debug session in its constructor. This is by design: HSM symbolication uses `HsmDebugSession.SetMetadata(assetId, MachineMetadata?)` driven from `Update()` via ECS component decode. There is no `NodeDebugMetadata` equivalent in HSM blobs. No change to `HsmAssetContributor` was needed.

5. **`PerspectiveWorkspaceRegistrar.RuntimeInspector` / `.TraceTimeline`** — These are public properties exposing the pre-built window instances. The pane/provider registration is done in `EditorSubsystem.RegisterWindows()` immediately after calling `_btreeRegistrar.RegisterWindows(windowManager)`, ensuring the windows are already registered before panes/providers are attached.

---

## Known Issues

None. All tasks per spec. Canvas overlays + breakpoint toggles + Watch/Breakpoints windows are BATCH-09 scope.

---

## Suggested Commit Message

```
feat(editor): AIE-030/031/032 — debug session registry + runtime inspector panes + trace timeline lanes (BATCH-08)

AIE-030: AiTracerCoordinator + BTreeDebugSession/HsmDebugSession created in
Initialize(); session factories registered in debugRegistry; BTreeAssetContributor
receives BTreeDebugSession for NodeDebugMetadata symbolication wiring.

AIE-031: BTreeRuntimeInspectorPane/HsmRuntimeInspectorPane registered with
per-perspective RuntimeInspectorWindow via PerspectiveWorkspaceRegistrar.RuntimeInspector.

AIE-032: BTreeTraceLaneProvider/HsmTraceLaneProvider registered with
per-perspective TraceTimelineWindow via PerspectiveWorkspaceRegistrar.TraceTimeline.

Tests: AiShared 695 (+3), BTree 367 (+13), HSM 318 (+16),
EditorSubsystemBoot 10/10, Blueprints 889/10 (DEBT-006 unchanged).
```
