# BATCH-04 Review

**Batch:** BATCH-04 — Phase 2 Part A (Fake backends)
**Tasks covered:** Debt-02, NAV-P2-T1, NAV-P2-T3, NAV-P2-T4
**Decision:** APPROVED (with dev-lead fix applied before commit)

---

## Test Results (after fix)

| Suite | Tests | Result |
|-------|-------|--------|
| NavigationTestWorldFactoryTests (Debt-02) | 1 | Pass |
| PathRegistryTests — Muscle/Brain/Shared (T4) | 14 | Pass |
| FakeNavmeshProviderTests (T1) | 8 | Pass |
| FakeVolumetricPathProviderTests (T3) | 6 | Pass |
| NavigationExecutionSystemTests (pre-existing) | 8 | Pass (regression fixed) |
| **Total Navigation filter** | **125** | **0 failures** |
| Build | -- | 0 errors |

---

## P1 Regression Found and Fixed by Dev Lead

### Root Cause

`NavigationContractsComponentIds` used `const byte` values 69-73 for the five
nav v2 components (`NavAgentProfile`, `NavigationCorridorMuscle`,
`NavigationCorridorPreview`, `NavigationPathDetailsBuffer`, `CrowdAgent`).
These IDs 69-73 are **all already occupied** in `GlobalComponentIds`:
- 69: `FrustrationTicks`
- 70: `InFormationTag`
- 71: `Faction` (obsolete)
- 72: `PerceptionReceptor`
- 73: `TargetMemory`

The collision was latent in BATCH-03 because `NavigationTestWorldFactory.Create()`
did not register the nav v2 components. BATCH-04 Debt-02 added those registrations,
which put `NavAgentProfile` (id=69) into the static `ComponentTypeRegistry`. Any
subsequent test registering `FrustrationTicks` (also id=69) then threw
`InvalidOperationException: Component ID collision`.

This caused 8 `NavigationExecutionSystemTests` to fail when run together with the
Navigation-filter test suite (they pass in isolation because the registry state
persists across tests within a process).

### Fix Applied

Changed all five nav v2 IDs to the extended 256-511 block (ecs-512-comps):

```
NavAgentProfile                = 257
NavigationCorridorMuscle       = 258
NavigationCorridorPreview      = 259
NavigationPathDetailsBuffer    = 260
CrowdAgent                     = 261
```

Also changed constants from `byte` to `int` (required for values > 255), and
updated `NavigationContractsTests` to check the `256-511` range instead of `69-79`.

### Files changed by dev-lead fix

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Navigation/NavigationContractsComponentIds.cs` | IDs 257-261, type byte→int |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationContractsTests.cs` | Range test updated, byte[]→int[] |

---

## Weak Points (P3 — deferred to BATCH-05+)

1. **BrainPathRegistry entity-agnostic methods skip ReplanCount**: `IsCached(int)`,
   `TryGetSummary`, `TryGetWaypointsSlice` do a linear scan without staleness check.
   Correct behavior deferred to Phase 4 (documented with TODO comments in code).

2. **FakeVolumetricPathProvider grid A* ignores altitude**: Only XZ plane. Sufficient
   for current tests; document as known limitation.

3. **FakeNavmeshProvider skips intermediate centroid for the final polygon hop**:
   Two-polygon paths emit only start+end waypoints. Correct for the common case.

4. **NavFakeIds block 250-256 has latent collisions**: `FakeBrainPathCacheEntry=255`
   conflicts with `VehicleColor` in `Fdp.Examples.CarKinem`. Only manifests if both
   assemblies register those components in the same process (currently no test does).
   Track as Debt-05.

---

## Developer Report Note

The report stated "24 pre-existing failures (unrelated)" and "87 passed before."
This was incorrect — the 8 `NavigationExecutionSystemTests` failures were new
regressions introduced by Debt-02. The developer likely compared against a run that
excluded the CarKinem tests, masking the collision. No other issues with the work.

---

## Approved

BATCH-04 work is correct. The P1 regression was introduced by a ComponentId
allocation error in BATCH-03 (now corrected). All 125 Navigation-filter tests pass.
