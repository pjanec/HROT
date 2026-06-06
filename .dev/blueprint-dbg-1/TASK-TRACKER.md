# TASK-TRACKER — Blueprint debugging UX (blueprint-dbg-1)

**Mission:** make **Slice-1** blueprint debugging work end-to-end in the live editor UX — set node breakpoints on
the canvas, pause the live tick, step Over/Into/Out, watch values — then extend toward Slice-2 (data breakpoints).

**Branch:** `blueprint-integ-1`. **Detailed instructions per batch:** [TASK-DETAIL.md](./TASK-DETAIL.md).
**Architect briefing (relayed):** [ARCHITECT-BRIEFING-01.md](./ARCHITECT-BRIEFING-01.md).

## Key verified facts (the foundation these batches rest on)
- Live canvas = shared `AiGraphCanvasWindow` + `Hrot.Blueprints.Editor/Host/*` (NOT the dead `GraphEditorWindow`).
- Editor compiles `CompilerMode.Debug` + embedded PDB (`QuickReloadService.cs:64`) → **NodeEnter probes ARE
  emitted; breakpoints can hit today.** Pin watches additionally need **Trace** mode (Debug emits no `PinValueChanged`).
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
| 0 | **Cleanup** — delete dead `GraphEditorWindow` (+ tests / registrar entry); retire `TODO(D-BP-04)` | — | ☐ Not started | n/a (headless) |
| A | **Breakpoint set + render** — inject debug session into `BlueprintDocumentFactory`+renderers; `BlueprintBreakpointContextMenuProvider` (right-click → `session.SetBreakpoint`, dual-store automatic); `BlueprintBreakpointGutterRenderer` (red bullet) | — (Q1 resolved) | ☐ Not started | set bp on ticking BP → sim halts; red bullet shows |
| B | **Runtime overlay** — `BlueprintRuntimeOverlayRenderer` (executing-node gold pulse + recent-history glyphs) | A | ☐ Not started | running BP highlights live node; visible pause |
| C | **Step controls** — Step Over/Into/Out/Continue UI + canvas pause overlay, wired to session methods | A | ☐ Not started | step advances one tick & re-pauses |
| D | **Watches** — per-asset Trace toggle (add `EditorMetadata.CompilerMode`); right-click-pin "Add Watch"; `WatchPanelWindow` live values | A (Q2 resolved) | ☐ Not started | watch a pin → value updates each tick |

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
