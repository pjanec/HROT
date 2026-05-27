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

- [x] **ANC-P0-01** `AnimNotifyCategory` canonical enum [details](./TASK-DETAIL.md#anc-p0-01--animnotifycategory-canonical-enum)
- [x] **ANC-P0-02** `ActorCapabilities` animation bits [details](./TASK-DETAIL.md#anc-p0-02--actorcapabilities-animation-bits)
- [x] **ANC-P0-03** `GlobalComponentIds` allocations (220–249) [details](./TASK-DETAIL.md#anc-p0-03--globalcomponentids-allocations-220249)
- [x] **ANC-P0-04** Channel param/state structs + action-id constants [details](./TASK-DETAIL.md#anc-p0-04--channel-paramstate-structs--action-id-constants)
- [x] **ANC-P0-05** Replicated/contractual components [details](./TASK-DETAIL.md#anc-p0-05--replicatedcontractual-components)
- [x] **ANC-P0-06** Muscle-internal components [details](./TASK-DETAIL.md#anc-p0-06--muscle-internal-components)
- [x] **ANC-P0-07** `IAnimationBackend` interface + supporting types [details](./TASK-DETAIL.md#anc-p0-07--ianimationbackend-interface--supporting-types)
- [x] **ANC-P0-08** Verification spike (dependency re-checks) [details](./TASK-DETAIL.md#anc-p0-08--verification-spike-dependency-re-checks)

## Phase 1 — `FakeAnimationBackend` (DD-Fake)
**Goal:** Deterministic render-free backend; state in one Tier-1 component; Layer-1 unit tests.

- [x] **ANC-P1-01** `FakeAnimBackendState` component + sub-structs [details](./TASK-DETAIL.md#anc-p1-01--fakeanimbackendstate-component--sub-structs)
- [x] **ANC-P1-02** Backend scaffold: Initialize, handle table, Register/Unregister [details](./TASK-DETAIL.md#anc-p1-02--backend-scaffold-initialize-handle-table-registerunregister)
- [x] **ANC-P1-03** Slot operations [details](./TASK-DETAIL.md#anc-p1-03--slot-operations)
- [x] **ANC-P1-04** Locomotion / aim / stance operations [details](./TASK-DETAIL.md#anc-p1-04--locomotion--aim--stance-operations)
- [x] **ANC-P1-05** Notify drain + hard-assert + metrics [details](./TASK-DETAIL.md#anc-p1-05--notify-drain--hard-assert--metrics)
- [x] **ANC-P1-06** Tick algorithm (slot/aim/stance advance) [details](./TASK-DETAIL.md#anc-p1-06--tick-algorithm-slotaimstance-advance)
- [x] **ANC-P1-07** Synthetic footstep emission [details](./TASK-DETAIL.md#anc-p1-07--synthetic-footstep-emission)
- [x] **ANC-P1-08** Layer-1 unit test suite [details](./TASK-DETAIL.md#anc-p1-08--layer-1-unit-test-suite)
- [x] **ANC-P1-09** Diagnostic ImGui window [details](./TASK-DETAIL.md#anc-p1-09--diagnostic-imgui-window)
- [x] **ANC-P1-10** JSON snapshot export + AAR integration [details](./TASK-DETAIL.md#anc-p1-10--json-snapshot-export--aar-integration)

## Phase 2 — TKB animation descriptor (DD-4)
**Goal:** Design-time JSON → runtime components, editor query API, validators ANIM001–007.

- [x] **ANC-P2-01** `CharacterAnimationDefDto` + nested DTOs [details](./TASK-DETAIL.md#anc-p2-01--characteranimationdefdto--nested-dtos)
- [x] **ANC-P2-02** Stable ID hashing [details](./TASK-DETAIL.md#anc-p2-02--stable-id-hashing)
- [x] **ANC-P2-03** `AnimationTkbTranslator.Inject` [details](./TASK-DETAIL.md#anc-p2-03--animationtkbtranslatorinject)
- [x] **ANC-P2-04** Per-class baked cache + hot reload [details](./TASK-DETAIL.md#anc-p2-04--per-class-baked-cache--hot-reload)
- [x] **ANC-P2-05** `CharacterAnimationDefRuntime` baking + `BakeForTest` [details](./TASK-DETAIL.md#anc-p2-05--characteranimationdefruntime-baking--bakefortest)
- [x] **ANC-P2-06** `IAnimationTkbQueries` editor query API [details](./TASK-DETAIL.md#anc-p2-06--ianimationtkbqueries-editor-query-api)
- [x] **ANC-P2-07** Validators ANIM001–ANIM007 [details](./TASK-DETAIL.md#anc-p2-07--validators-anim001anim007)
- [x] **ANC-P2-08** TKB translator/query test suite [details](./TASK-DETAIL.md#anc-p2-08--tkb-translatorquery-test-suite)

## Phase 3 — Muscle ECS systems (DD-1)
**Goal:** Seven systems + cleanup + capability-reactor extension, phase-ordered; Layer-2 system tests.

- [x] **ANC-P3-01** `AnimationDispatcherSystem` [details](./TASK-DETAIL.md#anc-p3-01--animationdispatchersystem)
- [x] **ANC-P3-02** `LookAtDispatcherSystem` [details](./TASK-DETAIL.md#anc-p3-02--lookatdispatchersystem)
- [x] **ANC-P3-03** `StanceTransitionSystem` [details](./TASK-DETAIL.md#anc-p3-03--stancetransitionsystem)
- [x] **ANC-P3-04** `MontageQueueAdvanceSystem` [details](./TASK-DETAIL.md#anc-p3-04--montagequeueadvancesystem)
- [x] **ANC-P3-05** `AnimationRuntimeBridgeSystem` [details](./TASK-DETAIL.md#anc-p3-05--animationruntimebridgesystem)
- [x] **ANC-P3-06** `NotifyEventEmitterSystem` [details](./TASK-DETAIL.md#anc-p3-06--notifyeventemittersystem)
- [x] **ANC-P3-07** `AnimationStateReporterSystem` [details](./TASK-DETAIL.md#anc-p3-07--animationstatereportersystem)
- [x] **ANC-P3-08** `AnimationBackendCleanupSystem` [details](./TASK-DETAIL.md#anc-p3-08--animationbackendcleanupsystem)
- [x] **ANC-P3-09** Capability-change reactor extension [details](./TASK-DETAIL.md#anc-p3-09--capability-change-reactor-extension)
- [x] **ANC-P3-10** Phase-ordering registration [details](./TASK-DETAIL.md#anc-p3-10--phase-ordering-registration)
- [x] **ANC-P3-11** Layer-2 system test suite [details](./TASK-DETAIL.md#anc-p3-11--layer-2-system-test-suite)

## Phase 4 — Events & Engine Event Catalog (DD-3)
**Goal:** Eight event types, picker attributes, catalog entries, BP2016/BP2017.

- [x] **ANC-P4-01** Eight event types + mandatory attributes [details](./TASK-DETAIL.md#anc-p4-01--eight-event-types--mandatory-attributes) ✓ BATCH-06
- [x] **ANC-P4-02** Picker attributes + drawers [details](./TASK-DETAIL.md#anc-p4-02--picker-attributes--drawers) ✓ BATCH-06
- [x] **ANC-P4-03** Catalog entries (incl. FootstepEvent exclusion) [details](./TASK-DETAIL.md#anc-p4-03--catalog-entries-incl-footstepevent-exclusion) ✓ BATCH-06
- [x] **ANC-P4-04** `BP2016` / `BP2017` validator rules [details](./TASK-DETAIL.md#anc-p4-04--bp2016--bp2017-validator-rules) ✓ BATCH-06

## Phase 5 — Blueprint authoring primitives (DD-5)
**Goal:** Nine AiPrimitive nodes + getters, `[InlineArray]`-safe codegen, validators ANIM008–012.

- [x] **ANC-P5-01** `PlayMontageNode` + `StopMontageNode` [details](./TASK-DETAIL.md#anc-p5-01--playmontagenode--stopmontagenode) ✓ BATCH-07
- [x] **ANC-P5-02** Queue-mutation nodes (Chain/Enqueue/Clear) [details](./TASK-DETAIL.md#anc-p5-02--queue-mutation-nodes-playmontagechainenqueueclearqueue) ✓ BATCH-07
- [x] **ANC-P5-03** `SetStanceNode` [details](./TASK-DETAIL.md#anc-p5-03--setstancenode) ✓ BATCH-07
- [x] **ANC-P5-04** Look-at nodes [details](./TASK-DETAIL.md#anc-p5-04--look-at-nodes) ✓ BATCH-08
- [x] **ANC-P5-05** Getter nodes [details](./TASK-DETAIL.md#anc-p5-05--getter-nodes) ✓ BATCH-08
- [x] **ANC-P5-06** Validators ANIM008–ANIM012 [details](./TASK-DETAIL.md#anc-p5-06--validators-anim008anim012) ✓ BATCH-08 (ANIM008–011 implemented; 012 deferred)
- [x] **ANC-P5-07** AiPrimitive registration + cross-subsystem reuse [details](./TASK-DETAIL.md#anc-p5-07--aiprimitive-registration--cross-subsystem-reuse) ✓ BATCH-10
- [ ] **ANC-P5-08** `PlayMontageChainNode` custom drawer (editor) [details](./TASK-DETAIL.md#anc-p5-08--playmontagechainnode-custom-drawer-editor) · plan: [Addendum A](./TASK-DETAIL.md#addendum-a--anc-p5-08-implementation-plan-playmontagechainnode-custom-drawer)
  - [ ] **ANC-P5-08a** Drawer + session skeleton (confirm dispatch route)
  - [ ] **ANC-P5-08b** Dynamic chain-entry UI + `ChainCount` management
  - [ ] **ANC-P5-08c** In-drawer validation feedback (ANIM005 / ANIM012)
  - [ ] **ANC-P5-08d** Registration + wiring tests

## Phase 6 — Replication (DD-2)
**Goal:** Cross-node DDS for the animation contract (depends on ANC-P0-08).

- [x] **ANC-P6-01** `AnimationChannel` intent/status translators [details](./TASK-DETAIL.md#anc-p6-01--animationchannel-intentstatus-translators) ✓ BATCH-14
- [x] **ANC-P6-02** `LookAtChannel` intent/status translators [details](./TASK-DETAIL.md#anc-p6-02--lookatchannel-intentstatus-translators) ✓ BATCH-14
- [x] **ANC-P6-03** Stance descriptor translators [details](./TASK-DETAIL.md#anc-p6-03--stance-descriptor-translators) ✓ BATCH-14
- [x] **ANC-P6-04** Side-buffer replication [details](./TASK-DETAIL.md#anc-p6-04--side-buffer-replication) ✓ BATCH-14
- [x] **ANC-P6-05** Seven event translator pairs [details](./TASK-DETAIL.md#anc-p6-05--seven-event-translator-pairs) ✓ BATCH-14
- [x] **ANC-P6-06** Topic/QoS registration + observability [details](./TASK-DETAIL.md#anc-p6-06--topicqos-registration--observability) ✓ BATCH-14

## Phase 7 — Integration tests, networkless stage-1 (DD-Tests)
**Goal:** Eight end-to-end scenarios over the full Muscle pipeline + fake backend.

- [x] **ANC-P7-01** `PumpUntil` + `IPumpableHarness` (shared infra) [details](./TASK-DETAIL.md#anc-p7-01--pumpuntil--ipumpableharness-shared-infra) ✓ BATCH-11
- [x] **ANC-P7-02** Animation diagnostics + command helpers [details](./TASK-DETAIL.md#anc-p7-02--animation-diagnostics--command-helpers) ✓ BATCH-11
- [x] **ANC-P7-03** Integration fixture + inline TKB test data [details](./TASK-DETAIL.md#anc-p7-03--integration-fixture--inline-tkb-test-data) ✓ BATCH-11
- [x] **ANC-P7-04** Scenario 1: happy-path single montage [details](./TASK-DETAIL.md#anc-p7-04--scenario-1-happy-path-single-montage) ✓ BATCH-11
- [x] **ANC-P7-05** Scenario 2: notify at keyframe [details](./TASK-DETAIL.md#anc-p7-05--scenario-2-notify-at-keyframe) ✓ BATCH-12
- [x] **ANC-P7-06** Scenario 3: stop → Interrupted [details](./TASK-DETAIL.md#anc-p7-06--scenario-3-stop--interrupted) ✓ BATCH-12
- [x] **ANC-P7-07** Scenario 4: stance transition [details](./TASK-DETAIL.md#anc-p7-07--scenario-4-stance-transition) ✓ BATCH-12
- [x] **ANC-P7-08** Scenario 5: montage chain via queue [details](./TASK-DETAIL.md#anc-p7-08--scenario-5-montage-chain-via-queue) ✓ BATCH-12
- [x] **ANC-P7-09** Scenario 6: enqueue mid-play [details](./TASK-DETAIL.md#anc-p7-09--scenario-6-enqueue-mid-play) ✓ BATCH-12
- [x] **ANC-P7-10** Scenario 7: footstep cadence [details](./TASK-DETAIL.md#anc-p7-10--scenario-7-footstep-cadence) ✓ BATCH-12
- [x] **ANC-P7-11** Scenario 8: look-at acquire/release [details](./TASK-DETAIL.md#anc-p7-11--scenario-8-look-at-acquirerelease) ✓ BATCH-12

## Phase 8 — Stride backend + networked stage-2 (full-surface extension)
**Goal:** Real Stride backend (smoke-tested) + networked replication test suite. Sequence after stage-1 is green.

- [x] **ANC-P8-01** `StrideAnimationBackend` skeleton [details](./TASK-DETAIL.md#anc-p8-01--strideanimationbackend-skeleton) ✓ BATCH-15
- [x] **ANC-P8-02** Stride scene/transform + notify mapping [details](./TASK-DETAIL.md#anc-p8-02--stride-scenetransform--notify-mapping) ✓ BATCH-15
- [x] **ANC-P8-03** `StrideBackendSmokeTest` suite [details](./TASK-DETAIL.md#anc-p8-03--stridebackendsmoketest-suite) ✓ BATCH-15
- [x] **ANC-P8-04** Networked stage-2 integration suite [details](./TASK-DETAIL.md#anc-p8-04--networked-stage-2-integration-suite) ✓ BATCH-18

