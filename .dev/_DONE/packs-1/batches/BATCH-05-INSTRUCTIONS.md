# BATCH-05: Phase 6 — ExCon Egress Anti-Corruption Layer

**Batch Number:** BATCH-05
**Tasks:** PACK-E001, PACK-E002
**Phase:** Phase 6 (ExCon Egress Anti-Corruption Layer)
**Estimated Effort:** 10–13 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 ✅, BATCH-02 ✅, BATCH-03 ✅ (MissionControlIntent/AckEvent types), BATCH-04 ✅ (ClusterOpIntent, OrchestrationObserverTranslator)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Phase 5 cleaned the *ingress* side of ExCon (what it *observes*: `ClusterUiCache` now reads
from the bus). This batch cleans the *egress* side — what ExCon *commands*.

**PACK-E001 — Eradicate DdsWriter from ClusterScenarioPanel:**
`ClusterScenarioPanel` currently has two construction paths:
- **Orchestrator-internal path** (`ClusterMaster, ClusterUiCache`) — already CQRS-clean, keep it.
- **ExCon/remote path** (`DdsWriter<ClusterOpRequest>, ClusterUiCache`) — **violation**: UI class
  holds a live DDS writer socket and builds raw JSON strings inline.

Fix: Remove the DDS-writer path. Add `FdpEventBus` as the egress channel for the remote path.
Create `ClusterOpEgressTranslator` in `Hrot.Common/Orchestration/` that consumes `ClusterOpIntent`
from the bus, serializes to JSON, and writes `ClusterOpRequest` DDS.

Define `ClusterOpIntent` event in `ClusterCqrsEvents.cs` with `[EventId(9018)]`.

**PACK-E002 — Eradicate IDdsWriter from MissionEditorService:**
`MissionEditorService` currently accepts `IDdsWriter<MissionControlRequest>` and sends DDS
directly. Replace this with `FdpEventBus` — publish `MissionControlIntent` (defined in PACK-P001)
to the bus instead.

Create `MissionControlEgressTranslator` that consumes `MissionControlIntent` from bus, serializes
parameters to JSON, writes `MissionControlRequest` DDS.

Also update the ACK ingress: `MissionEditorService` currently implements `IIngressHandler` for
`MissionControlAck`. Replace with a `MissionControlAckIngressTranslator` that publishes
`MissionControlAckEvent` onto the bus; `MissionEditorService` reads it via bus consumption.

### Required Reading (IN ORDER)

1. **Developer Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Architecture & Design:** `.dev/packs-1/DESIGN.md` — read §Phase 6 (§6.A and §6.B) fully
3. **Task Specifications:** `.dev/packs-1/TASK-DETAIL.md` — sections PACK-E001 and PACK-E002
4. **Previous Reviews:** `.dev/packs-1/reviews/BATCH-04-REVIEW.md`

### Source Code Locations

**PACK-E001:**
- `Hrot.ClusterRunner/Services/ClusterScenarioPanel.cs` — remove DDS ctor + field
- `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` — add `ClusterOpIntent [EventId(9018)]`
- `Hrot.Common/Orchestration/ClusterOpEgressTranslator.cs` — new file (same dir as `OrchestrationObserverTranslator.cs`)
- `Hrot.ClusterRunner/Services/ExConSubsystem.cs` — remove `_sysOpWriter`; wire `ClusterOpEgressTranslator`
- `Hrot.ClusterRunner/Services/OrchestratorSubsystem.cs` — remove `_sysOpWriter` if present
- Tests: `Hrot.ClusterRunner.Tests/ClusterScenarioPanelTests.cs` — update for bus ctor

**PACK-E002:**
- `Hrot.ExCon/Services/MissionEditorService.cs` — replace `IDdsWriter<MissionControlRequest>` with `FdpEventBus`
- `Hrot.ExCon/Network/MissionControlEgressTranslator.cs` — new file
- `Hrot.ExCon/Network/MissionControlAckIngressTranslator.cs` — new file (or extend existing)
- All `MissionEditorService` construction sites — `ExConLogic.cs`, test files
- Tests: `Hrot.ExCon.Tests/WorkflowTests.cs` and `Hrot.ExCon.Tests/MultiIosIntegrationTests.cs`

### Test Projects

- `Hrot.ClusterRunner.Tests/` — PACK-E001 unit tests
- `Hrot.ExCon.Tests/` — PACK-E002 unit tests
- `Hrot.ExCon.Tests/MultiIosIntegrationTests.cs` — integration test

### Report Submission

**When done, submit your report to:**
`.dev/packs-1/reports/BATCH-05-REPORT.md`

**If you have questions, create:**
`.dev/packs-1/questions/BATCH-05-QUESTIONS.md`

---

## 🔄 Mandatory Workflow: Test-Driven Task Progression

```
1. READ the task detail in TASK-DETAIL.md (understand WHY, not just WHAT)
2. READ the relevant source files before touching anything
3. WRITE the test(s) first — watch them FAIL
4. IMPLEMENT the minimum code to make tests PASS
5. VERIFY: dotnet test [relevant project] — ALL tests must pass
6. Only then move to the next task
```

**Never skip tests. Never fake assertions. Tests must check real logic/values/behavior.**

---

## 📌 Tasks

### Order of Execution

```
PACK-E001 → PACK-E002   (independent, but E001 is smaller — do first)
```

---

### PACK-E001 — Eradicate DdsWriter from ClusterScenarioPanel

See: `TASK-DETAIL.md#pack-e001`

**Summary:**

1. **Define `ClusterOpIntent`** in `ClusterCqrsEvents.cs`:
   ```csharp
   [EventId(9018)]
   [DataPolicy(DataPolicy.NoRecord)]
   public sealed class ClusterOpIntent
   {
       public Guid            RequestId;
       public ClusterOpType   OperationType;
       public object?         DomainPayload;  // typed payload, NOT raw JSON
   }
   ```

