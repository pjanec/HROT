# BATCH-11: Spatial grid correctness + DEM1-D009 Phase B (network slice) + optional replay migration

**Batch Number:** BATCH-11  
**Tasks:** P3 **perception grid stale-slot** (after index reuse) · **DEM1-D009** Phase B (DDS + replication slice) · doc fix **`IScenario.OnShutdown`** · optional **ParallelStories** + **`RecordingModule.Blocking`**  
**Phase:** Correctness + DEM1 Phase 5  
**Estimated Effort:** 16–22 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-10 approved — see `.dev-workstream/reviews/BATCH-10-REVIEW.md`

---

## Onboarding

### Developer instructions

1. **Task 1** (grid) first if it is small — prevents latent perception bugs from compounding with network work.  
2. **Task 2** is the main slice of **DEM1-D009**; deliver **one vertical milestone** with tests (e.g. one DDS topic round-trip **or** ghost visible on Muscle), then document remaining ticks for BATCH-12.  
3. **Tasks 3–4** as time allows.

### Required reading

1. `.dev-workstream/guides/CODE-STANDARDS.md`
2. `.dev-workstream/reviews/BATCH-10-REVIEW.md`
3. `.dev-workstream/reports/BATCH-10-REPORT.md` — “Remaining Phase B work”
4. `.dev-workstream/DEBT-TRACKER.md` — **Target BATCH-11+**
5. `docs/demos-1/DEM1-TASK-DETAIL.md` § DEM1-D009, `docs/demos-1/DEM1-DESIGN.md` §6.4
6. `Fdp.Examples.NetworkDemo` — translator / participant patterns

### Report / questions

- `.dev-workstream/reports/BATCH-11-REPORT.md`
- `.dev-workstream/questions/BATCH-11-QUESTIONS.md` (if needed)

---

## Mandatory workflow

Task 1 → perception tests green → Task 2 → scenario + replication/network tests green → optional tasks.

---

## Tasks

### Task 1: [DEBT] `SpatialHashGrid` / `LocalGridBuilderSystem` — stale slot after index reuse

**Debt:** `.dev-workstream/DEBT-TRACKER.md` — BATCH-10 review (Target BATCH-11+)

**Problem:** After **destroy + create** at **stable entity count**, the new entity is inserted correctly, but the **old** entity may remain in the hash until a **count-change** full rebuild — neighbor queries can return **dead** handles.

**Goal:** Remove or overwrite stale slots on incremental path (e.g. on **`Remove`**, when recycling index, scan chain for matching **Index** with mismatched **Generation** and splice out), **or** force a targeted cell rebuild when generation changes — pick the smallest change that preserves determinism.

**Tests:** Extend **`LocalGridBuilderSystemTests`** (or **`SpatialHashGridTests`**) to assert **no** dead entity in **`QueryNeighbors`** after index reuse.

---

### Task 2: [FEATURE] DEM1-D009 — Phase B (network + ELM continuation)

**Reference:** `DEM1-TASK-DETAIL.md` § DEM1-D009 (Brain/Muscle toolkits, TKB, ticks 10–50).

**Minimum for this batch (pick a coherent slice):**

- Use **`DemoTkbSetup.RegisterAll`** (or equivalent) so Brain spawn is real, **or** document why a slimmer stub is still justified.  
- Wire **at least one** Cyclone path between Brain and Muscle participants (patterns from **`Fdp.Examples.NetworkDemo`**).  
- Register **`ReplicationLogicModule`** on one or both nodes and add a test that proves **observable** progress (ghost entity, transform sample, or lifecycle sync).  
- Extend **`DistributedTankScenario`** and **`ScenarioTests`**; keep **`DEM1-TASK-TRACKER`** **unchecked** until the **full** success conditions in the task detail are met.

**Explicit deferrals** belong in **`BATCH-11-REPORT.md`** with estimated BATCH-12 scope.

---

### Task 3: [DOCS] `IScenario.OnShutdown` XML vs `ScenarioSubsystem.Shutdown` order

**File:** `FDP/Examples/Fdp.Examples.Common/IScenario.cs`

**Goal:** Align XML with actual teardown order (`_kernel.Dispose()` → `OnShutdown()` → `_world.Dispose()`), or adjust subsystem order if product owners require world-before-native (coordinate before changing order).

---

### Task 4: [OPTIONAL] `ParallelStoriesScenario` + `RecordingModule` + `Blocking: true`

**Goal:** Replace direct **`AsyncRecorder`** usage with **`RecordingModule`** configured with **`Blocking = true`**, preserving deterministic replay tests.

---

## Testing (minimum)

```powershell
dotnet test "FDP\Examples\Fdp.Examples.Scenarios.Tests\Fdp.Examples.Scenarios.Tests.csproj"
dotnet test "FDP\Toolkits\FDP.Toolkit.Perception.Tests\FDP.Toolkit.Perception.Tests.csproj"
```

Add **`FDP.Toolkit.Replication.Tests`**, **`Fdp.Examples.NetworkDemo`** tests, or Cyclone host tests as touched.

---

## Success criteria

- [ ] Task 1: Stale-slot behaviour improved **or** documented as accepted with test proving current contract.  
- [ ] Task 2: At least **one** new Phase B milestone with tests; report lists **remaining** D009 work.  
- [ ] Task 3: Doc/subsystem story is consistent.  
- [ ] Task 4: Optional; scenarios tests green.  
- [ ] `DEBT-TRACKER.md` updated by lead after review.  
- [ ] `BATCH-11-REPORT.md` submitted.

---

## Pitfalls

- **DDS:** Dispose writers/readers before participants; loopback Domain 0 only for demos.  
- **Replication:** Match authority / ghost rules already used in **`Fdp.Examples.NetworkDemo`** to avoid one-off semantics.  
- **Grid:** Any change must keep **SensorGrid** / autonomy scenarios deterministic in CI.
