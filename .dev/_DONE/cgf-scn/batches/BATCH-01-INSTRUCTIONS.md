# BATCH-01: Entity Creation Source Infrastructure

**Batch Number:** BATCH-01
**Tasks:** TASK-C001, TASK-C002, TASK-C003
**Phase:** Phase 1 — Entity Creation Source Infrastructure
**Estimated Effort:** 6-8 hours
**Priority:** HIGH
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This is the first batch in the `cgf-scn` workstream.  You are building the
plumbing that allows an in-memory scenario source to feed entity creation
requests into the same pipeline as live DDS requests.

You are implementing three tasks:
1. **TASK-C001** — a thread-safe in-memory source (`ScenarioEntityCreationRequestSource`)
2. **TASK-C002** — a composite wrapper that drains multiple sources in sequence
3. **TASK-C003** — wiring the composite source into the existing `CgfLogicPack`

### Required Reading (IN ORDER)

1. **Design:** `.dev/cgf-scn/DESIGN.md` — read the full Architectural Decisions
   section, especially Decision 1 (why CGF is genesis source), Decision 2
   (why in-memory, not DDS loopback), and Decision 9 (project placement rules).
2. **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — sections
   TASK-C001, TASK-C002, TASK-C003 (Phase 1).
3. **Onboarding:** `.dev/cgf-scn/ONBOARDING.md` — section 3 (folder layout)
   and section 4 (build commands).

### Existing Files You Must Read Before Coding

| File | What to understand |
|------|-------------------|
| `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs` | `IEntityCreationRequestSource` interface and `EntityCreationRequest` DTO |
| `Hrot/Network/Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs` | The only existing production `IEntityCreationRequestSource` implementation — model your new classes after it |
| `Hrot/Subsystems/Hrot.CGF/Systems/CgfLogicPack.cs` | Where `IEntityCreationRequestSource` is consumed and wired |
| `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` | Where `CgfLogicPack` is constructed — you will add `ScenarioEntityCreationRequestSource` construction here |

### Source Code Location

- **Primary Work Area (new files):**
  - `Hrot/Engine/Hrot.Core/Network/ScenarioEntityCreationRequestSource.cs` (NEW)
  - `Hrot/Engine/Hrot.Core/Network/CompositeEntityCreationRequestSource.cs` (NEW)
- **Modified files:**
  - `Hrot/Subsystems/Hrot.CGF/Systems/CgfLogicPack.cs`
  - `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`
- **Test Projects:**
  - `Hrot/Engine/Hrot.Core.Tests/` — for C001 and C002 tests
  - `Hrot/Subsystems/Hrot.CGF.Tests/` (or nearest available project) — for C003 tests

### Build Command

```powershell
# From repo root d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln

# Run Hrot.Core tests
dotnet test Hrot\Engine\Hrot.Core.Tests\Hrot.Core.Tests.csproj

# Run CGF tests (if project exists)
dotnet test Hrot\Subsystems\Hrot.CGF.Tests\Hrot.CGF.Tests.csproj
```

### Report Submission

**When done, submit your report to:**
`.dev/cgf-scn/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/cgf-scn/questions/BATCH-01-QUESTIONS.md`

---

## Context

CGF is the authoritative entity genesis source for the cluster.  Currently it
only participates in the `PrepareLive` handshake as a header-peek-only observer.
This batch creates the foundation: an in-memory `IEntityCreationRequestSource`
that scenario and episode load handlers (built in later batches) will use to
enqueue entity creation requests, multiplexed alongside the existing live NED
source so `CreateEntityRequestSystem` processes both identically.

