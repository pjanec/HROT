# BATCH-03 Instructions — dump-diag

**Batch Number:** BATCH-03
**Tasks:** DD-P3-T01, DD-P3-T02, DD-P4-T01, DD-P4-T02, DD-P4-T03, DD-P4-T04, DD-P4-T05
**Phase:** 3 (Multi-Select UI) + 4 (Cluster-Wide Dump Orchestration Protocol)
**Estimated Effort:** 14–18 hours

---

## 📋 Onboarding & Workflow

### Developer Instructions

BATCH-03 extends the diagnostic dump workstream in two areas:

1. **Phase 3 (UI):** Add multi-select copy-to-JSON to `EventBrowserPanel` and `EntityInspectorPanel`.
   Both panels already exist and have tests. You are extending them, not replacing them.
2. **Phase 4 (Protocol):** Wire `DumpDiagnostics` into the existing 2PC cluster op pipeline —
   enums, intent event, egress/master translators, consensus aggregator, and process manager.

**Do not stop for questions unless there is a breaking design flaw. Work autonomously until all
Success Conditions are met.**

### Required Reading (IN ORDER)

1. **Workflow guide:** `.github/skills/developer/SKILL.md`
2. **Design document:** `.dev/dump-diag/DESIGN.md` (Phases 3 and 4)
3. **Task definitions:** `.dev/dump-diag/TASK-DETAIL.md` sections DD-P3-T01 through DD-P4-T05
4. **Previous review:** `.dev/dump-diag/reviews/BATCH-02-REVIEW.md`
5. **Debt tracker:** `.dev/dump-diag/DEBT-TRACKER.md` (note P3 items for context)

### Source Code Locations

- **EventBrowserPanel:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EventBrowserPanel.cs`
- **EntityInspectorPanel:** `FDP/Engine/Fdp.Presentation/ImGui/Panels/EntityInspectorPanel.cs`
- **EventBrowserPanel Tests:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/EventBrowserPanelTests.cs`
- **EntityInspectorPanel Tests:** `FDP/Engine/Fdp.Presentation.Tests/ImGui/EntityInspectorPanelTests.cs`
- **ClusterOpType / NodeOpType enums:** `Hrot/Network/Hrot.Network.Orchestration/` (search for the files)
- **ClusterOpIntents.cs:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Events/ClusterOpIntents.cs`
- **OrchestrationPayloadDtos.cs:** `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs`
- **ClusterOpEgressTranslator:** `Hrot/Network/Hrot.Network.Orchestration/` (search for file)
- **ClusterOpMasterTranslator:** `Hrot/Network/Hrot.Network.Orchestration/` (search for file)
- **StorageConsensusAggregator:** search for it in `Hrot/Subsystems/Hrot.Orchestrator/` — use as model for DD-P4-T04
- **StorageProcessManager:** search for it in `Hrot/Subsystems/Hrot.Orchestrator/` — use as model for DD-P4-T05
- **Hrot.Orchestrator subsystem:** `Hrot/Subsystems/Hrot.Orchestrator/`
- **IEntityStateExtractionService:** `FDP/Toolkits/Fdp.Toolkits/Diagnostics/IEntityStateExtractionService.cs`
- **FdpJsonOptionsRegistry:** `FDP/Engine/Fdp.Core/Serialization/FdpJsonOptionsRegistry.cs`
- **JsonAestheticFormatter:** `FDP/Toolkits/Fdp.Toolkits/Serialization/JsonAestheticFormatter.cs`

### Report Submission

**When done, submit your report to:**
`.dev/dump-diag/reports/BATCH-03-REPORT.md`

**If you have questions, create:**
`.dev/dump-diag/questions/BATCH-03-QUESTIONS.md`

---

## 🔧 Corrective Task 0 — Tech Debt from BATCH-02 (P2 items only)

No P2 blockers from BATCH-02 require immediate correction.
P3 items are recorded in DEBT-TRACKER.md and may be addressed opportunistically.

---

## 📌 Task Specifications

### DD-P3-T01 — EventBrowserPanel Multi-Select

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p3-t01--eventbrowserpanel-multi-select`

**Summary:**
- Replace `_selectedEvent` with `HashSet<CapturedEventDto> _selectedEvents` and
  `int _lastClickedIndex = -1`.
- Multi-select behaviour:
  - Plain Click → clear set, add item, update `_lastClickedIndex`.
  - Ctrl+Click → toggle item, update `_lastClickedIndex`.
  - Shift+Click → add inclusive range `[min(_lastClickedIndex, current)..max(_lastClickedIndex, current)]`
    from the **current frame's filtered+sorted view list**. Do NOT update `_lastClickedIndex`.
- Context menu "Copy to JSON":
  - 1 selected → single event JSON object (existing path).
  - N > 1 selected → JSON array sorted by `Frame` ascending.
  - Both paths use `FdpJsonOptionsRegistry.Indented` + `JsonAestheticFormatter.FlattenNumericArrays`
    + `ImGui.SetClipboardText`.
