# BATCH-06 Review

**Batch:** BATCH-06  
**Tasks:** PACK2-U001, PACK2-U002, PACK2-U003  
**Reviewer:** GitHub Copilot (dev-lead)  
**Decision:** ✅ APPROVED

---

## Build Verification

| Check | Result |
|-------|--------|
| `dotnet build IOS-IG-SimHost.sln --no-incremental` | ✅ 0 errors, 337 warnings (+1 xUnit1031 pre-existing pattern) |

---

## Test Verification

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.IG.Tests` | 408 pass, 7 fail | 410 pass, 7 fail | +2 (audit + MiniExConPanel) |
| `Hrot.ExCon.Tests` | 347 pass | 350 pass | +3 (audit + boundary tests) |

All pre-existing 7 IG.Tests failures unchanged.

---

## Scope Check

### PACK2-U001 — UI-Logic Separation Audit ✅
- Reflection-based DDS writer field check for `Hrot.IG.UI.*` in `Hrot.IG.Tests/UiLogicSeparationAuditTests.cs`
- Reflection-based DDS writer field check for `Hrot.ExCon.Panels.*` in `Hrot.ExCon.Tests/UiLogicSeparationAuditTests.cs` (separate file because `Hrot.IG.Tests` doesn't reference `Hrot.ExCon`)
- Both tests pass → zero violations confirmed

### PACK2-U002 — Formalize ExCon UI Pack ✅
- `Hrot.ExCon.Tests/ExConUiPackBoundaryTests.cs` added:
  - `ExConPanels_DoNotReferenceToolTypes` — confirms no CreationTool/EditTool fields in ExCon panels
  - `OrbatPanel_HandleNewUnitClick_DelegatesToIExConLogic` — acknowledges existing coverage
- `OrbatPanel.HandleNewUnitClick` → `IExConLogic.StartPlacementMode` delegation already tested in `OrbatPanelTests.cs`

### PACK2-U003 — Formalize IG UI Pack ✅
- `MiniExConPanelStateTests.Submit_FiresOnCommandPublishedEvent` added — verifies `OnCommandPublished` event fires with correct TkbType
- Compile-time clean covered by U001 audit test
- `IgDebugPanel` + `PerformanceOverlay` DDS-clean confirmed by audit test; existing `PerformanceMetricsTests.cs` already covered the metrics API

---

## Quality Notes

- `Hrot.ExCon.Logic` namespace correction (`IExConLogic` is actually in `Hrot.ExCon`) — dev caught this before test failure
- `SpawnEntityCommand` is a struct → `captured!.Value.TkbType` — correctly fixed
- `DebugPanelState` default test skipped — constructor requires `MapUserConfig`; existing `DebugPanelStateTests.cs` covers the relevant behaviour
- No DDS violations found in any of the 16 panel files — Phase 3 clean state confirmed

---

## Suggested Commit Message

```
feat(packs-2): PACK2-U001 + PACK2-U002 + PACK2-U003 -- UI-Logic Separation Audit

U001: Reflection-based audit tests confirm no DDS writer fields in IG or ExCon panels
U002: ExConUiPackBoundaryTests confirm panels don't construct tool types directly
U003: MiniExConPanel OnCommandPublished test; compile-time clean confirmed

Tests: 410/417 IG (7 pre-existing), 350/350 ExCon
```
