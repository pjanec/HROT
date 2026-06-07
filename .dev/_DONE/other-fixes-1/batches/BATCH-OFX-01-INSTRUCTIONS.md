# BATCH-OFX-01: anim-ctrl Fixes

**Batch Number:** BATCH-OFX-01  
**Tasks:** OFX-002, OFX-003, OFX-004, OFX-005, OFX-006, OFX-009, OFX-012, OFX-022, OFX-023  
**Source:** `.dev/other-fixes-1/TASK-DETAIL.md`  
**Tracker:** `.dev/other-fixes-1/TASK-TRACKER.md`  
**Priority:** HIGH -- OFX-002 (events never typed), OFX-003 (fake backend state wrong), OFX-004 (blend-out broken), OFX-005 (blend weight always 0)  
**Dependencies:** None

---

## Onboarding & Workflow

This batch covers all anim-ctrl defects:
1. **Algorithm** (OFX-002..005, OFX-009, OFX-012): Real behavior bugs in the animation system and fake backend
2. **SC-anchor** (OFX-006, OFX-022, OFX-023): Missing/vacuous tests for ANIM validators and animation behavior

Work in priority order: algorithm bugs first (OFX-002, OFX-003, OFX-004, OFX-005), then OFX-009, OFX-012, then SC-anchor tests (OFX-006, OFX-022, OFX-023).

### Required Reading (IN ORDER)
1. **Task Details:** `.dev/other-fixes-1/TASK-DETAIL.md` -- all 9 tasks
2. **Animation DD-1:** Find the Animation Design DD-1 under the anim-ctrl docs (search codebase graph)
3. **Animation DD-Fake:** Find DD-Fake under anim-ctrl docs
4. **Animation DD-5:** Find DD-5 (validator spec, §10 ANIM008/009/010/011)
5. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
6. **Code Standards:** `.dev/.guides/CODE-STANDARDS.md`

### Codebase Memory MCP (MANDATORY)
Use `mcp_codebase-memo_list_projects` then `mcp_codebase-memo_get_architecture`. Find symbols with `mcp_codebase-memo_search_graph`.

---

## MANDATORY WORKFLOW (per task, in order)

For **each task**:
1. **Define success condition** before implementing
2. **Implement the fix**
3. **Write tests** -- behavioral verification
4. **Run all tests** -- ALL must pass
5. **Fix failures at root cause**
6. Only then move to next task

---

## Tasks

### Task 1: OFX-002 -- NotifyEventEmitterSystem ignores Kind (HIGH)

