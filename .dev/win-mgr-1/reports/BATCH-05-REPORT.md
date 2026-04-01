# BATCH-05 Report — Phase 6 + Phase 7 Complete

**Batch:** BATCH-05  
**Status:** ✅ COMPLETE  
**Date:** 2026-04-01  
**Tasks covered:** WM-S601, WM-S602, WM-S603, WM-S701, WM-S702, WM-S703

---

## Summary

All tasks implemented and verified. No regressions introduced.

| Build / Test Target | Result |
|---|---|
| `FDP.Toolkit.ImGui` build | ✅ 0 errors |
| `FDP.Toolkit.ImGui.Tests` | ✅ **152 passed** (143 baseline + 9 new) |
| `Hrot.Common` build | ✅ 0 errors |
| `Hrot.ClusterRunner` build | ✅ 0 errors |
| `Hrot.ClusterRunner.Tests` | ✅ **182 passed**, 6 pre-existing failures unchanged |

---

## Task Results

### WM-S601: `StatusBarManager` — Delegate Registry + Sorted Render Loop ✅

**File created:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/StatusBarManager.cs`

Implemented:
- `RegisterSection(id, sortOrder, renderDelegate)` with null-guard (`ArgumentNullException`) and last-write-wins duplicate replace.
- Deferred sort via `_needsSort` flag — sort only triggered on `Render()` when dirty.
- `Render()` computes `Height = Gui.GetFrameHeight() + Gui.GetStyle().WindowPadding.Y * 2f` and positions the bar at the bottom of the main viewport.
- `ImGuiWindowFlags.NoDecoration | NoDocking | NoSavedSettings | NoFocusOnAppearing | NoNav | NoMove` flags.
- Separator: `Gui.Text("|")` between sections — `SeparatorEx(ImGuiSeparatorFlags.Vertical)` is **not exposed** in ImGui.NET 1.91.0.1 bindings (internal ImGui function). Used `Text("|")` as the standard fallback.

**Tests:** `FDP.Toolkit.ImGui.Tests/WindowManager/StatusBarManagerTests.cs` — 9 tests covering all 9 success conditions.

---

### WM-S602: `WindowManager.StatusBar` Property + Integration ✅

**File modified:** `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs`

Changes:
1. Removed `public float StatusBarHeight => 0f;` stub.
2. Added `private readonly StatusBarManager _statusBar = new();` and `public StatusBarManager StatusBar => _statusBar;`.
3. Added `_statusBar.Render();` call LAST in `Render()`, after the `foreach window` loop.

**File modified:** `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs`

Changed `_windowManager?.StatusBarHeight ?? 0f` → `_windowManager?.StatusBar.Height ?? 0f`.

---

### WM-S603: Reference Section Registration in `Hrot.ClusterRunner` ✅

**File modified:** `Hrot.ClusterRunner/Program.cs`

After `orchestrator.Initialize()`, registered:
```csharp
windowManager.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
{
    ImGuiNET.ImGui.Text("System OK");
});
```

---

### WM-S701: `TogglePerspectiveEvent` Record ✅

**Already present** from BATCH-04 at `Hrot.Common/Events/TogglePerspectiveEvent.cs`. Verified correct signature:
```csharp
public record TogglePerspectiveEvent(string OldPerspective, string NewPerspective);
```
Tests already existed in `Hrot.ClusterRunner.Tests/TogglePerspectiveEventTests.cs` — all passing. Added additional coverage in `PerspectiveCoordinatorSystemTests.cs`.

---

### WM-S702: `ActivePerspective` Singleton ECS Component ✅

**File created:** `Hrot.Common/Components/ActivePerspective.cs`

**Design deviation:** Implemented as `sealed class` rather than `struct` because `string Name` makes it a managed type, incompatible with `SetSingletonUnmanaged<T>` (`T : unmanaged` constraint). Using `SetSingletonManaged<ActivePerspective>` / `GetSingletonManaged<ActivePerspective>` instead.

```csharp
public sealed class ActivePerspective
{
    public string Name { get; set; } = string.Empty;
}
```

**Tests:** 4 tests in `PerspectiveCoordinatorSystemTests.cs` verifying default value, set/get, sealed, and class (not struct) constraints.

---

### WM-S703: `PerspectiveCoordinatorSystem` ✅

**File created:** `Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs`

Implemented as a standalone class (not extending `ComponentSystem`) with:
- `ConcurrentQueue<TogglePerspectiveEvent> _queue` — thread-safe, populated from UI thread via `Enqueue()`.
- `ProcessPendingEvents()` — drains queue, calls `orchestrator.SwitchMapOwner(subsystemName)` for known perspectives, always updates `CurrentPerspective`.
- Unknown perspectives: no orchestrator call, `CurrentPerspective` still updated.

**File created:** `Hrot.ClusterRunner/Services/PerspectiveUpdateSubsystem.cs`

Thin `ISubsystem` wrapper with `internal PerspectiveCoordinatorSystem? Coordinator { get; set; }`. Coordinator is assigned after `orchestrator.Initialize()` (deferred injection pattern) — necessary because coordinator requires orchestrator reference. `Update(dt)` calls `Coordinator?.ProcessPendingEvents()` — no-op when null.

**File modified:** `Hrot.ClusterRunner/Program.cs`

Wiring:
1. `PerspectiveUpdateSubsystem perspSubsystem = new()` added as **first subsystem** in the list.
2. After `orchestrator.Initialize()`: coordinator created with perspective→subsystem name map and `perspSubsystem.Coordinator = coordinator`.
3. `windowManager.OnPerspectiveChanged` now calls `coordinator.Enqueue(new TogglePerspectiveEvent(...))` (replacing the stub Console.WriteLine TODO).
4. Status bar section registration also wired here.

**Tests:** `Hrot.ClusterRunner.Tests/PerspectiveCoordinatorSystemTests.cs` — 11 tests covering all WM-S703 conditions plus WM-S701/S702 re-verification.

---

## Implementation Notes & Deviations

1. **`SeparatorEx` not available:** ImGui.NET 1.91.0.1 does not expose `ImGuiNET.ImGui.SeparatorEx()` or the `ImGuiSeparatorFlags` enum (these are internal ImGui functions). Used `Gui.Text("|")` as the visual separator. Functionally equivalent for a status bar context.

2. **`ActivePerspective` as managed class:** Per batch instructions, `struct` is incompatible with `SetSingletonManaged<T>` due to the `string Name` field making the type managed (not unmanaged). Using `sealed class` is correct.

3. **PerspectiveUpdateSubsystem deferred injection:** The subsystem must be the first in the list (registered before orchestrator construction), but the coordinator needs the orchestrator reference (available only after construction). The `Coordinator` property setter pattern resolves this cleanly without requiring a factory or lazy wrapper.

4. **Pre-existing test failures (6 total):**
   - `RunnerConfigurationTests`: 3 CGF/All-mode flag tests — failing since before BATCH-05 (RunMode.CGF was added without updating test assertions).
   - `WaitingRoomCoordinatorTests.WaitForPeers_AllPeersPresent_ReturnsSuccessfully` — DDS timing issue, pre-existing (5 s timeout).
   - `OrchestratorSubsystemTests.Initialize_SysOpWriter_IsDiscoverableOnDomain` — DDS pre-existing.
   - `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent` — DDS pre-existing.
   All 6 confirmed pre-existing by stash comparison. **BATCH-05 introduced zero regressions.**

---

## Files Created / Modified

| File | Action |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/StatusBarManager.cs` | Created |
| `FDP/Toolkits/FDP.Toolkit.ImGui/WindowManager/WindowManager.cs` | Modified (WM-S602) |
| `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/WindowManager/StatusBarManagerTests.cs` | Created |
| `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` | Modified (StatusBar.Height ref) |
| `Hrot.Common/Components/ActivePerspective.cs` | Created |
| `Hrot.ClusterRunner/Systems/PerspectiveCoordinatorSystem.cs` | Created |
| `Hrot.ClusterRunner/Services/PerspectiveUpdateSubsystem.cs` | Created |
| `Hrot.ClusterRunner/Tests/PerspectiveCoordinatorSystemTests.cs` | Created |
| `Hrot.ClusterRunner/Tests/Mocks/MockMapSubsystem.cs` | Deleted (MapCameraSubsystemMock exists) |
| `Hrot.ClusterRunner/Program.cs` | Modified (WM-S603, WM-S703 wiring) |
