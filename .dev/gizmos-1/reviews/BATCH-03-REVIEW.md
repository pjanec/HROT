# BATCH-03 Review

**Batch:** BATCH-03
**Reviewer:** Dev Lead
**Decision:** APPROVED

---

## Summary

Build clean, 95/95 gizmo tests pass (74 prior + 21 new settings tests). All Phase 3 success
conditions are covered. One additive deviation: `IsRegistered(uint)` internal method added to
support `LoadOverrides` forward-compat logic.

---

## Test Quality Review

### Coverage breadth

All TASK-DETAIL success conditions exercised:

- **SC-GZ007-1:** `RegisterSetting("NavMesh.ShowGrid", From(false))` then `Read(hash)` returns `BoolValue == false`.
- **SC-GZ007-2:** `Write(hash, From(true))` then `Read(hash).BoolValue == true`.
- **SC-GZ007-3:** `Write(hash, From(5.0f))` then `ResetToDefault(hash)` restores `1.0f`.
- **SC-GZ007-4:** `Read(0xDEADBEEFu)` returns `default(GizmoSettingValue)`, no exception.
- **SC-GZ007-5:** Two distinct keys (`KeyAlpha`, `KeyBeta`); write to A does not affect B.
- **SC-GZ007-6:** `From(3.14f)` — `Type == SettingType.Float32`, `FloatValue == 3.14f`.
- **SC-GZ007-7:** `Marshal.SizeOf<GizmoSettingValue>() == 8`.
- **SC-GZ008-1:** Save then load into fresh registry; `Read(hash).BoolValue == true`.
- **SC-GZ008-2:** Default-value setting absent from JSON file.
- **SC-GZ008-3:** Missing file path does not throw.
- **SC-GZ008-4:** `Write(hash, value, ecb)` → playback → SwapBuffers → `ReadEvents<GizmoSettingChangedEvent>()` yields one event with correct `KeyHash`.
- **SC-GZ008-5:** `Write` then `ResetToDefault` then `SaveOverrides` — JSON does not contain the key.

### Test depth — specific observations

**SC-GZ008-4 (event round-trip via command buffer):** Uses `new EntityCommandBuffer()` (default
capacity), calls `ecb.Playback(repo)`, then `repo.Bus.SwapBuffers()`, then
`ReadEvents<GizmoSettingChangedEvent>()`. Asserts `events.Length == 1` and `events[0].KeyHash == hash`.
This is the correct end-to-end path that mirrors production usage.

**SC-GZ008-2 and SC-GZ008-5 (default suppression):** Both check `File.ReadAllText(file)` directly
and call `Assert.DoesNotContain(key, json)`. Robust: no parsing needed, catches any formatting
variant that might accidentally include the key.

**IsDirty transitions:** Dedicated test checks: false after `RegisterSetting`, true after `Write`,
false after `ResetToDefault`. `SaveOverrides` also tested to clear `IsDirty`.

**Equality tests:** `From(1) != From(1.0f)` correctly distinguishes by `Type` tag despite both
having the same 4-byte payload value.

**`OnSettingChanged` event test:** Verifies exactly 1 invocation per `Write`, with the correct
`uint` hash argument.

**Temp file cleanup:** All persistence tests use `Path.GetTempFileName()` with `finally { File.Delete(file); }` blocks — correct resource management.

### Test count breakdown

| Class | Tests |
|-------|-------|
| GizmoSettingValueTests | 7 |
| GizmoSettingsRegistryTests | 7 |
| GizmoSettingsPersistenceTests | 6 |
| GizmoSettingChangedEventTests | 1 |
| **New total** | **21** |

---

## Production Code Quality

- `GizmoSettingValue.Equals` compares `IntValue` overlay (covers all three payload types at offset 4) — correct.
- `GizmoSettingsRegistry.Write` does not allocate on the hot path — only updates dictionary entry, fires nullable delegate.
- `EnumerateAll` is correctly documented as cold path.
- `ClearDirty` is `internal` — only `GizmoSettingsPersistence` (same assembly) calls it.
- `IsRegistered` is `internal` — used only by `LoadOverrides` for forward-compat check.
- `GizmoSettingsPersistence` uses `CultureInfo.InvariantCulture` for float serialization — correct for locale-independent persistence.
- `LoadOverrides` calls `registry.Write(hash, parsedValue)` without a cmd — correct (no event on load, matches spec).

## Deviation Accepted

- `IsRegistered(uint)` internal method: minor additive; required by `LoadOverrides` for forward-compat
  registration of unknown keys. Spec-compliant — TASK-DETAIL specifies "call `RegisterSetting(key, default)` if key not yet registered".
- 21 tests instead of 20: one extra float persistence roundtrip test. Additive quality improvement.