**Task Definition:** [OFX-002](../TASK-DETAIL.md#ofx-002----notifyeventemittersystem-ignores-animnotifycategorykind---footstephitwindow-events-never-typed)

**Success Condition:** For each drained `RawNotifyEvent`, the system dispatches based on `Kind`: `Footstep` -> emit `FootstepEvent`, `HitWindowOpened` -> emit `HitWindowOpenedEvent`, generic -> emit `AnimNotifyEvent`. Tests must verify each typed event is emitted for the corresponding Kind.

**Tests Required:**
- Footstep `RawNotifyEvent` results in `FootstepEvent` emitted
- HitWindowOpened `RawNotifyEvent` results in `HitWindowOpenedEvent` emitted
- Generic `RawNotifyEvent` results in `AnimNotifyEvent` emitted

---

### Task 2: OFX-003 -- FakeAnimationBackend uses managed Dictionary not ECS component (HIGH)

**Task Definition:** [OFX-003](../TASK-DETAIL.md#ofx-003----fakeanimationbackend-stores-per-entity-state-in-a-managed-dictionary-not-the-tier-1-ecs-component)

**Success Condition:** Entity state is stored in the `FakeAnimBackendState` ECS component (not a managed Dictionary). `Tick` iterates via ECS query. `Initialize` injects `EntityRepository`.

**Tests Required:**
- After `RegisterEntity`, `FakeAnimBackendState` component is readable from the entity
- After `ResetWorld`, state is cleared

---

### Task 3: OFX-004 -- StopMontageOnSlot hard-clears instead of blend-out (HIGH)

**Task Definition:** [OFX-004](../TASK-DETAIL.md#ofx-004----stopmontageonslot-hard-clears-slots-instead-of-triggering-graceful-blend-out)

**Success Condition:** `StopMontageOnSlot` triggers graceful blend-out by setting `InBlendOutWindow=1`, adjusting `ElapsedSeconds` to `total - BlendOutTime`, and letting the slot complete naturally. Tests must verify the slot is still active after stop but `InBlendOutWindow==1`.

**Tests Required:**
- After `StopMontageOnSlot`, slot `InBlendOutWindow == 1` (not immediately cleared)
- Slot completes naturally after blend-out time

---

### Task 4: OFX-005 -- BlendWeight always 0 in AdvanceSlots (HIGH)

**Task Definition:** [OFX-005](../TASK-DETAIL.md#ofx-005----blendweight-never-computed-in-advanceslots---always-0)

**Success Condition:** `AdvanceSlots` computes `BlendWeight` using the three-branch formula (ramp-in / hold 1.0 / ramp-out) per DD-Fake §4.1. Tests must verify `BlendWeight > 0` during hold phase and `BlendWeight < 1` during ramp phases.

**Tests Required:**
- `BlendWeight` is 0 before blend-in time elapses
- `BlendWeight` is 1.0 during hold phase
- `BlendWeight` decreases during ramp-out phase

---

### Task 5: OFX-009 -- MontageQueueAdvanceSystem never crossfades (MEDIUM)

**Task Definition:** [OFX-009](../TASK-DETAIL.md#ofx-009----montagequeueadvancesystem-never-crossfades-advances-only-after-the-slot-goes-silent)

**Success Condition:** Queue advancement triggers on `InBlendOutWindow == 1` (not waiting for silence), and issues a `CrossfadeMontageOnSlot` call. Tests must verify the crossfade occurs while the slot is still active (not after it goes silent).

**Tests Required:**
- Queue advances when slot enters `InBlendOutWindow` (not after silence)
- `CrossfadeMontageOnSlot` is called for the next queue entry

---

### Task 6: OFX-012 -- Intent egress dirty-check omits ActionParams comparison (MEDIUM)

**Task Definition:** [OFX-012](../TASK-DETAIL.md#ofx-012----animation-intent-egress-dirty-check-omits-the-actionparams-blob-comparison)

**Success Condition:** `ScanAndPublish` publishes an update when the `ActionParams` blob changes even if `ActionInstanceId` is unchanged. Tests must verify same-instance-id param mutation triggers publication.

**Tests Required:**
- Same `ActionInstanceId` but different `ActionParams` blob triggers publication

---

### Task 7: OFX-006 -- ANIM008/009/010/011 validators missing (HIGH)

**Task Definition:** [OFX-006](../TASK-DETAIL.md#ofx-006----anim008009010011-validators-have-no-production-implementation-their-tests-are-vacuous-stubs)

**Success Condition:** ANIM008/009/010/011 validators are implemented per DD-5 §10 with real Blueprint-IR analysis. Tests feed real Blueprint graphs (positive and negative cases per rule). Tests no longer use hard-coded booleans.

**Tests Required:**
- ANIM008 positive/negative test with real graph
- ANIM009 positive/negative test with real graph
- ANIM010 positive/negative test with real graph
- ANIM011 positive/negative test with real graph

---

### Task 8: OFX-022 -- AdvanceFootsteps multi-emit and no stationary reset (LOW)

**Task Definition:** [OFX-022](../TASK-DETAIL.md#ofx-022----advancefootsteps-uses-a-while-loop-multi-emit-and-doesnt-bleed-off-distance-when-stationary)

**Success Condition:** `AdvanceFootsteps` resets `DistanceSinceLastFootstep = 0` in the stationary guard branch. The while-vs-if behavior is updated per DD-Fake §5.

**Tests Required:**
- Stationary entity has distance reset to 0 (not accumulated)

---

### Task 9: OFX-023 -- Missing ANC-P1-06 unit tests (LOW)

**Task Definition:** [OFX-023](../TASK-DETAIL.md#ofx-023----missing-anc-p1-06-unit-tests-tick_rampsaimblendweight-tick_completesstancetransition)

**Success Condition:** Add `Tick_RampsAimBlendWeight` and `Tick_CompletesStanceTransition` tests per ANC-P1-06 SC.

**Tests Required:**
- `Tick_RampsAimBlendWeight` verifies aim blend weight increases over ticks
- `Tick_CompletesStanceTransition` verifies stance commits on completion

---

## Quality Standards

- **OFX-002**: Each event type must be tested with actual Kind dispatch (not just "no exception")
- **OFX-005**: Must assert actual BlendWeight numeric values (e.g., `Assert.Equal(1.0f, weight)`)
- **OFX-006**: Must use real Blueprint IR graphs, not hard-coded bool flags

## Report

Write report to:
`d:\WORK\IOS-IG-SimHost-FDP\.dev\other-fixes-1\reports\BATCH-OFX-01-REPORT.md`

Create the `reports/` folder if it doesn't exist.

## Workspace Root
`d:\WORK\IOS-IG-SimHost-FDP`
