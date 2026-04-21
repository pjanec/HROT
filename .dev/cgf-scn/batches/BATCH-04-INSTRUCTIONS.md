# BATCH-04: CGF Scenario/Episode Load Handlers + SimHost Passive Demotion

**Batch Number:** BATCH-04
**Tasks:** TASK-C006, TASK-C007, TASK-C012
**Phase:** Phase 3 (CGF Scenario Load Handler) + Phase 4 (CGF Episode Load Handler) + Phase 6 (SimHost Passive Demotion)
**Estimated Effort:** 8-10 hours
**Priority:** HIGH — TASK-C007 and TASK-C012 MUST ship together (split-brain risk)
**Dependencies:** BATCH-01 (ScenarioEntityCreationRequestSource), BATCH-02 (C013, C005), BATCH-03 (StagingEntityExtractor)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch connects the infrastructure built in Batches 1-3 to the cluster orchestration
layer by implementing the two CGF load handlers and making the required SimHost change.

**Critical deployment constraint:**  TASK-C007 and TASK-C012 MUST be in the same release.
Deploying `CgfEpisodeLoadHandler` without demoting SimHost's episode handler will cause
duplicate entity creation (split-brain).  Both changes are in this batch.

### Required Reading (IN ORDER)

1. **Design:** `.dev/cgf-scn/DESIGN.md`
   - Decision 1 — CGF as single genesis source
   - Decision 8 — why episode loading is moved to the staging pipeline
   - Decision 9 — project placement and the constraint about NOT modifying `ReferenceEpisodeLoadHandler`
2. **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — sections TASK-C006, TASK-C007, TASK-C012.
3. **Previous Reviews:** `.dev/cgf-scn/reviews/BATCH-03-REVIEW.md`

### Existing Files You Must Read Before Coding

| File | What to understand |
|------|-------------------|
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceScenarioLoadHandler.cs` | The existing scenario handler being replaced on CGF (DO NOT MODIFY) |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceEpisodeLoadHandler.cs` | The existing episode handler — copy parsing logic; DO NOT MODIFY the file |
| `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` | Where load handlers are registered — you will change registration here |
| `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` (or equivalent) | SimHost composition root where `ReferenceEpisodeLoadHandler` is registered with a live world — find this file and change `world: <liveRepo>` to `world: null` |
| `FDP/Toolkits/Fdp.Toolkits/Orchestration/IClusterStateHandler.cs` | The interface your new handlers must implement |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | Built in BATCH-03 — your new handlers call `Extract()` |
| `Hrot/Engine/Hrot.Core/Network/ScenarioEntityCreationRequestSource.cs` | The queue your handlers enqueue into (from BATCH-01) |

### Source Code Location

- **New files:**
  - `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfScenarioLoadHandler.cs`
  - `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfEpisodeLoadHandler.cs`
- **Modified files:**
  - `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` — swap handler registrations
  - `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` (or equivalent) — `world: null` for `ReferenceEpisodeLoadHandler`
- **Test files:**
  - Add tests to `Hrot/Subsystems/Hrot.SimHost.Tests/` (CgfScenarioLoadHandlerTests.cs, CgfEpisodeLoadHandlerTests.cs)

### Build Commands

```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2

dotnet build IOS-IG-SimHost.sln

dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/cgf-scn/reports/BATCH-04-REPORT.md`

---

## Context

`CgfScenarioLoadHandler` replaces the header-peek-only `ReferenceScenarioLoadHandler(world:null)` on the CGF node.  On `Commit`, it calls `StagingEntityExtractor.Extract` and enqueues all returned requests into `ScenarioEntityCreationRequestSource`.

`CgfEpisodeLoadHandler` replaces `ReferenceEpisodeLoadHandler(serializer, loader, world:null)` on the CGF node.  It handles `StartEpisode` (extract + enqueue with `EpisodeTag`) and `StopEpisode` (query world for `EpisodeTag`-matching entities, publish `DestroyEntityCommand` per entity).

`TASK-C012` changes SimHost's `ReferenceEpisodeLoadHandler` registration from `world: <liveRepo>` to `world: null`, making it a no-op header-peek observer.  Without this, both CGF and SimHost would materialize episode entities independently — a split-brain disaster.