**Related Tasks:**
- [TASK-C001](./../TASK-DETAIL.md#task-c001--scenarioentitycreationrequestsource) — ScenarioEntityCreationRequestSource
- [TASK-C002](./../TASK-DETAIL.md#task-c002--compositeentitycreationrequestsource) — CompositeEntityCreationRequestSource
- [TASK-C003](./../TASK-DETAIL.md#task-c003--wire-composite-source-into-cgflogicpack) — Wire composite source into CgfLogicPack

---

## 🎯 Batch Objectives

- Implement `ScenarioEntityCreationRequestSource` with thread-safe enqueue/drain
- Implement `CompositeEntityCreationRequestSource` wrapping an ordered list of sources
- Modify `CgfLogicPack` to accept `ScenarioEntityCreationRequestSource` and construct
  a `CompositeEntityCreationRequestSource` wrapping both NED and scenario sources
- Modify `CgfApplication` to construct `ScenarioEntityCreationRequestSource` once
  and pass it to `CgfLogicPack`
- All unit tests passing

---

## ✅ Tasks

### Task 1: ScenarioEntityCreationRequestSource (TASK-C001)

**File:** `Hrot/Engine/Hrot.Core/Network/ScenarioEntityCreationRequestSource.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c001--scenarioentitycreationrequestsource)

**Summary of requirements:**
- Namespace: `Hrot.Core.Network`
- Implements `IEntityCreationRequestSource`
- Internal backing: `ConcurrentQueue<EntityCreationRequest>`
- Public `Enqueue(EntityCreationRequest request)` method (called from load-handler thread)
- `ProcessRequests` drains at most `_maxRequestsPerTick` per call (default 500)
- Constructor parameter: `int maxRequestsPerTick = 500`

Do NOT allocate inside `ProcessRequests`.

**Tests Required (in `Hrot.Core.Tests` or nearest available project):**
- Basic enqueue/drain — 3 requests, handler called 3 times in FIFO order
- Max-items cap — 600 enqueued, cap 500: first call drains 500, second call drains 100
- Empty queue no-op — handler never called, no exception
- Concurrent safety — 1000 items enqueued from 4 tasks while 5th drains; total 1000, no exceptions

### Task 2: CompositeEntityCreationRequestSource (TASK-C002)

**File:** `Hrot/Engine/Hrot.Core/Network/CompositeEntityCreationRequestSource.cs` (NEW FILE)
**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c002--compositeentitycreationrequestsource)

**Summary of requirements:**
- Namespace: `Hrot.Core.Network`
- Implements `IEntityCreationRequestSource`
- Constructor: `IReadOnlyList<IEntityCreationRequestSource> innerSources`
- Throws `ArgumentException` if `innerSources` is empty
- `ProcessRequests` iterates inner sources in order, calling each source's `ProcessRequests`
- Exceptions from inner sources propagate (no swallowing)

**Tests Required:**
- Both sources drained in order (R1 from A, R2+R3 from B, all delivered in that order)
- Empty sources are no-ops
- Single-source passthrough
- Constructor rejects empty list → `ArgumentException`

### Task 3: Wire Composite Source into CgfLogicPack (TASK-C003)

**Files:**
- `Hrot/Subsystems/Hrot.CGF/Systems/CgfLogicPack.cs` (MODIFY)
- `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs` (MODIFY)

**Task Definition:** See [TASK-DETAIL.md](../TASK-DETAIL.md#task-c003--wire-composite-source-into-cgflogicpack)

**Summary of requirements:**
- `CgfLogicPack` constructor gains a new parameter:
  `ScenarioEntityCreationRequestSource scenarioSource`
- Inside `CgfLogicPack`, replace the direct use of `NedEntityCreationRequestSource` as the
  source with `new CompositeEntityCreationRequestSource([nedSource, scenarioSource])`
- `CgfLogicPack` must throw `ArgumentNullException` if `scenarioSource` is null
- `CgfApplication` constructs `ScenarioEntityCreationRequestSource` once and passes it
  to `CgfLogicPack`; the same instance will later be passed to load handlers (Phases 3-4)
- Existing unit tests for `CgfLogicPack` must still pass
- The live NED path must be completely unaffected when `ScenarioEntityCreationRequestSource`
  is empty

**Tests Required:**
- NED requests still processed (stub NED source + tick → `SpawnEntityCommand` emitted)
- Scenario requests processed during same tick (NED empty, scenario queue has 1 request)
- Both sources processed in same tick (NED has Ra, scenario has Rb → both spawn commands)
- Null `scenarioSource` → `ArgumentNullException`

---

## 🧪 Testing Requirements

- Minimum 4 tests for C001, 4 tests for C002, 4 tests for C003
- All tests must assert on values/behavior — not just "no exception" (except
  the concurrency no-exception test where the exception absence IS the assertion)
- No test may use `Thread.Sleep` as a synchronization mechanism; use proper
  `Task.WhenAll` / `CountdownEvent` patterns
- Every test must be isolated (no shared static mutable state between tests)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests before moving on:**

1. **Task 1 (C001):** Implement → Write tests → Run tests → **ALL pass** ✅
2. **Task 2 (C002):** Implement → Write tests → Run tests → **ALL pass** ✅
3. **Task 3 (C003):** Implement → Write tests → Run tests → **ALL pass** ✅

**Do NOT stop to ask for permission to run tests, fix compilation errors, or
proceed to the next task.**  If tests fail, fix the root cause and re-run until
all pass.  Only then write the report.

**Do NOT skip tests.** Tests are a delivery requirement, not optional.

---

## 📊 Report Requirements

Submit `.dev/cgf-scn/reports/BATCH-01-REPORT.md` with the following:

### 1. Completion Summary
List all files created/modified with a one-line description of each change.

### 2. Test Results
Paste the final `dotnet test` output showing all tests passing.

### 3. Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase (`IEntityCreationRequestSource`,
`CgfLogicPack`, `CgfApplication`)? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives
did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or thread-safety concerns you noticed
that aren't covered by the current implementation?

**Q6:** Suggested git commit message for this batch.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `ScenarioEntityCreationRequestSource` implemented with thread-safe enqueue/drain, max cap
- [ ] `CompositeEntityCreationRequestSource` implemented, empty-list rejection
- [ ] `CgfLogicPack` modified with null-checked `scenarioSource` parameter
- [ ] `CgfApplication` constructs `ScenarioEntityCreationRequestSource` and passes it to `CgfLogicPack`
- [ ] All C001 tests pass (min 4)
- [ ] All C002 tests pass (min 4)
- [ ] All C003 tests pass (min 4)
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with zero errors
- [ ] Report submitted to `.dev/cgf-scn/reports/BATCH-01-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid

- Do NOT use `lock` on the queue itself in `ScenarioEntityCreationRequestSource` — use
  `ConcurrentQueue<T>` which is already thread-safe.
- Do NOT allocate a temporary `List` inside `ProcessRequests` to drain the queue —
  drain directly via `TryDequeue` in a loop.
- Do NOT use `ConcurrentQueue.Count` as the loop termination condition in a concurrent
  context; use a counted loop up to `_maxRequestsPerTick` with `TryDequeue` returning
  false as the early-exit.
- Do NOT construct `ScenarioEntityCreationRequestSource` inside `CgfLogicPack` — it
  must be injected so the same instance is shared with load handlers later.
- Do NOT modify `NedEntityCreationRequestSource` or `CreateEntityRequestSystem`.

---

## 📚 Reference Materials

- **Task Details:** `.dev/cgf-scn/TASK-DETAIL.md` — TASK-C001, TASK-C002, TASK-C003
- **Design:** `.dev/cgf-scn/DESIGN.md` — Decision 2 (in-memory source), Decision 9 (project placement)
- **Interface definition:** `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs`
- **Existing impl reference:** `Hrot/Network/Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs`
- **Integration point:** `Hrot/Subsystems/Hrot.CGF/Systems/CgfLogicPack.cs`
