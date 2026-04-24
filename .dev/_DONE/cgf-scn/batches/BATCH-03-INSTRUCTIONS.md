# BATCH-03: StagingEntityExtractor (TASK-C004)

**Batch Number:** BATCH-03
**Tasks:** TASK-C004
**Phase:** Phase 2 — Staging Entity Extractor
**Estimated Effort:** 10-12 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (ScenarioEntityCreationRequestSource), BATCH-02 (EntityCreationRequest extension, ScenarioBehaviorRemapper)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements `StagingEntityExtractor` — the core two-pass extraction
engine that turns scenario JSON into `EntityCreationRequest` objects ready for
injection into the genesis pipeline.

This is the most complex task in the entire workstream.  Read the design document
and task detail carefully before writing a single line of code.

### Required Reading (IN ORDER)

1. **Design:** `.dev/cgf-scn/DESIGN.md`
   - Decision 3 — why a staging repository is used
   - Decision 4 — BitMask256 exclusion mask (which components are excluded)
   - Decision 5 — root-entity-only extraction and `PartMetadata` filtering
   - Decision 6 — two-pass network ID remapping
   - Decision 10 — translator-consumed components excluded to prevent ECS handle leakage
2. **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — section `TASK-C004` (all constraints and success conditions).
3. **Previous Reviews:** `.dev/cgf-scn/reviews/BATCH-01-REVIEW.md`, `.dev/cgf-scn/reviews/BATCH-02-REVIEW.md`

### Existing Files You Must Read Before Coding

| File | What to understand |
|------|-------------------|
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Component type ID constants — MUST use these named constants for the exclusion mask, NOT raw integers |
| `FDP/Engine/Fdp.Core/IComponentTable.cs` | `GetRawObject(int index)` used for boxing-based component extraction |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` | How to hydrate a staging `EntityRepository` from JSON (`Deserialize` method) |
| `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs` | `GetConsumedComponentsMask()` — translator-handled component IDs |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/ScenarioBehaviorRemapper.cs` | The remapper built in BATCH-02, used here to remap behavior param IDs |
| `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs` | `EntityCreationRequest` with new `PreAllocatedNetworkId` and `ChildComponentOverrides` from BATCH-02 |
| `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` | Confirms `INetworkIdAllocator` interface used for ID allocation |
| `Hrot/Engine/Hrot.Core/Network/ScenarioEntityCreationRequestSource.cs` | The target queue for extraction results (from BATCH-01) |

Find the correct namespace and location by reading these files first:
- `FDP/Engine/Fdp.Core/EpisodeTag.cs` — check `EpisodeTag` structure (component ID 84)
- Any existing `EntityRepository` usage in `Hrot.CGF` to understand patterns

### Source Code Location

- **Primary new file:** `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` (NEW FILE)
- **Test file:** `Hrot/Subsystems/Hrot.CGF.Tests/` (if this project exists)
  OR `Hrot/Subsystems/Hrot.SimHost.Tests/StagingEntityExtractorTests.cs` (add here if no CGF.Tests project)

### Build Commands

```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2

# Build full solution
dotnet build IOS-IG-SimHost.sln

# Run CGF / SimHost tests
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj

# Alternatively if Hrot.CGF.Tests exists:
dotnet test Hrot\Subsystems\Hrot.CGF.Tests\Hrot.CGF.Tests.csproj

# Run Core tests (should stay green)
dotnet test Hrot\Engine\Hrot.Core.Tests\Hrot.Core.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/cgf-scn/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/cgf-scn/questions/BATCH-03-QUESTIONS.md`

---

## Context

`StagingEntityExtractor` bridges the scenario JSON world and the ECS genesis
pipeline.  It:
1. Deserializes JSON into a transient staging `EntityRepository`
2. Pass 1: pre-allocates new network IDs for every entity with a `NetworkIdentity`
3. Pass 2: extracts root-entity components (filtered by exclusion mask), builds
   `EntityCreationRequest` with `PreAllocatedNetworkId`, and harvests child
   component overrides
