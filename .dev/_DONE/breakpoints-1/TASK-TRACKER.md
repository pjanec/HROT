# Universal Breakpoints — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success conditions. Design rationale lives in [DESIGN.md](./DESIGN.md).

---

## Phase P0 — Foundation rename

**Goal:** Generalize the Slice 1 time-controller interface so universal breakpoints can plug in as another debug subscriber.

- [x] **UBP-P0T1** Rename `IBlueprintTimeController` → `IEngineDebugTimeController` [details](./TASK-DETAIL.md#ubp-p0t1--rename-iblueprinttimecontroller--ienginedebugtimecontroller)

---

## Phase P1 — Snapshot orchestration

**Goal:** Triple-buffer infrastructure with reference-counted gate; production frame cost stays zero when no breakpoints are armed.

- [x] **UBP-P1T1** `DebugSnapshotProvider` system [details](./TASK-DETAIL.md#ubp-p1t1--debugsnapshotprovider-system)
- [x] **UBP-P1T2** `IDataBreakpointManager` skeleton + reference-counted gate [details](./TASK-DETAIL.md#ubp-p1t2--idatabreakpointmanager-skeleton--reference-counted-gate)
- [x] **UBP-P1T3** Triple-buffer pause primitives [details](./TASK-DETAIL.md#ubp-p1t3--triple-buffer-pause-primitives)

---

## Phase P2 — Universal substrate

**Goal:** Live evaluation of polymorphic `SearchPredicateDto` against live ECS via JIT-compiled delegates; covers component data, transient events, structural/spatial/lifecycle modes.

- [x] **UBP-P2T1** `DataBreakpointSystem` — component-data path [details](./TASK-DETAIL.md#ubp-p2t1--databreakpointsystem-component-data-path)
- [x] **UBP-P2T2** `DataBreakpointSystem` — event path [details](./TASK-DETAIL.md#ubp-p2t2--databreakpointsystem-event-path)
- [x] **UBP-P2T3** Structural / Spatial / Lifecycle scanners [details](./TASK-DETAIL.md#ubp-p2t3--structural--spatial--lifecycle-scanners)

---

## Phase P3 — Virtual snapshot UI swap

**Goal:** Editor and gizmos render the rewound `_preTickSnapshot` during a pause without mutating live memory.

- [x] **UBP-P3T1** `IEntityStatefulGizmo` signature change [details](./TASK-DETAIL.md#ubp-p3t1--ientitystatefulgizmo-signature-change)
- [x] **UBP-P3T2** Inspector adapter view repointing [details](./TASK-DETAIL.md#ubp-p3t2--inspector-adapter-view-repointing)
- [x] **UBP-P3T3** Temporal status banner [details](./TASK-DETAIL.md#ubp-p3t3--temporal-status-banner)

---

## Phase P4 — Deferred mutation

**Goal:** Inspector edits while paused are captured in a queue and drained into the ECB at the N+1 tick boundary — no resimulation.

- [x] **UBP-P4T1** `PendingDebugMutation` envelope + `StageMutation` API [details](./TASK-DETAIL.md#ubp-p4t1--pendingdebugmutation-envelope--stagemutation-api)
- [x] **UBP-P4T2** `StructEdit` commit interception [details](./TASK-DETAIL.md#ubp-p4t2--structedit-commit-interception)
- [x] **UBP-P4T3** ECB drain pipeline [details](./TASK-DETAIL.md#ubp-p4t3--ecb-drain-pipeline)

---

## Phase P5 — Trace-buffer integration

**Goal:** BTree and HSM execution breakpoints (Enter / Exit / Abort / Transition / Guard) via predicate-compiled scans over `BTreeTraceWorkingMemory1024` / `HsmTraceWorkingMemory1024`.

- [x] **UBP-P5T1** Compiler extension for trace-buffer scans [details](./TASK-DETAIL.md#ubp-p5t1--compiler-extension-for-trace-buffer-scans)
- [x] **UBP-P5T2** BTree breakpoints end-to-end [details](./TASK-DETAIL.md#ubp-p5t2--btree-breakpoints-end-to-end)
- [x] **UBP-P5T3** HSM breakpoints end-to-end [details](./TASK-DETAIL.md#ubp-p5t3--hsm-breakpoints-end-to-end)

---

## Phase P6 — Blueprint variable integration

**Goal:** Dynamic-partition memory breakpoints on Blueprint instance variables across tiered `BlueprintBlackboard*` components.

- [x] **UBP-P6T1** `BlueprintVariablePredicateDto` + JSON registration [details](./TASK-DETAIL.md#ubp-p6t1--blueprintvariablepredicatedto--json-registration)
- [x] **UBP-P6T2** Slot-table-aware IL emission [details](./TASK-DETAIL.md#ubp-p6t2--slot-table-aware-il-emission)

---

## Phase P7 — Graph-editor synthesis

**Goal:** Right-click context menus in BTree / HSM / Blueprint canvases auto-synthesise the correct predicate DTOs; existing gutter glyphs reused.

- [x] **UBP-P7T1** BTree context menu [details](./TASK-DETAIL.md#ubp-p7t1--btree-context-menu)
- [x] **UBP-P7T2** HSM context menu [details](./TASK-DETAIL.md#ubp-p7t2--hsm-context-menu)
- [x] **UBP-P7T3** Blueprint context menu integration [details](./TASK-DETAIL.md#ubp-p7t3--blueprint-context-menu-integration)
- [x] **UBP-P7T4** Probe-tag predicate bridge [details](./TASK-DETAIL.md#ubp-p7t4--probe-tag-predicate-bridge)

---

## Phase P8 — Manager UI

**Goal:** Data Breakpoint Manager window with StructEdit-hosted Predicate Builder, JSON clipboard, enable/disable controls, and the temporal status banner.

- [x] **UBP-P8T1** Data Breakpoint Manager window shell [details](./TASK-DETAIL.md#ubp-p8t1--data-breakpoint-manager-window-shell)
- [x] **UBP-P8T2** Predicate Builder (StructEdit host) [details](./TASK-DETAIL.md#ubp-p8t2--predicate-builder-structedit-host)
- [x] **UBP-P8T3** JSON clipboard [details](./TASK-DETAIL.md#ubp-p8t3--json-clipboard)
- [x] **UBP-P8T4** Temporal status banner integration [details](./TASK-DETAIL.md#ubp-p8t4--temporal-status-banner-integration)

---

## Phase P9 — Resilience polish

**Goal:** Hot-reload auto-rebind, "Step abandoned" notification, and watch persistence to `watches.json`.

- [x] **UBP-P9T1** Hot-reload auto-rebind [details](./TASK-DETAIL.md#ubp-p9t1--hot-reload-auto-rebind)
- [x] **UBP-P9T2** "Step abandoned" preemption [details](./TASK-DETAIL.md#ubp-p9t2--step-abandoned-preemption)
- [x] **UBP-P9T3** Watch persistence (`watches.json`) [details](./TASK-DETAIL.md#ubp-p9t3--watch-persistence-watchesjson)

---

## Cross-phase / integration

**Goal:** End-to-end validation against the design's 10 success conditions. (Validated against the mocked test harness only — real-engine validation is re-run in Phase P12 after P10 wires the library into running subsystems.)

- [x] **UBP-INT1** End-to-end Universal Breakpoint flow [details](./TASK-DETAIL.md#ubp-int1--end-to-end-universal-breakpoint-flow)
- [x] **UBP-INT2** Performance budget integration [details](./TASK-DETAIL.md#ubp-int2--performance-budget-integration)
- [x] **UBP-INT3** Flight Recorder invariance [details](./TASK-DETAIL.md#ubp-int3--flight-recorder-invariance)

---

## Phase P10 — Production integration

**Goal:** Wire the library into real subsystem hosts so the feature is reachable from the running editor. Addresses gap-analysis items G1–G9.

- [x] **UBP-P10T1** Editor subsystem wiring [details](./TASK-DETAIL.md#ubp-p10t1--editor-subsystem-wiring)
- [x] **UBP-P10T2** CGF subsystem wiring [details](./TASK-DETAIL.md#ubp-p10t2--cgf-subsystem-wiring)
- [x] **UBP-P10T3** Register `DataBreakpointManagerWindow` per perspective [details](./TASK-DETAIL.md#ubp-p10t3--register-databreakpointmanagerwindow-per-perspective)
- [x] **UBP-P10T4** Inject `IActiveViewProvider` into gizmo systems [details](./TASK-DETAIL.md#ubp-p10t4--inject-iactiveviewprovider-into-gizmo-systems)
- [x] **UBP-P10T5** Inject `IMutationInterceptor` into `ComponentEditWindow` [details](./TASK-DETAIL.md#ubp-p10t5--inject-imutationinterceptor-into-componenteditwindow)
- [x] **UBP-P10T6** Wire `BlueprintDebugSession` ↔ manager bridge [details](./TASK-DETAIL.md#ubp-p10t6--wire-blueprintdebugsession--manager-bridge)
- [x] **UBP-P10T7** BTree canvas: invoke menu populator + wire gutter renderer [details](./TASK-DETAIL.md#ubp-p10t7--btree-canvas-invoke-menu-populator--wire-gutter-renderer)
- [x] **UBP-P10T8** HSM canvas: invoke menu populator + wire gutter renderer [details](./TASK-DETAIL.md#ubp-p10t8--hsm-canvas-invoke-menu-populator--wire-gutter-renderer)
- [x] **UBP-P10T9** Blueprint canvas: invoke menu populator [details](./TASK-DETAIL.md#ubp-p10t9--blueprint-canvas-invoke-menu-populator)
- [x] **UBP-P10T10** Subscribe manager to `AiHotReloadCoordinator` [details](./TASK-DETAIL.md#ubp-p10t10--subscribe-manager-to-aihotreloadcoordinator)
- [x] **UBP-P10T11** Watches save/load editor lifecycle integration [details](./TASK-DETAIL.md#ubp-p10t11--watches-saveload-editor-lifecycle-integration)

---

## Phase P11 — Hot-path & correctness hardening

**Goal:** Fix the implementation deviations identified in gap analysis G10–G24. Brings the implementation in line with Success Conditions #1, #2, #5, #6.

- [x] **UBP-P11T1** Zero-allocation `DataBreakpointSystem.Execute` [details](./TASK-DETAIL.md#ubp-p11t1--zero-allocation-databreakpointsystemexecute)
- [x] **UBP-P11T2** Chunk-version-aware `QueryDelta` scanning [details](./TASK-DETAIL.md#ubp-p11t2--chunk-version-aware-querydelta-scanning)
- [x] **UBP-P11T3** Enforce `DataBreakpointSystem` ordering after `RecorderTickSystem` [details](./TASK-DETAIL.md#ubp-p11t3--enforce-databreakpointsystem-ordering-after-recordertricksystem)
- [x] **UBP-P11T4** `OnHit` re-entrancy guard [details](./TASK-DETAIL.md#ubp-p11t4--onhit-re-entrancy-guard)
- [x] **UBP-P11T5** `PausedTick` uses `GlobalTime.TotalWallTicks` [details](./TASK-DETAIL.md#ubp-p11t5--pausedtick-uses-globaltimetotalwallticks)
- [x] **UBP-P11T6** `OnExternalHit` fallback removal [details](./TASK-DETAIL.md#ubp-p11t6--onexternalhit-fallback-removal)
- [x] **UBP-P11T7** Predicate Builder respects `ReadOnlyChildIndices` [details](./TASK-DETAIL.md#ubp-p11t7--predicate-builder-respects-readonlychildindices)
- [x] **UBP-P11T8** `StageMutation` size resolution via ECS registry [details](./TASK-DETAIL.md#ubp-p11t8--stagemutation-size-resolution-via-ecs-registry)
- [x] **UBP-P11T9** Eliminate `Mounted*` accessor allocations [details](./TASK-DETAIL.md#ubp-p11t9--eliminate-mounted-accessor-allocations)
- [x] **UBP-P11T10** Reflection-free spatial position read [details](./TASK-DETAIL.md#ubp-p11t10--reflection-free-spatial-position-read)
- [x] **UBP-P11T11** Reusable hits buffer in `EvaluateStatefulBreakpoints` [details](./TASK-DETAIL.md#ubp-p11t11--reusable-hits-buffer-in-evaluatestatefulbreakpoints)
- [x] **UBP-P11T12** API / DESIGN alignment (`OccurrenceThreshold`, `OnPauseStateChanged`, `AddBreakpoint`) [details](./TASK-DETAIL.md#ubp-p11t12--api--design-alignment)
- [x] **UBP-P11T13** Lifecycle `NetworkId` resolution [details](./TASK-DETAIL.md#ubp-p11t13--lifecycle-networkid-resolution)

---

## Phase P12 — End-to-end revalidation in a wired subsystem

**Goal:** Re-run the integration-flavoured success conditions against the actual editor (not the mocked harness used for UBP-INT1/INT2/INT3) once P10 and P11 land.

- [x] **UBP-P12T1** Wired end-to-end flow [details](./TASK-DETAIL.md#ubp-p12t1--wired-end-to-end-flow)
- [x] **UBP-P12T2** Wired performance budget [details](./TASK-DETAIL.md#ubp-p12t2--wired-performance-budget)
- [x] **UBP-P12T3** Wired Flight Recorder invariance [details](./TASK-DETAIL.md#ubp-p12t3--wired-flight-recorder-invariance)
- [x] **UBP-P12T4** Multi-subsystem isolation check [details](./TASK-DETAIL.md#ubp-p12t4--multi-subsystem-isolation-check)
