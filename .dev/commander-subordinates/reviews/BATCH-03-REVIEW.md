# BATCH-03 Review

**Status: APPROVED**

---

## Summary

All 6 tasks are complete. Build is clean (0 errors). 16+ new tests pass across SimHost, IG, and
Hrot.Map.Common test suites. No regressions introduced.

Three `Fdp.Presentation.Tests.EntityInspectorPanelTests` failures appeared in a full-solution run
but are pre-existing: `EntityInspectorPanel.GetFilteredEntities` was updated in a prior commit
(`f10213f feat: global singleton entity inspector`) to always include a `SingletonEntity`
pseudo-entity, but the tests were never updated to account for it. BATCH-03's FDP submodule
change (`EntityInfo.cs`) does not touch `EntityInspectorPanel` or its test file.

---

## Task Assessments

### CT-2 — Remove UnitHierarchySystem from CgfLogicPack

**APPROVED.**

Correct fix. `UnitHierarchySystem` remains in `SimHostCoreLogicPack` (covers SimHost and Editor).
`IgApplication` retains its own instance. `CgfLogicPack` count correctly restored to 16/18.
Stale XML comment in `UnitHierarchySystem.cs` updated.

### CS008 — TacticalDesignation field in DDS EntityInfo descriptor

**APPROVED.**

Field added at end of `Hrot.NED.Descriptors.EntityInfo` partial struct preserving wire order.

### CS009 — Remove CommanderId from Fdp.Core.EntityInfo

**APPROVED.**

All cascading compile fixes applied:
- `CreateEntityRequestSystem` child initializer cleaned.
- `SimHostScenarioManager` 6×`CommanderId = 0` lines removed.
- `DescriptorMapper` both dtEntityInfo usages removed.
- `EditorOrbatAdapter` reads `UnitSubordinate.Commander.Index` correctly.
- Test assertions in `AttributeCompilerFactoryTests` and `IgEntityDataTests` updated.
- `EditorOrbatAdapter` adapter tests updated to register `UnitSubordinate` and use new lookup.

### CS010 — EntityInfoEgressTranslator reads UnitSubordinate

**APPROVED.**

`IDdsWriter<T>` + testable constructor follows the established pattern (`WeaponFireNotificationEgressTranslator`, etc.). Authority guard and dirty-state gate both preserved. 3 new egress tests cover all required scenarios.

### CS011 — EntityInfoIngressTranslator with deferred queues

**APPROVED.**

The three-queue architecture (`_pendingSubordinates`, `_pendingUnspawnedSubordinates`,
`_recentlyRegistered`) correctly handles all ordering cases. Event signature
`(long netId, Entity entity)` matches `NetworkEntityMap.EntityRegistered` delegate. `Shutdown()`
properly unsubscribes. `CommanderId == 0` path guards against event-bus flooding by checking for
existing `UnitSubordinate`. 11 new tests cover all TASK-CS011 success conditions.

### CS023 — Component registry integration tests

**APPROVED.**

`UnitRoster` and `UnitSubordinate` table assertions added. Global uniqueness test added.

---

## Issues Found

No P1 issues. No corrective tasks required.

**P3 note:** `Fdp.Presentation.Tests.EntityInspectorPanelTests` (3 tests) are pre-existing
failures not caused by this batch. Will be tracked in DEBT-TRACKER. Developer did not touch
those files.

---

## DEBT-TRACKER update

| ID | Source | Description | Target |
|----|--------|-------------|--------|
| D-01 | BATCH-02 CT-2 | Remove UnitHierarchySystem from CgfLogicPack (P1) | BATCH-03 ✅ |
| D-02 | BATCH-03 | `EntityInspectorPanelTests` 3 pre-existing failures (P3) | Future |

---

## Next Steps

BATCH-04 will cover: CS013, CS014, CS026, CS027, CS017, CS018, CS019.
