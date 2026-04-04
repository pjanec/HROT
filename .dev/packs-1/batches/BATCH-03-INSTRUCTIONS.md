# BATCH-03: Anti-Corruption Layer — Split MissionControl + Strip NetworkEntityMap

**Batch Number:** BATCH-03
**Tasks:** PACK-P001, PACK-P003
**Phase:** Phase 4 (Anti-Corruption Layer)
**Estimated Effort:** 12–15 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 ✅, BATCH-02 ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes the remaining Phase 4 Anti-Corruption Layer work:

**PACK-P001 — Split MissionControlRequestSystem:** The current `MissionControlRequestSystem` is a
monolith that owns DDS readers/writers AND domain logic AND JSON deserialization. It must be split
into three clean pieces following the Translator + Logic pattern:
1. `MissionControlIngressTranslator` — polls DDS, deserializes JSON, publishes `MissionControlIntent`
2. `MissionControlAckEgressTranslator` — consumes `MissionControlAckEvent` from bus, writes DDS ACK
3. `MissionControlExecutionSystem` — pure domain logic, no DDS whatsoever

The two new domain events (`MissionControlIntent` class and `MissionControlAckEvent` struct)
need to be defined in `FDP.Toolkit.Behavior`.

**PACK-P003 — Strip NetworkEntityMap from HitResolutionSystem and AimAndFireExecutor:** The
combat/physics core systems must not know about network IDs. Replace `long` shooter/hit IDs in
`DetonationNotification` and `WeaponFireIntent` with ECS `Entity` handles. Move ID resolution
to the egress translators.

### Required Reading (IN ORDER)

1. **Developer Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Architecture & Design:** `.dev/packs-1/DESIGN.md` — read §Phase 4 §4.A and §4.C carefully
3. **Task Specifications:** `.dev/packs-1/TASK-DETAIL.md` — sections PACK-P001 and PACK-P003
4. **Previous Reviews:** `.dev/packs-1/reviews/BATCH-01-REVIEW.md` and `BATCH-02-REVIEW.md`

### Source Code Locations

**PACK-P001:**
- Current monolith: grep for `MissionControlRequestSystem` in `Hrot.SimHost/`
- JSON deserialization: look for `System.Text.Json` usage in the current class
- Event bus: `FdpEventBus` (FDP.Kernel)
- New event types location: `FDP/Toolkits/FDP.Toolkit.Behavior/Events/`
- New translator locations: `Hrot.SimHost/Network/Ingress/` and `Hrot.SimHost/Network/Egress/`
- New execution system location: `Hrot.SimHost/Systems/MissionControlExecutionSystem.cs`

**PACK-P003:**
- `FDP/Toolkits/FDP.Toolkit.Combat.Contracts/` — `DetonationNotification.cs`
- `FDP/Toolkits/FDP.Toolkit.Combat.Events/` — grep for `WeaponFireIntent`
- `FDP/Toolkits/FDP.Toolkit.Physics/Systems/` — grep for `HitResolutionSystem`
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/` — grep for `AimAndFireExecutor`
- `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs`
- `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs`

### Test Projects

- `Hrot.SimHost.Tests/` — main test project
- `Hrot.SimHost.Integration.Tests/` — integration tests (verify MissionControl pipeline end-to-end)
- `FDP/Toolkits/FDP.Toolkit.Combat.Tests/` — combat system tests
- `FDP/Toolkits/FDP.Toolkit.Physics.Tests/` (if it exists) — or test inside SimHost.Tests

### Report Submission

**When done, submit your report to:**
`.dev/packs-1/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/packs-1/questions/BATCH-03-QUESTIONS.md`

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

Both tasks are independent and can be done in either order. Recommended: P003 first (smaller
surface area, establishes the Entity-handle pattern), then P001 (larger, builds on the
pattern).

### PACK-P003 — Strip NetworkEntityMap from HitResolutionSystem and AimAndFireExecutor

See: `TASK-DETAIL.md#pack-p003`

**Summary (5 steps):**

1. **Modify `DetonationNotification`:** Replace `long ShooterNetworkEntityId` and
   `long HitEntityNetworkId` (or similar long fields) with `Entity Shooter` and `Entity Target`
   ECS handles.

2. **Modify `WeaponFireIntent`:** Replace shooter/target `long` net IDs with local `Entity` handles.

3. **Refactor `HitResolutionSystem`:** Remove the `NetworkEntityMap` overload. Always emit
   `DetonationNotification` (with local Entity handles). In an offline context the event is
   simply ignored by translators that aren't listening.

4. **Refactor `AimAndFireExecutor`:** Remove `NetworkEntityMap` from constructor.

5. **Update egress translators:** Inject `NetworkEntityMap` into
   `MunitionDetonationEgressTranslator` and `WeaponFireIntentEgressTranslator`. Resolve
   local `Entity` handles → `long` net IDs before writing DDS. If the entity is NOT in the
   map: log a warning and skip (do NOT throw).

