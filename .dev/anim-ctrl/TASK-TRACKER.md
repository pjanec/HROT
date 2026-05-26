# Animation Control — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task
descriptions and success conditions, and the DD-* documents in this folder for
the design. Deferred items: [DEBT-TRACKER.md](./DEBT-TRACKER.md).

**Legend:** `[ ]` not done · `[x]` done. Task IDs are `ANC-<phase>-<n>`
(distinct from the `ANIM0xx` validator-rule IDs used inside the design docs).

**Scope:** Full surface. Phases 0–5 + 7 are the networkless stage-1 vertical
slice (independently shippable / verifiable). Phase 6 (replication) and Phase 8
(Stride + networked tests) extend it and should follow once stage-1 is green.

---

## Phase 0 — Foundations & shared contracts
**Goal:** Enum, capability bits, component IDs, channel params, components, and the `IAnimationBackend` interface — fixed contracts for everything else.

- [ ] **ANC-P0-01** `AnimNotifyCategory` canonical enum [details](./TASK-DETAIL.md#anc-p0-01--animnotifycategory-canonical-enum)
- [ ] **ANC-P0-02** `ActorCapabilities` animation bits [details](./TASK-DETAIL.md#anc-p0-02--actorcapabilities-animation-bits)
- [ ] **ANC-P0-03** `GlobalComponentIds` allocations (220–249) [details](./TASK-DETAIL.md#anc-p0-03--globalcomponentids-allocations-220249)
- [ ] **ANC-P0-04** Channel param/state structs + action-id constants [details](./TASK-DETAIL.md#anc-p0-04--channel-paramstate-structs--action-id-constants)
- [ ] **ANC-P0-05** Replicated/contractual components [details](./TASK-DETAIL.md#anc-p0-05--replicatedcontractual-components)
- [ ] **ANC-P0-06** Muscle-internal components [details](./TASK-DETAIL.md#anc-p0-06--muscle-internal-components)
- [ ] **ANC-P0-07** `IAnimationBackend` interface + supporting types [details](./TASK-DETAIL.md#anc-p0-07--ianimationbackend-interface--supporting-types)
- [ ] **ANC-P0-08** Verification spike (dependency re-checks) [details](./TASK-DETAIL.md#anc-p0-08--verification-spike-dependency-re-checks)

## Phase 1 — `FakeAnimationBackend` (DD-Fake)
**Goal:** Deterministic render-free backend; state in one Tier-1 component; Layer-1 unit tests.

- [ ] **ANC-P1-01** `FakeAnimBackendState` component + sub-structs [details](./TASK-DETAIL.md#anc-p1-01--fakeanimbackendstate-component--sub-structs)
- [ ] **ANC-P1-02** Backend scaffold: Initialize, handle table, Register/Unregister [details](./TASK-DETAIL.md#anc-p1-02--backend-scaffold-initialize-handle-table-registerunregister)
- [ ] **ANC-P1-03** Slot operations [details](./TASK-DETAIL.md#anc-p1-03--slot-operations)
- [ ] **ANC-P1-04** Locomotion / aim / stance operations [details](./TASK-DETAIL.md#anc-p1-04--locomotion--aim--stance-operations)
- [ ] **ANC-P1-05** Notify drain + hard-assert + metrics [details](./TASK-DETAIL.md#anc-p1-05--notify-drain--hard-assert--metrics)
- [ ] **ANC-P1-06** Tick algorithm (slot/aim/stance advance) [details](./TASK-DETAIL.md#anc-p1-06--tick-algorithm-slotaimstance-advance)
- [ ] **ANC-P1-07** Synthetic footstep emission [details](./TASK-DETAIL.md#anc-p1-07--synthetic-footstep-emission)
- [ ] **ANC-P1-08** Layer-1 unit test suite [details](./TASK-DETAIL.md#anc-p1-08--layer-1-unit-test-suite)
- [ ] **ANC-P1-09** Diagnostic ImGui window [details](./TASK-DETAIL.md#anc-p1-09--diagnostic-imgui-window)
- [ ] **ANC-P1-10** JSON snapshot export + AAR integration [details](./TASK-DETAIL.md#anc-p1-10--json-snapshot-export--aar-integration)

## Phase 2 — TKB animation descriptor (DD-4)
**Goal:** Design-time JSON → runtime components, editor query API, validators ANIM001–007.

- [ ] **ANC-P2-01** `CharacterAnimationDefDto` + nested DTOs [details](./TASK-DETAIL.md#anc-p2-01--characteranimationdefdto--nested-dtos)
- [ ] **ANC-P2-02** Stable ID hashing [details](./TASK-DETAIL.md#anc-p2-02--stable-id-hashing)
- [ ] **ANC-P2-03** `AnimationTkbTranslator.Inject` [details](./TASK-DETAIL.md#anc-p2-03--animationtkbtranslatorinject)
- [ ] **ANC-P2-04** Per-class baked cache + hot reload [details](./TASK-DETAIL.md#anc-p2-04--per-class-baked-cache--hot-reload)
- [ ] **ANC-P2-05** `CharacterAnimationDefRuntime` baking + `BakeForTest` [details](./TASK-DETAIL.md#anc-p2-05--characteranimationdefruntime-baking--bakefortest)
- [ ] **ANC-P2-06** `IAnimationTkbQueries` editor query API [details](./TASK-DETAIL.md#anc-p2-06--ianimationtkbqueries-editor-query-api)
- [ ] **ANC-P2-07** Validators ANIM001–ANIM007 [details](./TASK-DETAIL.md#anc-p2-07--validators-anim001anim007)
- [ ] **ANC-P2-08** TKB translator/query test suite [details](./TASK-DETAIL.md#anc-p2-08--tkb-translatorquery-test-suite)

## Phase 3 — Muscle ECS systems (DD-1)
**Goal:** Seven systems + cleanup + capability-reactor extension, phase-ordered; Layer-2 system tests.

- [ ] **ANC-P3-01** `AnimationDispatcherSystem` [details](./TASK-DETAIL.md#anc-p3-01--animationdispatchersystem)
- [ ] **ANC-P3-02** `LookAtDispatcherSystem` [details](./TASK-DETAIL.md#anc-p3-02--lookatdispatchersystem)
- [ ] **ANC-P3-03** `StanceTransitionSystem` [details](./TASK-DETAIL.md#anc-p3-03--stancetransitionsystem)
- [ ] **ANC-P3-04** `MontageQueueAdvanceSystem` [details](./TASK-DETAIL.md#anc-p3-04--montagequeueadvancesystem)
- [ ] **ANC-P3-05** `AnimationRuntimeBridgeSystem` [details](./TASK-DETAIL.md#anc-p3-05--animationruntimebridgesystem)
- [ ] **ANC-P3-06** `NotifyEventEmitterSystem` [details](./TASK-DETAIL.md#anc-p3-06--notifyeventemittersystem)
- [ ] **ANC-P3-07** `AnimationStateReporterSystem` [details](./TASK-DETAIL.md#anc-p3-07--animationstatereportersystem)
- [ ] **ANC-P3-08** `AnimationBackendCleanupSystem` [details](./TASK-DETAIL.md#anc-p3-08--animationbackendcleanupsystem)
- [ ] **ANC-P3-09** Capability-change reactor extension [details](./TASK-DETAIL.md#anc-p3-09--capability-change-reactor-extension)
- [ ] **ANC-P3-10** Phase-ordering registration [details](./TASK-DETAIL.md#anc-p3-10--phase-ordering-registration)
- [ ] **ANC-P3-11** Layer-2 system test suite [details](./TASK-DETAIL.md#anc-p3-11--layer-2-system-test-suite)

## Phase 4 — Events & Engine Event Catalog (DD-3)
**Goal:** Eight event types, picker attributes, catalog entries, BP2016/BP2017.

- [ ] **ANC-P4-01** Eight event types + mandatory attributes [details](./TASK-DETAIL.md#anc-p4-01--eight-event-types--mandatory-attributes)
- [ ] **ANC-P4-02** Picker attributes + drawers [details](./TASK-DETAIL.md#anc-p4-02--picker-attributes--drawers)
- [ ] **ANC-P4-03** Catalog entries (incl. FootstepEvent exclusion) [details](./TASK-DETAIL.md#anc-p4-03--catalog-entries-incl-footstepevent-exclusion)
- [ ] **ANC-P4-04** `BP2016` / `BP2017` validator rules [details](./TASK-DETAIL.md#anc-p4-04--bp2016--bp2017-validator-rules)

## Phase 5 — Blueprint authoring primitives (DD-5)
**Goal:** Nine AiPrimitive nodes + getters, `[InlineArray]`-safe codegen, validators ANIM008–012.

- [ ] **ANC-P5-01** `PlayMontageNode` + `StopMontageNode` [details](./TASK-DETAIL.md#anc-p5-01--playmontagenode--stopmontagenode)
- [ ] **ANC-P5-02** Queue-mutation nodes (Chain/Enqueue/Clear) [details](./TASK-DETAIL.md#anc-p5-02--queue-mutation-nodes-playmontagechainenqueueclearqueue)
- [ ] **ANC-P5-03** `SetStanceNode` [details](./TASK-DETAIL.md#anc-p5-03--setstancenode)
- [ ] **ANC-P5-04** Look-at nodes [details](./TASK-DETAIL.md#anc-p5-04--look-at-nodes)
- [ ] **ANC-P5-05** Getter nodes [details](./TASK-DETAIL.md#anc-p5-05--getter-nodes)
- [ ] **ANC-P5-06** Validators ANIM008–ANIM012 [details](./TASK-DETAIL.md#anc-p5-06--validators-anim008anim012)
- [ ] **ANC-P5-07** AiPrimitive registration + cross-subsystem reuse [details](./TASK-DETAIL.md#anc-p5-07--aiprimitive-registration--cross-subsystem-reuse)
- [ ] **ANC-P5-08** `PlayMontageChainNode` custom drawer (editor) [details](./TASK-DETAIL.md#anc-p5-08--playmontagechainnode-custom-drawer-editor)

## Phase 6 — Replication (DD-2)
**Goal:** Cross-node DDS for the animation contract (depends on ANC-P0-08).

- [ ] **ANC-P6-01** `AnimationChannel` intent/status translators [details](./TASK-DETAIL.md#anc-p6-01--animationchannel-intentstatus-translators)
- [ ] **ANC-P6-02** `LookAtChannel` intent/status translators [details](./TASK-DETAIL.md#anc-p6-02--lookatchannel-intentstatus-translators)
- [ ] **ANC-P6-03** Stance descriptor translators [details](./TASK-DETAIL.md#anc-p6-03--stance-descriptor-translators)
- [ ] **ANC-P6-04** Side-buffer replication [details](./TASK-DETAIL.md#anc-p6-04--side-buffer-replication)
- [ ] **ANC-P6-05** Seven event translator pairs [details](./TASK-DETAIL.md#anc-p6-05--seven-event-translator-pairs)
- [ ] **ANC-P6-06** Topic/QoS registration + observability [details](./TASK-DETAIL.md#anc-p6-06--topicqos-registration--observability)

## Phase 7 — Integration tests, networkless stage-1 (DD-Tests)
**Goal:** Eight end-to-end scenarios over the full Muscle pipeline + fake backend.

- [ ] **ANC-P7-01** `PumpUntil` + `IPumpableHarness` (shared infra) [details](./TASK-DETAIL.md#anc-p7-01--pumpuntil--ipumpableharness-shared-infra)
- [ ] **ANC-P7-02** Animation diagnostics + command helpers [details](./TASK-DETAIL.md#anc-p7-02--animation-diagnostics--command-helpers)
- [ ] **ANC-P7-03** Integration fixture + inline TKB test data [details](./TASK-DETAIL.md#anc-p7-03--integration-fixture--inline-tkb-test-data)
- [ ] **ANC-P7-04** Scenario 1: happy-path single montage [details](./TASK-DETAIL.md#anc-p7-04--scenario-1-happy-path-single-montage)
- [ ] **ANC-P7-05** Scenario 2: notify at keyframe [details](./TASK-DETAIL.md#anc-p7-05--scenario-2-notify-at-keyframe)
- [ ] **ANC-P7-06** Scenario 3: stop → Interrupted [details](./TASK-DETAIL.md#anc-p7-06--scenario-3-stop--interrupted)
- [ ] **ANC-P7-07** Scenario 4: stance transition [details](./TASK-DETAIL.md#anc-p7-07--scenario-4-stance-transition)
- [ ] **ANC-P7-08** Scenario 5: montage chain via queue [details](./TASK-DETAIL.md#anc-p7-08--scenario-5-montage-chain-via-queue)
- [ ] **ANC-P7-09** Scenario 6: enqueue mid-play [details](./TASK-DETAIL.md#anc-p7-09--scenario-6-enqueue-mid-play)
- [ ] **ANC-P7-10** Scenario 7: footstep cadence [details](./TASK-DETAIL.md#anc-p7-10--scenario-7-footstep-cadence)
- [ ] **ANC-P7-11** Scenario 8: look-at acquire/release [details](./TASK-DETAIL.md#anc-p7-11--scenario-8-look-at-acquirerelease)

## Phase 8 — Stride backend + networked stage-2 (full-surface extension)
**Goal:** Real Stride backend (smoke-tested) + networked replication test suite. Sequence after stage-1 is green.

- [ ] **ANC-P8-01** `StrideAnimationBackend` skeleton [details](./TASK-DETAIL.md#anc-p8-01--strideanimationbackend-skeleton)
- [ ] **ANC-P8-02** Stride scene/transform + notify mapping [details](./TASK-DETAIL.md#anc-p8-02--stride-scenetransform--notify-mapping)
- [ ] **ANC-P8-03** `StrideBackendSmokeTest` suite [details](./TASK-DETAIL.md#anc-p8-03--stridebackendsmoketest-suite)
- [ ] **ANC-P8-04** Networked stage-2 integration suite [details](./TASK-DETAIL.md#anc-p8-04--networked-stage-2-integration-suite)
