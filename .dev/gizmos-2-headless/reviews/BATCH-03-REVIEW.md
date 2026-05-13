# BATCH-03 Review

## Tasks: DEBT-002 / GZH-014 / GZH-016
**Verdict: APPROVED**

---

## Test Runs (Dev-Lead verification)

| Suite | Filter | Result |
|---|---|---|
| Fdp.Toolkits.Tests | `FullyQualifiedName~Diagnostics.Gizmos` | 187/187 PASS |
| Hrot.SimHost.Tests | `GZH011\|DEBT` | 3/3 PASS |
| Hrot.ClusterRunner.Tests | `GZH014\|PerspectiveCoordinator` | 11/11 PASS |
| Hrot.IG.Tests | `GZH016` | 3/3 PASS |
| Hrot.Editor.Tests | `DEBT` | 1/1 PASS |

*Note: Hrot.SimHost.Tests/Hrot.IG.Tests binaries were stale due to a pre-existing CycloneDDS.Schema
"Question build" environment issue. Used `dotnet msbuild /p:BuildProjectReferences=false /t:Build`
to produce fresh binaries before running.*

*Pre-existing unrelated failures in AreaQueryBatchDataTests, HillAttackNodeTests,
SimHostEntityPresentationGizmoTests (SC_GZ057), ContextActionIngressSystemTests (SC_ER007) were
confirmed to be pre-existing and not caused by BATCH-03.*

---

## Code Review

### IGizmoControllable (Hrot.Common)
- Correct placement in `Hrot.Common.Diagnostics.Gizmos`; avoids circular dependency.
- Interface definition is minimal and correct.

### SubsystemConfig.IsActiveMapOwner (FDP)
- `Func<bool> IsActiveMapOwner { get; set; } = () => true;` — correct default (safe for single-subsystem scenarios).

### SubsystemOrchestrator.Initialize() (FDP)
- `var captured = subsystem; cfg.IsActiveMapOwner = () => _activeMapOwner == captured;` — closure capture
  is correct; `captured` avoids the classic foreach-closure variable-capture bug.

### PerspectiveCoordinatorSystem (GZH-014)
- Listener transfer in `ProcessPendingEvents()` is correct.
- Guard `TryGetValue(evt.NewPerspective, perspectiveMap)` wraps the entire block, so unknown
  perspectives skip listener transfer AND SwitchMapOwner — appropriate safe behaviour.
- Optional `gizmoControllables` parameter with null-coalescing default is clean.

### IgSubsystem / IgApplication (GZH-016)
- `IGizmoControllable` on `IgSubsystem`; `_isActiveMapOwner` propagated to inner `IgApplication` via setter.
- Input gate in `IgApplication.Update()`: `if (_isActiveMapOwner() && !ImGui.GetIO().WantCaptureMouse)` — correct.

### SimHostApp / SimHostSubsystem (DEBT-002)
- `_gizmoUiHub` inline-initialized; `GizmoUiHub` exposed `internal` (accessible to tests via InternalsVisibleTo).
- `LayerControlGizmo` ctor receives `_gizmoUiHub` as publisher — correct wiring.
- `IGizmoControllable` on `SimHostSubsystem` delegates to `_app?.GizmoController` — correct indirection.

### EditorSubsystem (DEBT-002 + GZH-014 + GZH-016)
- Same hub+gate pattern as IgApplication; `_isActiveMapOwner` set from `config.IsActiveMapOwner` in `Initialize()`.
- Input gate in `DrawUI()` canvas block correct.

### CgfSubsystem (GZH-014)
- Explicit interface implementation `IGizmoControllable.GizmoController => _cgfGizmoController` is the right
  approach since the existing property has a different name.

### Program.cs (GZH-014)
- `gizmoControllables` built via `OfType<IGizmoControllable>()` after orchestrator setup — correct.
- Keys match perspective names via `((ISubsystem)s).Name` — consistent with `perspectiveMap` keys.

---

## Test Quality Notes

### GZH014_1 — GOOD
Real `GizmoExecutionController` instances used; verifies listener count transitions (0→1→0) across
a perspective switch. Exercises the actual wiring end-to-end.

### GZH014_2 — ACCEPTABLE (comment corrected by dev lead)
Tests that no exception is thrown when new perspective is unknown. The original comment incorrectly
stated "RemoveListener fires for SubA" — this was corrected to reflect that the entire block is
skipped when new perspective is not in the map. The `Assert.Equal(0, ctrlA.ListenerCount)` is a
valid regression guard.

### GZH016_1/2/3 — ACCEPTABLE
Boolean predicate tests following the same pattern as the pre-existing `InputGate_WantCaptureMouse*`
tests in `IgApplicationPanelTests.cs`. The actual input-gating logic cannot be tested through ImGui
without a full Raylib context, so predicate composition tests are appropriate here.

### DEBT002_SimHost + DEBT002_Editor — ACCEPTABLE
`Assert.NotNull` on inline-initialized hub property. Provides a regression guard against the field
being removed or changed to a lazy/nullable pattern.

---

## Minor Fix Applied by Dev Lead
- Corrected misleading comment in `GZH014_2` test (wrong claim about RemoveListener firing).
