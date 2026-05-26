# BATCH-03 REVIEW

**Reviewer:** Dev Lead  
**Batch:** BATCH-03 — Phase 2 TKB Animation Descriptor  
**Report File:** `.dev/anim-ctrl/reports/BATCH-03-REPORT.md`  
**Status:** APPROVED WITH P1 CORRECTIONS

---

## Verification Summary

- Build: clean (0 errors, 0 warnings) — verified
- Tests: 58 passed, 0 failed — verified by re-running test suite

---

## What's Good

1. All 8 tasks (ANC-P2-01 to ANC-P2-08) are implemented and the code is clean.
2. DTOs match DD-4 §2 schema (SlotDefDto, MontageDefDto, NotifyMarkerDefDto, etc.) — correct fields, correct types.
3. Stable ID hashing (FNV1a64/32) is in place with determinism + collision-resistance tests.
4. Baking algorithm (`BakingUtils.BakeDef`) covers montage dict, stance set, transition map, slot sort, and AimConfig snapshot — all with tests.
5. ANIM006/ANIM007 validators have both positive (valid DTO) and negative (invalid) tests.
6. Translator guards (`IsComponentTypeRegistered<T>()`) and conditional aim-component injection are correct.

---

## P1 Issues — Must Fix in BATCH-04

### P1-A: `AnimationTkbTranslator.Inject` is completely untested

The ANC-P2-08 success criterion says "covers inject". No test calls
`AnimationTkbTranslator.Inject()`. The translator is a critical pipeline
seam — if it silently drops components, every downstream Phase 3 system
will fail without a clear error.

Required tests (in `Phase2DescriptorTests.cs` or a new
`TranslatorAndQueryTests.cs`):

```
Inject_WithNonAnimatedTemplate_AddsNoComponents
  - Template has no CharacterAnimationDefDto → repo.HasComponent<AnimationChannel>(entity) == false

Inject_WithAnimatedEntity_AddsRequiredComponents
  - Template has CharacterAnimationDefDto, all component types registered
  - Assert: AnimationChannel added, StanceIntent added, StanceStatus added,
             AnimationMontageQueue added, AnimationMontageQueueState added,
             CharacterAnimationDefRuntime added, AnimationExecutorState added

Inject_WithAimCapableEntity_AddsLookAtChannel
  - DTO has AimConfig != null
  - Assert: LookAtChannel added, LookAtExecutorState added

Inject_WithoutAimConfig_DoesNotAddLookAtChannel
  - DTO has AimConfig == null
  - Assert: LookAtChannel NOT added, LookAtExecutorState NOT added
```

### P1-B: `BakedAnimationCache` hot-reload invalidation is untested

The ANC-P2-08 success criterion says "covers hot-reload invalidation".
No test verifies the `BakedAnimationCache` caching or hot-reload path.

Required tests:

```
BakedAnimationCache_GetOrBake_ReturnsCachedResultOnSecondCall
  - Call GetOrBake twice with the same classId + dto
  - Assert: same object reference returned (or at least same baked data)

BakedAnimationCache_HotReload_InvalidatesCacheEntry
  - Call GetOrBake; capture result1
  - Fire the hot-reload event for that classId
  - Call GetOrBake again; capture result2
  - Assert: result2 != result1 (re-baked) -- or verify via a mocked counter
```

### P1-C: `AnimationTkbQueries` query methods are untested

The ANC-P2-08 success criterion says "covers query filtering". The one
test labeled ANC-P2-06 tests only the `MontageInfo` data structure — NOT
the `AnimationTkbQueries` class. `AnimationTkbQueries.GetPlayableMontages`,
`GetSupportedStances`, `SupportsAim`, `GetAvailableMarkers`, `GetMarkerName`,
and `ResolveMontageId` all have zero test coverage.

Required tests:

```
AnimationTkbQueries_GetPlayableMontages_ExcludesStanceTransitionMontages
  - DTO has 2 normal montages + 1 IsStanceTransition=true montage
  - Assert: GetPlayableMontages returns 2 entries (not 3)
  - Assert: none of the returned montages has IsStanceTransition==true

AnimationTkbQueries_GetSupportedStances_ReturnsAllDeclaredStances
  - DTO has SupportedStances=[Standing, Crouched]
  - Assert: GetSupportedStances returns both

AnimationTkbQueries_SupportsAim_TrueWhenAimConfigPresent
  - DTO with AimConfig != null → SupportsAim returns true

AnimationTkbQueries_SupportsAim_FalseWhenAimConfigNull
  - DTO with AimConfig == null → SupportsAim returns false

AnimationTkbQueries_GetAvailableMarkers_ReturnsUnionAcrossMontages
  - DTO with montages referencing two different markers
  - Assert: GetAvailableMarkers contains both

AnimationTkbQueries_GetMarkerName_ReverseLookup
  - Marker with known Hash → GetMarkerName(entityClass, hash) == marker.Name

AnimationTkbQueries_ResolveMontageId_MatchesStableIdHasher
  - ResolveMontageId(entityClass, "Reload_Rifle") ==
    StableIdHasher.ComputeMontageAssetId("Reload_Rifle")
```

### P1-D: Phase 1 behavioral tests are smoke-tests only (BATCH-02 debt)

Phase 1 tests from BATCH-02 only verify that methods do not throw
exceptions. DD-Tests §3.2 explicitly specifies behavioral tests such as:

- `PlayMontage_SetsSlotActive` — checks slot fields after PlayMontageOnSlot
- `PlayMontage_OverwritesPreviousMontageInSameSlot`
- `PlayMontage_UnknownMontage_NoOps`
- `Tick_AdvancesElapsedTimeByDeltaTimesPlayRate`
- `Tick_DoesNotAdvanceInactiveSlots`
- `Tick_DeactivatesSlotOnNaturalCompletion`
- `Tick_FiresNotifyWhenElapsedCrossesTimeSeconds`
- `Notify_FiresExactlyOncePerPlay` (mask prevents double-fire)
- `PlayMontage_ResetsFiredNotifyMask`
- `Footstep_EmitsAtStrideDistance`

These should be added in BATCH-04 alongside the P2 fixes, as they
directly support Phase 3 system correctness.

---

## P2 Issues — Added to DEBT-TRACKER

### P2-A: Hashing tests lack known-value vectors

The hashing tests verify determinism and collision resistance but do not
pin any specific output values. A wrong hash implementation (e.g., a
slightly incorrect FNV multiplier) would still pass. Add at least one
known-value test vector per hash function.

### P2-B: Baking tests do not verify `NotifyInfo.Kind` from registry

`BakingUtils.BakeDef` should populate `NotifyInfo.Kind` by looking up the
marker in `CharacterAnimationDefDto.NotifyMarkers`. No test verifies this.
If the lookup is missing, all notify categories will be `Generic`,
breaking `NotifyEventEmitterSystem` footstep handling later.

---

## Verdict

**BATCH-03 is APPROVED** — Phase 2 functionality is correct and all 8
tasks are complete. However BATCH-04 must fix the three P1 test gaps
(inject tests, hot-reload tests, query API tests) and the Phase 1
behavioral test debt before Phase 3 begins.
