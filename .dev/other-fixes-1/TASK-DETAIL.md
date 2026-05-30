# Other Subsystems -- Design Conformance Fixes (Task Detail)

Implementation-correctness issues found auditing the codebase against the design docs in
`anim-ctrl`, `eqs-2`, `navig-2`, `utility-ai`, `visual-asset-comparison`, `group-maneuvers`.
For an independent AI coding agent (Claude Sonnet 4.6) to fix. **Reference the design sections;
do not duplicate them** -- open the cited `§` + code symbol before fixing.

> Companion tracker: [TASK-TRACKER.md](./TASK-TRACKER.md)

## Method & confidence

Found via a 19-cluster **hunt + adversarial-verify** workflow (Sonnet agents, codebase-memory
graph tools only -- no `search_code`/grep). 85 candidates were produced; **28 survived adversarial
re-verification** (each refuter re-read the actual code + design + the cluster's `DEBT-TRACKER.md`
and defaulted to "not a defect" unless it positively re-confirmed). The 28 collapse to **26 distinct
issues** (two bugs were independently found by two clusters). Severities below are the refuter's
*corrected* severity.

**Confidence caveat:** these are verified by the audit agents, not independently re-read by a human.
Each cites the exact design `§` and code symbol; confirm against those before changing.

**Clusters with ZERO findings (held up to scrutiny):** `uai-runtime-core`, `uai-sourcegen`,
`eqs-generators-tests`, `vac-sanitizers`, `anim-events-catalog`. The Utility-AI scoring/aggregation/
source-gen core, the EQS generators/scorers/LOS math, the VAC sanitizer determinism, and the animation
event catalog are solid.

Each item: **ID** -- `OFX-NNN`; **Lens** (algorithm / integration-seam / reachability / invariant /
dual-path / spec-drift / SC-anchor, where SC-anchor = a passing test that asserts against a stub or
bypasses the real path); **Folder**; **Design**; **Code**; **Gap**; **Fix**.

---

## CRITICAL / HIGH

### OFX-001 -- Nav backend auto-select checks only the start point; Hybrid is dead code
- **Severity:** High | **Lens:** algorithm | **Folder:** navig-2
- **Design:** Navigation_Design_v2_0.md §5.2 (Auto: both ends near road -> RoadGraph; mixed -> Hybrid/splice; else -> Navmesh)
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/PathfindingSolverSystem.cs` `SelectBackend` (L103-132)
- **Gap:** `SelectBackend` tests only `start2D` proximity to the road network; `end2D` is never used. It returns `NavRoadGraph` whenever the start is near a road, even if the end is far -> the road-graph Dijkstra runs on an off-network endpoint (Unreachable or navmesh leg ignored). `NavigationBackend.Hybrid` is never returned -> the spliced path is dead code. Not in DEBT-TRACKER; no Auto-heuristic mixed-endpoint test.
- **Fix:** add the end-point proximity check; select Hybrid on mixed proximity (or implement the splice and route to it).

### OFX-002 -- `NotifyEventEmitterSystem` ignores `AnimNotifyCategory.Kind` -> Footstep/HitWindow events never typed
- **Severity:** High | **Lens:** algorithm | **Folder:** anim-ctrl
- **Design:** DD-1 §11 (dispatch on `raw.Kind`: Footstep->FootstepEvent, HitWindowOpened->HitWindowOpenedEvent, Generic->AnimNotifyEvent)
- **Code:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/NotifyEventEmitterSystem.cs` `Execute` (L52-59)
- **Gap:** Every drained `RawNotifyEvent` is unconditionally published as `AnimNotifyEvent`; `n.Kind` is never read. No `FootstepEvent`/`HitWindowOpenedEvent`/`HitWindowClosedEvent` is ever emitted. DEBT D-05 (OPEN) documents *footstep enrichment* deferral only -- it does not cover the missing Kind switch or the HitWindow categories. Subscribers to those typed events get nothing.
- **Fix:** add the `Kind` dispatch switch per §11; emit the typed events. (Footstep position enrichment may stay deferred per D-05, but the dispatch must exist.)

