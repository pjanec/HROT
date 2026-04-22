# BATCH-08 Review

**Status:** APPROVED ✅  
**Reviewer:** Dev Lead  
**Tasks:** PACK2-F002 · PACK2-F003 · PACK2-F004

---

## Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Hrot.ScenarioEditor.Tests | 14 | 14 | 0 (no regressions) |
| Hrot.Editor.Tests | 9 | 15 | +6 ✅ |

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot.ScenarioEditor/Services/ScenarioFileService.cs` | Added `GlobalTime` singleton reset after `SoftClear()` in both `NewScenario()` and `LoadScenario()` |
| `Hrot.Editor.Tests/IntegrationTests/EditorFileOpsIntegrationTests.cs` | New — 6 integration tests for F002/F003/F004 |

---

## Deviations

- **`Entities` JSON format is an object not an array**: `ScenarioSerializer` serializes `Entities` as a JSON object (keyed by entity index), not as a JSON array. The F003 test was correctly adapted to use `entities.EnumerateObject().Count()` instead of `entities.GetArrayLength()`. This is a documentation deviation only — no design impact.

---

## Quality Assessment

- All 6 new integration tests exercise the full `IEditorLogic → ScenarioFileService → ScenarioSerializer → file system` chain.
- `GlobalTime` reset is correctly guarded with `HasSingletonUnmanaged<GlobalTime>()` — no throw when singleton was never registered.
- Round-trip fidelity verified for float values within `precision: 4`.
- Invalid SubsystemType guard verified: `InvalidOperationException` thrown before `SoftClear` → repo left empty.
- Cross-app compatibility verified: "Hrot.SimHost" header loads successfully.
- Test isolation: `ComponentTypeRegistry.Clear()` in ctor and `Dispose()`.

---

## Tasks Completed

- [x] PACK2-F002 — "Load Empty" integration test
- [x] PACK2-F003 — "Save Scenario" round-trip integration test
- [x] PACK2-F004 — "Load Scenario" round-trip integration test