- Detail pane: show only when exactly 1 event selected; for multi-select show "Multiple events selected".

**Success Conditions (must be tests, not just compile checks):**
1. Ctrl+Click rows 1 and 3 → `_selectedEvents.Count == 2`.
2. Plain-click row 2, Shift+Click row 5 in 8-item view → `_selectedEvents` contains exactly items
   at indices 2, 3, 4, 5; `_lastClickedIndex` remains 2.
3. Clipboard string for 2 selected events is a valid JSON array with 2 elements in ascending frame order.
4. `FixedString64` fields in copied JSON are string values (not struct JSON).
5. Regression: single-event "Copy to JSON" still works.

---

### DD-P3-T02 — EntityInspectorPanel Multi-Select

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p3-t02--entityinspectorpanel-multi-select`

**Summary:**
- Extend `EntityInspectorPanel` with `HashSet<Entity> _selectedEntities` and
  `int _lastClickedIndex = -1`.
- Same plain/Ctrl/Shift-Click semantics as DD-P3-T01.
- Add `IEntityContextMenuHandler.PopulateMenu(IReadOnlyCollection<Entity>, IContextMenuBuilder)` overload.
- "Copy to JSON (N items)" context menu calls `IEntityStateExtractionService.ExtractEntities` with
  selected entities' network IDs, then runs the two-stage JSON pipeline.
- Detail pane: only for exactly 1 entity; multi-select shows "Multiple entities selected — details not available".
- Single-entity context menu items only shown when exactly 1 entity selected.

**Constructor note:** `EntityInspectorPanel` will need `IEntityStateExtractionService` injected.
Check its current constructor and update all instantiation sites.

**Success Conditions:**
1. Select 3 entities, invoke "Copy to JSON (3 items)" → valid JSON array with 3 elements each having
   `Components` dict with `NetworkIdentity` entry.
2. Plain-click index 1, Shift+Click index 4 in 7-item view → 4 entities at indices 1-4 selected;
   `_lastClickedIndex` remains 1.
3. Multi-select with entities lacking `NetworkIdentity` falls back gracefully.
4. Regression: single-select context menu unaffected.

---

### DD-P4-T01 — Enum Extensions and DiagnosticDumpPayloadDto

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p4-t01--enum-extensions-and-diagnosticdumppayloaddto`

**Summary:**
- Add `DumpDiagnostics = 16` to `ClusterOpType` enum.
- Add `DumpDiagnostics = 28` to `NodeOpType` enum.
- Add `DiagnosticDumpPayloadDto` record to `OrchestrationPayloadDtos.cs`:
  ```csharp
  public record DiagnosticDumpPayloadDto
  {
      [JsonPropertyName("transaction_id")]   public Guid     TransactionId    { get; init; }
      [JsonPropertyName("requested_at")]     public DateTime RequestedAt      { get; init; }
      [JsonPropertyName("target_node_ids")]  public int[]?   TargetNodeIds    { get; init; }
      [JsonPropertyName("dump_events")]      public bool     DumpEvents       { get; init; }
      [JsonPropertyName("dump_entities")]    public bool     DumpEntities     { get; init; }
      [JsonPropertyName("dump_architecture")]public bool     DumpArchitecture { get; init; }
      [JsonPropertyName("dump_logs")]        public bool     DumpLogs         { get; init; }
      [JsonPropertyName("event_providers")]  public string[]?EventProviders   { get; init; }
      [JsonPropertyName("use_markdown")]     public bool     UseMarkdownWrapper{ get; init; }
      [JsonPropertyName("max_age_hours")]    public float    MaxAgeHours      { get; init; } = 24f;
      [JsonPropertyName("severity_threshold")]public int     SeverityThreshold{ get; init; }
  }
  ```

**Success Conditions:**
1. `ClusterOpType.DumpDiagnostics == 16` and `NodeOpType.DumpDiagnostics == 28`.
2. DTO round-trips via `FdpJsonOptionsRegistry.DefaultRelaxed` correctly.
3. Deserialising `"EventProviders": null` → `EventProviders == null`.
4. Two DTOs 1 second apart have different `RequestedAt`; round-trip preserves it to second precision.

---

### DD-P4-T02 — ExecuteDiagnosticDumpIntent

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p4-t02--executediagnosticdumpintent`

**Summary:**
- Add `ExecuteDiagnosticDumpIntent` struct to `ClusterOpIntents.cs`:
  ```csharp
  [EventId(9058)]
  [DataPolicy(DataPolicy.NoRecord)]
  public struct ExecuteDiagnosticDumpIntent
  {
      public Guid   RequestId;
      public string PayloadJson;  // serialised DiagnosticDumpPayloadDto
  }
  ```
- Follow the pattern of `ExecuteStorageOpIntent` (examine that struct first for exact field conventions).

**Success Conditions:**
1. Compiles. EventId 9058 unique in the file.
2. Accessible from both `Fdp.Toolkits` and `Hrot.Network.Orchestration`.

---

### DD-P4-T03 — ClusterOpEgressTranslator and ClusterOpMasterTranslator

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p4-t03--clusteropegress-translator--clusteropmastertranslator`

