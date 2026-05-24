# BATCH-17: Phase 16 Execution Flaw Repairs + GZ049 Settings Scopes

**Batch Number:** BATCH-17
**Tasks:** TASK-GZ043, TASK-GZ044, TASK-GZ045, TASK-GZ046, TASK-GZ047, TASK-GZ049
**Phase:** Phase 16 (Execution Flaw Repairs) + Phase 17 partial (Settings Scopes)
**Estimated Effort:** 10-14 hours
**Priority:** HIGH — GZ043-GZ047 are P1 blockers for the interactive remote-viewer scenario
**Dependencies:** BATCH-16 (Fdp.Diagnostics.Contracts and Fdp.Diagnostics.Network assemblies must exist)

---

## Mandatory Reading (IN ORDER)

1. **Task Definitions:** `.dev/gizmos-1/TASK-DETAIL.md` — sections TASK-GZ043 through TASK-GZ047, and TASK-GZ049
2. **Design Document:** `.dev/gizmos-1/DESIGN.md`
3. **Feedback:** `.dev/gizmos-1/feedback2.md` (identifies the five structural flaws this batch fixes)
4. **Coding Standards:** `AGENTS.md`
5. **Previous Review:** `.dev/gizmos-1/reviews/BATCH-16-REVIEW.md`
6. **Tracker:** `.dev/gizmos-1/TASK-TRACKER.md`

## Source Code Locations

- **PipelineTarget enum:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PipelineTarget.cs`
- **MapDescriptors (IGCapabilitiesAnnounce):** `Hrot/Network/Hrot.Network.NED/MapDescriptors.cs`
- **IGCapabilitiesPublisherSystem:** `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesPublisherSystem.cs`
- **IgApplication:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- **SimHostApp:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- **GizmoInteractionProxyTool:** `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
- **IMapTool:** `FDP/Toolkits/Fdp.Toolkit.Vis2D/Abstractions/IMapTool.cs`
- **MapCanvas:** `FDP/Toolkits/Fdp.Toolkit.Vis2D/MapCanvas.cs` (or `FDP/Engine/Fdp.Presentation/Vis2D/MapCanvas.cs` — verify path)
- **GizmoInteractionEvents:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
- **GizmoInteractionEgressSystem:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressSystem.cs`
- **GizmoInteractionIngressSystem:** `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressSystem.cs`
- **GizmoInteractionBatch (in Fdp.Diagnostics.Network):** `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionBatch.cs`
- **DebugGizmoLayer:** `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs`
- **GizmoSettingsRegistry:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`
- **Test projects:**
  - `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/`
  - `Hrot/Network/Hrot.Network.NED.Tests/`
  - `FDP/Engine/Fdp.Presentation.Tests/`
  - `FDP/Toolkits/Fdp.Toolkits.Tests/`

## Build and Test Commands

```
dotnet build IOS-IG-SimHost.sln --no-incremental -clp:ErrorsOnly
dotnet test FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/Fdp.Diagnostics.Contracts.Tests.csproj
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj
dotnet test FDP/Engine/Fdp.Presentation.Tests/Fdp.Presentation.Tests.csproj
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
```

## Pre-existing Failures (Do NOT count against your work)
- ~26 in Fdp.Toolkits.Tests (non-gizmo areas)
- ~4 in Hrot.IG.Tests (CS011_ EntityInfoTranslator)
- ~3 in Fdp.Presentation.Tests
- ~20 in Hrot.SimHost.Tests

---

## Context

This batch fixes five structural execution flaws identified in `feedback2.md` and adds the
`SettingScope` feature from Phase 17. All Phase 16 tasks (GZ043-GZ047) are P1 blockers.

**Related Tasks:**
- [TASK-GZ043](.dev/gizmos-1/TASK-DETAIL.md) — PipelineTarget enum fix (flags arithmetic bug)
- [TASK-GZ044](.dev/gizmos-1/TASK-DETAIL.md) — IGCapabilitiesPublisherSystem DDS hygiene + reflection
- [TASK-GZ045](.dev/gizmos-1/TASK-DETAIL.md) — Wire composition roots (phantom network systems)
- [TASK-GZ046](.dev/gizmos-1/TASK-DETAIL.md) — Click-away commit hazard in GizmoInteractionProxyTool
- [TASK-GZ047](.dev/gizmos-1/TASK-DETAIL.md) — Screen-space coordinate mismatch
- [TASK-GZ049](.dev/gizmos-1/TASK-DETAIL.md) — SettingScope enum + GizmoSettingsRegistry scoped API

---

## Batch Objectives

