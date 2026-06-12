# BATCH-HS-06 Review — TASK-HS-06 validation surfacing [VISUAL GATE]

**Reviewer:** Dev Lead · **Date:** 2026-06-13 · **Status:** ✅ APPROVED (headless) · **Impl:** Zoo

## Verification (independent — read all 5 diffs, built composition root, re-ran suite)
- **(a) Registrar (EditorSubsystem.cs):** exactly the minimal change — `validators: new IAssetValidator[] { new HsmAssetValidator(sharedSchemaExporter) }` added to `_hsmRegistrar` ctor (lines 1961–1964), mirroring the BTree registrar and reusing the same shared exporter. Nothing else in that file touched. **Built `Hrot.Editor` independently → 0 errors** (composition root sound).
- **(b) Node-state (HsmAsset + HsmGraphModel):** `StateNode` gains nullable ephemeral `DiagnosticState`/`DiagnosticTooltip`; `State => DiagnosticState ?? (IsBreakpoint ? Warning : Normal)` — breakpoint fallback preserved (nullable, never set to Normal). `BuildCaches` runs `HsmValidator.Validate(_asset, _asset as IBlackboardManagedAsset)`, maps each `TargetStableId` with Error-wins, resets to null when no diagnostic. Correct.
- **(c) Renderer feed:** `HsmRegionConflictsRenderer.CurrentDiagnostics` getter added; `HsmGraphModel` exposes `LastDiagnostics` + `DiagnosticsRecomputed` event; `HsmDocumentFactory` captures the renderer via an `out` param and wires `graphModel.DiagnosticsRecomputed += renderer.SetDiagnostics` + initial push. **Model stays decoupled** (no renderer-type reference) — matches the decoupled-event decision.
- **No cheating:** touched only the 5 named production files + new test file.
- **Tests (9, behavioral):** composite-without-initial → `State==Error` + tooltip contains the message; valid → Normal/null; **breakpoint preserved** (DiagnosticState null → Warning); `LastDiagnostics` non-empty; `DiagnosticsRecomputed` fires on rebuild; renderer wiring push (`CurrentDiagnostics` same ref); **OutputLaneConflict** present for a conflicting parallel; HsmAssetValidator SupportedKind==Hsm + produces diagnostics. Values/enums/codes, not strings.
- **Re-run (no regenerate flag):** `Hrot.Hsm.Editor.Tests` **432/0** (9 new, 0 pre-existing failures).

## Issues
None. **[VISUAL GATE]** — the actual node Error/Warning outline + the yellow region-conflict line + "!" glyph confirmed by lead at REVIEW-HS.

## Verdict
APPROVED (headless). HSM diagnostics now reach the Diagnostics window (validator registered), the canvas (per-state node-state/tooltip), and the region-conflict overlay (renderer fed).

## Commit message
```
feat(hsm-editor): validation surfacing — Diagnostics + node-state + region-conflict feed (BATCH-HS-06 / TASK-HS-06)

(a) Register HsmAssetValidator in the HSM PerspectiveWorkspaceRegistrar so HSM
diagnostics show in the Diagnostics window (mirrors BT-04). (b) StateNode gains
ephemeral DiagnosticState/DiagnosticTooltip; HsmGraphModel.BuildCaches runs
HsmValidator and projects per-StableId severity (Error-wins, breakpoint fallback
preserved), exposing LastDiagnostics + a DiagnosticsRecomputed event. (c) Feed
HsmRegionConflictsRenderer.SetDiagnostics via that event, wired in
HsmDocumentFactory (model stays decoupled from the renderer). +9 headless tests.
Pixel overlay is the lead's visual gate (REVIEW-HS).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
