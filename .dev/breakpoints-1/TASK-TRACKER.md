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

**Goal:** End-to-end validation against the design's 10 success conditions.

- [x] **UBP-INT1** End-to-end Universal Breakpoint flow [details](./TASK-DETAIL.md#ubp-int1--end-to-end-universal-breakpoint-flow)
- [x] **UBP-INT2** Performance budget integration [details](./TASK-DETAIL.md#ubp-int2--performance-budget-integration)
- [x] **UBP-INT3** Flight Recorder invariance [details](./TASK-DETAIL.md#ubp-int3--flight-recorder-invariance)