**Related Tasks:**
- [TASK-C006](../TASK-DETAIL.md#task-c006--cgfscenarioloadhandler) — CgfScenarioLoadHandler
- [TASK-C007](../TASK-DETAIL.md#task-c007--cgfepisodeloadhandler) — CgfEpisodeLoadHandler
- [TASK-C012](../TASK-DETAIL.md#task-c012--simhost-episode-handler-passive-demotion) — SimHost passive demotion

---

## 🎯 Batch Objectives

- Implement `CgfScenarioLoadHandler` (`IClusterStateHandler` for `PrepareLive`)
- Implement `CgfEpisodeLoadHandler` (`IClusterStateHandler` for `StartEpisode`/`StopEpisode`)
- Register both handlers in `CgfApplication` replacing the existing handlers
- Change SimHost's `ReferenceEpisodeLoadHandler` registration to `world: null`
- All tests passing; solution builds cleanly

---

## ✅ Tasks

### Task 1: CgfScenarioLoadHandler (TASK-C006)

**File:** `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfScenarioLoadHandler.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c006--cgfscenarioloadhandler)

Key design requirements:
- Implements `IClusterStateHandler`
- `CanHandle(NodeOpType op)`: returns `true` only for `NodeOpType.PrepareLive`
- Constructor injects: `IScenarioLoader scenarioLoader`, `StagingEntityExtractor extractor`,
  `ScenarioEntityCreationRequestSource source`, `ScenarioBehaviorRemapper? remapper = null`
- `PrepareAsync(intent)`: call `scenarioLoader.TryLoadScenarioJson(...)` and store the result
  (could be null); no side effects on `source`
- `Commit(intent)`: if stored JSON is null → no-op; else call `extractor.Extract(...)`,
  iterate results, call `source.Enqueue(req)` for each
- `Abort()`: clear the stored JSON so a subsequent `Commit` is a no-op

**Tests Required:**
1. Happy path — loader returns JSON with 2 root entities; after `PrepareAsync` + `Commit`: queue has 2 requests
2. Scenario not found — loader returns `null`; queue empty after `Commit`
3. `Abort` clears pending state — `PrepareAsync` (loader returns JSON), then `Abort`, then `Commit`: queue empty
4. `CanHandle` returns `true` only for `PrepareLive`

### Task 2: CgfEpisodeLoadHandler (TASK-C007)

**File:** `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfEpisodeLoadHandler.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c007--cgfepisodeloadhandler)

Key design requirements:
- Implements `IClusterStateHandler`
- `CanHandle`: returns `true` for `StartEpisode` and `StopEpisode`
- Constructor injects: `IScenarioLoader loader`, `StagingEntityExtractor extractor`,
  `ScenarioEntityCreationRequestSource source`, `IEntityRepository world` (live world
  for `StopEpisode` query), `ScenarioBehaviorRemapper? remapper = null`
- **`StartEpisode.PrepareAsync`:** parse `EpisodeHandlerPayload` from intent to get `episodeId`;
  load episode JSON via loader; store both
- **`StartEpisode.Commit`:** call `extractor.Extract(..., episodeId: _pendingEpisodeId)`, enqueue results
- **`StopEpisode.Commit`:** Query `world` for entities with `EpisodeTag.EpisodeId == _pendingEpisodeId`
  using `EntityLifecycle.All`; for each matching entity, read `NetworkIdentity.Value`,
  publish `DestroyEntityCommand` to the local event bus (NOT `EntityRepository.DestroyEntity`)
- `Abort()`: clear stored episode state
- **Copy** the `EpisodeHandlerPayload` parsing pattern from `ReferenceEpisodeLoadHandler`
  (handles `DomainPayload` variants for `IsStart` + `EpisodeId`)

**Tests Required:**
1. `StartEpisode` enqueues requests with `EpisodeTag` — loader returns JSON with 3 entities; after commit: queue has 3 requests; each has `EpisodeTag.EpisodeId == G`
2. `StopEpisode` publishes `DestroyEntityCommand` per episode entity — world has 5 entities: 2 with matching `EpisodeTag`, 3 without; after commit: 2 destroy commands published; no direct `DestroyEntity` call
3. `CanHandle` true for `StartEpisode` and `StopEpisode`, false for `PrepareLive`
4. `Abort` before `Commit` leaves queue empty
5. Missing episode JSON — loader returns `null`; queue empty after `Commit`

### Task 3: Register Handlers in CgfApplication (TASK-C006/C007 wiring)

**File:** `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` (MODIFY)

- Replace `ReferenceScenarioLoadHandler(world: null)` registration with `CgfScenarioLoadHandler`
- Replace `ReferenceEpisodeLoadHandler(serializer, loader, world: null)` registration
  with `CgfEpisodeLoadHandler`
- Pass `ScenarioBehaviorRemapper` if it is available in `CgfApplication`

### Task 4: SimHost Passive Demotion (TASK-C012)

**File:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` (or equivalent SimHost composition root)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c012--simhost-episode-handler-passive-demotion)

First, **find** the file that registers `ReferenceEpisodeLoadHandler` in the SimHost composition
root.  Search for `new ReferenceEpisodeLoadHandler` in the `Hrot/Subsystems/Hrot.SimHost/` directory.

The change is exactly **one constructor argument**: change the `world:` parameter from a live
`EntityRepository` reference to `null`.

Verify there is no second registration of `ReferenceEpisodeLoadHandler` on SimHost that still
receives a non-null world.

**Tests:**
- SC1 (integration) requires a running cluster — skip for unit tests; rely on the build-clean
  verification (`dotnet build Hrot\Subsystems\Hrot.SimHost\Hrot.SimHost.csproj` succeeds)

---

## 🧪 Testing Requirements

- 4 tests for TASK-C006
- 5 tests for TASK-C007
- To satisfy the `StopEpisode` test: use an in-memory `EntityRepository` (or mock); verify
  that `DestroyEntityCommand` events were published to the event bus, NOT that
  `EntityRepository.DestroyEntity` was called directly
- Tests for `TASK-C012` are SC2-4 level (cluster-level tests), which are integration
  tests beyond this batch scope — at minimum verify the build is clean

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Task 1 (C006):** Implement → Write tests → ALL pass ✅
2. **Task 2 (C007):** Implement → Write tests → ALL pass ✅
3. **Task 3 (wiring):** Update `CgfApplication.cs` → `dotnet build` passes ✅
4. **Task 4 (C012):** Change `world: null` in SimHost → `dotnet build` passes ✅
5. **Final:** `dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj` → ALL pass ✅

**Do NOT stop to ask for permission. Fix all failures at the root cause.
Write the report only after all tests pass and the full solution builds with 0 errors.**

---

## 📊 Report Requirements

Submit `.dev/cgf-scn/reports/BATCH-04-REPORT.md` with:

### 1. Completion Summary
All files created/modified.

### 2. Test Results
Final `dotnet test` output.

### 3. Developer Insights

**Q1:** What issues did you encounter? How did you resolve them?

**Q2:** Did you find unexpected complexity in the `EpisodeHandlerPayload` parsing
(copied from `ReferenceEpisodeLoadHandler`)? Any edge cases?

**Q3:** Was finding the SimHost composition root straightforward? Where exactly is
the `ReferenceEpisodeLoadHandler(world: <liveRepo>)` registration?

**Q4:** Any weak points spotted in `CgfApplication.cs` composition root?

**Q5:** Suggested git commit message.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `CgfScenarioLoadHandler` implemented; 4 tests pass
- [ ] `CgfEpisodeLoadHandler` implemented; 5 tests pass
- [ ] `CgfApplication.cs` updated: both handlers registered replacing the reference handlers
- [ ] SimHost composition root: `ReferenceEpisodeLoadHandler` called with `world: null`
- [ ] No second `ReferenceEpisodeLoadHandler` registration on SimHost with a non-null world
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors
- [ ] `dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\...` — all pass
- [ ] Report submitted

---

## ⚠️ Common Pitfalls to Avoid

- Do NOT call `EntityRepository.DestroyEntity` in `StopEpisode` — publish
  `DestroyEntityCommand` to the local event bus instead
- `StopEpisode` MUST use `EntityLifecycle.All` to catch entities still in `Constructing`
- `episodeId` stored during `PrepareAsync` must be the same GUID used in `StopEpisode`
- Do NOT modify `ReferenceEpisodeLoadHandler` or `ReferenceScenarioLoadHandler`
- Do NOT skip the SimHost change (TASK-C012) — it MUST ship with TASK-C007

---

## 📚 Reference Materials

- **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — TASK-C006, TASK-C007, TASK-C012
- **Design:** `.dev/cgf-scn/DESIGN.md` — Decisions 1, 8, 9
- **Reference handlers (read-only):** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/`
- **StagingEntityExtractor:** `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs`
- **CgfApplication:** `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`
- **Previous reviews:** `.dev/cgf-scn/reviews/BATCH-01-REVIEW.md` through `BATCH-03-REVIEW.md`
