# IOS-BATCH-03 Report

**Batch:** IOS-BATCH-03  
**Phase:** IOS-P8 (Application Shell)  
**Date Completed:** 2026-07-14  
**Status:** ✅ DONE  
**Tests:** 172 passing / 0 failing / 0 skipped

---

## Completed Tasks

| Task | File(s) | Status |
|---|---|---|
| Corrective: IOS-DEBT-034 | `Hrot.ExCon/Panels/InteractionPanel.cs` | ✅ |
| IOS.8.1 – IOS Main Logic | `Hrot.ExCon/IosLogic.cs`, `Hrot.ExCon/IosLogicConstants.cs` | ✅ |
| IOS.8.1 – Event queue abstraction | `Hrot.ExCon/Services/IEventQueue.cs`, `ConcurrentEventQueue.cs` | ✅ |
| IOS.8.2 – IosMock orchestrator | `Hrot.ExCon/IosMock.cs` | ✅ |
| IOS.8.2 – Program & CLI | `Hrot.ExCon/Program.cs` | ✅ |
| Raylib package refs | `Hrot.ExCon/Hrot.ExCon.csproj` | ✅ |
| InteractionPanel tests update | `Hrot.ExCon.Tests/InteractionPanelTests.cs` | ✅ |
| IosLogic tests | `Hrot.ExCon.Tests/IosLogicTests.cs` | ✅ |
| IosMock tests | `Hrot.ExCon.Tests/IosMockTests.cs` | ✅ |
| Debt tracker update | `.dev-workstream/IOS-DEBT-TRACKER.md` | ✅ |

---

## Developer Insights

### Q1: What mechanisms did you employ to handle the safe concurrent draining of the Event Log (DEBT-034)?

The fix follows a **producer/consumer drain model** entirely inside `InteractionPanel`:

1. **`AddLog` (producer)** — was a direct `List<T>.Add` on whatever thread called it.  
   Now enqueues into a `private readonly ConcurrentQueue<LogEntry> _pending`.  
   `ConcurrentQueue<T>` is lock-free and safe to call from any number of threads simultaneously, so DDS ingress callbacks (which fire on CycloneDDS thread pool threads) can safely call `AddLog` at any point.

2. **`DrainPendingLogs` (consumer)** — new public method, called from the main Raylib thread at the top of every frame inside `IosLogic.Update()`.  
   It dequeues entries one by one into the already-existing `_log` list, enforcing the `MaxLogEntries` cap through the same head-eviction logic as before.  
   Returns the drain count so callers can skip redundant work when nothing was queued.

3. **`Entries` / `EntryCount`** — exposed properties reflect only the **drained** (committed) list.  
   A background thread that has enqueued but whose entries have not yet been drained will not see partially-updated reads; the list is never touched except from the main thread.

The result is **zero locking on the draw path**: `AddLog` races only within `ConcurrentQueue`'s own internal compare-and-swap loops; `DrainPendingLogs` reads from a `ConcurrentQueue`, which is safe even with concurrent enqueuers; the `_log` list itself is single-threaded at all times.

Tests in `InteractionPanelTests` (`AddLog_ConcurrentWriters_AllEntriesDrained`, `AddLog_ConcurrentWriters_NoExceptionsThrown`) spin 8–10 threads against a single `InteractionPanel` and verify neither data corruption nor exceptions occur.

---

### Q2: Did you identify edge cases related to Raylib lifecycle shutdown or orphaned dependencies while implementing `IosMock.cs`?

Several edge cases were identified and mitigated:

1. **Dispose ordering in `IosMock`**: `IosMock` disposes only the `IosLogic` instance (which it owns) via `Dispose`. The five panels are injected and are **not disposed by `IosMock`** — they must outlive it or be disposed by the outermost owner (`Program.cs`). This is documented explicitly in the XML doc. Reversing the dispose order (disposing panels before `IosLogic` while `IosLogic` still holds a reference to `InteractionPanel`) would cause a use-after-free on the next `DrainPendingLogs` call.

2. **`ThrowIfDisposed` in both `Update` and `DrawUI`**: Both methods guard against post-dispose calls with `ObjectDisposedException`. This matters because Raylib's `WindowShouldClose` poll loop could deliver one final frame after the user closes the window before the C# `while` check exits, theoretically calling `Update` after `Dispose`. The guard makes this scenario visible rather than silent.

3. **`rlImGui.Shutdown()` must precede `Raylib.CloseWindow()`**: The ImGui context holds GPU-allocated texture resources. `rlImGui.Shutdown()` frees those before the Raylib OpenGL context is destroyed. `Program.cs` calls them in the correct order: `rlImGui.Shutdown()` → `Raylib.CloseWindow()`.

4. **`rlImGui.Setup` takes a target FPS parameter**: Called as `rlImGui.Setup(TargetFps)` before the main loop. Omitting it (calling `Setup()` with no argument) causes rlImGui to use a default refresh rate that does not match Raylib's vsync, causing screen tearing.

5. **No `IosLogic.Update()` after `IosMock.Dispose()`**: `IosLogic.Dispose()` sets `_disposed = true` and `IosLogic.Update()` throws `ObjectDisposedException`. This is tested in both `IosLogicTests` and `IosMockTests`.