Fix all five interaction pipeline and DDS contract flaws so the remote-viewer scenario is
functionally correct end-to-end. Add settings scopes for project/session lifecycle management.

---

## Mandatory Workflow

Complete tasks in sequence. Do NOT move to the next task until the current task compiles and
all tests pass:

1. **GZ043** → implement → write tests → ALL tests pass
2. **GZ044** → implement → write tests → ALL tests pass
3. **GZ045** → implement → write tests → ALL tests pass
4. **GZ046** → implement → write tests → ALL tests pass
5. **GZ047** → implement → write tests → ALL tests pass
6. **GZ049** → implement → write tests → ALL tests pass

Run `dotnet build IOS-IG-SimHost.sln` after each task, not just at the end.

**No stopping mid-batch to ask for permission to run tests or fix issues. You run the tests and
fix the root cause until everything is green, then write the report.**

---

## Tasks

### Task 1: GZ043 — Fix PipelineTarget Enum

**Task Definition:** Read TASK-GZ043 in `.dev/gizmos-1/TASK-DETAIL.md`

**File:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts/Primitives/PipelineTarget.cs` (MODIFY)

**Change:** Add `NodeGraph = 4` and update `All` from `3` to `7`.

**Test file:** `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` (UPDATE)
Add tests SC-GZ043-1 through SC-GZ043-5. Also search for any existing test that asserts
`PipelineTarget.All == Map2D | Viewport3D` and update it to include `NodeGraph`.

**Search for existing assertions:**
```
grep -r "PipelineTarget.All" FDP/ Hrot/
```
Update any test that references the old value.

**Required tests (SC-GZ043):**
- SC-GZ043-1: `Assert.Equal(PipelineTarget.All, PipelineTarget.Map2D | PipelineTarget.Viewport3D | PipelineTarget.NodeGraph);`
- SC-GZ043-2: `Assert.Equal((byte)4, (byte)PipelineTarget.NodeGraph);`
- SC-GZ043-3: `Assert.NotEqual(0, (int)(PipelineTarget.All & PipelineTarget.NodeGraph));`
- SC-GZ043-4: Both `Map2D` and `Viewport3D` bits still set in `All`
- SC-GZ043-5: `DebugPrimitive` with `TargetView = PipelineTarget.All` has byte pattern `0b00000111` at its PipelineTarget field offset

---

### Task 2: GZ044 — Fix IGCapabilitiesPublisherSystem

**Task Definition:** Read TASK-GZ044 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to modify:**
- `Hrot/Network/Hrot.Network.NED/MapDescriptors.cs` — add `RegisteredGizmosJson` field; change `SupportedShapes` from `byte` to `uint`
- `Hrot/Subsystems/Hrot.IG/Gizmos/IGCapabilitiesPublisherSystem.cs` — rewrite Execute with reflection-based capability derivation

**Key changes (read TASK-GZ044 for full detail):**
1. Add `[DdsManaged] public string RegisteredGizmosJson;` to `IGCapabilitiesAnnounce` after `TkbManifestJson`
2. Change `SupportedShapes` field type from `byte` to `uint` (field must be renamed `SupportedShapeMask` if it isn't already — check current name)
3. Update publisher constructor to remove registries, add `PipelineTarget supportedTargets = PipelineTarget.Map2D` parameter
4. Execute: derive `SupportedShapeMask` by reflecting over `DebugPrimitiveShape`; set `RegisteredGizmosJson = "[]"`; set `SupportedLayerMask = 0xFFFF`
5. Find any callers of the old constructor and update them

**IMPORTANT:** After changing `SupportedShapes` type from `byte` to `uint`, do a project-wide search for callers of `IGCapabilitiesAnnounce.SupportedShapes` (or whatever the field is currently named) and update them.

**Test file:** Create `Hrot/Network/Hrot.Network.NED.Tests/IGCapabilitiesPublisherSystemTests.cs`

**Required tests (SC-GZ044):**
- SC-GZ044-1: `IGCapabilitiesAnnounce` struct has a field named `RegisteredGizmosJson` of type `string` (reflection check)
- SC-GZ044-2: `Execute` sets `RegisteredGizmosJson = "[]"` (verified via capturing writer)
- SC-GZ044-3: Both `RegisteredGizmosJson` and `LayerTreeJson` exist as distinct fields (both non-null by reflection)
- SC-GZ044-4: `SupportedShapeMask` field is `uint` type (reflection check: `FieldType == typeof(uint)`)
- SC-GZ044-5: `SupportedShapeMask` has bit for value 10 (SpatialAnchor, if it exists) — or at minimum bits for all shapes 0-7 set
- SC-GZ044-6: `SupportedLayerMask == 0xFFFF`
- SC-GZ044-7: Second `Execute` call does NOT write again (once-only publish gated by `_published`)

---

### Task 3: GZ045 — Wire Composition Roots

**Task Definition:** Read TASK-GZ045 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to modify:**
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs`
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- Possibly `Hrot/Network/Hrot.Network.NED/IIgNetworkAdapter.cs` (add `GizmoInteractionWriter` property if absent)
- Possibly `Hrot/Network/Hrot.Network.NED/ISimHostNetworkAdapter.cs` (add `GizmoInteractionReader` if absent)

