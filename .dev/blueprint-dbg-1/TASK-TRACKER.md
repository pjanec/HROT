# TASK-TRACKER — Blueprint debugging UX (blueprint-dbg-1)

**Mission:** make **Slice-1** blueprint debugging work end-to-end in the live editor UX — set node breakpoints on
the canvas, pause the live tick, step Over/Into/Out, watch values — then extend toward Slice-2 (data breakpoints).

**Branch:** `blueprint-integ-1`. **Detailed instructions per batch:** [TASK-DETAIL.md](./TASK-DETAIL.md).
**Architect briefing (relayed):** [ARCHITECT-BRIEFING-01.md](./ARCHITECT-BRIEFING-01.md).

## ⚠️ CORRECTION (2026-06-08): breakpoints do NOT pause — node-identity bug found
The claim below that "breakpoints can hit today" is **FALSE in practice.** Live diagnosis proved the node ID the
editor sets a breakpoint with ≠ the node ID the runtime probes with: lowering drops the authored `NodeId`
(Delay `0b561966`→`976ef338`, Sequence `da9a9c0b`→`0ec3b253`) and `DebugProbeInsertion` mis-attributes each block's
probe to `Statements[0]` (a data-input read). The STATUS.md "probe coverage" theory rests on a **wrong node-ID
table** and is superseded. Fix = corrective batches **CF-1/CF-2/CF-3** (see TASK-DETAIL.md). The backend pause chain
itself is fine (proven by `BreakpointTests`).

## Key verified facts (the foundation these batches rest on)
- Live canvas = shared `AiGraphCanvasWindow` + `Hrot.Blueprints.Editor/Host/*` (NOT the dead `GraphEditorWindow`).
- Editor compiles `CompilerMode.Debug` + embedded PDB (`QuickReloadService.cs:64`) → NodeEnter probes are emitted,
  **but keyed to the WRONG node ids (see CF correction above), so editor breakpoints never match.** Pin watches
  additionally need **Trace** mode (Debug emits no `PinValueChanged`).
- Backend is wired: `DebugProbe.Sink = bpBlueprintSession`, `MasterSyncTimeControllerAdapter`
  (`EditorSubsystem.cs:870,887`); `BlueprintDebugSession.OnNodeEnter → RequestPause()`. **The gap is purely the
  canvas UX wiring.**
- Proven template to mirror: the sibling **BTree** debug UX on the same canvas —
  `BTreeDocumentFactory.Build` (debug params + `BuildRenderers`), `BTreeEditorHostServices.SetBreakpointManager`,
  `BTreeBreakpointContextMenuProvider`, `BTreeBreakpointGutterRenderer`, `BTreeRuntimeOverlayRenderer`.
  **Graph model stays debug-unaware** — visuals are drawn by renderers reading the session each frame (do NOT
  mutate `NodeState`).

## Batches
| # | Batch | Depends on | Status | User smoke |
|---|---|---|---|---|
| 0 | **Cleanup** — delete dead `GraphEditorWindow` (+ tests / registrar entry); retire `TODO(D-BP-04)` | — | ✅ Done (BF-UX1 FIX D) | n/a (headless) |
| A | **Breakpoint set + render** — inject debug session into `BlueprintDocumentFactory`+renderers; `BlueprintBreakpointContextMenuProvider` (right-click → `session.SetBreakpoint`, dual-store automatic); `BlueprintBreakpointGutterRenderer` (red bullet) | — (Q1 resolved) | ✅ Done (headless gates; VISUAL SMOKE PENDING) | set bp on ticking BP → sim halts; red bullet shows |
| B | **Runtime overlay** — `BlueprintRuntimeOverlayRenderer` (executing-node gold pulse + recent-history glyphs) | A | ✅ Done (headless gates; VISUAL SMOKE PENDING) | running BP highlights live node; visible pause |
| C | **Step controls** — Step Over/Into/Out/Continue UI + canvas pause overlay, wired to session methods | A | ✅ Done (headless gates; VISUAL SMOKE PENDING) | step advances one tick & re-pauses |
| D | **Watches** — per-asset Trace toggle (add `EditorMetadata.CompilerMode`); right-click-pin "Add Watch"; `WatchPanelWindow` live values | A (Q2 resolved) | ✅ Done (CompilerMode + QuickReload; pin menu deferred; VISUAL SMOKE PENDING) | watch a pin → value updates each tick |
| E | **Breakpoint Toggle fix** — bridge `IBlueprintDebugSession` → NodeEdit `IDebugSession`; add "Toggle Breakpoint" to `CanvasRenderer`; register `editor.toggle-breakpoint` command | A | ✅ Done (UX only — does NOT pause; see CF) | set bp on ticking BP → sim halts; red bullet shows |
| CF-1 | **Ground-truth diagnostic** — reporting test: compile `Count4` Debug, dump DebugMap entries + emitted `NodeEnter` ids + authored-id coverage to `reports/CF1-NODE-IDENTITY-REPORT.md`. No production code change | E | ✅ Done (Zoo) | n/a (headless report) |
| CF-2 | **Preserve authored node identity** — add `OriginNodeId` provenance through lowering; key probe to the block's owning exec node (not `Statements[0]`); DebugMap + `NodeEnter` use authored id; pure data nodes get no probe | CF-1 | ✅ Done (Zoo + lead supplemental fixes) | bp on Delay/Sequence (authored id) pauses sim |
| CF-3 | **Reconcile tests + editor gating + cleanup** — fix probe/step count tests (old→new documented); gate canvas breakpoint toggle to DebugMap-eligible nodes; remove `DiagLog`/`bp-diag.log` | CF-2 | 🔄 In progress (Zoo) | bp only settable on exec nodes; Delay pauses; data node toggle disabled |

