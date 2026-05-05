# BATCH-03 Report — Gizmo Settings Store

**Tasks:** GZ007, GZ008
**Date:** 2026-05-06

---

## Files Created

1. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingValue.cs`
   - `SettingType` enum and `GizmoSettingValue` struct with explicit 8-byte layout.
   - `From(bool)`, `From(int)`, `From(float)` factory methods.
   - `IEquatable<GizmoSettingValue>` comparing `Type` + `IntValue` overlay.

2. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`
   - `RegisterSetting`, `Read`, `Write`, `ResetToDefault`, `ComputeHash`, `EnumerateAll`.
   - `IsDirty` bool flag, `OnSettingChanged` event, `ClearDirty()` / `IsRegistered()` internal helpers.

3. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingChangedEvent.cs`
   - `[EventId(8050)] struct GizmoSettingChangedEvent { uint KeyHash; }`

4. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsPersistence.cs`
   - `SaveOverrides`: writes only non-default values, calls `ClearDirty()` after save.
   - `LoadOverrides`: silently skips missing file; auto-registers unknown keys with `default`.
   - Uses `System.Text.Json` and `CultureInfo.InvariantCulture` for float formatting.

5. `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSettingsTests.cs`
   - `GizmoSettingValueTests` (7 tests): SC-GZ007-6, SC-GZ007-7, plus roundtrips and equality.
   - `GizmoSettingsRegistryTests` (7 tests): SC-GZ007-1 through SC-GZ007-5, dirty flag, event.
   - `GizmoSettingsPersistenceTests` (6 tests): SC-GZ008-1 through SC-GZ008-5, float roundtrip.
   - `GizmoSettingChangedEventTests` (1 test): SC-GZ008-4 via real `EntityCommandBuffer`.

---

## Test Results

```
Passed!  - Failed: 0, Passed: 95, Skipped: 0, Total: 95
```

- Prior gizmo tests: 74
- New settings tests: 21
- **Total: 95 — all pass, zero failures.**

---

## Design Deviations

1. **`IsRegistered(uint hash)` internal method added** — the spec implied checking whether a key is
   registered in `LoadOverrides` but gave no API for it. Added `internal bool IsRegistered(uint hash)`
   to `GizmoSettingsRegistry` (internal = same assembly, no public API surface change). Alternative
   was always calling `RegisterSetting`, which would have clobbered registered defaults.

2. **21 new tests instead of the implied 20** — one extra test `Float32_Roundtrip_Via_Persistence`
   was added to exercise the `Float32` persistence path end-to-end. This is an additive quality
   improvement with no impact on existing tests.

---

## Issues Encountered

None. Explicit-layout bool/int/float overlap at offset 4 compiled cleanly in net8.0. `System.Text.Json`
serializes the private `SettingRecord` inner class correctly without additional options.

## Design Decisions Beyond the Spec

- Used `IntValue` overlay in `Equals` rather than comparing raw bytes via `unsafe` to avoid
  unnecessary `unsafe` context in a managed-only file.
- `EnumerateAll` iterates `_keyNames` (not `_active`), ensuring only registered keys appear
  in enumeration even if `Write` was called for unregistered hashes (e.g., from `LoadOverrides`
  before `RegisterSetting`).
