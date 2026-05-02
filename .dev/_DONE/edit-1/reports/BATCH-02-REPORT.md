# BATCH-02 Report

**Batch:** BATCH-02  
**Tasks:** EDIT1-P001, EDIT1-P002, EDIT1-P003  
**Date:** 2025-01-24  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| EDIT1-P001 | ✅ Complete | SpawnerPanel migrated; ISpawnController wired |
| EDIT1-P002 | ✅ Complete | MissionPanel migrated; IMissionEditorService + IMapPickService wired |
| EDIT1-P003 | ✅ Complete | ConfigPanel migrated; IMapConfigController wired; JSON construction removed |

---

## 🧪 Testing Results

**Unit Tests Passed:** 348 / 348  
**Integration Tests Passed:** 348 / 348 (combined run — includes panel unit tests, IosMock, MultiIos, TwoAck, Workflow, Integration suites)

**Key Test Scenarios Verified:**
- ✅ `SpawnerPanelTests` — HandleActivatePlacementTool, HandleStartAreaAuthoring, HandleStartRouteAuthoring delegate to ISpawnController
- ✅ `ConfigPanelTests` (14 tests) — ApplyConfig, LoadConfig, field defaults, partial-update immutability
- ✅ `MissionPanelTests` — HandleJump/Abort/Commit/ForceCommit, HandleConflictResult (any failure), DrawContent calls GetAvailableBehaviors before ImGui guard
- ✅ `MissionPanelDrawPendingTests` (3 new tests) — Draw with entity selected, Draw completes without exception, Draw with no selection
- ✅ `MultiIosIntegrationTests` — conflict detection, version error propagation, DismissConflict
- ✅ `TwoAckIosTests` — ACK lifecycle (InProgress → Success/Error), DismissAlert, IsEntityPending
- ✅ `IosMockTests`, `WorkflowTests`, `IntegrationTests`, `OrbatPanelTests` — no regressions

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**Cascading namespace errors across multiple files.**  
Moving the three panels out of `Hrot.ExCon.Panels` left 10+ files silently broken: `OrbatPanel.cs`,
`ExConMock.cs`, `ExConSubsystem.cs`, `ExConWindows.cs`, plus six test files
(`IosMockTests`, `MultiIosIntegrationTests`, `IntegrationTests`, `OrbatPanelTests`, `WorkflowTests`,
`TwoAckIosTests`).  Resolution: added `using Hrot.UI.Common.Panels;` to each consumer.

**`MissionCommitResult` type collision in `MultiIosIntegrationTests`.**  
The test file imports both `Hrot.ExCon.Services` (for the concrete service's `CommitMissionAsync`
return type) and `Hrot.UI.Common.Models` (for `HandleConflictResult`'s parameter).  Both assemblies
define `MissionCommitResult` with the same simple name.  Resolution: added
`using UiMissionResult = Hrot.UI.Common.Models.MissionCommitResult;` and converted all three
conflict-result call sites with `new UiMissionResult(result.Success, result.NewVersion, result.ErrorMessage)`.

**`PanelConstants` ambiguity in `MultiIosIntegrationTests`.**  
`VersionConflictErrorCode` exists only in `Hrot.ExCon.Panels.PanelConstants` (ExCon-specific fault
code `7`), while `VersionConflictErrorMessage` is the shared string in
`Hrot.UI.Common.Panels.PanelConstants`.  Resolution: two explicit aliases
(`ExConPanelConst` / `UiPanelConst`) disambiguate all five usages.

**`IExConLogic`-coupled window classes in `Hrot.ClusterRunner`.**  
`ExConWindows.cs` passed `IExConLogic` to `DrawContent` on all three migrated panels, which no
longer accept it.  The shims (`ExConMapConfigShim`, `ExConMissionShim`, `ExConMapPickShim`,
`ExConSpawnShim`) are `internal` to `Hrot.ExCon` and not visible from `Hrot.ClusterRunner`.
Resolution: exposed the shims through four new `public` properties on `ExConMock`
(`MapConfigShim`, `MissionShim`, `MapPickShim`, `SpawnShim`), updated the window constructors to
accept the port interfaces directly, and updated `ExConSubsystem.RegisterWindows` to pass `_mock.*Shim`
instead of `logic`.  Also added a direct `<ProjectReference>` to `Hrot.UI.Common` in
`Hrot.ClusterRunner.csproj` so the facade types resolve at compile time.

**`MissionPanelDrawPendingTests` tested removed behavior.**  
The old `Draw(IExConLogic)` overload and `IsEntityPending` guard were removed in the migration.
The three tests in `MissionPanelDrawPendingTests` verified `BeginDisabled` logic that no longer
exists.  Resolution: rewrote the class to verify the equivalent contract in the new API —
`GetAvailableBehaviors` is called every frame, and `Draw(svc, pick)` completes without exception
for both selected and unselected entity IDs.

**Duplicate `TestHook_ClearDraftAndDismissConflict`.**  
An `internal` version already existed on `MissionPanel`.  When a test called it from
`Hrot.ExCon.Tests` (which has no `InternalsVisibleTo` for `Hrot.UI.Common`), the call failed.
Resolution: changed the existing method's accessibility from `internal` to `public`.

---

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

**`ExConPanelAdapters.cs` is a Phase-1 liability.**  
All four shims (`ExConSpawnShim`, `ExConMapConfigShim`, `ExConMissionShim`, `ExConMapPickShim`)
adapt `IExConLogic` → Port interface by delegating one-to-one.  They exist because `ExConLogic`
itself wasn't changed to implement the port interfaces.  This is intentional (DESIGN.md §Phase 6),
but each shim is a manual synchronisation point that can drift.  If `IExConLogic` evolves its
delegation surface, the shims silently lag behind.

**`ExConMapConfigShim.GetCurrentConfig()` returns hard-coded defaults.**  
The current implementation returns `new MapLayerState(false, false, false, false)` rather than
reflecting live panel state.  Phase 6 must wire this through the real config reader.

**`ExConMissionShim.GetAvailableBehaviors()` always returns empty.**  
Populating the behavior dropdown requires `BehaviorRegistry` access, which is coupled to the
concrete `ExConLogic` in ways not yet bridged by the interface.  Panels will silently show an
empty behavior list in production until Phase 6 is implemented.

---

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

**`GetAvailableBehaviors` called before the ImGui guard in `DrawContent`.**  
The spec noted panels cannot be render-tested.  To make `DrawContent`-level tests possible without
spinning up a native ImGui context, the single external-world call
(`service.GetAvailableBehaviors(_selectedEntityId)`) was placed before the
`if (ImGui.GetCurrentContext() == IntPtr.Zero) return;` guard.  Alternative considered: a separate
`RefreshBehaviors()` method called from `Draw` — rejected as it exposes unnecessary surface area and
duplicates the call site.

**Port interfaces used directly in managed window constructors (not IExConLogic).**  
An alternative was to leave `ExConWindows.cs` accepting `IExConLogic` and call the shims inside
`DrawClientArea`.  This was rejected because it would require `Hrot.ClusterRunner` to have
visibility of `internal ExCon*Shim` types, or would require ExConWindows to re-implement the shim
logic.  Using typed port interfaces in the constructors is cleaner and future-proof: Phase 6 can
swap the shim arguments for real implementations transparently.

**`TestHook_ClearDraftAndDismissConflict` made `public` rather than using `InternalsVisibleTo`.**  
Adding an `InternalsVisibleTo` assembly attribute to `Hrot.UI.Common.csproj` would create a
test-assembly coupling at the project level.  A single `public` test-hook method is cheaper and
consistent with the rest of MissionPanel's test surface (`TestHook_PollCommitCompletion` was already
internal for the same reason).

