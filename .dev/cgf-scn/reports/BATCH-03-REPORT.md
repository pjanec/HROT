# BATCH-03 Report

**Batch:** BATCH-03
**Tasks:** TASK-C004
**Developer:** AI Agent
**Date:** 2026-04-24
**Status:** COMPLETE

---

## 1. Completion Summary

### New Files

| File | Description |
|------|-------------|
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | Two-pass staging extractor: Pass 1 allocates network IDs, Pass 2 extracts/filters/patches root entities and harvests child overrides |
| `Hrot/Subsystems/Hrot.SimHost.Tests/StagingEntityExtractorTests.cs` | 13 unit tests covering all 12 success conditions |

### Modified Files

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` | Added `public IReadOnlyList<IEntityScenarioTranslator> Translators` property (needed by extractor to obtain translator-consumed component masks) |

---

## 2. Implementation Notes

### StagingEntityExtractor

`StagingEntityExtractor` lives in `Hrot.CGF.Orchestration` namespace.  Key design points:

**Static exclusion mask** is built once in `BuildStaticMask()` using `GlobalComponentIds`
named constants for all 8 baseline entries (`LifecycleDescriptor`, `NetworkIdentity`,
`NetworkAuthority`, `DescriptorOwnership`, `TkbIdentity`, `GhostStateTracker`,
`NetworkOwnership`, `PendingNetworkAck`).

**Per-call exclusion mask** is computed in `Extract()` by struct-copying the static mask
and OR-ing in `translator.GetConsumedComponentsMask()` for each translator registered
with the provided `ScenarioSerializer`.  A secondary `childExclusionMask` additionally
excludes `PartMetadata` (its `ParentEntity` is a volatile ECS handle).

**Staging repo bootstrap** (`RegisterAllGlobalTypesInRepo`): An empty `EntityRepository`
is created per extraction call.  Before `Deserialize`, component tables are pre-created
by iterating `ComponentTypeRegistry.GetAllTypeIds()` and calling the internal
`RegisterUnmanagedComponent<T>()` (for value types) or `RegisterManagedComponentInternal<T>()`
(for class types) methods via cached `MethodInfo` objects.  A broad `catch (Exception)`
swallows constraint violations (e.g. structs containing managed references that fail
`where T : unmanaged`) and any other registration failure — such types cannot appear
in serialised scenario files.

Note: the narrower `catch (TargetInvocationException)` that was initially used missed
`ArgumentException` thrown directly by `MakeGenericMethod` when a type fails the
`unmanaged` constraint check.  The first such type in the registry would abort the
entire loop, leaving all subsequent types (including `SimTransform`) un-registered.
Fixing this to `catch (Exception)` resolved all 12 failing tests.

**Pass 1 (ID allocation)** iterates all active entities, checks `ComponentMask` for
`GlobalComponentIds.NetworkIdentity`, reads the old `NetworkIdentity.Value`, allocates
a new ID via `INetworkIdAllocator.AllocateId()`, and stores the mapping in `oldToNewMap`.

**Pass 2 (extraction)** iterates all active entities again:
- Entities WITH `PartMetadata` are treated as TKB structural children: their components
  are extracted into a child buffer keyed by `(parentEntity, InstanceId)`.
- Entities WITHOUT `PartMetadata` are root entities: `TkbType` is read from
  `TkbIdentity.TkbType` (0 if missing); `PreAllocatedNetworkId` comes from `oldToNewMap`
  (0 if entity has no `NetworkIdentity`); components are extracted via
  `GetRegisteredComponentTypes()` / `GetRawObject`, filtered by the exclusion mask.
  `ScenarioBehaviorRemapper.RemapJson` is called for each `DomainMissionTask` in an
  `ActiveMissionPlan` (if present and a remapper was supplied).  `EpisodeTag` is
  appended last if `episodeId` is non-null.
- After the entity pass, child buffers are attached to matching root requests as
  `ChildComponentOverrides`.

**Disposal** happens in a `finally` block; `StagingRepositoryDisposedCallback` (an
`internal Action?`) is invoked after `Dispose()` to support test-time verification.

### Test Infrastructure

`StagingEntityExtractorTests` shares the namespace `Hrot.SimHost.Tests` to access
`StubIdAllocator` from `CreateEntityRequestSystemTests`.  The fixture constructor
registers all component types on a shared gold `EntityRepository` so they are present
in `ComponentTypeRegistry` when `FdpAutoSerializer.Build()` runs during `BuildSerializer()`.

Two private helper translators are defined inline:
- `MissionPlanTranslator` — round-trips `ActiveMissionPlan` via a plain JSON string DOM key.
- `ConsumeOneBitTranslator` — marks an arbitrary component-type bit as consumed to verify
  translator-consumed exclusion logic.

---

## 3. Test Results

### Hrot.SimHost.Tests (TASK-C004 + full suite regression)

```
Passed!  - Failed: 0, Passed: 391, Skipped: 3, Total: 394
```

- 13 new `StagingEntityExtractorTests` added and passing
- All 3 pre-existing skips are unrelated network-adapter registration tests
- No regressions in the 378 tests that existed before this batch

### StagingEntityExtractorTests (all 13 pass)

| # | Test Name | Status |
|---|-----------|--------|
| 1 | `Extract_SingleRootEntity_ReturnsSingleRequestWithCorrectTkbType` | Passed |
| 2 | `Extract_EntityWithPartMetadata_IsFilteredOutFromResults` | Passed |
| 3a | `Extract_TwoEntitiesNeitherHasPartMetadata_BothExtracted` | Passed |
| 3b | `Extract_WithEpisodeId_AppendsEpisodeTagToComponents` | Passed |
| 4 | `Extract_WithBehaviorRemapper_ReplacesNetworkIdInBehaviorParams` | Passed |
| 5a | `Extract_EntityWithoutNetworkIdentity_NoExceptionReturnsSingleRequest` | Passed |
| 5b | `Extract_EntityWithoutNetworkIdentity_PreAllocatedNetworkIdIsZero` | Passed |
| 6 | `Extract_TranslatorConsumedComponent_IsExcludedFromInitialComponents` | Passed |
| 7 | `Extract_Always_DisposesStagingRepository` | Passed |
| 8 | `Extract_EntityWithNetworkIdentity_SetsPreAllocatedNetworkId` | Passed |
| 9 | `Extract_EntityWithoutNetworkIdentity_PreAllocatedNetworkIdIsZero` | Passed |
| 10 | `Extract_WithChildEntity_PopulatesChildComponentOverrides` | Passed |
| 11 | `Extract_RootEntityWithNoChildren_ChildComponentOverridesIsNull` | Passed |
| 12 | `Extract_ChildWithNetworkIdentity_CarriesPreAllocatedIdToOverrides` | Passed |

### Full Solution Build

```
Build succeeded.
    0 Error(s)
