# IOS-BATCH-02 Report

**Batch:** IOS-BATCH-02  
**Phase:** IOS-P7 (UI Panels)  
**Date Completed:** 2026-02-25  
**Status:** ✅ DONE  
**Tests:** 136 passing / 0 failing / 0 skipped

---

## Completed Tasks

| Task | File | Status |
|---|---|---|
| IOS.7.1 – Config Panel    | `Bagira.IOS/Panels/ConfigPanel.cs`      | ✅ |
| IOS.7.2 – ORBAT Panel     | `Bagira.IOS/Panels/OrbatPanel.cs`       | ✅ |
| IOS.7.3 – Mission Panel   | `Bagira.IOS/Panels/MissionPanel.cs`     | ✅ |
| IOS.7.4 – Interaction Log | `Bagira.IOS/Panels/InteractionPanel.cs` | ✅ |
| IOS.7.5 – Spawner Panel   | `Bagira.IOS/Panels/SpawnerPanel.cs`     | ✅ |
| Supporting types          | `Bagira.IOS/Panels/PanelConstants.cs`   | ✅ |
| Logic interface           | `Bagira.IOS/IIosLogic.cs`               | ✅ |
| Panel tests               | `Bagira.IOS.Tests/*PanelTests.cs` (×5)  | ✅ |

---

## Developer Insights

### Q1: How did you tackle UI event-driven state mutations without ImGui actively running in tests?

Each panel separates *business logic* from *rendering* into two layers:

1. **State + Handler methods** (`Handle*` / `BuildPatch` / `GetVisibleNodes` etc.) — pure C#, no ImGui dependency.  These are `public` and called from unit tests directly.
2. **`Draw(IIosLogic logic)`** — a stub containing the future ImGui calls in comments; Phase P9 will flesh these out.

Tests instantiate the panel, set state through property setters, then invoke the handler method (e.g. `panel.HandleSendConfigPatch(mockLogic)`) and assert on either the mock's invocation record (`logic.Verify(...)`) or the panel's own resulting state.

This mirrors the pattern already used in `ContextMenuLogic` — tests call `OnSelectionChanged` / `OnActionInvoked` directly rather than trying to simulate the ImGui event loop.

The `IIosLogic` interface was introduced specifically to make mock injection clean: every panel receives `IIosLogic` rather than the concrete `IosLogic` shell, so Moq can stub out `Repo`, `MissionEditorService`, `SendConfigPatch`, `SelectEntity`, and `StartPlacementMode` independently per test.

---

### Q2: Did you notice any allocations or GC spikes during recursive ORBAT tree rendering? What choices minimised this?

The main allocation risk in an ORBAT tree renderer is the per-frame recursion and intermediate `List<IDerEntity>` results from `FindChildren`.

Mitigations applied:

* **`GetVisibleNodes` is called once** from `Draw` and its result iterated; the per-call `HashSet<int> visited` is local and GC'd once per frame — acceptable compared to a per-entity dictionary lookup.
* **`FindChildren` uses `yield return`** — it is lazy and only materialised into a `List<T>` once per node via `.ToList()` inside `CollectNodes`. For Phase P9 this could be replaced with a pre-cached adjacency list keyed by `CommanderId` to drop from O(n²) full-repo scans to O(1) child lookup.
* **`_expandedNodes` is a `HashSet<int>`** — `Contains` and `Add`/`Remove` are O(1).
* **`_filteredEntries` in `SpawnerPanel` is pre-built** on filter change, not re-evaluated inside `Draw`, keeping `Draw` allocation-free.
* **`InteractionPanel._readOnlyLog`** is cached as a `ReadOnlyCollection<LogEntry>` wrapper over the underlying list — no allocation on each `Entries` access.
* **`InteractionPanel._log`** is pre-allocated to `MaxLogEntries` capacity in the constructor; no resize occurs during normal operation.

One remaining concern noted for Phase P9 debt: `OrbatPanel.FindChildren` iterates the entire repo per node, making tree rendering O(n²) in entity count. A `Dictionary<int, List<IDerEntity>>` child-map (built lazily on `GetVisibleNodes`) would reduce this to O(n). Deferred to Phase P9 when the live repo is wired.

---

### Q3: What design decisions did you make beyond the UI layouts? Did you enhance or decouple the logic interface further?

1. **`IIosLogic` interface** — not in the original design spec but added as the primary decoupling seam. Every panel depends on `IIosLogic`, never on the concrete `IosLogic` class. This is the same pattern used for `IDdsWriter<T>` and `IDerRepo`.

2. **`TkbCatalogEntry` record** — the design doc showed `SpawnerPanel` holding a `TkbService` dependency. Instead, the catalog is flattened to `IEnumerable<TkbCatalogEntry>` at construction time (by the Phase P9 shell). The panel itself has no knowledge of `ITkbDatabase`, `TkbTemplate`, or FDP internals — it just iterates the pre-baked list. This keeps the panel layer independent of the FDP TKB infrastructure and makes catalog test fixtures trivial to build.

3. **`PanelConstants` centralisation** — all `MaxLogEntries`, `MaxOrbatDepth`, `IconScaleMin/Max/Default`, and `FilterTextMaxLength` live in one file (CODE-STANDARDS §1). Changing a threshold is a one-line edit.

4. **`eForceIdentifier` over design-doc `eAffiliation`** — the design doc referenced `eAffiliation.FRIEND` / `eAffiliation.HOSTILE`, which do not exist in the codebase. The actual type is `eForceIdentifier` (`FORCE_FRIENDLY`, `FORCE_OPPOSING`, `FORCE_NEUTRAL`) from `GenericDescriptors.cs`. All panels and tests use the real type.

