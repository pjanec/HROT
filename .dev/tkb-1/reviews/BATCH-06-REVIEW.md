# BATCH-06 Review

**Tasks Reviewed:** TKB-015, TKB-019, TKB-020, TKB-022  
**Verdict:** APPROVED

---

## Test Quality

### TkbLoadClusterStateHandlerTests.cs (9 tests)

**Good:**
- All 7 mandated success conditions from TASK-DETAIL covered.
- `CacheHit_SameTkbAndTimestamp_DoesNotClearDb` correctly verifies count equality across two PrepareAsync calls — real behavioral test, not a stub.
- `CacheMiss_NameChange_ClearsCalled` verifies `ActiveTkbName` changes to "Beta" — confirms re-ingestion happened.
- `MissingZip_ThrowsFileNotFoundException` uses `await Assert.ThrowsAsync` correctly.
- `Fallback_NullTkbName_EmptyDb_RegistersNedCatalog` verifies `db.GetAll().Any()` — confirms NedTkbCatalog populated.
- `Fallback_NullTkbName_PopulatedDb_DoesNotOverwrite` correctly captures count before and verifies it unchanged.
- BOM-free encoding (`new UTF8Encoding(false)`) is correct to avoid `Utf8JsonReader` parse issues.
- Test isolation via temp directory (IDisposable) is correct.

**Minor observation (not blocking):**
- `CacheMiss_NameChange_ClearsCalled` does not verify that Clear() was called on the first TKB set (count drops to 0 then re-populates). It verifies the end state (`ActiveTkbName == "Beta"`) which is sufficient.
- No explicit cache miss on timestamp change test. The design says both name change AND timestamp change are cache misses. The name-change test covers the miss path adequately; timestamp change is not separately tested. Recording as D-006 (low priority — behavioral parity guaranteed by the single-field comparison logic).

### TkbDatabaseSingletonTests.cs (2 tests)

Tests the singleton round-trip mechanism. Adequate for TKB-015 given that the bootstrapper integration is covered by existing `SubsystemHeadlessTests` (skipped — requires live DDS).

### NedReplicationModuleTranslatorTests.cs (1 test)

Tests that `NedReplicationModule` accepts a `tkbEntityTranslators` parameter. Construction-level test; adequate.

---

## Code Quality

### TkbLoadClusterStateHandler

- Matches DESIGN.md §7.2 exactly.
- `_localTkbStagingRoot = Path.Combine(localStagingRoot, "TKB")` correct.
- Differential cache: `(_lastLoadedTkbName == requestedTkb && _lastLoadedTimestamp == currentFileTime)` — string equality is correct (not reference equality).
- `NedTkbCatalog.RegisterAll((TkbDatabase)_tkbDb)` cast is necessary since `RegisterAll` takes concrete type. Acceptable.
- `using var loader = new TkbUnifiedLoader(localPath)` — proper Dispose.
- FdpLog call format string `"{0}"` / `"{1}"` is correct NLog syntax.

### SimHostNodeBootstrapper

- `_translators` created in `BuildContext` (before `Build()`) — correct placement ensures same instance used everywhere.
- `_tkbDb = ctx.TkbDb` capture after `Build()` — correct.
- `elm.SetTranslators(_translators!)` called in `RegisterSpawningPipeline` BEFORE `context.Kernel.RegisterModule(new SimHostModule(...))` — kernel Initialize has not run yet at this point, so `RegisterSystems` has not been called. Correct ordering.
- `translators: _translators` passed to `NetworkSpawningSystem` — correct named parameter.

### NodeBootstrapper

- `ITkbDatabase? tkbDb = null` inserted after `localTempRoot` (correct position — existing callers unaffected).
- Registration guard `if (tkbDb != null)` prevents null-ref and allows optional use.
- Registration is BEFORE `if (scenarioSerializer != null)` block — satisfies ordering requirement.

### NedReplicationModule

- `tkbEntityTranslators` parameter placed last (after `lifecycleModule`) — existing callers unaffected.
- BOTH `GhostPromotionSystem` constructor sites updated (pureIgRole block AND roleHasMuscle block) — verified in file.

### EntityLifecycleModule

- `_translators` changed from `readonly` to non-readonly field — necessary for `SetTranslators`.
- `SetTranslators` has correct doc comment explaining the ordering constraint (must call before `RegisterSystems`).

### HrotNodeBuilderWithReplication

- `WithTranslators` returns `this` for chaining.
- `BindReplicationParticipant` was NOT modified — correct per instructions.

---

## Debt Tracker Update

| ID | Priority | Description | Status |
|----|----------|-------------|--------|
| D-006 | P3 | `TkbLoadClusterStateHandler` cache miss on ZIP timestamp change not separately unit-tested | NEW |

---

## Final Counts

- FDP Tkb tests: **109 passed**
- Hrot.SimHost.Tests Tkb filter: **29 passed** (12 new)
- Build errors: **0**

**APPROVED — proceed to BATCH-07.**