---

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

**`TkbCatalogEntry` used in `ExConSubsystem.cs` as a concrete array type.**  
The type moved from `Hrot.ExCon.Panels` to `Hrot.UI.Common.Panels`, but `ExConSubsystem.cs`
allocated `new TkbCatalogEntry[]` directly when building the spawner catalog.  Adding
`using Hrot.UI.Common.Panels;` toPanelConstants `ExConSubsystem.cs` resolved it; the `OrbatPanel(IEnumerable<TkbCatalogEntry>?)`
constructor picked up the correct type automatically.

**`ExConSubsystem.cs` had `using Hrot.ExCon.Panels;` which previously resolved `ConfigPanel`,
`MissionPanel`, `SpawnerPanel`.**  This import became inert after migration but its removal would
break `OrbatPanel`, `DiagnosticsPanel`, and `InteractionPanel` which still live in `Hrot.ExCon.Panels`.
The import was kept; `using Hrot.UI.Common.Panels;` was simply added alongside it.

---

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

The highest BATCH-03 risk lies in the **remaining panels not yet migrated**:

- **`OrbatPanel`** — still uses `IExConLogic` directly for catalog display and drag-drop spawn.
  It accesses `logic.Repo` (DerRepo) for entity lookup and drive a full tree traversal per frame.
  Once migrated it will need the `IOrganizationController` port (or equivalent) which doesn't exist
  yet in `Hrot.UI.Common.Facades`.

- **`InteractionPanel`** / **`DiagnosticsPanel`** — both are ExCon-internal diagnostic views that
  expose `ExConLogic` internals (message queues, pending-entity counts).  Migrating them requires
  either accepting `IExConLogic` as-is (temporary) or defining new narrow diagnostic ports.

- **`SharedOrbatPanel` drag-drop** — if Phase 2 introduces any panel doing drag-and-drop by overlaying
  ImGui hit-rects with native window coordinates, the headless-test pattern used here (call service
  before the ImGui guard) breaks down; those panels need an explicit `IsDragging` state-machine
  interface instead.

---

## ⚠️ Outstanding Issues / Next Steps

- Phase-1 shims are temporary — Phase 6 should replace `ExConPanelAdapters.cs` with real bridge implementations inside `ExConLogic`.
- `ExConMapConfigShim.GetCurrentConfig()` returns hard-coded defaults; will need real state reader in Phase 6.
- `ExConMissionShim.GetAvailableBehaviors()` always returns empty; behavior registry access needs bridging.
- `OrbatPanel`, `InteractionPanel`, `DiagnosticsPanel` are still ExCon-internal and rely on `IExConLogic`; scheduled for BATCH-03+.