```

---

## 4. Success Conditions Checklist

All 12 success conditions from TASK-C004 are satisfied:

- [x] SC1 — Basic extraction: single root entity, correct TkbType, excluded components absent
- [x] SC2 — TKB structural child filtered out (PartMetadata check)
- [x] SC3 — ORBAT subordinates NOT filtered (CommanderId irrelevant)
- [x] SC3b — Episode tag appended last to InitialComponents
- [x] SC4 — BehaviorParams network-ID remapping via ScenarioBehaviorRemapper
- [x] SC5 — Entities without NetworkIdentity: no exception, single request returned
- [x] SC6 — Translator-consumed components excluded from InitialComponents
- [x] SC7 — Staging repository disposed exactly once after extraction
- [x] SC8 — PreAllocatedNetworkId correctly set from Pass 1 allocation
- [x] SC9 — Entity without NetworkIdentity has PreAllocatedNetworkId == 0
- [x] SC10 — ChildComponentOverrides populated with child components and pre-allocated ID
- [x] SC11 — ChildComponentOverrides is null when root has no PartMetadata children
- [x] SC12 — Child pre-allocated ID carried through to ChildComponentOverrides.PreAllocatedId

---

## 5. Technical Debt

None introduced.  The `RegisterAllGlobalTypesInRepo` helper uses two cached `MethodInfo`
objects (`s_registerUnmanagedMethod`, `s_registerManagedInternalMethod`) that rely on
the internal methods `RegisterUnmanagedComponent<T>` and `RegisterManagedComponentInternal<T>`
retaining those names.  This is documented in the method's XML comment.