**Key actions (read TASK-GZ045 for full detail):**
1. In `IgApplication`: register `GizmoInteractionEgressSystem` with `_networkAdapter?.GizmoInteractionWriter`
2. In `IgApplication`: create `_ingressTranslator = new DebugPrimitivesIngressTranslator(...)` after `_gizmoBuffer` is created; call `_ingressTranslator?.PollAndApply()` in the Update loop
3. In `SimHostApp`: register `GizmoInteractionIngressSystem` before `DataDrivenGizmoSystem`

**Verify:** Before adding the wiring, check that `GizmoInteractionEgressSystem` has `[UpdateInPhase(SystemPhase.BeforeSync)]` (or whatever pre-simulation phase attribute it uses). If wrong, correct it.

**Test file:** Create `Hrot/Network/Hrot.Network.NED.Tests/CompositionRootWiringTests.cs`

**Required tests (SC-GZ045):**
- SC-GZ045-3: With `networkAdapter == null`, all three systems execute without throwing (use headless in-process setup with null adapter)
- SC-GZ045-4: After `PollAndApply()` with a mock reader supplying one `DebugPrimitivesBatch`, `_gizmoBuffer.GetFrame().Length > 0`

Note: SC-GZ045-1 (full round-trip via in-process DDS mock) and SC-GZ045-2 (spy translator) are complex integration tests. Implement SC-GZ045-3 and SC-GZ045-4 as unit tests with fakes/stubs. SC-GZ045-1 and SC-GZ045-2 are best-effort — skip with a `// SC-GZ045-1: full in-process round-trip requires live IgApplication+SimHostApp — integration test` comment if the composition root does not easily support headless construction.

---

### Task 4: GZ046 — Fix GizmoInteractionProxyTool Click-Away Hazard

**Task Definition:** Read TASK-GZ046 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to modify:**
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
- `FDP/Toolkits/Fdp.Toolkit.Vis2D/Abstractions/IMapTool.cs` — add `HandlePress` default method
- `FDP/Toolkits/Fdp.Toolkit.Vis2D/MapCanvas.cs` (or wherever `ProcessInputPipeline` / the press-routing code lives — verify path by reading the existing file)

**Key actions (read TASK-GZ046 for full detail):**
1. Add `default bool HandlePress(Vector2 worldPos, MouseButton button) => false;` to `IMapTool`
2. Route press events to active tool in `MapCanvas` press handler (before layer routing)
3. Add `_dragActive` field and `HandlePress` override to `GizmoInteractionProxyTool`
4. Update `HandleDrag` to gate on `_dragActive`
5. Update `HandleClick` to distinguish genuine drag-commit from click-away cancel

**IMPORTANT:** Do NOT introduce `goto` in `MapCanvas`. Use the existing control-flow pattern
(likely a `bool` flag or an early `if (consumed) return;` block). Read the existing
`ProcessInputPipeline` method to understand the existing pattern before modifying.

**Test file:** Create `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolClickAwayTests.cs`

**Required tests (SC-GZ046):** All 7 SC-GZ046 tests from TASK-GZ046 in TASK-DETAIL.md. Use
`FdpEventBus` and `null` canvas for tool tests (no real MapCanvas needed). For SC-GZ046-6, create
a mock `IMapTool` that counts `HandlePress` calls, push it onto a `MapCanvas`, and verify the
canvas calls `HandlePress` before routing to layers.

---

### Task 5: GZ047 — Fix Screen-Space Coordinate Mismatch