---

### Q3: The original spec lacks clarity around the lifetime of the UI Panels versus the `IosMock`. How did you wire their initialization contexts?

The design doc describes `IosMock` as the orchestrator but does not specify who constructs or owns the panels. The resolution:

**Panels are constructed by `Program.cs` and injected into `IosMock`.** Ownership belongs to `Program.cs`.

Rationale:
- Panels carry no mutable DDS state — they are pure presentation-logic objects. Their lifetime is naturally scoped to the application run, not to any single IosMock instance.
- If `IosMock` owned the panels it would prevent the pattern of unit-testing panels in isolation (a separate `new InteractionPanel()` per test) and would also prevent hot-reload scenarios where the application shell is re-created without losing panel scroll position or filter state.
- `IosMock` receives the five panel instances through its constructor and validates them for null. It **never calls `Dispose` on panels**. This is explicitly documented in the class XML doc.

The resulting call graph in `Program.cs`:

```
new InteractionPanel()  ──┐
new ConfigPanel()         │ injected into both IosLogic
new OrbatPanel()          │ and IosMock
new MissionPanel()        │
new SpawnerPanel()  ──────┘
        │
        ▼
new IosLogic(repo, ..., interactionPanel)   // holds only interactionPanel
        │
        ▼
new IosMock(logic, configPanel, orbatPanel, missionPanel, interactionPanel, spawnerPanel)
```

`IosLogic` holds a reference only to `InteractionPanel` (for `DrainPendingLogs`).
`IosMock` holds references to all five panels (for future `DrawUI` ImGui calls).

This separation means panels can be constructed with zero dependencies and immediately tested without any DDS infrastructure.

---

### Q4: Were there any network serialization disparities between `MapClickEvent` and the creation payloads structured?

Yes, two significant disparities were observed:

**1. `MapClickEvent` carries a `GeoPoint` but `CreateEntityRequest` requires a full `EntityDescriptorUnion` list.**

`MapClickEvent.Position` is a raw `GeoPoint { Latitude, Longitude, Altitude }`. The `CreateEntityRequest.InitialDescriptors` list expects an `EntityDescriptorUnion[]` containing at minimum:
- `EntityMaster` descriptor (`TkbType`, `NodeId`, `ForceIdentifier`)
- `WorldPos` descriptor (wrapping the same lat/lon/alt)

`IosLogic.BuildInitialDescriptors(MapClickEvent, long tkbType, eForceIdentifier)` bridges this gap by constructing both descriptors from the click's position and the placement-mode state (`PlacementType`, current `eForceIdentifier`). The `NodeId` is sourced from `_mapGroupId` (default 0, overridable via constructor).

**2. `MapClickEvent.InteractionContextId` is a `Guid`; `CreateEntityRequest.RequestId` is also a `Guid` but is a fresh one.**

The context ID is used only to validate that the click belongs to the current placement session. When a valid click is processed, a **new** `Guid` is generated for `CreateEntityRequest.RequestId` (the per-request correlation ID tracked by `IRequestTransactionManager`). The context ID is not forwarded into the request — these are orthogonal identifiers serving different purposes.

**3. Affiliation encoding divergence.**

`MapClickEvent` carries no affiliation field — the user's selected affiliation lives in `IosLogic.PlacementType` configuration state (set via `StartPlacementMode`). The `CreateEntityRequest.InitialDescriptors` `EntityMaster` descriptor carries the affiliation explicitly as `eForceIdentifier`. This means the spawning context (affiliation) is stateful on the IOS side rather than being encoded per-click, which matches the intent of the `MapInteractionConfig → toolConfig` design.

---

## Test Coverage Summary

| Test Class | Tests | Key Contracts Verified |
|---|---|---|
| `InteractionPanelTests` | 17 | DEBT-034 drain model; thread safety (8 concurrent writers); cap enforcement; order preservation; concurrent smoke test |
| `IosLogicTests` | 18 | `StartPlacementMode` context ID generation and JSON patch content; click drop on mismatched/empty context ID; click drop on zero placement type; valid click generates `CreateEntityRequest` with correct TkbType; `TransactionManager.TrackRequest` called; `CheckTimeouts` called each frame; `SelectEntity`/`OpenSpawner`/`ConsumeSpawnerRequest` state transitions; `SendConfigPatch` forwards JSON; dispose guard; interaction log drain |
| `IosMockTests` | 14 | `Update` smoke test; selected-entity propagation to `MissionPanel`; sequential `SelectEntity` propagation; `SpawnerRequested` cleared after `Update`; no-spurious-flag when spawner not open; dispose once/twice; `Update`/`DrawUI` post-dispose guard; `Logic` property identity |
| *(Previous batches)* | 123 | Unchanged; all pass |
| **Total** | **172 (all pass)** | |

---

## Debt Items Resolved

| ID | Sev | Description | Resolved In |
|---|---|---|---|
| IOS-DEBT-034 | P3 | `InteractionPanel.AddLog` thread safety via `ConcurrentQueue` drain model | IOS-BATCH-03 |

## Debt Items Added

None. Existing open items IOS-DEBT-029 through IOS-DEBT-033 remain unchanged.