**Key constraint:** After this task, `FDP.Toolkit.Physics` and `FDP.Toolkit.Combat` must have
**zero** references to `NetworkEntityMap`.

**Tests to write:**
1. `HitResolutionSystem` offline (no-arg ctor): produces `DetonationNotification` with Entity handles.
2. `MunitionDetonationEgressTranslator`: resolves Entity → net ID correctly.
3. `MunitionDetonationEgressTranslator`: skips (no throw) when entity not in map.
4. Compile gate: `FDP.Toolkit.Physics.csproj` and `FDP.Toolkit.Combat.csproj` have zero `NetworkEntityMap` references.

---

### PACK-P001 — Split MissionControlRequestSystem into Translator + Logic

See: `TASK-DETAIL.md#pack-p001`

**Summary (4 steps):**

1. **Define new domain events** in `FDP/Toolkits/FDP.Toolkit.Behavior/Events/MissionControlCqrsEvents.cs`:
   ```csharp
   public class MissionControlIntent
   {
       public Guid RequestId;
       public long TargetEntityId;
       public long BaseVersion;
       public MissionCommandUnion Payload;
   }
   public struct MissionControlAckEvent
   {
       public Guid RequestId;
       public int ErrorCode;
       public string? ErrorMessage;
       public long NewVersion;
   }
   ```
   Register both in the FdpEventBus event registry.

2. **Create `MissionControlIngressTranslator`** in `Hrot.SimHost/Network/Ingress/`:
   - Polls `DdsReader<MissionControlRequest>`
   - Deserializes the JSON `Parameters` field
   - Publishes `MissionControlIntent` to `FdpEventBus`

3. **Create `MissionControlAckEgressTranslator`** in `Hrot.SimHost/Network/Egress/`:
   - Consumes `MissionControlAckEvent` from bus
   - Writes `MissionControlAck` to DDS

4. **Refactor existing `MissionControlRequestSystem` → `MissionControlExecutionSystem`**:
   - Remove all DDS fields (`DdsReader`, `DdsWriter`, `DdsParticipant`)
   - Remove all `System.Text.Json` references
   - Consume `MissionControlIntent` from bus
   - Publish `MissionControlAckEvent` to bus
   - Delete the `DdsWriter<EntityMission>` (already handled by
     `EntityMissionEgressTranslator` automatically)
   - The internal test constructor should accept `FdpEventBus` directly

**KEY CONSTRAINT:** `MissionControlExecutionSystem` must have **zero** references to `DdsReader`,
`DdsWriter`, `DdsParticipant`, or `System.Text.Json` after the refactor.

**Tests to write:**
1. Unit: `MissionControlExecutionSystem` is DDS-free — instantiate with bus only; publish intent; assert `MissionPlanQueue` updated + `MissionControlAckEvent` published.
2. Unit: Error path — invalid `BaseVersion` → non-zero ErrorCode, queue not mutated.
3. (Optional) Integration test: end-to-end DDS→ingress→execution→egress round-trip.
4. Grep: `MissionControlExecutionSystem.cs` has zero `EntityMission` DDS writer references.

---

## ✅ Batch Success Criteria

1. All tasks implemented per TASK-DETAIL.md specifications.
2. All tests written with real behavioral assertions pass.
3. `dotnet build` succeeds for the full solution (0 errors).
4. `dotnet test` succeeds for:
   - `Hrot.SimHost.Tests/`
   - `FDP.Toolkit.Combat.Tests/`
   - `Hrot.SimHost.Integration.Tests/` (smoke — no new failures)
5. Grep `FDP.Toolkit.Physics` and `FDP.Toolkit.Combat` for `NetworkEntityMap` → zero results.
6. Grep `MissionControlExecutionSystem.cs` for `DdsReader`, `DdsWriter`, `DdsParticipant`,
   `System.Text.Json` → zero results.

---

## 💡 Developer Insights Section

In your report, please explicitly answer:

1. **What issues were encountered?** (compile errors, unexpected dependencies, etc.)
2. **What weak points were spotted in the codebase?** (fragile patterns, missing abstractions,
   excessive coupling you noticed beyond the task scope)
3. **What design decisions were made beyond the spec?** (choices you made to resolve ambiguities)
4. **Did any test reveal something unexpected about the current behavior?**

---

## 📄 Report Format

Submit to `.dev/packs-1/reports/BATCH-03-REPORT.md` using this structure:

```markdown
# BATCH-03 Report

## Status
[COMPLETE / PARTIAL — list any incomplete tasks]

## Tasks Completed
- PACK-P003: [brief summary]
- PACK-P001: [brief summary]

## Test Results
[Paste dotnet test summary output]

## Developer Insights
### Issues Encountered
[...]
### Weak Points Spotted
[...]
### Design Decisions Beyond Spec
[...]
### Unexpected Findings from Tests
[...]

## Files Changed
[List of all modified/created/deleted files]
```