Slice-2 (true break-on-pin-write data breakpoints) is a cheap follow-on once D lands, since `OnPinValueChanged`
already runs in Trace mode.

## Architect answers — RESOLVED & verified against code
- **Q1 (→ Batch A) — DONE in code already.** Coexisting stores: `BlueprintDebugSession` is authority for the
  editor UI / canvas; `IDataBreakpointManager` is authority for the engine's snapshot pause-gate. **Verified:**
  `BlueprintDebugSession.SetBreakpoint` (`:251`) records `AssetStructureHashAtSetTime` and forwards to
  `_dataBreakpointManager.AddBreakpoint(new ExternalHitTagPredicateDto { Tag = nodeIdStr })` (`:267-272`), tracks
  `_mgrBpIds`, clears from both, and the session is wired in production at `EditorSubsystem.cs:886`
  (`SetDataBreakpointManager(_bpManager)`). **Consequence:** Batch A's context menu just calls
  `session.SetBreakpoint`/`ClearBreakpoint` — dual-registration is automatic, no manual manager wiring in the menu.
- **Q2 (→ Batch D) — answered, with two corrections.** Intended UX = a per-asset **Debug/Trace dropdown** (default
  Debug) that writes `asset.EditorMetadata.CompilerMode`, then the user runs Quick Reload to re-emit instrumented
  code. Corrections from code: (a) the toggle goes in the **real production toolbar**, NOT the dead
  `GraphEditorWindow`; (b) **`EditorMetadata.CompilerMode` does not exist yet** — Batch D must add it
  (`JsonIgnore`-when-default for byte-stability) and make `QuickReloadService.cs:64` read it instead of hardcoding
  `CompilerMode.Debug`. Sibling HSM/BTree use a *runtime* trace-buffer flag (`DebugState.EnableTraceBuffer`, live
  via inspector context menu) — NOT applicable to blueprints (pin values are polymorphic; compile-time `Trace`
  emits strongly-typed `PinValueChanged<T>` instead).

## Operating contract (per onboarding §1 / DEV-LEAD-GUIDE)
- Lead plans a batch → delegates implementation to a **`sonnet`** sub-agent (`general-purpose`) → **reviews HARD**
  (reads code + test assertions, re-runs suites independently, diffs failure-set by name) → **lead commits per batch**.
- Each batch is **interactively user-verifiable**; build + headless gate, commit with "VISUAL/INTERACTIVE
  VERIFICATION PENDING", then user smokes.

## Gates & baseline (per onboarding §4)
- `dotnet build IOS-IG-SimHost.sln -c Debug` → 0/0. Editor must be CLOSED (DLL locks).
- `Hrot.Blueprints.Tests` → **7 pre-existing failures, 0 new** (list full failure-set by name in every review).
- `Hrot.Editor.AiShared.Tests`; `EditorSubsystemBoot` 10/10.
- Don't regenerate golden snapshots unless codegen intentionally changes.
