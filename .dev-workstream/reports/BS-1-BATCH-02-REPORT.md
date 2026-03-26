# BS-1-BATCH-02 Report

**Batch:** BS-1-BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2025-07-24  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TD-3 | ✅ Complete | Fixed `AuthorityExtensions.HasAuthority` to return `true` when `NetworkAuthority` absent; 4 unit tests added |
| TD-1 | ✅ Complete | `NetworkEntityMap` registration wired in `ScenarioDirector.SpawnEntity`; `EntityMap` property exposed on `HeadlessDemoApp`; integration test constructor updated |
| BS1-T005 | ✅ Complete | `WeaponFireIntentEgressTranslator` created; 4 unit tests (SC-1..SC-4) |
| BS1-T006 | ✅ Complete | `WeaponFireRequestIngressTranslator` created; 4 unit tests (SC-1..SC-4) |
| BS1-T007 | ✅ Complete | `FireProcessingSystem` refactored; all call sites updated; 6 unit tests |
| TD-2 | ✅ Complete | UrbanAmbush milestone assertions restored |
| Batch01-Review-Issue3 | ✅ Complete | `AimAndFireExecutorTests` happy-path extended with `WeaponIndex` and `channel.Status` assertions |

---

## 🧪 Testing Results

**Unit Tests Passed:** all  
**Integration Tests Passed:** all

| Project | Passed | Failed |
|---------|--------|--------|
| `FDP.Toolkit.Replication.Tests` | 38 | 0 |
| `FDP.Toolkit.Combat.Tests` | 38 | 0 |
| `Bagira.SimHost.Tests` | 340 | 0 |
| `Fdp.Examples.UrbanCombat.Tests` | 29 | 0 |

**Key Test Scenarios Verified:**
- ✅ BS1-T005 SC-1: Egress translator writes `WeaponFireRequest` when authoritative
- ✅ BS1-T005 SC-2: Egress translator skips when not authoritative
- ✅ BS1-T005 SC-3: Egress translator is no-op on empty bus
- ✅ BS1-T005 SC-4: Egress translator skips when shooter ID not in entity map
- ✅ BS1-T006 SC-1: Ingress translator publishes `WeaponFireIntent` when both entities known
- ✅ BS1-T006 SC-2: Ingress skips sample when shooter unknown
- ✅ BS1-T006 SC-3: Ingress skips sample when target unknown
- ✅ BS1-T006 SC-4: `PollIngress` with null participant is no-op and does not throw
- ✅ BS1-T007: `FireProcessingSystem` spawns bullet from `WeaponFireIntent`
- ✅ BS1-T007: `FireProcessingSystem` publishes `WeaponFireNotification` after bullet exists
- ✅ BS1-T007: `FireProcessingSystem` skips when shooter or target entity unknown
- ✅ TD-3: `HasAuthority` returns `true` when `NetworkAuthority` absent
- ✅ TD-2: `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` asserts HIT, CAPABILITY LOST, HSM TRANSITION, INTERACTION, FLEE

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**Issue 1 — `IDescriptorTranslator` namespace ambiguity in `Bagira.SimHost`.**  
The interface lives in `Fdp.Interfaces` (not `ModuleHost.Core.Abstractions`). The wrong using was initially added because `ModuleHost.Core.Abstractions` is the home of the *old* version of the interface (with `IDataReader`/`IDataWriter` parameters). The fix was to add `using Fdp.Interfaces;` plus keep `using ModuleHost.Core.Abstractions;` for `IEntityCommandBuffer`.

**Issue 2 — `IDdsWriter<T>` ambiguity in `Bagira.SimHost`.**  
Both `Bagira.IG.Abstractions.IDdsWriter<T>` and `Bagira.Map.Common.Dds.IDdsWriter<T>` exist and differ: the Map-Common version adds `DisposeInstance(T key)`. `DdsWriterAdapter<T>` implements the Map-Common interface. Following the pattern from `Bagira.IG` (which uses the IG-Abstractions version) pulled in both namespaces and caused the ambiguity. Resolution: dropped `using Bagira.IG.Abstractions;` from the production translator; updated `CapturingWriter<T>` in tests to implement `DisposeInstance` as a no-op; used `Bagira.Map.Common.Dds.IDdsWriter<T>` exclusively in `Bagira.SimHost`.

**Issue 3 — `in sample.Data` is not addressable (CS8156).**  
`DdsReader<T>` loan enumerator exposes `Data` as a property, making the expression a non-addressable temporary. Fixed by assigning to a local before passing `in`.

**Issue 4 — `BallisticsAndHitScenario` also calls `new FireProcessingSystem()`.**  
`Fdp.Examples.Scenarios` is a standalone example that never needed `NetworkEntityMap` before. Once `FireProcessingSystem` required the map, the project failed to build. Fixed by adding `FDP.Toolkit.Replication` project reference, creating a local `NetworkEntityMap`, registering shooter/target with fixed network IDs (1/2), and switching the injected event from `FireRequestEvent` to `WeaponFireIntent`.

