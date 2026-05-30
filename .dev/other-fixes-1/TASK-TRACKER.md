# Other Subsystems -- Design Conformance Fixes -- Task Tracker

One line per issue. Check the box when fixed (or converted to a documented intentional deviation).
Full descriptions + design/code refs in [TASK-DETAIL.md](./TASK-DETAIL.md).

Scope: `anim-ctrl`, `eqs-2`, `navig-2`, `utility-ai`, `visual-asset-comparison`, `group-maneuvers`.
Found via hunt + adversarial-verify workflow (Sonnet, graph tools only). 85 candidates -> 28 confirmed
-> 26 distinct. Severity = refuter's corrected severity. Confidence: all verified by the audit agents,
re-confirm against the cited design + code before changing.

Zero-finding clusters (held up): uai-runtime-core, uai-sourcegen, eqs-generators-tests, vac-sanitizers, anim-events-catalog.

---

## HIGH
- [ ] **OFX-001** (navig-2, algorithm) -- Nav backend auto-select checks only start point; Hybrid is dead code -> [details](./TASK-DETAIL.md#ofx-001----nav-backend-auto-select-checks-only-the-start-point-hybrid-is-dead-code)
- [ ] **OFX-002** (anim-ctrl, algorithm) -- `NotifyEventEmitterSystem` ignores `Kind`; Footstep/HitWindow events never typed -> [details](./TASK-DETAIL.md#ofx-002----notifyeventemittersystem-ignores-animnotifycategorykind---footstephitwindow-events-never-typed)
- [ ] **OFX-003** (anim-ctrl, algorithm) -- FakeAnimationBackend state in managed Dictionary, not the ECS component -> [details](./TASK-DETAIL.md#ofx-003----fakeanimationbackend-stores-per-entity-state-in-a-managed-dictionary-not-the-tier-1-ecs-component)
- [ ] **OFX-004** (anim-ctrl, algorithm) -- `StopMontageOnSlot` hard-clears instead of blend-out -> [details](./TASK-DETAIL.md#ofx-004----stopmontageonslot-hard-clears-slots-instead-of-triggering-graceful-blend-out)
- [ ] **OFX-005** (anim-ctrl, algorithm) -- `BlendWeight` never computed in `AdvanceSlots` (always 0) -> [details](./TASK-DETAIL.md#ofx-005----blendweight-never-computed-in-advanceslots---always-0)
- [ ] **OFX-006** (anim-ctrl, SC-anchor) -- ANIM008/009/010/011 validators absent; tests are vacuous stubs -> [details](./TASK-DETAIL.md#ofx-006----anim008009010011-validators-have-no-production-implementation-their-tests-are-vacuous-stubs)
- [ ] **OFX-007** (group-maneuvers, algorithm) -- `MergeContact` ties 3D position to max-threat, not most-recent sighting -> [details](./TASK-DETAIL.md#ofx-007----squadperceptionmergesystemmergecontact-ties-3d-position-to-max-threat-ownership-not-most-recent-sighting)
- [ ] **OFX-008** (visual-asset-comparison, spec-drift) -- comparison outline drawn solid, design requires dashed -> [details](./TASK-DETAIL.md#ofx-008----comparison-annotation-outline-drawn-solid-design-requires-a-dashed-stroke)

## MEDIUM
- [ ] **OFX-009** (anim-ctrl, algorithm) -- `MontageQueueAdvanceSystem` never crossfades; advances only after slot silent -> [details](./TASK-DETAIL.md#ofx-009----montagequeueadvancesystem-never-crossfades-advances-only-after-the-slot-goes-silent)
- [ ] **OFX-010** (navig-2, algorithm) -- `FakeDtCrowdProvider` separation threshold/formula/NearbyAgentCount deviate -> [details](./TASK-DETAIL.md#ofx-010----fakedtcrowdprovider-separation-threshold--formula--nearbyagentcount-range-deviate-from-design)
- [ ] **OFX-011** (navig-2, spec-drift) -- `BlockPolygon` layer-agnostic; design requires per-layer scoping -> [details](./TASK-DETAIL.md#ofx-011----fakenavmeshproviderblockpolygon-is-layer-agnostic-design-requires-per-layer-scoping)
- [ ] **OFX-012** (anim-ctrl, dual-path) -- intent egress dirty-check omits `ActionParams` blob comparison -> [details](./TASK-DETAIL.md#ofx-012----animation-intent-egress-dirty-check-omits-the-actionparams-blob-comparison)
- [ ] **OFX-013** (group-maneuvers, dual-path) -- `RoleSlotAssignmentPrimitive` leaves stale RoleIds for unassigned members -> [details](./TASK-DETAIL.md#ofx-013----roleslotassignmentprimitive-leaves-stale-roleids-for-unassigned-members)
- [ ] **OFX-014** (group-maneuvers, invariant) -- `PhaseSequencer` `>=` off-by-one + `dwellTimeoutTicks==0` immediate abort -> [details](./TASK-DETAIL.md#ofx-014----phasesequenceradvance-uses--off-by-one-and-treats-dwelltimeoutticks0-as-immediate-abort)
- [ ] **OFX-015** (utility-ai, SC-anchor) -- emitter round-trip test vacuous (no Roslyn reparse -> reflect) -> [details](./TASK-DETAIL.md#ofx-015----utility-emitter-round-trip-test-is-vacuous-no-roslyn-parse---reflect---structural-equality)
- [ ] **OFX-016** (eqs-2, spec-drift) -- EQS002 diagnostic points at the method, not the offending identifier -> [details](./TASK-DETAIL.md#ofx-016----eqs002-purity-diagnostic-points-at-the-method-not-the-offending-identifier)
- [ ] **OFX-017** (eqs-2, integration-seam) -- Brain-side EqsResult child cache never pruned on sensor removal -> [details](./TASK-DETAIL.md#ofx-017----brain-side-eqsresultingresstranslator-child-entity-cache-is-never-pruned-on-sensor-removal)
- [ ] **OFX-018** (navig-2, algorithm) -- `ReplanTimeBudget` guard absent; replan bounded only by count -> [details](./TASK-DETAIL.md#ofx-018----replantimebudget-guard-absent-replan-bounded-only-by-maxreplans-count)
- [ ] **OFX-019** (navig-2, dual-path) -- `FollowPathExecutor` doesn't map `FailedBlocked` -> stuck Running -> [details](./TASK-DETAIL.md#ofx-019----followpathexecutor-doesnt-map-failedblocked-to-failure---stuck-running-forever)
- [ ] **OFX-020** (visual-asset-comparison, spec-drift) -- `connection_changed` badge draws at first endpoint, not midpoint -> [details](./TASK-DETAIL.md#ofx-020----connection_changed-edgemidpoint-badge-draws-at-the-first-endpoint-not-the-geometric-midpoint)

## LOW
- [ ] **OFX-021** (eqs-2, algorithm) -- cross-tick raycast polling skip-guard absent (perf; correctness-neutral) -> [details](./TASK-DETAIL.md#ofx-021----eqs-cross-tick-raycast-polling-skip-guard-absent-full-pipeline-re-runs-every-awaiting-tick)
- [ ] **OFX-022** (anim-ctrl, algorithm) -- `AdvanceFootsteps` while-loop multi-emit + no stationary reset -> [details](./TASK-DETAIL.md#ofx-022----advancefootsteps-uses-a-while-loop-multi-emit-and-doesnt-bleed-off-distance-when-stationary)
- [ ] **OFX-023** (anim-ctrl, SC-anchor) -- missing ANC-P1-06 aim-blend / stance-completion unit tests -> [details](./TASK-DETAIL.md#ofx-023----missing-anc-p1-06-unit-tests-tick_rampsaimblendweight-tick_completesstancetransition)
- [ ] **OFX-024** (navig-2, spec-drift) -- `IFakeNavmeshProviderTestApi.BumpVersion` missing -> [details](./TASK-DETAIL.md#ofx-024----ifakenavmeshprovidertestapibumpversion-missing)
- [ ] **OFX-025** (navig-2, SC-anchor) -- FakeDtCrowd separation test asserts only NearbyAgentCount -> [details](./TASK-DETAIL.md#ofx-025----fakedtcrowd-separation-test-asserts-only-nearbyagentcount-not-velocity-divergence)
- [ ] **OFX-026** (group-maneuvers, SC-anchor) -- `AssignmentSlot` round-trip test omits the `Flags` field -> [details](./TASK-DETAIL.md#ofx-026----assignmentslot-layout-round-trip-test-omits-the-flags-field-it-was-specified-to-pin)

---

### Status legend
- [ ] open  /  [x] fixed or converted to documented intentional deviation
- Do not delete rows; mark resolved instead.

### Per-folder counts
anim-ctrl 9 · navig-2 7 · group-maneuvers 4 · eqs-2 3 · visual-asset-comparison 2 · utility-ai 1