4. Remaps behavior param JSON using `ScenarioBehaviorRemapper`
5. Appends `EpisodeTag` if `episodeId` is provided
6. Disposes the staging repository

**Related Task:**
- [TASK-C004](../TASK-DETAIL.md#task-c004--stagingentityextractor) — StagingEntityExtractor

---

## 🎯 Batch Objectives

- Implement `StagingEntityExtractor` with full two-pass extraction
- Include exclusion mask (8 static entries + translator-consumed mask)
- Root-entity filtering via `PartMetadata` (component ID 55)
- `ChildComponentOverrides` harvesting for `PartMetadata`-carrying entities
- `ScenarioBehaviorRemapper` integration for behavior param ID remapping
- `EpisodeTag` appending
- Staging repository disposal
- All 12 unit tests passing (covering all success conditions in TASK-DETAIL.md)

---

## ✅ Tasks

### Task 1: Implement StagingEntityExtractor (TASK-C004)

**File:** `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c004--stagingentityextractor) — read ALL constraints carefully.

**Key architectural requirements (from DESIGN.md and TASK-DETAIL.md constraints):**

1. **Static exclusion mask** — built using `GlobalComponentIds` named constants:
   - `LifecycleDescriptor` (5), `NetworkIdentity` (50), `NetworkAuthority` (51),
     `DescriptorOwnership` (59), `TkbIdentity` (65), `GhostStateTracker` (66),
     `NetworkOwnership` (140), `PendingNetworkAck` (141)
   - At construction: OR in all `GetConsumedComponentsMask()` from registered translators

2. **Root-entity detection:** entity has NO `PartMetadata` component
   (`GlobalComponentIds.PartMetadata = 55`) → it is a root entity (extract it)

3. **Child entity detection:** entity HAS `PartMetadata` → do NOT add to root list;
   instead harvest its non-excluded components into a buffer keyed by
   `(PartMetadata.ParentEntity, PartMetadata.InstanceId)`

4. **Pass 1 (ID allocation):** for every entity with `NetworkIdentity`, call
   `INetworkIdAllocator.AllocateId()`, build `Dictionary<long, long> oldToNewMap`

5. **Pass 2 (extraction):** for each root entity:
   - Read `TkbType` from `TkbIdentity.TkbType` (0 if no `TkbIdentity`)
   - Read `DisType` from `stagingRepo.GetEntityHeader(entity.Index).DisType.Value`
   - Iterate component table, skip excluded components (by exclusion mask)
   - Read `PreAllocatedNetworkId` from `oldToNewMap` (lookup by old `NetworkIdentity.Value`)
   - Build `InitialComponents` list; append `EpisodeTag` last if `episodeId != null`
   - Remap behavior params via `ScenarioBehaviorRemapper` if provided:
     read `ActiveMissionPlan` from staging repo; mutate `Task.BehaviorParams` in-place
     (intentional, staging repo is transient — see TASK-DETAIL.md constraint on in-place mutation)
   - Attach `ChildComponentOverrides` from buffer if any children were found for this entity

6. **Disposal:** `stagingRepo.Dispose()` must be called after extraction (even on exception)

**Method signature (suggested):**
```csharp
public IReadOnlyList<EntityCreationRequest> Extract(
    ScenarioSerializer serializer,
    string json,
    INetworkIdAllocator idAllocator,
    Guid? episodeId = null,
    ScenarioBehaviorRemapper? behaviorRemapper = null)
```

**Tests Required** (all 12 success conditions from TASK-DETAIL.md):
1. Basic extraction — single root entity; correct TkbType; excluded components absent
2. TKB structural child entities filtered out
3. ORBAT subordinates NOT filtered out (CommanderId != 0 is still extracted)
4. Episode tag appended
5. Network ID remapping — FireAtTarget BehaviorParams updated
6. Entities without NetworkIdentity extracted without Pass 1 entry
7. Translator-handled components excluded
8. Disposal of staging repo called
9. PreAllocatedNetworkId set from Pass 1 allocation
10. Entity without NetworkIdentity has PreAllocatedNetworkId == 0
11. ChildComponentOverrides populated from PartMetadata children
12. Child entity ID carried through to ChildComponentOverrides.PreAllocatedId

---

## 🧪 Testing Requirements

- All 12 success conditions from TASK-DETAIL.md must be covered by tests
- Tests must use in-memory `EntityRepository` (or a stub); do not spin up
  full cluster
- Disposal test: use a counting wrapper or `Mock<EntityRepository>` to detect
  `Dispose()` calls
- No `Thread.Sleep` in tests

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**Implement → Write tests (one success condition at a time) → ALL pass ✅**

Build and run tests after every 2-3 tests. Do not batch all 12 tests for the
final run — fix failures as you go.

```
dotnet build IOS-IG-SimHost.sln  (after every logical checkpoint)
dotnet test <test-project>        (verify after each group of tests)
```

**Do NOT stop to ask for permission. Fix compilation errors and test failures
immediately.  Only write the report after ALL 12 tests pass and the full
solution builds with 0 errors.**

---

## 📊 Report Requirements

Submit `.dev/cgf-scn/reports/BATCH-03-REPORT.md` with:

### 1. Completion Summary
Files created/modified.

### 2. Test Results
Final `dotnet test` output showing all tests passing.

### 3. Developer Insights

**Q1:** What was the hardest part of the `StagingEntityExtractor` implementation?
What patterns in the existing codebase helped?

**Q2:** Were there any ambiguities in the task spec that required a judgement call?
What did you decide?

**Q3:** What weak points did you spot in the existing component extraction machinery
(`IComponentTable`, `EntityRepository`, `ScenarioSerializer`)?

**Q4:** What edge cases did you discover that weren't in the spec?

**Q5:** Any performance observations — are there obvious hot-path allocations in
the extraction loop that could be avoided?

**Q6:** Suggested git commit message.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `StagingEntityExtractor` implemented with two-pass extraction
- [ ] Static exclusion mask uses `GlobalComponentIds` named constants (no raw ints)
- [ ] Translator-consumed mask ORed into instance exclusion mask at construction
- [ ] Root-entity filter uses `PartMetadata` (ID 55), NOT `CommanderId`
- [ ] `ChildComponentOverrides` harvested correctly from child entities
- [ ] `ScenarioBehaviorRemapper.RemapJson` called for each `ActiveMissionPlan` task
- [ ] `EpisodeTag` appended when `episodeId` is provided
- [ ] Staging `EntityRepository` disposed after extraction
- [ ] All 12 unit tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors
- [ ] Report submitted to `.dev/cgf-scn/reports/BATCH-03-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

- Do NOT use `EntityInfo.CommanderId` to detect TKB children — use `PartMetadata` only
- Do NOT allocate deep clones of `ActiveMissionPlan`; mutation in-place on the staging
  object is intentional (the staging repo is disposed immediately after)
- The exclusion mask MUST exclude `PartMetadata` (ID 55) from child override components
  (it's already in the per-child mask by the time you extract child components)
- When `NetworkIdentity.Value` lookup fails in Pass 1 (entity has no `NetworkIdentity`):
  set `PreAllocatedNetworkId = 0`, no exception needed
- Do NOT retain any reference to staging repo entities/components after `Dispose()`
- Remember to include the `using` statement so the staging repo is disposed even on exception

---

## 📚 Reference Materials

- **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — TASK-C004 (entire section)
- **Design:** `.dev/cgf-scn/DESIGN.md` — Decisions 3, 4, 5, 6, 10
- **GlobalComponentIds:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **ScenarioSerializer:** `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs`
- **IEntityScenarioTranslator:** `FDP/Toolkits/Fdp.Toolkits/Scenario/IEntityScenarioTranslator.cs`
- **EntityCreationRequest:** `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs`
- **ScenarioBehaviorRemapper:** `FDP/Toolkits/Fdp.Toolkits/Behavior/ScenarioBehaviorRemapper.cs`
- **Previous reviews:** `.dev/cgf-scn/reviews/BATCH-01-REVIEW.md`, `.dev/cgf-scn/reviews/BATCH-02-REVIEW.md`
