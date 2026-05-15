# FDP Replay Browser — Task Tracker

**Reference**: See [TASK-DETAILS.md](./TASK-DETAILS.md) for detailed task descriptions and binary success conditions, and [DESIGN.md](./DESIGN.md) for the architecture.

Status legend: `[ ]` not done, `[x]` done. Backend-first ordering — never mark a stage done while its corresponding test IDs (EX-T*, FND-T*, DIF-T*, SR-T*) are red.

---

## Stage 1 — Headless JSON Export Pipeline

**Goal**: Working `.fdp` → JSON streaming, with full CLI, fully covered by EX-T01..EX-T32.

- [x] **RB-1.0** Codebase Audit and Gap Fix [details](./TASK-DETAILS.md#rb-10--codebase-audit-and-gap-fix)
- [x] **RB-1.1** `FdpRecordingHarness` Test Substrate [details](./TASK-DETAILS.md#rb-11--fdprecordingharness-test-substrate)
- [x] **RB-1.2** Domain DTOs (`JsonExportOptions`, `ChangelogEntryDto`, enums) [details](./TASK-DETAILS.md#rb-12--domain-dtos-jsonexportoptions-changelogentrydto-enums)
- [x] **RB-1.3** `IRecordingExportService` Contract [details](./TASK-DETAILS.md#rb-13--irecordingexportservice-contract)
- [x] **RB-1.4** Headless `ReplayBrowserContext` [details](./TASK-DETAILS.md#rb-14--headless-replaybrowsercontext)
- [x] **RB-1.5** `RecordingExportService` Implementation (absolute-state path) [details](./TASK-DETAILS.md#rb-15--recordingexportservice-implementation)
- [x] **RB-1.6** `Fdp.Tools.RecordingDumper` Console App [details](./TASK-DETAILS.md#rb-16--fdptoolsrecordingdumper-console-app)
- [x] **RB-1.7** Stage 1 Acceptance Gate (all EX-T green) [details](./TASK-DETAILS.md#rb-17--stage-1-acceptance-gate)

---

## Stage 3 — Diff Engine (Backend portion lands before Stage 2 UI)

**Goal**: `IComponentDiffService` complete and covered by DIF-T01..DIF-T13, changelog export EX-T27..T29 passing.

- [x] **RB-3.1** `DiffNode` Hierarchy [details](./TASK-DETAILS.md#rb-31--diffnode-hierarchy)
- [x] **RB-3.2** `IComponentDiffService` + `ComponentDiffService` [details](./TASK-DETAILS.md#rb-32--icomponentdiffservice--componentdiffservice)
- [x] **RB-3.3** Wire Changelog Mode into `RecordingExportService` [details](./TASK-DETAILS.md#rb-33--wire-changelog-mode-into-recordingexportservice)

---

## Stage 2 — Replay Browser Subsystem Foundation

**Goal**: Perspective-bound subsystem with timeline + 4 reused windows + history navigation. All FND-T tests passing.

- [x] **RB-2.1** History Trackers (`EntitySelectionHistory`, `PlaybackHistoryTracker`) [details](./TASK-DETAILS.md#rb-21--history-trackers)
- [x] **RB-2.2** `ImGuiEntityLink` Utility [details](./TASK-DETAILS.md#rb-22--imguientitylink-utility)
- [x] **RB-2.3** `ReplayBrowserSubsystem` Skeleton [details](./TASK-DETAILS.md#rb-23--replaybrowsersubsystem-skeleton)
- [x] **RB-2.4** Reused Panel Wiring + 5 Windows [details](./TASK-DETAILS.md#rb-24--reused-panel-wiring-inspector--events-and-4-windows)
- [x] **RB-2.5** `ReplayTimelinePanel` (full layout incl. export expander) [details](./TASK-DETAILS.md#rb-25--replaytimelinepanel)
- [x] **RB-2.6** Subsystem Composition Root + Delegate Wiring [details](./TASK-DETAILS.md#rb-26--subsystem-composition-root-and-delegate-wiring)
- [x] **RB-2.7** Stage 2 Acceptance Gate (all FND-T green) [details](./TASK-DETAILS.md#rb-27--stage-2-acceptance-gate)

---

## Stage 3.B — Diff Panel UI

- [x] **RB-3.4** `ComponentDiffPanel` (preserve full layout) [details](./TASK-DETAILS.md#rb-34--componentdiffpanel)
- [x] **RB-3.5** Stage 3 Acceptance Gate [details](./TASK-DETAILS.md#rb-35--stage-3-acceptance-gate)

---

## Stage 4 — Advanced Search (Backend first)

**Goal Backend (4.1–4.7)**: All SR-T01..SR-T36 passing before any search UI is written.

- [x] **RB-4.1** Search Domain DTOs [details](./TASK-DETAILS.md#rb-41--search-domain-dtos)
- [x] **RB-4.2** `IPropertyEvaluator` (StructEdit binding) [details](./TASK-DETAILS.md#rb-42--ipropertyevaluator--structedit-binding)
- [x] **RB-4.3** `IPredicateCompiler` (+ `PredicateCompiler`) [details](./TASK-DETAILS.md#rb-43--ipredicatecompiler--predicatecompiler)
- [x] **RB-4.4** `IEventScannerCompiler` (+ scanners) [details](./TASK-DETAILS.md#rb-44--ieventscannercompiler--fasteventscannert-managedeventscannert-occurrence-scanner)
- [x] **RB-4.5** `IRecordingSearchService` (+ `RecordingSearchService`) [details](./TASK-DETAILS.md#rb-45--irecordingsearchservice--recordingsearchservice)
- [x] **RB-4.6** `BoundingBoxPickerGizmo` [details](./TASK-DETAILS.md#rb-46--boundingboxpickergizmo)
- [x] **RB-4.7** Stage 4 Backend Acceptance Gate (all SR-T green) [details](./TASK-DETAILS.md#rb-47--stage-4-backend-acceptance-gate)

**Goal UI (4.8–4.11)**:

- [x] **RB-4.8** StructEdit Plumbing for the Search Panel [details](./TASK-DETAILS.md#rb-48--structedit-plumbing-for-the-search-panel)
- [x] **RB-4.9** Custom `IImGuiFieldDrawer`s (BBox, BehaviorHash, FilteredTypeCombo) [details](./TASK-DETAILS.md#rb-49--custom-iimguifielddrawers)
- [x] **RB-4.10** `ReplaySearchPanel` — all five modes incl. compound [details](./TASK-DETAILS.md#rb-410--replaysearchpanel-all-five-modes)
- [x] **RB-4.11** Stage 4 Final Gate [details](./TASK-DETAILS.md#rb-411--stage-4-final-gate)

---

## Stage 5 — Global Registration

- [x] **RB-5.1** Add `Hrot.ReplayBrowser.csproj` to the ClusterRunner Solution [details](./TASK-DETAILS.md#rb-51--add-hrotreplaybrowsercsproj-to-the-clusterrunner-solution)
- [ ] **RB-5.2** End-to-End Manual Smoke [details](./TASK-DETAILS.md#rb-52--end-to-end-manual-smoke)

---

## Cross-Stage / Continuous

- [ ] **RB-X.1** Documentation Hygiene [details](./TASK-DETAILS.md#rb-x1--documentation-hygiene)
- [ ] **RB-X.2** Style and Allocation Audits [details](./TASK-DETAILS.md#rb-x2--style-and-allocation-audits)