2. **Refactor `ClusterScenarioPanel`**: Remove the `DdsWriter<ClusterOpRequest>` constructor
   overload and field. The `FdpEventBus` is the sole egress channel for the remote path.
   `SendRequest()` becomes `_bus.PublishManaged(new ClusterOpIntent { ... })`.
   Delete all inline `PayloadJson = $"..."` string interpolations from this class.

3. **Create `ClusterOpEgressTranslator`** in `Hrot.Common/Orchestration/`:
   - Consumes `ClusterOpIntent` from `FdpEventBus`
   - Serializes `DomainPayload` to JSON via `System.Text.Json`
   - Writes `ClusterOpRequest` to DDS
   - This is the **only** class that may call `System.Text.Json.JsonSerializer` for this flow.

4. **Update `ExConSubsystem.cs`**: Replace `_sysOpWriter` field with `_clusterOpEgressTranslator`.
   Remove DDS writer injection into `ClusterScenarioPanel`.

5. **Update `OrchestratorSubsystem.cs`**: Remove `_sysOpWriter` if it creates one.

6. **Update `ClusterScenarioPanelTests`**: Update tests to use the new bus-based constructor.
   Assert that `ClusterOpIntent` events are published to the bus (not DDS writes).

**Key constraints:**
- `ClusterScenarioPanel.cs` must have zero references to `CycloneDDS.Runtime`, `DdsWriter`, or `System.Text.Json`.
- `ClusterOpEgressTranslator` is the ONLY class in the egress stack that calls `System.Text.Json.JsonSerializer`.

**Tests to write:**
1. Panel publishes `ClusterOpIntent` with correct `OperationType` and payload; zero DDS packets.
2. `ClusterOpEgressTranslator` serializes to DDS correctly — payload round-trips.
3. Compile gate: `ClusterScenarioPanel.cs` has zero `DdsWriter`/`DdsParticipant`/`System.Text.Json` references.
4. Regression: existing `ClusterScenarioPanelTests` pass (after updating for bus ctor).

---

### PACK-E002 — Eradicate IDdsWriter from MissionEditorService

See: `TASK-DETAIL.md#pack-e002`

**Summary:**

1. **`MissionEditorService` switches to `FdpEventBus`**:
   - Remove `IDdsWriter<MissionControlRequest>` constructor parameter and field.
   - Accept `FdpEventBus`.
   - In `CommitMissionAsync`, publish `MissionControlIntent` (defined in PACK-P001/BATCH-03) to
     the bus instead of calling `_requestWriter.Write(...)`.

2. **Create `MissionControlEgressTranslator`** in `Hrot.ExCon/Network/`:
   - Consumes `MissionControlIntent` from `FdpEventBus`
   - Serializes parameters to JSON
   - Writes `MissionControlRequest` DDS message

3. **ACK ingress**:
   - Create `MissionControlAckIngressTranslator` that polls `DdsReader<MissionControlAck>` and
     publishes `MissionControlAckEvent` onto the bus.
   - `MissionEditorService` reads ACKs via `_bus.ConsumeManaged<MissionControlAckEvent>()` (polled
     on service tick) OR subscribes as a bus consumer — your choice.

4. **Update all construction sites**: `ExConLogic.cs`, `WorkflowTests.cs`, `MultiIosIntegrationTests.cs`.

**Key constraints:**
- `MissionEditorService.cs` must have zero references to `IDdsWriter`, `DdsWriter`, `DdsReader`,
  or any type from `Hrot.NED.Messages`.
- `MissionControlEgressTranslator` is the ONLY class in the ExCon mission stack that calls
  `System.Text.Json.JsonSerializer`.
- Pending-commit timeout logic (`_commitTimeoutMs`, `_pendingCommits`, `CancellationToken`) is
  **preserved unchanged** — only the transport mechanism changes.
- `WorkflowTests` currently passes a stub `IDdsWriter<MissionControlRequest>`. Update tests to
  pass a bus and verify `MissionControlIntent` events are published.

**Tests to write:**
1. `CommitMissionAsync` publishes `MissionControlIntent` with correct fields; no DDS writer referenced.
2. ACK resolves commit: publish `MissionControlAckEvent`; await resolves `Success=true, NewVersion=2`.
3. Timeout still works: no ACK published → commit resolves `Success=false` after timeout.
4. Compile gate: `MissionEditorService.cs` has zero DDS references.
5. Existing `WorkflowTests` pass (after updating to bus).

---

## ✅ Batch Success Criteria

1. All tasks implemented per TASK-DETAIL.md specifications.
2. All tests with real behavioral assertions pass.
3. `dotnet build` succeeds for the full solution (0 errors).
4. `dotnet test Hrot.ClusterRunner.Tests` — 192/195 or better (same pre-existing 3 failures).
5. `dotnet test Hrot.ExCon.Tests` — all pass.
6. `ClusterScenarioPanel.cs` — zero DDS code references.
7. `MissionEditorService.cs` — zero DDS code references.

---

## 💡 Developer Insights Section

In your report, please explicitly answer:

1. **What issues were encountered?**
2. **What weak points were spotted in the codebase?**
3. **What design decisions were made beyond the spec?**
4. **Did any test reveal something unexpected?**

---

## 📄 Report Format

Submit to `.dev/packs-1/reports/BATCH-05-REPORT.md`:

```markdown
# BATCH-05 Report

## Status
[COMPLETE / PARTIAL]

## Tasks Completed
- PACK-E001: [summary]
- PACK-E002: [summary]

## Test Results
[Paste dotnet test summary output]

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Beyond Spec
### Unexpected Findings from Tests

## Files Changed
[List]
```
