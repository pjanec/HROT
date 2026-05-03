# Task Tracker — Cluster-Wide Diagnostic Dump

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: JSON Serialisation Foundation

**Goal:** Centralise JSON options, move converters to Fdp.Core, extract the aesthetic formatter,
fix the FixedString64 bug.

- [x] **DD-P1-T01** Move FixedString/Vector Converters to Fdp.Core [details](./TASK-DETAIL.md#dd-p1-t01--move-fixedstring-converters-to-fdpcore)
- [x] **DD-P1-T02** FdpJsonOptionsRegistry [details](./TASK-DETAIL.md#dd-p1-t02--fdpjsonoptionsregistry)
- [x] **DD-P1-T03** JsonAestheticFormatter [details](./TASK-DETAIL.md#dd-p1-t03--jsonaestheticformatter)
- [x] **DD-P1-T04** Refactor Existing JSON Callers [details](./TASK-DETAIL.md#dd-p1-t04--refactor-existing-callers)

---

## Phase 2: Diagnostic Data Service Interfaces and Implementations

**Goal:** Extract event history, architecture diagnostics, entity extraction, and log archive
access into headless services shared by UI panels and the cluster dump handler.

- [x] **DD-P2-T01** IDiagnosticEventHistoryService and CapturedEventDto [details](./TASK-DETAIL.md#dd-p2-t01--idiagnosticeventhistoryservice-and-capturedeventdto)
- [x] **DD-P2-T02** Refactor EventBrowserPanel to Use IDiagnosticEventHistoryService [details](./TASK-DETAIL.md#dd-p2-t02--refactor-eventbrowserpanel-to-use-idiagnosticeventhistoryservice)
- [x] **DD-P2-T03** IArchitectureDiagnosticsService [details](./TASK-DETAIL.md#dd-p2-t03--iarchitecturediagnosticsservice)
- [x] **DD-P2-T04** IEntityStateExtractionService [details](./TASK-DETAIL.md#dd-p2-t04--ientitystateextractionservice)
- [x] **DD-P2-T05** ILogArchiveExtractionService [details](./TASK-DETAIL.md#dd-p2-t05--ilogarchiveextractionservice)

---

## Phase 3: Multi-Select Copy-to-JSON in UI Panels

**Goal:** Allow operators to select and copy multiple events or entities as JSON arrays.

- [ ] **DD-P3-T01** EventBrowserPanel Multi-Select [details](./TASK-DETAIL.md#dd-p3-t01--eventbrowserpanel-multi-select)
- [ ] **DD-P3-T02** EntityInspectorPanel Multi-Select [details](./TASK-DETAIL.md#dd-p3-t02--entityinspectorpanel-multi-select)

---

## Phase 4: Cluster-Wide Dump Orchestration Protocol

**Goal:** Wire DumpDiagnostics into the existing 2PC pipeline from ExCon through the Orchestrator
to each node and back.

- [ ] **DD-P4-T01** Enum Extensions and DiagnosticDumpPayloadDto [details](./TASK-DETAIL.md#dd-p4-t01--enum-extensions-and-diagnosticdumppayloaddto)
- [ ] **DD-P4-T02** ExecuteDiagnosticDumpIntent [details](./TASK-DETAIL.md#dd-p4-t02--executediagnosticdumpintent)
- [ ] **DD-P4-T03** ClusterOpEgressTranslator and ClusterOpMasterTranslator [details](./TASK-DETAIL.md#dd-p4-t03--clusteropegress-translator-and-clusteropmastertranslator)
- [ ] **DD-P4-T04** DiagnosticsConsensusAggregator [details](./TASK-DETAIL.md#dd-p4-t04--diagnosticsconsensusaggregator)
- [ ] **DD-P4-T05** DiagnosticsDumpProcessManager [details](./TASK-DETAIL.md#dd-p4-t05--diagnosticsdumpprocessmanager)

---

## Phase 5: Node-Side Handler and NLog Configuration

**Goal:** Each node produces its local dump files in response to the cluster-wide dump command.

- [ ] **DD-P5-T01** NLog File Target, Layout, and Auto-Rotation [details](./TASK-DETAIL.md#dd-p5-t01--nlog-file-target-layout-and-auto-rotation)
- [ ] **DD-P5-T02** HrotRunnerConfiguration `--log-dir` Option [details](./TASK-DETAIL.md#dd-p5-t02--hrotrunnerconfiguration----log-dir-option)
- [x] **DD-P5-T03** HrotNodeConfig.LogDirectory [details](./TASK-DETAIL.md#dd-p5-t03--hrotnodeconfiglogdirectory)
- [ ] **DD-P5-T04** DiagnosticsDumpClusterOpHandler [details](./TASK-DETAIL.md#dd-p5-t04--diagnosticsdumpclusterophandler)
- [ ] **DD-P5-T05** Node LocalTempRoot Isolation and ClusterConfiguration NasBasePath [details](./TASK-DETAIL.md#dd-p5-t05--node-localtemproot-isolation-and-clusterconfiguration-nasbasepath)

---

## Phase 6: Cluster Diagnostics UI Panel

**Goal:** Operator-facing panel in ExCon and Orchestrator for triggering, monitoring, and
accessing dump results.

- [ ] **DD-P6-T01** ClusterDiagnosticsPanel — Configuration and Execution [details](./TASK-DETAIL.md#dd-p6-t01--clusterdiagnosticspanel-configuration--execution)
- [ ] **DD-P6-T02** ClusterDiagnosticsPanel — Results Tree and Context Menus [details](./TASK-DETAIL.md#dd-p6-t02--clusterdiagnosticspanel-results-tree--context-menus)
- [ ] **DD-P6-T03** Register Panel in OrchestratorSubsystem and ExConSubsystem [details](./TASK-DETAIL.md#dd-p6-t03--register-clusterdiagnosticspanel-in-orchestratorsubsystem-and-exconsubsystem)

---

## Phase 7: IFileDialogService — Reusable Save As Dialog

**Goal:** Provide a domain-agnostic ImGui file save dialog following the IMapPickService bridge
pattern.

- [ ] **DD-P7-T01** IFileDialogService Interface [details](./TASK-DETAIL.md#dd-p7-t01--ifiledialogservice-interface)
- [ ] **DD-P7-T02** ImGuiFileDialogService Implementation [details](./TASK-DETAIL.md#dd-p7-t02--imguifiledialogservice-implementation)
- [ ] **DD-P7-T03** Wire ImGuiFileDialogService into WindowManager [details](./TASK-DETAIL.md#dd-p7-t03--wire-imguifiledialogservice-into-windowmanager)

---

## Phase 8: Cluster Log Merge

**Goal:** Optional post-process that merges all per-node log files into one chronological stream.

- [ ] **DD-P8-T01** DiagnosticLogMergeWorker [details](./TASK-DETAIL.md#dd-p8-t01--diagnosticlogmergeworker)
- [ ] **DD-P8-T02** MergeLogsIntent and LogMergeCompletedEvent [details](./TASK-DETAIL.md#dd-p8-t02--mergelogsentent-and-logmergecompletedelement)
- [ ] **DD-P8-T03** Merged Log Entry in ClusterDiagnosticsPanel [details](./TASK-DETAIL.md#dd-p8-t03--merged-log-entry-in-clusterdiagnosticspanel)
