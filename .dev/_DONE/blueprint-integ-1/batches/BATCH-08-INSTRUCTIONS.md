# BATCH-08: Debug session registry + runtime inspector + trace timeline (BTree/HSM)
**Tasks:** AIE-030, AIE-031, AIE-032   **Phase:** 3   **Est:** ~11h
**Dependencies:** BATCH-04 (composition + per-perspective RuntimeInspector/TraceTimeline windows already registered).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/blueprint-integ-1/DESIGN.md` §5.6; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-030, AIE-031, AIE-032.
3. `.dev/blueprint-integ-1/reviews/BATCH-07-REVIEW.md` — current state (Phase 2 complete).

Use **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`. Headless tests must not call ImGui without a context.

## Ground truth — verified components (wire, don't rebuild)
- Shared debug infra (`Hrot/Editor/Hrot.Editor.AiShared/Debug/`): `AiTracerCoordinator`, `DebugSessionRegistry` (`RegisterSessionFactory<T>`, `TryAcquireSession<T>`). Existing tests: `AiTracerCoordinatorTests`, `DebugSessionRegistryTests`.
- Sessions: `BTreeDebugSession` (`Hrot.BTree.Editor/Debug/`), `HsmDebugSession` (`Hrot.Hsm.Editor/Debug/`) — both inherit `AiDebugSessionBase`. Verify ctors (e.g. `BTreeDebugSession(AiTracerCoordinator?)`) and the `Update(EntityRepository, Entity)` / snapshot APIs.
- Windows already registered per perspective (BATCH-04): `RuntimeInspectorWindow`, `TraceTimelineWindow` (AiShared). Verify their pane/lane registration APIs (`RegisterPane`, `RegisterProvider` or similar).
- Panes/lanes: `BTreeRuntimeInspectorPane`/`HsmRuntimeInspectorPane` and `BTreeTraceLaneProvider`/`HsmTraceLaneProvider` — **verify they exist** (design-talk references them); if a name differs, follow the code. The editor owns `_world` (EntityRepository), `_kernel`, `_timeController`, and `_aiCoordinator`.

## Tasks (in order)

### Task 1: Debug session registry + factories (AIE-030) — `EditorSubsystem.cs` + (if needed) contributor wiring
Instantiate `AiTracerCoordinator` + `DebugSessionRegistry` once; register `BTreeDebugSession`/`HsmDebugSession` factories (bound to the editor's live `_world`/`_kernel`/`_timeController`). Wire `NodeDebugMetadata` into the sessions via the contributors (`BTreeAssetContributor` accepts a `BTreeDebugSession`) so node-index→`VisualId` symbolication works. Preserve all existing wiring.
**Tests required:** `DebugRegistry_AcquireBTreeSession_ReturnsSession`; `DebugRegistry_AcquireHsmSession_ReturnsSession`; `Contributor_WiresDebugMetadata_IntoSession` (a node index symbolicates to the expected `VisualId`). Keep existing `DebugSessionRegistryTests`/`AiTracerCoordinatorTests` + `EditorSubsystemBoot` green.

### Task 2: Runtime inspector panes per perspective (AIE-031) — composition + panes
Register `BTreeRuntimeInspectorPane`/`HsmRuntimeInspectorPane` with the per-perspective `RuntimeInspectorWindow`, bound to the active debug session; the pane renders the kind's kernel state (BTree: running node + stack + registers; HSM: active configuration).
**Tests required:** `RuntimeInspector_BTree_ShowsRunningNodeAndStack` (over a fake/stub snapshot — assert the projected values, not just non-null); `RuntimeInspector_Hsm_ShowsActiveConfiguration`. Keep `RuntimeInspectorWindowTests` green.

### Task 3: Trace timeline lane providers per perspective (AIE-032) — composition
Register `BTreeTraceLaneProvider`/`HsmTraceLaneProvider` with the per-perspective `TraceTimelineWindow`.
**Tests required:** `TraceTimeline_BTree_RegistersExpectedLanes` (assert the lane ids/labels: nodes/stack/async/errors); `TraceTimeline_Hsm_RegistersExpectedLanes` (states/events/actions/guards/timers/conflicts as implemented). Keep `TraceTimelineWindowTests` green.

## Success Criteria
- [ ] AIE-030/031/032 per success conditions.
- [ ] Green (full, no crashes): `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter. Blueprints no new failures beyond DEBT-006's 10.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-08-REPORT.md`.

## Execution rules
- Tasks in sequence; run the named suites yourself; fix root causes; never fake a pass. Verify session/pane/lane class names + APIs against the code (do not invent); if a name differs, follow the code and note it.
- Canvas runtime overlays + breakpoint toggles + Watch/Breakpoints windows are **BATCH-09** — out of scope here.

## Report Requirements
In `reports/BATCH-08-REPORT.md`: the actual session/pane/lane class names + APIs used; how sessions bind to world/kernel/time; how debug-metadata symbolication was verified; actual test counts; confirm `EditorSubsystemBoot` 10/10; suggested commit message. No comprehension questions.
