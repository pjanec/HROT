# BS-1-BATCH-04 Instructions

**Workstream:** BS-1 (Brain / Muscle Node Separation)  
**Estimated Effort:** 10–12 hours  

## Onboarding
If you are new to this workstream, please read:
1. **[DEV-LEAD-GUIDE.md](../guides/DEV-LEAD-GUIDE.md)**: Describes the TDD and CQRS workflow standards.
2. **[BS-1-DESIGN.md](../../docs/brain-split/BS-1-DESIGN.md)**: High-level architecture of the Brain/Muscle split.
3. **[BS-1-TASK-DETAIL.md](../../docs/brain-split/BS-1-TASK-DETAIL.md)**: The definitive specification for the tasks below.

**Important Developer Workflow Note:**
As per our developer guidelines, DO NOT implement more than what is requested in the spec. Build exactly what is described, verify it with tests, and note down any missing dependencies, edge cases, or potential tech debt for the lead to triage in the batch report.

## Tech Debt Items (Complete First)

Please start by resolving these technical debt items accumulated from previous batches.

### TD-6: FireProcessingSystem Authority Gate
- **Problem:** `FireProcessingSystem` currently processes *all* `WeaponFireIntent` events it sees. It relies entirely on the fact that only the Muscle node will be running this system. If a misconfigured node runs it, it could spawn duplicate bullets.
- **Fix:** Add a check to ensure `FireProcessingSystem` only spawns bullets if the node is authoritative over the shooter entity (`HasAuthority`). You can look at `DamageSystem` for reference.
- **Validation:** Add a test verifying that `WeaponFireIntent` is skipped if the local node lacks authority over the shooter.

### TD-7: RaycastRequest.IgnoreEntity Documentation
- **Problem:** `HitResolutionSystem` assumes `RaycastRequest.IgnoreEntity` carries the shooter's network ID (as populated by `BallisticsSystem`). This convention is undocumented and brittle.
- **Fix:** Update the XML documentation for `RaycastRequest.IgnoreEntity` in the Physics toolkit to explicitly state this contract/assumption.
- **Validation:** Visual verification of XML doc update.

### TD-8: Physics Parallel Arrays Test
- **Problem:** `HitResolutionSystem` relies on `batch.Requests[i]` and `batch.Hits[i]` being perfectly parallel arrays (the hit at index `i` corresponds to the request at index `i`). 
- **Fix:** Add a unit test to the `RaycastSolverSystem` (or relevant physics solver tests) that verifies this parallel-array contract explicitly.
- **Validation:** The new unit test passes.

---

## Core Tasks (Phase 3: Detonation & Damage Assessment)

These tasks construct the detonation and damage CQRS pipeline, taking the `DetonationNotification` emitted in BATCH-03 and propagating it through the network to become health updates.

### 1. BS1-T011 Create MunitionDetonationEgressTranslator
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t011--create-munitiondetonationegress-translator](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t011--create-munitiondetonationegress-translator)
- **Goal:** Consume `DetonationNotification` on the Muscle node and publish `MunitionDetonation` via DDS.

### 2. BS1-T012 Create DamageAssessmentModule
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t012--create-damageassessmentmodule](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t012--create-damageassessmentmodule)
- **Goal:** An ingress translator on the Brain node that polls `MunitionDetonation` DDS topic, runs hit validation, and publishes local `DamageAssessedEvent`.

### 3. BS1-T013 Create DamageAssessedEgressTranslator
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t013--create-damageassessedegress-translator](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t013--create-damageassessedegress-translator)
- **Goal:** Consume `DamageAssessedEvent` on the Brain node and publish `EntityHitDamage` via DDS.

### 4. BS1-T014 Create EntityHitDamageIngressTranslator + HealthApplicationSystem
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t014--create-entityhitdamage-ingress-translator-and-healthapplicationsystem](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t014--create-entityhitdamage-ingress-translator-and-healthapplicationsystem)
- **Goal:** Complete the loop on the Muscle node: ingest `EntityHitDamage` DDS messages into local `DamageEvent`, which `DamageSystem` will process. Also implement `HealthApplicationSystem` to translate resulting health updates back. (Wait, follow the spec carefully for the system responsibilities).

### 5. BS1-T015 Create EntityDamageEgressTranslator
- **Spec:** [BS-1-TASK-DETAIL.md#bs1-t015--create-entitydamageegress-translator](../../docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t015--create-entitydamageegress-translator)
- **Goal:** Brain node egress that pushes the confirmed `EntityDamage` state to IG/IOS for UI rendering.

---

## Deliverables
1. Source code implementation of TD-6..TD-8 and BS1-T011..BS1-T015.
2. Full unit test coverage for all new translators and refactored systems.
3. Write a report at `.dev-workstream/reports/BS-1-BATCH-04-REPORT.md` answering:
   - What challenges did you encounter?
   - Any design gaps or edge cases not covered by the spec?
   - Did you have to introduce any temporary hacks or deviations?