**Issue 5 — TD-1: entities were registered in the world but never in `_entityMap`.**  
`AimAndFireExecutor` looks up entity IDs via `NetworkEntityMap`; without registration the map returns `0/0`, and `FireProcessingSystem` silently skips the event. Resolution: added `_entityMap?.Register(_nextNetId++, entity)` in `ScenarioDirector.SpawnEntity` using a sequential counter starting at 1.

---

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- **Two `IDdsWriter<T>` interfaces** in the same solution (`Bagira.IG.Abstractions` vs `Bagira.Map.Common.Dds`) are structurally identical except for `DisposeInstance`. Adding both to projects that reference `Bagira.IG` and `Bagira.Map.Common` will always produce this ambiguity. A future consolidation to a single shared interface (or an explicit using alias convention) would remove the fragility.
- **`AuthorityExtensions.HasAuthority` returning `false` when no `NetworkAuthority` is present** was a latent bug masked by the fact that most unit tests bypassed authority checks altogether. The contract was documented as "assume local authority" but the implementation disagreed. The fix is low-risk but the discrepancy existed undetected through Batch 01.
- **`FireProcessingSystem` now requires `NetworkEntityMap` at construction** but callers like standalone example scenarios have to create an artificial map just to compile. A factory helper or a "local-only" mode flag would reduce boilerplate for non-network scenarios.

---

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **`WeaponFireIntentEgressTranslator` implements `IDescriptorTranslator` directly** rather than extending `CycloneNativeEventTranslator<,>`. The base class's `ScanAndPublish` is not virtual and does not support per-event authority filtering. The direct implementation pattern is already established by `NavigationIntentEgressTranslator`; this is consistent.
- **`WeaponFireRequestIngressTranslator` accepts `DdsParticipant?` (nullable) in its production constructor.** This avoids a separate test-only constructor and keeps the null-participant → no-op contract explicit. Alternative: a `bool testMode` flag, but that's less idiomatic.
- **`ScenarioDirector` uses an auto-incrementing `_nextNetId` counter.** Considered using entity handles converted to `long` directly, but that would couple network IDs to internal memory addresses. Sequential integers from 1 are stable, predictable in tests, and match how a real server would allocate IDs.
- **`BallisticsAndHitScenario` registers entities under fixed IDs (1, 2)**, not dynamic allocation. This is fine because the scenario is a self-contained demo with exactly one shooter and one target.

---

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **AllInOne topology double-fire risk**: In an AllInOne process `FireProcessingSystem` (Input phase) will consume all `WeaponFireIntent` events before `WeaponFireIntentEgressTranslator.ScanAndPublish` runs in the Egress phase. The bus will be empty when the egress translator polls, so it produces no DDS traffic. This is correct behaviour (the Muscle-side system already fired locally), but it means the egress translator is effectively a no-op in AllInOne mode. This is documented in the class XML comment.
- **Direction fallback in `FireProcessingSystem`**: when shooter and target are at the same position (distance ≈ 0), normalizing the zero vector produces NaN. Added an explicit fallback to `Vector3.UnitX` to avoid propagating NaN into bullet velocity components.
- **`WeaponFireNotification` must be registered in `EntityRepository` before `FireProcessingSystem` can publish it.** Tests that create a world and add `FireProcessingSystem` without registering the event would panic at runtime. All new test worlds register `WeaponFireNotification` explicitly.

---

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- **`WeaponFireIntentEgressTranslator.ScanAndPublish`** calls `view.ConsumeEvents<WeaponFireIntent>()` which drains the bus. In an AllInOne topology this always returns an empty span (as noted above), so the overhead is a single empty-span iteration. No concern.
- **`NetworkEntityMap.TryGetEntity`** is a dictionary lookup per event. Fire events are low-frequency (one per weapon per engagement), so no concern.
- **`WeaponFireRequestIngressTranslator.PollIngress`** iterates a DDS loan and calls `ProcessSample` for each sample. Fire messages are low-frequency. The `in` pass-by-reference avoids copying the `WeaponFireRequest` struct on each call.
- No allocations on the hot path in any of the new translators.

---

## ⚠️ Outstanding Issues / Next Steps
- None blocking. All Batch 02 tasks are complete and all test suites pass.
- **Suggested follow-up (not in scope for this batch):** Consolidate the two `IDdsWriter<T>` interfaces into one shared definition to prevent future ambiguity.
- **Suggested follow-up:** `BallisticsAndHitScenario` now uses `WeaponFireIntent` via an artificial `NetworkEntityMap`. A brief comment explaining this design choice would help the next developer.