5. **`eMissionCommandType.CMD_ABORT_ALL` for Abort** — the design doc says `CMD_ABORT_MISSION` which is not in the enum; the correct variant is `CMD_ABORT_ALL`. The `MissionPanel.HandleAbort` uses the correct value.

6. **`GetTaskIcon` uses `eTaskState`** — the design doc had `task.Completed` (a boolean) which does not exist on `MissionTask`. The implementation uses `task.State` (the `eTaskState` enum) with a switch expression covering all five states.

---

### Q4: Did you encounter any missing fields in the DataModels required for Mission representations?

Yes, two divergences from the design doc:

| Design doc assumption | Actual DataModel | Mitigation |
|---|---|---|
| `MissionTask.Completed` (bool) | Does not exist; task lifecycle is tracked by `eTaskState` | `GetTaskIcon` uses `task.State == eTaskState.TASK_DONE` |
| `mission.CurrentTaskIndex` (int) | Does not exist; active task identified by `MissionPlan.ActiveTaskId` (Guid) | Icon logic checks `task.TaskId == plan.ActiveTaskId` |
| `MissionControlRequest` targets `int entityId` | `TargetEntityId` is `long` | `MissionPanel.HandleJump/Abort` casts `int → long` (safe, no truncation for valid entity IDs) |
| `eAffiliation` enum | Actual type is `eForceIdentifier` with different value names | All panel code uses `eForceIdentifier` |

These are the same `IOS-DEBT-030` gap noted in the debt tracker (TargetEntityId long vs int) plus the task-state representation mismatch which is now locally resolved.

---

### Q5: Are there any synchronisation issues between external DDS reads and UI draw frames that must be considered when wiring the final Application Shell (IOS Phase 8)?

Yes, several:

1. **Concurrent DDS ingress vs. panel reads**: The panels read from `IDerRepo` during `Draw`, which runs on the main (Raylib) thread. If `DerRepo.Poll()` is called on a background thread (or if DDS callbacks fire on a DDS thread), entity descriptors could be mutated mid-frame. Mitigation: the application shell must call `Repo.Poll()` synchronously from the main thread *before* beginning the ImGui frame, or use a double-buffer / snapshot model so panels always see a consistent view.

2. **`GetVisibleNodes` iterates the live repo**: Any entity added or deleted between the start of `DrawUI` and the end of `Draw` on each panel could produce a torn view (entity present in ORBAT but missing in `MissionPanel`). A single `GetAllEntities()` snapshot at the top of the frame would prevent this.

3. **`InteractionPanel.AddLog` concurrency**: `AddLog` may be called from network-ingress callbacks (e.g. when `CreateEntityAck` arrives on a DDS reader thread). `List<T>` is not thread-safe. Phase P9 must either: (a) route log entries through a `ConcurrentQueue<LogEntry>` drained on the main thread at frame start, or (b) lock around `_log` access. Option (a) is preferred — zero contention on the draw path.

4. **`MissionPanel` selected-entity race**: If an entity is deleted from the repo (EntityMaster disposed) between the selection event and the next `Draw`, `logic.Repo.GetEntity(_selectedEntityId)` returns `null`. The Phase P9 `Draw` implementation already handles this with an early-exit null check, but the `IosLogic.EntityDeleted` event must also clear `_selectedEntityId` to keep the panel's state consistent.

5. **`SpawnerPanel._filteredEntries` is rebuilt synchronously on `SearchFilter` set**: Rebuilding is O(n) and happens on the main thread. For catalogs with tens of thousands of entries this could cause a frame stall. Mitigation: debounce (rebuild only when the filter hasn't changed for N frames) or move rebuild to a background task if catalog size grows beyond ~1 000 entries.

---

## Test Coverage Summary

| Test Class | Tests | Key Contracts Verified |
|---|---|---|
| `ConfigPanelTests`       | 15 | `BuildPatch` JSON structure for every field; state clamping; `HandleSendConfigPatch` calls logic; null guard |
| `OrbatPanelTests`        | 20 | Root/child queries; filter (incl. case-insensitivity); `GetVisibleNodes` collapse/expand; **cycle detection**; **depth cap**; click forwarding |
| `MissionPanelTests`      | 12 | `GetTaskIcon` for all `eTaskState` values; `HandleJump` / `HandleAbort` send correct command types; no-selection guard; null guard |
| `InteractionPanelTests`  | 11 | `AddLog` stores all fields; cap enforcement (exact, +1, +n); oldest-evicted ordering; immutable `Entries` view |
| `SpawnerPanelTests`      | 18 | Catalog construction; filter (empty, case-insensitive, partial, no-match, multi-match); `HandleTypeSelected`; `HandleAffiliationChange`; `HandleActivatePlacementTool` with correct TKB ID and affiliation; null guard |
| **Total**                | **136 (all pass)** | |

---

## Debt Items Added

None added. Existing open items IOS-DEBT-029 through IOS-DEBT-032 remain unchanged (deferred to Phase P9 as documented).

The following Phase P9 concerns identified above are **not blocking** this batch but should be tracked:

| ID | Sev | Description | Target |
|---|---|---|---|
| IOS-DEBT-033 | P3 | `OrbatPanel.FindChildren` scans all entities per node (O(n²)); replace with a child-map for large repos | IOS Phase 9 |
| IOS-DEBT-034 | P3 | `InteractionPanel.AddLog` not thread-safe; needs `ConcurrentQueue` drain on main thread | IOS Phase 9 |