**Task Definition:** Read TASK-GZ047 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs` — add `CoordinateSpace Space` to `GizmoDragUpdateEvent` and `GizmoInteractionCommitEvent`
- `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs` — add `CoordinateSpace _space` field + constructor param; populate in events
- `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionEgressSystem.cs` — propagate `Space`
- `Hrot/Network/Hrot.Network.NED/Gizmos/GizmoInteractionIngressSystem.cs` — restore `Space`
- `FDP/Diagnostics/Fdp.Diagnostics.Network/GizmoInteractionBatch.cs` — add `Space` field
- `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` — pass `hitPrimitive.Space` when creating `GizmoInteractionProxyTool`

**Key constraint:** `GizmoInteractionStartedEvent` and `GizmoInteractionCancelEvent` do NOT get the `Space` field. After adding it to the `DragUpdate`/`Commit` events, check for any callers that construct these events and update them.

**Test file:** Add tests to `Hrot/Network/Hrot.Network.NED.Tests/GizmoInteractionTranslatorTests.cs`
(or create a new file `GizmoInteractionCoordinateSpaceTests.cs` in the same project)

**Required tests (SC-GZ047):** All 5 SC-GZ047 tests from TASK-GZ047 in TASK-DETAIL.md:
- SC-GZ047-1: ProxyTool created with `space = Screen` publishes `GizmoDragUpdateEvent.Space == Screen`
- SC-GZ047-2: DDS round-trip preserves `Space` field (egress writes, ingress reads)
- SC-GZ047-3: Existing tests still pass (no `space` param = defaults to `World`)
- SC-GZ047-4: Verify compile-time that `GizmoInteractionStartedEvent` has no `Space` field
- SC-GZ047-5: `Marshal.SizeOf<GizmoDragUpdateEvent>()` is valid (no gaps/unsafe layout issues)

---

### Task 6: GZ049 — Settings Scopes

**Task Definition:** Read TASK-GZ049 in `.dev/gizmos-1/TASK-DETAIL.md`

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/GizmoSettingsRegistry.cs`

**File to create:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Settings/SettingScope.cs`

**Key actions (read TASK-GZ049 for full detail):**
1. Create `SettingScope` enum (Global=0, Project=1, Session=2)
2. Add `_scopes` dictionary to `GizmoSettingsRegistry`
3. Extend `Write` with optional `scope` parameter
4. Add `GetScope` method
5. Extend `SaveToDisk` with optional `scope` filter parameter
6. Extend `LoadFromDisk` with optional `scope` parameter
7. Add `DiscardScope` method

**Test file:** Add SC-GZ049 tests to `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoSettingsRegistryTests.cs`
(check what the existing test class is named for GizmoSettingsRegistry)

**Required tests (SC-GZ049):** All 8 SC-GZ049 tests from TASK-GZ049. Use temp files for
`SaveToDisk`/`LoadFromDisk` tests (write to `Path.GetTempFileName()`, read back, delete after test).

---

## Quality Standards

**TEST QUALITY EXPECTATIONS:**
- NOT ACCEPTABLE: Tests that only verify "no exception thrown"
- REQUIRED: Tests that verify actual values (field content, count, enum value)
- REQUIRED: Tests that verify negative cases (e.g. click-away returns `false`, wrong scope not saved)
- REQUIRED: Regression tests for existing behavior (e.g. old `Write(hash, value)` callers still work)

**CODE QUALITY:**
- All new files: follow the namespace and coding conventions of adjacent files
- All paths must resolve: do NOT leave any `// TODO` stubs
- No magic numbers — use named constants or enum values

---

## Success Criteria

This batch is DONE when:
- [ ] GZ043: `PipelineTarget.NodeGraph = 4`, `All = 7`; 5 tests pass
- [ ] GZ044: `IGCapabilitiesAnnounce` has `RegisteredGizmosJson` string field + `SupportedShapeMask` is `uint`; 7 tests pass
- [ ] GZ045: Three wiring points registered in `IgApplication` and `SimHostApp`; SC-GZ045-3 and SC-GZ045-4 pass
- [ ] GZ046: Click-away cancel path implemented; 7 SC-GZ046 tests pass
- [ ] GZ047: `CoordinateSpace` field threaded through events + DDS batch; 5 SC-GZ047 tests pass
- [ ] GZ049: `SettingScope` enum + scoped API on `GizmoSettingsRegistry`; 8 SC-GZ049 tests pass
- [ ] Build: `dotnet build IOS-IG-SimHost.sln` → 0 errors
- [ ] All new tests pass; no new pre-existing failures introduced
- [ ] TASK-TRACKER.md updated (mark GZ043-GZ047 and GZ049 as done)
- [ ] Report submitted

---

## Report Submission

**Submit your report to:** `.dev/gizmos-1/reports/BATCH-17-REPORT.md`

**If you have questions:** `.dev/gizmos-1/questions/BATCH-17-QUESTIONS.md`

## Developer Insights (required in report)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Which of the five execution flaw fixes was most complex? What made it difficult?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** Did you discover any edge cases not mentioned in the spec?

**Q5:** Suggested commit message (FDP submodule + root repo separately).
