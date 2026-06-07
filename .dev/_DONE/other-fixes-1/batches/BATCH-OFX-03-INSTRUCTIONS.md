# BATCH-OFX-03: Remaining Fixes (group-maneuvers, eqs-2, visual-asset-comparison, utility-ai)

**Batch Number:** BATCH-OFX-03  
**Tasks:** OFX-007, OFX-008, OFX-013, OFX-014, OFX-015, OFX-016, OFX-017, OFX-020, OFX-021, OFX-026  
**Source:** `.dev/other-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/other-fixes-1/TASK-TRACKER.md`  
**Priority:** OFX-007/OFX-014 (HIGH severity algorithm bugs), then the rest in order

---

## Onboarding & Workflow

This is the final batch. It covers 4 folders:
- **group-maneuvers** (OFX-007, OFX-013, OFX-014, OFX-026): Squad perception, role assignment, phase sequencer
- **eqs-2** (OFX-016, OFX-017, OFX-021): EQS analyzer location, ingress cache pruning, raycast skip-guard
- **visual-asset-comparison** (OFX-008, OFX-020): Dashed outline, edge midpoint badge position
- **utility-ai** (OFX-015): Emitter round-trip test with Roslyn

Work in priority order: OFX-007, OFX-014, OFX-013, OFX-017, OFX-016, OFX-021, OFX-015, OFX-026, OFX-008, OFX-020.

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/other-fixes-1/TASK-DETAIL.md` -- all 10 tasks
2. **Squad Design:** Find `Squad_Coordination_Design_v1_1.md` §4 (for OFX-007, OFX-013, OFX-014)
3. **EQS Design:** Find `EQS_Design_v1.3` §7.4 (for OFX-017, OFX-021); find `TASK-EQS-020` (for OFX-016)
4. **Visual Asset Comparison Design:** Find `Visual_Asset_Comparison_Detailed_Design.md` §6.4 (for OFX-008, OFX-020)
5. **Utility AI Design:** Find `Utility_AI_Editor_Design_v1_2.md` §8.2/§12 (for OFX-015)
6. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
7. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
```
mcp_codebase-memo_list_projects()   // step 0 -- always first
mcp_codebase-memo_get_architecture({ "project": "<name>" })  // step 1
mcp_codebase-memo_search_graph({ "project": "<name>", "name_pattern": "<symbol>" })
mcp_codebase-memo_get_code_snippet({ "project": "<name>", "qualified_name": "<fn>" })
```

---

## MANDATORY WORKFLOW (per task, in order)

For **each task**:
1. **Define success condition** before implementing
2. **Implement the fix**
3. **Write tests** -- behavioral, not vacuous
4. **Run tests for affected project** after each task
5. **Fix ALL failures** before moving to next task

---

## Tasks

### Task 1: OFX-007 -- MergeContact position tied to max-threat, not most-recent sighting (HIGH)

**Task Definition:** [OFX-007](../TASK-DETAIL.md#ofx-007----squadperceptionmergesystemmergecontact-ties-3d-position-to-max-threat-ownership-not-most-recent-sighting)

**Success Condition:** Position is updated when `lastSeenTick > span[i].LastSeenTick`, independent of threat score comparison. Tests assert position updates with newer lower-threat sighting.

**What to do:**
1. Read `SquadPerceptionMergeSystem.MergeContact`
2. Add a separate `if (lastSeenTick > span[i].LastSeenTick)` guard for position/lastSeenTick update (independent of the `if (threatScore > ...)` guard)
3. Write test: two sightings of same contact; second sighting is newer, lower-threat, different position; assert merged contact has position from second sighting

**Tests Required:**
- `MergeContact_NewerLowerThreat_UpdatesPosition`

---

### Task 2: OFX-014 -- PhaseSequencer >= off-by-one + dwellTimeoutTicks==0 immediate abort (HIGH)

**Task Definition:** [OFX-014](../TASK-DETAIL.md#ofx-014----phasesequenceradvance-uses--off-by-one-and-treats-dwelltimeoutticks0-as-immediate-abort)

**Success Condition:** `Advance` uses strict `>` (not `>=`); `dwellTimeoutTicks==0` means "never timeout". Tests probe exact boundary ticks.

**What to do:**
1. Read `PhaseSequencer.Advance`
2. Change `>=` to `>` on the dwell comparison
3. Add special case: `if (dwellTimeoutTicks == 0) return false;` (never advance on zero)
4. Write tests:
   - `Advance_AtExactDwellTick_DoesNotAdvance` (boundary: `currentTick - entered == dwellTimeout`, should NOT advance)
   - `Advance_OneTick_AfterDwell_DoesAdvance` (one tick past boundary, SHOULD advance)
   - `Advance_DwellTimeoutZero_NeverAdvances`

**Tests Required:**
- All three tests above

---

### Task 3: OFX-013 -- RoleSlotAssignmentPrimitive leaves stale RoleIds (MEDIUM)

**Task Definition:** [OFX-013](../TASK-DETAIL.md#ofx-013----roleslotassignmentprimitive-leaves-stale-roleids-for-unassigned-members)

**Success Condition:** `AssignRoles` clears all `state.Roles` entries before the greedy write-back. Tests verify unassigned members get `RoleId = 0` (not previous-phase value).

**What to do:**
1. Read `RoleSlotAssignmentPrimitive.AssignRoles`
2. Zero out `state.Roles` before the greedy loop (mirror `ThreatMatrixAssignmentSystem`)
3. Write test: run assignment where one member cannot be assigned (all-non-positive score for that member); assert that member's `RoleId == 0` (not the stale previous value)

**Tests Required:**
- `AssignRoles_UnassignableMember_RoleIdClearedToZero`

---

### Task 4: OFX-017 -- EqsResultIngressTranslator child-entity cache never pruned (MEDIUM)

**Task Definition:** [OFX-017](../TASK-DETAIL.md#ofx-017----brain-side-eqsresultingresstranslator-child-entity-cache-is-never-pruned-on-sensor-removal)

**Success Condition:** `PollIngress` handles `NotAliveDisposed` samples by removing the entry from `_childEntityCache`. Re-subscription after removal re-populates the cache. Test verifies no stale entity is returned after disposal.

**What to do:**
1. Read `EqsResultIngressTranslator.PollIngress`
2. Add `NotAliveDisposed` handling: `_childEntityCache.Remove(key)` when `!sample.IsValid`
3. Write test: register entity -> process NotAliveDisposed -> query cache -> assert entry removed; then send a new live sample -> assert cache repopulated

**Tests Required:**
- `PollIngress_NotAliveDisposed_RemovesCacheEntry`

---

### Task 5: OFX-016 -- EQS002 diagnostic points at method, not offending identifier (MEDIUM)

**Task Definition:** [OFX-016](../TASK-DETAIL.md#ofx-016----eqs002-purity-diagnostic-points-at-the-method-not-the-offending-identifier)

**Success Condition:** EQS002 squiggle points to the impure identifier (`id.GetLocation()`) not the method symbol. Test triggers EQS002 and asserts location is within the method body.

**What to do:**
1. Read `EqsTemplatePurityAnalyzer.AnalyzeNamedType`
2. Change `Diagnostic.Create(EQS002, generatorOverload.Locations.FirstOrDefault(), ...)` to `Diagnostic.Create(EQS002, id.GetLocation(), ...)`
3. Add a test: analyzer test using a template that reads impure state; assert EQS002 is reported at the location of the offending read expression

**Tests Required:**
- EQS002 analyzer test asserting the diagnostic location is within the method body (not at the method declaration)

---

### Task 6: OFX-021 -- EQS cross-tick raycast polling skip-guard absent (LOW)

**Task Definition:** [OFX-021](../TASK-DETAIL.md#ofx-021----eqs-cross-tick-raycast-polling-skip-guard-absent-full-pipeline-re-runs-every-awaiting-tick)

**Success Condition:** `EvaluateSensor` returns early when `evalState.Phase == AwaitingRaycasts` (after polling the ring buffer). Test verifies generation phase doesn't re-run while awaiting.

**What to do:**
1. Read `EqsSolverSystem.EvaluateSensor`
2. Add early-return branch: `if (evalState.Phase == _AwaitingRaycasts) { PollRaycasts(); return; }`
3. Write test: start evaluation, advance to AwaitingRaycasts phase, call EvaluateSensor again; verify generation step count did NOT increment (generation was skipped)

**Tests Required:**
- `EvaluateSensor_AwaitingRaycasts_SkipsGeneration`

---

### Task 7: OFX-015 -- Utility emitter round-trip test vacuous (MEDIUM)

**Task Definition:** [OFX-015](../TASK-DETAIL.md#ofx-015----utility-emitter-round-trip-test-is-vacuous-no-roslyn-parse---reflect---structural-equality)

**Success Condition:** Test builds a `UtilityDecisionAsset` model -> emits C# -> parses with Roslyn -> reflects a new model -> asserts structural equality with original.

**What to do:**
1. Read `UtilityFluentEmitterTests.cs` and emitter code
2. Find the Roslyn compilation + reflection utilities available in the test project
3. Add a round-trip test: create a minimal `UtilityDecisionAsset` with at least 2 considerations, emit it, parse the C# with `Microsoft.CodeAnalysis.CSharp`, compile and reflect a `UtilityDecisionAsset` from the emitted code, compare to original
4. The comparison must check consideration names, weights, and context types -- not just byte equality of the raw string

**Tests Required:**
- `EmitAndRoundTrip_UtilityDecisionAsset_StructuralEquality`

---

### Task 8: OFX-026 -- AssignmentSlot round-trip test omits Flags field (LOW)

**Task Definition:** [OFX-026](../TASK-DETAIL.md#ofx-026----assignmentslot-layout-round-trip-test-omits-the-flags-field-it-was-specified-to-pin)

**Success Condition:** `AssignmentSlotArray_GetSlot_RoundTrip` test writes `Flags=0x05` and reads it back, asserting byte-exact match.

**What to do:**
1. Read `AssignmentSlotLayoutTests.cs`
2. Add `Flags = 0x05` to the write-back and assert it reads back as `0x05`

**Tests Required:**
- Extend existing `AssignmentSlotArray_GetSlot_RoundTrip` with Flags assertion

---

### Task 9: OFX-008 -- Comparison annotation outline drawn solid, not dashed (HIGH spec-drift)

**Task Definition:** [OFX-008](../TASK-DETAIL.md#ofx-008----comparison-annotation-outline-drawn-solid-design-requires-a-dashed-stroke)

**Success Condition:** `DrawAnnotation` emits a dashed stroke (manual dash loop) with inverse-zoom-stable dash sizing. Tests verify the correct draw-list calls are made.

**What to do:**
1. Read `ComparisonAnnotationRenderer.DrawAnnotation`
2. Replace `AddRect(..., ImDrawFlags.None, 2f)` with a dashed-stroke implementation:
   - Compute dash/gap sizes based on inverse zoom (scale = `1f / zoomLevel`)
   - Iterate along the rectangle perimeter, emitting `AddLine` segments for each dash
3. Write test: mock draw-list; call `DrawAnnotation`; assert `AddLine` was called (not just `AddRect`); assert dash lengths are inverse-zoom-scaled

**Tests Required:**
- `DrawAnnotation_OutputsDashedStroke_NotSolid`

---

### Task 10: OFX-020 -- connection_changed badge draws at first endpoint, not midpoint (MEDIUM)

**Task Definition:** [OFX-020](../TASK-DETAIL.md#ofx-020----connection_changed-edgemidpoint-badge-draws-at-the-first-endpoint-not-the-geometric-midpoint)

**Success Condition:** `ResolveDrawNode` for `EdgeMidpoint` placement returns a synthetic position at the average of the two endpoint positions. Tests assert the midpoint is computed correctly.

**What to do:**
1. Read `ComparisonAnnotationRenderer.ResolveDrawNode`
2. For `AnnotationPlacement.EdgeMidpoint`: resolve BOTH endpoints, compute midpoint = `(posA + posB) / 2f`, return that position
3. Refactor `DrawAnnotation` to accept an explicit position instead of a node (or add an overload) if needed
4. Write test: set up two nodes with known positions; call `DrawAnnotation` for an edge annotation; assert draw position is the midpoint of the two nodes

**Tests Required:**
- `DrawAnnotation_EdgeMidpoint_DrawsAtGeometricMidpoint`

---

## Quality Standards

- **OFX-007**: Test must have TWO distinct sightings (newer = lower threat, different position) and assert on the position of the merged contact
- **OFX-014**: Test must probe the exact `currentTick - enteredTick == dwellTimeout` boundary (should NOT advance) AND the `+1` case (SHOULD advance) AND the `dwellTimeout==0` case
- **OFX-015**: Must use real Roslyn parse + reflection, not string matching
- **OFX-008 / OFX-020**: If a test of the draw-list render path is not feasible (ImGui draws are fire-and-forget with no mock), at minimum assert the helper method outputs the correct data (dash params, midpoint coords)

## Report

Write report to:
`d:\WORK\IOS-IG-SimHost-FDP\.dev\other-fixes-1\reports\BATCH-OFX-03-REPORT.md`

## Workspace Root
`d:\WORK\IOS-IG-SimHost-FDP`