### OFX-003 -- FakeAnimationBackend stores per-entity state in a managed `Dictionary`, not the Tier-1 ECS component
- **Severity:** High | **Lens:** algorithm | **Folder:** anim-ctrl
- **Design:** DD-Fake §1 (Principles 2/3), §3, §4; ANC-P1-01/02/06
- **Code:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/FakeAnimationBackend.cs` (ctor, `RegisterEntity`, `Tick`)
- **Gap:** All state lives in `Dictionary<uint, EntityBehavioralState>` (managed class). The correctly-defined `FakeAnimBackendState` component (`[ComponentId(240)]`) is never populated/read; no `EntityRepository` is injected; `Initialize` is a no-op; `Tick` iterates `_entityStates.Values` not an ECS query. The AAR-recording fast-path, entity-inspector integration, and `ResetWorld` isolation from DD-Fake §2/§9 are entirely absent. Undocumented in DEBT-TRACKER. (Test/dev-only backend, so bounded to tooling/observability, not AI behaviour -- hence High not Critical.)
- **Fix:** move state into the `FakeAnimBackendState` component; inject `EntityRepository`; convert `Tick` to a query.

### OFX-004 -- `StopMontageOnSlot` hard-clears slots instead of triggering graceful blend-out
- **Severity:** High | **Lens:** algorithm | **Folder:** anim-ctrl
- **Design:** DD-Fake §3.3 (force blend-out: set `BlendOutTime`, rewind `ElapsedSeconds` to `total-blendOut`, set `InBlendOutWindow=1`, complete naturally over ticks)
- **Code:** `FakeAnimationBackend.cs` `StopMontageOnSlot` (L161-175)
- **Gap:** Sets `IsActive=0` and `ElapsedSeconds=0` on every active slot immediately; `@params.BlendOutTime` is never read. The natural-completion / `InBlendOutWindow` path is never observable -> `AnimationStateReporterSystem` never sees blend-out-based EndReason. Test `StopMontageOnSlot_Succeeds` is vacuous (stops a slot with nothing playing).
- **Fix:** implement the blend-out rewind per §3.3.

### OFX-005 -- `BlendWeight` never computed in `AdvanceSlots` -> always 0
- **Severity:** High | **Lens:** algorithm | **Folder:** anim-ctrl
- **Design:** DD-Fake §4.1 (three-branch blend weight: ramp-in / hold 1.0 / ramp-out)
- **Code:** `FakeAnimationBackend.cs` `AdvanceSlots` (L236-290); init at `PlayMontageOnSlot` (L156, `BlendWeight=0`)
- **Gap:** `slot.BlendWeight` is initialised to 0 and never written in the tick. Any consumer of `MontagePlaybackState.BlendWeight` (state reporter, diagnostic window) sees 0 for the whole montage. Note: `AdvanceAim` *does* implement the equivalent ramp -- the pattern exists, it's just missing on the slot path. No test asserts `BlendWeight`.
- **Fix:** add the three-branch blend-weight computation per §4.1.

### OFX-006 -- ANIM008/009/010/011 validators have no production implementation; their tests are vacuous stubs
- **Severity:** High | **Lens:** SC-anchor | **Folder:** anim-ctrl *(found by both `anim-tkb-descriptor` and `anim-blueprint-primitives` clusters)*
- **Design:** DD-5 §10 (ANIM008/009/011 static control-flow analysis); ANC-P5-06 SC ("positive/negative test per rule"); ANIM010 emitted-AST Pattern-A/B test
- **Code:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/AnimationValidatorTests.cs` (L56-257); production `Validation/AnimationValidators.cs` (only ANIM006/007 + ANIM001-003 stubs)
- **Gap:** The tests hard-code booleans (`hasEnqueue=true`, `usesUnsafePattern=false`, `isValidForContext=true`) and call `ctx.ReportWarning/Error` inline -- no production validator is invoked. ANIM010/011 set their predicate to a constant so the branch is dead and `Assert.Empty` always passes. There is **no** production ANIM008/009/010/011 validator anywhere (the file the test comment names doesn't exist). Not in DEBT-TRACKER.
- **Fix:** implement the ANIM008/009/010/011 validators per DD-5 §10 with real Blueprint-IR analysis; rewrite the tests to feed real graphs (positive + negative per rule).

### OFX-007 -- `SquadPerceptionMergeSystem.MergeContact` ties 3D position to max-threat ownership, not most-recent sighting
- **Severity:** High | **Lens:** algorithm | **Folder:** group-maneuvers *(found by both `squad-primitives-assignment` and `squad-perception-maneuver`)*
- **Design:** Squad_Coordination_Design_v1_1.md §4 ("keep max threat score **and** most-recent 3D position" -- independent invariants; bridge-deck vs street example)
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadPerceptionMergeSystem.cs` `MergeContact` (L118-143)
- **Gap:** `PositionX/Y/Z` are written only inside `if (threatScore > span[i].ThreatScore)`. A newer-but-lower-threat sighting updates `LastSeenTick` but leaves the position frozen at the older higher-threat sighting -> the merged pool carries a stale 3D position for a contact that has moved (feeds wrong fire/role/slot decisions). DEBT-TRACKER empty. Test `TwoMembersSeeSameContact...` never asserts position.
- **Fix:** guard the position update by `lastSeenTick > span[i].LastSeenTick` (independent of the threat-max update).

### OFX-008 -- Comparison annotation outline drawn solid, design requires a dashed stroke
- **Severity:** High | **Lens:** spec-drift | **Folder:** visual-asset-comparison
- **Design:** Visual_Asset_Comparison_Detailed_Design.md §6.4 (dashed 2px outline, 3px outward, 6px dash / 4px gap, inverse-zoom-stable)
- **Code:** `Hrot/Editor/Hrot.Editor.AiShared/Comparison/Rendering/ComparisonAnnotationRenderer.cs` `DrawAnnotation` (L198-199, `AddRect(... ImDrawFlags.None, 2f)`)
- **Gap:** Solid rectangle, no dash, no inverse-zoom scaling. The dashed outline is the only visual differentiator from the solid selection/validation/executing outlines -> comparison annotations are visually ambiguous, defeating the design rationale. The stale comment on L196 still claims "Dashed". Tests assert only `AnnotationRecord` fields; `DrawAnnotation` is never exercised (stub DrawList is null).
- **Fix:** draw a dashed stroke (manual dash loop or equivalent) with inverse-zoom dash sizing per §6.4.

---

## MEDIUM

### OFX-009 -- `MontageQueueAdvanceSystem` never crossfades; advances only after the slot goes silent
- **Severity:** Medium | **Lens:** algorithm | **Folder:** anim-ctrl
- **Design:** DD-1 §7 step 3 (on `InBlendOutWindow` + next entry exists -> request `CrossfadeMontageOnSlot`, increment index)
- **Code:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Systems/MontageQueueAdvanceSystem.cs` `Execute` (L81-121)
- **Gap:** Gate is `trackingActive && !IsAnySlotActive(handle)` -- it acts only once the slot is fully dark, then stages a `PlayMontageOnSlot` (not a crossfade). No `QuerySlotState`/`InBlendOutWindow` check. Result: >=1 frame of silence between queue entries instead of a seamless crossfade. `CrossfadeMontageOnSlot` and `QuerySlotState` don't exist on the implemented `IAnimationBackend` at all (DD-1 §3 specifies both). D-11 (RESOLVED) covered the executor stub, not this trigger condition.
- **Fix:** add `QuerySlotState`/`CrossfadeMontageOnSlot` to the backend; trigger advance on `InBlendOutWindow` per §7.

### OFX-010 -- `FakeDtCrowdProvider` separation threshold + formula + `NearbyAgentCount` range deviate from design
- **Severity:** Medium | **Lens:** algorithm | **Folder:** navig-2
- **Design:** DD-Fake-Nav §4.3 (separation at `(combinedR*1.5)^2`, push `delta.Normalized/max(sqrt(d),0.01)*SeparationWeight`; `NearbyAgentCount` at `(combinedR*4)^2`)
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeDtCrowdProvider.cs` `Update` (L144-162)
- **Gap:** Single threshold `dist < sumR` (1.0x = actual overlap) used for both the force and the count; force is `overlap*0.5/dt`. Agents in the 1.0-1.5x band get no separation; `NearbyAgentCount` is always 0 for non-overlapping-but-near agents (wrong snapshot/diagnostic value). Fake/best-effort component, so Medium.
- **Fix:** use the two designed thresholds and the designed push formula.

### OFX-011 -- `FakeNavmeshProvider.BlockPolygon` is layer-agnostic; design requires per-layer scoping
- **Severity:** Medium | **Lens:** spec-drift | **Folder:** navig-2
- **Design:** DD-Fake-Nav §3.4 (`BlockPolygon(int polygonId, NavLayerMask layer)`)
- **Code:** `FakeNavmeshProvider.cs` `IFakeNavmeshProviderTestApi.BlockPolygon` (L16) + impl (L210-226)
- **Gap:** Interface + impl take only `polygonId`; impl blocks the polygon in *every* layer. Tests cannot block an Infantry-layer polygon while leaving the Vehicle-layer copy walkable (the S3_TwoLayersRouting intent).
- **Fix:** add the `NavLayerMask layer` parameter and scope the block.

### OFX-012 -- Animation intent egress dirty-check omits the `ActionParams` blob comparison
- **Severity:** Medium | **Lens:** dual-path | **Folder:** anim-ctrl
- **Design:** DD-2 §2.4 (dirty = `ActionInstanceId` change **combined with** full `ActionParams` blob compare)
- **Code:** `Hrot/Subsystems/Hrot.Animation.Replication/Translators/Channels/AnimationChannelIntentEgressTranslator.cs` `ScanAndPublish` (L53-93); identical in `LookAtChannelIntentEgressTranslator.cs`
- **Gap:** Publication gated solely on `ActionInstanceId` inequality; the 32-byte `Params` blob is never compared. In-place param mutation that reuses the same `ActionInstanceId` is silently dropped (Muscle runs stale params). Low triggering probability + TransientLocal QoS partial recovery -> Medium. Related risk noted as D-27 for the queue channel.
- **Fix:** add the blob comparison to the dirty signal per §2.4 (or enforce bump-on-mutation).

### OFX-013 -- `RoleSlotAssignmentPrimitive` leaves stale `RoleId`s for unassigned members
- **Severity:** Medium | **Lens:** dual-path | **Folder:** group-maneuvers
- **Design:** TASK-SQD-P1-03 ("same greedy assignment as ThreatMatrixAssignmentSystem -- DO NOT duplicate"); SC-P1-03-2 ("re-running overwrites state.Roles")
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/RoleSlotAssignmentPrimitive.cs` `AssignRoles` (L45-65); cf. `Utility/Group/ThreatMatrixAssignmentSystem.cs` `Run` (L68-75)
- **Gap:** `AssignRoles` never clears `state.Roles` first; `GreedyMatrixAssigner` returns -1 when best score <=0 and the write-back skips those members -> previous-phase `RoleId` persists. `ThreatMatrixAssignmentSystem` correctly zeroes all slots before the pass -- the two "shared" paths diverge. Edge-case input (all-non-positive scores) -> Medium. DEBT-TRACKER empty.
- **Fix:** clear/zero `state.Roles` before the greedy write-back (match the ThreatMatrix path).

### OFX-014 -- `PhaseSequencer.Advance` uses `>=` (off-by-one) and treats `dwellTimeoutTicks==0` as immediate abort
- **Severity:** Medium | **Lens:** invariant | **Folder:** group-maneuvers
- **Design:** TASK-SQD-P1-04 SC-P1-04-2 (strict `>`); `SquadHsmShell` docs ("0 = never")
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Squad/Primitives/PhaseSequencer.cs` `Advance` (L103)
- **Gap:** `currentTick - PhaseEnteredTick >= dwellTimeoutTicks` -- expires one tick early; and with the documented `dwellTimeoutTicks=0` ("never") default on a fresh `default(SquadCognitiveState)` (both ticks 0) it fires `0>=0` immediately -> every maneuver using the default shell aborts on tick 1. Tests use deltas that don't probe the boundary.
- **Fix:** use strict `>`; special-case `dwellTimeoutTicks==0` as "no timeout".

### OFX-015 -- Utility emitter round-trip test is vacuous (no Roslyn-parse -> reflect -> structural equality)
- **Severity:** Medium | **Lens:** SC-anchor | **Folder:** utility-ai
- **Design:** Utility_AI_Editor_Design_v1_2.md §8.2/§12 SC-P5-1 ("model -> emit -> Roslyn-parse -> reflect -> structural equality")
- **Code:** `Hrot/Editor/Hrot.Utility.Editor.Tests/UtilityFluentEmitterTests.cs`
- **Gap:** Tests only assert byte-stable re-emit + `Assert.Contains` on output text. No test parses emitted C# back via Roslyn, reflects a `UtilityDecisionAsset`, and compares to the original -> a dropped consideration / mistranslated context / zeroed weight would not be caught. BATCH-15 scoped to "first half"; the Roslyn tier is undocumented-missing. (Emitter code not shown wrong -> Medium.)
- **Fix:** add the Roslyn-parse -> reflect -> structural-equality round-trip over the starter-pack corpus.

### OFX-016 -- EQS002 purity diagnostic points at the method, not the offending identifier
- **Severity:** Medium | **Lens:** spec-drift | **Folder:** eqs-2
- **Design:** TASK-EQS-020 (analyzer flags the impure access so the dev knows what to fix)
- **Code:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/EqsTemplatePurityAnalyzer.cs` `AnalyzeNamedType` (~L116-119)
- **Gap:** `Diagnostic.Create(EQS002, generatorOverload.Locations.FirstOrDefault(), ...)` uses the method-symbol location; the matched `IdentifierNameSyntax id` (the actual impure read) is used for matching but not for the squiggle location -> the warning lands under `Build` rather than the offending expression. No test triggers EQS002 at all.
- **Fix:** pass `id.GetLocation()`; add an EQS002-triggering test.

### OFX-017 -- Brain-side `EqsResultIngressTranslator` child-entity cache is never pruned on sensor removal
- **Severity:** Medium | **Lens:** integration-seam | **Folder:** eqs-2
- **Design:** TASK-EQS-038 §C/§E (NotAliveDisposed -> remove cache entry; lazy rebuild on miss)
- **Code:** `Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs` `PollIngress` (L46-103); `Dispose` is empty
- **Gap:** `if (!sample.IsValid) continue;` skips NotAliveDisposed samples; `_childEntityCache` has no removal path. A re-spawned sensor at the same `(ParentNetworkId, LocalChildIndex)` key returns the stale dead `Entity`; `IsAlive` guard prevents a crash but results are silently dropped indefinitely. The Muscle-side `EqsSensorConfigIngressTranslator` has the symmetric `Remove` -- the Brain-side is missing it (design §C specified it only for the config translator).
- **Fix:** handle NotAliveDisposed on the result cache (remove the entry) so the scan rebuilds.

### OFX-018 -- `ReplanTimeBudget` guard absent; replan bounded only by `MaxReplans` count
- **Severity:** Medium | **Lens:** algorithm | **Folder:** navig-2
- **Design:** Navigation_Design_v2_0.md §3.4 (`ReplanCount < MaxReplans AND elapsed < ReplanTimeBudget`); §13.1 `MoveToParams` has a 4-byte `ReplanTimeBudget`
- **Code:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs` `Execute` (L168-210); `Navigation/NavigationActions.cs` `MoveToParams` (L33-77)
- **Gap:** `MoveToParams` has no `ReplanTimeBudget` field at all; the replan guard checks only `ReplanCount < effectiveMax`. The time-budget cut-off the design requires doesn't exist (slow replans run to the count limit). Hard failure still eventually occurs via the count -> Medium.
- **Fix:** add `ReplanTimeBudget` to `MoveToParams` and the elapsed-time guard.

### OFX-019 -- `FollowPathExecutor` doesn't map `FailedBlocked` to Failure -> stuck Running forever
- **Severity:** Medium | **Lens:** dual-path | **Folder:** navig-2
- **Design:** Navigation_Design_v2_0.md §6.1 (`FailedBlocked` -> MoveCompletedEvent + BTree Failure); §3.2 (FollowPath inherits MoveTo following semantics)
- **Code:** `FDP/Toolkits/Fdp.Toolkits/Navigation/Executors/FollowPathExecutor.cs` `Execute` (L40-58); cf. `MoveToExecutor.cs` (L95-107, handles it)
- **Gap:** `FollowPathExecutor` switch maps FailedInvalidHandle/Unreachable/NoPath/NoLayer -> Failure but has no `FailedBlocked` case -> it falls to default (Running). A frustrated follow keeps returning Running indefinitely. Asymmetric with `MoveToExecutor`. Tests never exercise the FailedBlocked path.
- **Fix:** add the `FailedBlocked` -> Failure case (mirror `MoveToExecutor`).

### OFX-020 -- `connection_changed` EdgeMidpoint badge draws at the first endpoint, not the geometric midpoint
- **Severity:** Medium | **Lens:** spec-drift | **Folder:** visual-asset-comparison
- **Design:** Visual_Asset_Comparison_Detailed_Design.md §6.4 (badge floats at the edge's midpoint)
- **Code:** `Comparison/Rendering/ComparisonAnnotationRenderer.cs` `ResolveDrawNode` (L206-213)
- **Gap:** For `AnnotationPlacement.EdgeMidpoint` it returns `TryFindNode(elementId[..sep])` = nodeA; `DrawAnnotation` then draws at nodeA's position. The true midpoint between the two nodes is never computed (`DrawAnnotation` only takes one node). Test asserts only the `Placement` enum, never the draw position.
- **Fix:** compute the midpoint (synthetic position = average of the two nodes) and draw there; may require `DrawAnnotation` to accept an explicit screen position.

---

## LOW

### OFX-021 -- EQS cross-tick raycast polling skip-guard absent: full pipeline re-runs every awaiting tick
- **Severity:** Low (performance, not correctness) | **Lens:** algorithm | **Folder:** eqs-2
- **Design:** EQS_Design_v1.3 §7.4 (sensors in `_AwaitingRaycasts` skip re-generation, poll the ring buffer, return early)
- **Code:** `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` `EvaluateSensor` (L84-268)
- **Gap:** No `if (evalState.Phase == _AwaitingRaycasts) -> poll & skip` branch; the full pipeline (Generation..ScoreExpensive + ray re-submission) re-runs every tick while awaiting. Verified **correctness-neutral** (deterministic rayIds dedupe; budget-gated re-submission), so the cost is redundant work, not wrong results -> Low.
- **Fix:** add the phase skip-guard so awaiting sensors poll the ring buffer and return early.

### OFX-022 -- `AdvanceFootsteps` uses a while-loop (multi-emit) and doesn't bleed off distance when stationary
- **Severity:** Low | **Lens:** algorithm | **Folder:** anim-ctrl
- **Design:** DD-Fake §5 (single `if` per tick; reset `DistanceSinceLastFootstep=0` in the below-min-speed guard)
- **Code:** `FakeAnimationBackend.cs` `AdvanceFootsteps` (L331-378)
- **Gap:** `while (Distance >= stride)` emits multiple footsteps per large-dt tick; the stationary guard `return`s without resetting accumulated distance -> a burst on the next moving tick. (Fake/best-effort; while-loop is arguably more physically correct; existing test depends on multi-emit -> Low.)
- **Fix:** reset distance in the guard branch; reconsider while-vs-if per §5 (and update the dependent test).

### OFX-023 -- Missing ANC-P1-06 unit tests: `Tick_RampsAimBlendWeight`, `Tick_CompletesStanceTransition`
- **Severity:** Low | **Lens:** SC-anchor | **Folder:** anim-ctrl
- **Design:** ANC-P1-06 SC; DD-Tests §3.2
- **Code:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Phase1BackendBehaviorTests.cs`
- **Gap:** Both mandated layer-1 tests are absent; `AdvanceAim`/`AdvanceStance` (implemented) are never exercised through the public API, so the aim blend-weight ramp and stance commit-on-completion are unverified (also why OFX-005 went unnoticed). Production paths look implemented -> Low.
- **Fix:** add the two unit tests.

### OFX-024 -- `IFakeNavmeshProviderTestApi.BumpVersion` missing
- **Severity:** Low | **Lens:** spec-drift | **Folder:** navig-2
- **Design:** DD-Fake-Nav §3.4 (`BumpVersion(BoundingBox2D, NavLayerMask)`); §8.2 "Bump version" button
- **Code:** `FakeNavmeshProvider.cs` `IFakeNavmeshProviderTestApi` (L10-35)
- **Gap:** Method absent. No runtime impact today: S5 triggers replan via `BlockPolygon` (which bumps version as a side effect), and the §8.2 button is a deferred Phase-2 UI. -> Low.
- **Fix:** add `BumpVersion` for a pure version bump without the block side effect.

### OFX-025 -- FakeDtCrowd separation test asserts only `NearbyAgentCount`, not velocity divergence
- **Severity:** Low | **Lens:** SC-anchor | **Folder:** navig-2
- **Design:** DD-Tests-Nav §3.2 (`Update_TwoAgentsCrossingPaths_Avoid`: both reach targets, min separation >= ~0.8x combined R; `Update_AgentSurroundedByThreeStationary_VelocityNearZero`)
- **Code:** `FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/FakeDtCrowdProviderTests.cs` `Update_TwoAgentsCollide_SeparationApplied` (L152-173)
- **Gap:** Single-tick test asserts only `NearbyAgentCount > 0`; no velocity/position-divergence check. A separation impl that sets the count but applies zero force would pass. The two design-named tests are absent. (Production Update is correct as-written -> Low; pairs with OFX-010.)
- **Fix:** add the design's crossing-paths and surrounded-agent tests asserting separation/velocity.

### OFX-026 -- `AssignmentSlot` layout round-trip test omits the `Flags` field it was specified to pin
- **Severity:** Low | **Lens:** SC-anchor | **Folder:** group-maneuvers
- **Design:** TASK-SQD-P0-01 SC-P0-01-4 (write `AssignmentScore=0.42`, `FocusFireCount=3`, `Flags=0x05`; read back byte-exact, no aliasing)
- **Code:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/AssignmentSlotLayoutTests.cs` `AssignmentSlotArray_GetSlot_RoundTrip` (L26-37)
- **Gap:** Test writes/reads only `AssignedTargetHandle`/`AssignmentScore`/`FocusFireCount`; never touches `Flags` (offset+13, adjacent to `FocusFireCount` at +12). A layout regression aliasing those two bytes wouldn't be caught. (Struct layout is currently correct -> Low.)
- **Fix:** write/read `Flags=0x05` in the round-trip per SC-P0-01-4.

---

## Per-folder index
- **anim-ctrl (9):** OFX-002, 003, 004, 005, 006, 009, 012, 022, 023
- **navig-2 (7):** OFX-001, 010, 011, 018, 019, 024, 025
- **group-maneuvers (4):** OFX-007, 013, 014, 026
- **eqs-2 (3):** OFX-016, 017, 021
- **visual-asset-comparison (2):** OFX-008, 020
- **utility-ai (1):** OFX-015

## Closing recommendation
The defects cluster in **fake/test backends** (anim 5, nav 4) and **vacuous SC-anchor tests** (OFX-006/015/023/025/026). Two prevention steps would catch most: (1) layer-1 unit tests that assert the *computed* tick outputs (blend weight, stance commit, separation velocity) rather than presence/no-crash; (2) for emitters/validators, a real round-trip (emit -> reparse -> reflect) and feeding real IR through validators instead of hard-coded booleans.