**Summary:**
- Follow the exact same pattern as `SaveScenario` / `ExecuteStorageOpIntent` in both translators.
- Egress: handle `ExecuteDiagnosticDumpIntent` → write `ClusterOpRequest` with
  `OperationType = DumpDiagnostics` and `DomainPayload = intent.PayloadJson`.
- Master: handle `ClusterOpRequest` with `OperationType == DumpDiagnostics` → publish
  `ExecuteDiagnosticDumpIntent` to Orchestrator bus.

**Success Conditions:**
1. Integration test: Egress translates intent → DDS request with `DumpDiagnostics` op type.
2. Integration test: Incoming DDS request → master publishes intent on orchestrator bus.

---

### DD-P4-T04 — DiagnosticsConsensusAggregator

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p4-t04--diagnosticsconsensusaggregator`

**Summary:**
- Create `DiagnosticsConsensusAggregator.cs` in `Hrot.Orchestrator`.
- Implement `INodeResponseAggregator` for `NodeOpType.DumpDiagnostics`.
- Flatten `List<FileManifestEntry>` payloads from all nodes into a single list.
- Strip `SourceUnc` before building the `ClusterOpStatus` payload for DDS transmission (ExCon
  cannot access node-local UNC paths).
- Full manifest (with SourceUnc) retained internally for `DiagnosticsDumpProcessManager.PullToNasAsync`.

**Model to follow:** `StorageConsensusAggregator` — inspect it carefully.

**Success Conditions:**
1. 3 responses × 2 entries → 6 entries aggregated.
2. Empty manifest in one response → no exception.
3. Internal manifest has SourceUnc; DDS payload manifest has SourceUnc stripped.

---

### DD-P4-T05 — DiagnosticsDumpProcessManager

**Full spec:** `.dev/dump-diag/TASK-DETAIL.md#dd-p4-t05--diagnosticsdumpprocessmanager`

**Summary:**
- Create `DiagnosticsDumpProcessManager.cs` in `Hrot.Orchestrator`.
- Observe `ClusterOpCompletedEvent` for `ClusterOpType.DumpDiagnostics`.
- Observe transaction abort events for `ClusterOpType.DumpDiagnostics` → publish
  `ClusterOpStatus(Failure)` without calling `PullToNasAsync`.
- On success: call `StorageGatewayModule.PullToNasAsync(fullManifest, _config.NasBasePath)`.
- After pull: publish final `ClusterOpStatus` with stripped manifest.

**Model to follow:** `StorageProcessManager`.
**NAS base path:** `_config.NasBasePath` — NOT `OrchestrationConstants.DefaultStagingDirectory`.

**Success Conditions:**
1. Pull success → `ClusterOpStatus(Success)` published.
2. Pull failure → `ClusterOpStatus(Failure)` with error text.
3. Abort → `PullToNasAsync` NOT called; `ClusterOpStatus(Failure)` published immediately.

---

## 🧪 Test-Driven Task Progression

For each task, follow this mandatory sequence:

1. **Read** all relevant source files (interfaces, existing tests, models to follow).
2. **Write test stubs first** — all test methods present, all asserting `false` initially.
3. **Implement** the feature until all test stubs pass.
4. **Build** — zero errors required before moving to the next task.
5. **Run tests** — all new tests must pass before proceeding.

**Never skip a task's tests to "come back later".**

---

## 🚀 Developer Insights Required in Report

Your BATCH-03-REPORT.md must answer:

1. **What issues were encountered?** List each blocker and how you resolved it.
2. **What weak points did you spot in the codebase?** (beyond the tasks themselves)
3. **What design decisions did you make beyond the spec?** (no surprises — document them)
4. **Which existing patterns did you use as models?** (name the files you copied from)

---

## 📋 Report Format

```markdown
# BATCH-03 Report — dump-diag

**Date:** YYYY-MM-DD
**Workstream:** dump-diag
**Batch:** BATCH-03

## Summary
[1-2 sentence overall status]

## Task Status
| Task ID | Title | Status | Tests |
|---------|-------|--------|-------|
| DD-P3-T01 | ... | Completed/Partial/Blocked | X/Y |
...

## Detailed Task Notes
[Per-task: what was implemented, what decisions were made]

## Issues Encountered
[List of blockers and resolutions]

## Weak Points Spotted
[Codebase observations]

## Design Decisions Made Beyond the Spec
[Any choices not explicitly covered by the spec]

## Test Results (New Tests Only)
| Suite | New Tests | Passed |
|-------|-----------|--------|
...
```
