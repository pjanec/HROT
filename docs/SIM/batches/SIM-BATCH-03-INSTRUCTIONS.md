# SIM-BATCH-03: EntityMission Translators (Phase S4.2)

**Batch Number:** SIM-BATCH-03  
**Tasks:** TASK-S4.2  
**Phase:** S4  
**Estimated Effort:** 6 hours (0.75 days)  
**Priority:** HIGH  
**Dependencies:** S4.1 (Behavior Toolkit Integration)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! With the ECS kernel topological structure complete under `SimulationLogicModule`, we now need to provide the translation layer for DDS `EntityMission` data so that our systems can receive behavior directives from IOS and publish progress back.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/design/TASK-DETAILS-SIMHOST.md#task-s42-implement-entitymissiontranslator-and-entitymissionegresstranslator`
3. **Edge Cases:** `docs/design/EDGE-CASES-AND-MITIGATIONS.md` - Please make sure you are aware of how DDS state tracking interacts with ECS lifecycles.

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost/Translators/`
- **Application Bootstrapper:** `Bagira.SimHost/Program.cs`
- **Test Project:** `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

DDS applications communicate intention via `EntityMission` topics. We need SimHost to monitor the `EntityMission` DDS topic, setting or removing the internal `Bagira.DDS.DataModel.EntityMission` component on the correct entity via the `IManagedTranslator` interface on the ingress.
Conversely, when `MissionAdapterSystem` acts on the ECS state setting the mission active or failing it, we want an egress translator `IEgressTranslator` to fire and broadcast those changes locally owned entities back to DDS using dirty flag queries.

---

## 🎯 Batch Objectives
- Implement `EntityMissionTranslator` (Ingress)
- Implement `EntityMissionEgressTranslator` (Egress)
- Register both in `Program.cs` inside the pipeline.
- Test that local ECS component reads and updates translate directly to DDS messages, and vice versa.

---

## ✅ Tasks

### Task 1: Implement EntityMission Translators (TASK-S4.2)

**File 1:** `Bagira.SimHost/Translators/EntityMissionTranslator.cs` (Ingress)
**File 2:** `Bagira.SimHost/Translators/EntityMissionEgressTranslator.cs` (Egress)
**File 3:** `Bagira.SimHost/Program.cs` 

**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s42-implement-entitymissiontranslator-and-entitymissionegresstranslator)

**Description:**
Create the DDS adapters required to convert incoming DDS `EntityMission` topics to ECS `EntityMission` payload objects, and vice versa. 

**Requirements:**
- The Ingress must implement `IManagedTranslator` subscribing to `EntityMission`.
  - On valid data reception, `.SetComponent(entity, s.Data);`
  - On `InstanceState.NotAliveDisposed`, `.RemoveComponent<EntityMission>(entity);`
- The Egress must implement `IEgressTranslator` publishing to `EntityMission`.
  - It only writes out the ECS state if `.Changed<EntityMission>()` marks it dirty, and is filtered to `.With<NetworkAuthority>()` to only bounce data the host owns.
- Both translators must be registered properly in `Program.cs`.

*Note: The actual logic that modifies the ECS `EntityMission` is built tomorrow in Task S4.3. Do not worry about `MissionAdapterSystem` for now.*

**Tests Required:**
- ✅ A unit/integration test suite simulating DDS ingestion modifying an ECS component.
- ✅ A unit/integration test suite verifying that modifying the underlying component locally and triggering the egress queue correctly publishes out the DDS sample.
- ✅ A test proving empty ECS topics or non-dirty objects do not publish unwanted DDS traffic.

---

## 🧪 Testing Requirements
Focus strictly on the translation boundaries. Given an entity ID generated in a mock context, mock a DDS pub taking the appropriate payload and observe it appearing in the destination `EntityRepository` and vice versa.

---

## 📊 Report Requirements

**Q1 Threading & Ownership Edge Cases:** Did you notice any possible race conditions regarding network entities disappearing before the update arrives? If so, does the `NetworkEntityMap` safety check handle it cleanly?
- **Q2 Dirty Flag Optimization:** ECS relies on explicit updates to trigger `Changed()`. Do you foresee any issues testing missing states if the dirty flag isn't manipulated explicitly by the kernel tests? 
- **Q3 Unknown Network IDs:** What happens in your ingress implementation if the `EntityId` inside the sample does not map successfully via `_entityMap.TryGetEntity()`?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-S4.2 completed.
- [ ] Both ingress and egress classes perfectly adhere to the respective interfaces.
- [ ] Tests successfully round-trip the component payload with and without the `Changed()` flag trigger.
- [ ] Report submitted via markdown file.

---

## ⚠️ Common Pitfalls to Avoid
- Failing to restrict Egress queries with `With<NetworkAuthority>()`. SimHost should only update its own tasks. If omitted, we will enter an infinite broadcast loop with the network.
- Redefining the ECS `EntityMission` wrapper class instead of utilizing the `Bagira.DDS.DataModel.EntityMission` type.

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md) - See TASK-S4.2
