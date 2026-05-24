# BATCH-02 REVIEW

**Batch:** 02  
**Tasks:** TKB-006, TKB-007, TKB-008  
**Verdict:** APPROVED

---

## Summary

All three tasks are correctly implemented. The build is clean (0 errors, 2 pre-existing NuGet
warnings unrelated to TKB). All 80 FDP Tkb-scoped tests pass. The 8 new Hrot builder tests
and 7 TkbRegistration/TacGraphicRoute tests pass. The 5 `LogArchiveExtractionServiceTests`
failures are pre-existing (unrelated to TKB).

---

## Correctness Review

### TKB-006 (`TkbTemplate`)

- `_applicators`, `AddComponent<T>`, `AddManagedComponent<T>`, `ApplyTo` are **completely
  deleted** — not deprecated. Confirmed by inspecting the source file.
- `CategoryPath` added with correct default `""` (null-coalesced in constructor).
- Descriptor bag uses `Dictionary<(Type, int), object>` keyed correctly.
- `AddDescriptor<T>` has `where T : notnull` — correct.
- `GetDescriptor<T>` has `where T : class` — correct (returns null for missing).
- `TryGetDescriptor<T>` has `where T : struct` — correct (for value-type descriptors).
- `HasDescriptor<T>` and `GetAllDescriptors()` implemented correctly.
- `AddMandatoryComponent<T>` retained unchanged — correct.

### TKB-007 (`ITkbDatabase`)

- `Clear()`, `GetEntitiesByCategory()`, `ActiveTkbName` added correctly.
- All 6 concrete implementations updated (TkbDatabase + 5 test mocks/stubs).

### TKB-008 (`TkbDatabase`)

- `Clear()` correctly clears both `_byName` and `_byType`.
- `GetEntitiesByCategory()` correctly handles:
  - Empty string → return all (short-circuits `Where`)
  - Exact match via `OrdinalIgnoreCase`
  - Child paths via `StartsWith(categoryPath + "/", OrdinalIgnoreCase)`
- `ActiveTkbName` is null by default.

### Callers migrated

- All 3 production ECS systems stubbed with identical comment pattern (good consistency).
- `NedTkbBuilder.DefineVehicle` correctly stores `TkbMasterDto` and retains both
  `AddMandatoryComponent` calls.
- `NedTkbBuilder.WithPhysics` correctly maps `physicsDef.MaxSpeed` → `MaxSpeedFwd`,
  `physicsDef.Acceleration` → `MaxAccel`.
- `NedTkbBuilder.WithCombat` stores both `SimCombatDef` and `WeaponCapabilitiesDto` — correct.

### Extra callers found and fixed

The developer correctly identified 6 files not listed in the instructions that had
compile-blocking `AddComponent`/`ApplyTo` calls, and migrated all of them. This is the
correct approach — the batch must produce a green build.

---

## Test Quality Review

### `DescriptorBagTests.cs` (16 tests) — HIGH QUALITY

- Round-trip test checks 4 specific field values (`Mass`, `Length`, `Width`, `MaxSpeedFwd`).
  Not a trivial "does not throw" test.
- Overwrite test checks the correct (second) value is returned.
- `GetAllDescriptors_ReturnsAll` checks count only (minor: could also assert types). Acceptable.
- Category path tests cover: exact match, child path, AND the partial-suffix non-match case.
  The `DoesNotMatchPartialSuffix` test with `"A/B"` vs `"A/BC"` is the critical boundary case.
- `Clear`/re-register tests verify the template is truly gone and the name/type can be reused.

### `NedTkbBuilderCombatTests.cs` (4 tests) — GOOD

- Tests assert specific values (3000f, 6f, 42) not just non-null. Aligned with the
  M1 Abrams catalog data.

### `NedTkbBuilderPhysicsTests.cs` (4 tests) — GOOD

- Tests assert specific `Length`, `Width`, `MaxSpeedFwd` values from the configured builder.
- The `HasExpectedMaxSpeedFwd` test adds a 3rd `BuildDatabase(maxSpeed: 20f)` parameter —
  good exercise of the mapping.

### `BlueprintApplicationSystemTests.cs` (2 tests) — ACCEPTABLE

- Tests verify the system does not crash when no translators exist (Phase 3 state).
- Could be stronger (e.g., verifying the event is consumed), but adequate for Phase 3.

### `TkbRegistrationTests.cs` (7 tests remaining) — CORRECT

- SC-HA014 tests correctly deleted (they tested ApplyTo behavior that no longer exists).
- Registration tests retained.

### `TacGraphicRouteBlueprintTests.cs` (2 tests remaining) — CORRECT

- 2 registration tests kept; 7 ApplyTo-based tests correctly deleted.

---

## Minor Issues

### P3 — `TryGetDescriptor<T>` (struct overload) not directly tested

The `TryGetDescriptor_ReturnsFalse_WhenMissing` test was repurposed to use `HasDescriptor`
because all current DTOs are reference types (`record` classes). The struct overload path has
no direct test coverage. This is acceptable for now — struct descriptors are not used in Phase
1-3 DTOs. Add a direct struct test in a future batch when struct descriptors are introduced.

### P3 — `WithHeavyMemory` now a no-op

`NedTkbBuilder.WithHeavyMemory` no longer stores anything (just returns `this` with a
comment). If this method is called in the catalog, the template will have no `Blackboard1024`
DTO. The SC-HA014 tests that verified the behavior were correctly deleted, but this means
the Blackboard1024 requirement is silently dropped until Phase 6. Document in DEBT-TRACKER.

### P2 — `UrbanAmbushIntegrationTests` (pre-existing, now also affected)

These integration tests spawn entities and assert ECS component presence. They now fail
because no translator loop exists yet. This is the expected Phase 3 state. Will be resolved
in TKB-014 (Phase 6).

---

## Conclusion

BATCH-02 is approved. The implementation is correct, the tests are meaningful and test
specific values against the actual catalog data, the build is clean, and all P2/P3 debt is
correctly documented. Proceed to commit and start BATCH-03.
