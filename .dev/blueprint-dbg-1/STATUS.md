# STATUS — Blueprint Debugging UX (blueprint-dbg-1)

**Date:** 2026-06-08 (rev 2 — diagnosis corrected)
**Branch:** `blueprint-integ-1`
**Mission:** Make blueprint debugging work end-to-end — breakpoints, stepping, watch window.

---

## TL;DR — the real bug (supersedes rev 1)

Breakpoints don't pause because **the node ID the editor sets a breakpoint with ≠ the node ID the runtime fires
`OnNodeEnter` probes with.** It is *not* a probe-coverage problem and the backend pause chain is fine. The fix is
corrective batches **CF-1 → CF-2 → CF-3** (see [TASK-DETAIL.md](./TASK-DETAIL.md), section "CORRECTIVE BATCHES").

> ⚠️ **Rev 1 of this file was wrong.** It diagnosed a "probe coverage" gap using an **incorrect node-ID table**
> (it claimed `…0004`=Add and `0b561966`=Save Variable). The attempted multi-probe / Stage-5 fixes were chasing
> that wrong table. The corrected table and diagnosis are below. Lesson: always map node id→kind against the
> source `.bp.json`, never an inferred table.

---

## Corrected node-ID table (ground truth from `Count4.bp.json`)

Asset `47fe9c55-c6ca-4c69-9c5a-d46de25745de`, graph `10000006-0000-0000-0000-000000000001`:

| Authored node | Authored Id | Kind | In compiled DebugMap (keyed by authored id)? | Runtime probe |
|---|---|---|---|---|
| EventEntry | `…0001` | EventEntry | verify (CF-1) | — |
| SetVariable | `…0002` | SetVariable | yes | verify |
| **Add** | `…0003` | FunctionCall | yes | not firing as `…0003` |
| Get Count | `…0004` | GetVariable | yes | **fires `…0004`** (data node — wrong attribution) |
| Sequence | `da9a9c0b-…` | Sequence | **NO** → replaced by `0ec3b253-…` |
| **Delay** | `0b561966-…` | Delay | **NO** → replaced by `976ef338-…` |
| Return | `7b6da53f-…` | Return | verify (CF-1) | — |

**Evidence:** the live `bp-diag.log` shows the user set breakpoints correctly on Add (`…0003`) and Delay
(`0b561966`), but the only probes that ever fire are `…0004` and `976ef338`; after a compile, the in-memory
DebugMap was confirmed to contain `…0002/3/4`, `0ec3b253`, `976ef338` — and **not** `0b561966` or `da9a9c0b`
(editor reported `resolved=<NOT-IN-MAP>` for those). The synthetic `BreakpointTests` prove the pause chain hits
when the ids match — so the chain is not the problem.

---

## Two confirmed identity breaks (root cause)

1. **Provenance loss through lowering.** Stage-6 wait/instance lowering (`WaitLowering_Instance.cs`) and
   `Stage3_Normalize.SynthesizedGuid` emit replacement/synthesized statements that drop the authored `NodeId`
   (`IrDebugAnnotation.Synthesized = "stage6-wait-lower-inst"`, `NodeId = null`). The authored Delay/Sequence ids
   never reach the DebugMap.
2. **Probe mis-attribution.** `DebugProbeInsertion.cs:24` keys each block's `NodeEnter` probe to
   `block.Statements[0].Debug.NodeId`. A block's first statement is often an inlined **data-input read**
   (GetVariable `…0004`) — so the probe fires as the data node, not the exec node the block represents.

DebugMap entries are driven by statements carrying a `Debug.NodeId` (`CSharpEmitter.EmitNodeStart` is gated on
`debug?.NodeId != null` → `DebugMapBuilder.RecordNodeStart`), so both breaks also corrupt the map.

---

## What actually works (verified — do NOT re-investigate)

- Pause chain: `OnNodeEnter → HandleBreakpointHit → DataBreakpointManager.OnExternalHit → RequestPause` (synthetic
  `BreakpointTests` pass end-to-end).
- `StringComparer.Ordinal` breakpoint dictionary; F9 + right-click "Toggle Breakpoint"; the
  `IBlueprintDebugSession → IDebugSession` adapter; red marker rendering.
- The editor compiles `CompilerMode.Debug` and (after a compile) registers the DebugMap in-memory
  (`QuickReloadService.cs:73,161`). Compiling first is necessary but **cannot** fix an identity-space mismatch.

---

## The fix (corrective batches — see TASK-DETAIL.md)

- **CF-1 (diagnostic, no prod change):** reporting test compiles `Count4` Debug and writes
  `reports/CF1-NODE-IDENTITY-REPORT.md` — the authoritative authored-id ↔ DebugMap ↔ emitted-probe mapping. Pins
  down exactly what `976ef338`/`0ec3b253` are and where identity is lost. **Paste-ready Zoo prompt:**
  [batches/CF-1-ZOO-PROMPT.md](./batches/CF-1-ZOO-PROMPT.md).
- **CF-2 (fix):** add `OriginNodeId` provenance through lowering; key each block's probe to the **owning exec
  node** (not `Statements[0]`); DebugMap + `NodeEnter` use the authored id; pure data nodes get no probe. Success
  is machine-checked: DebugMap contains entries keyed by Delay `0b561966` and Sequence `da9a9c0b`; the generated
  source contains `DebugProbe.NodeEnter(self, "0b561966-…")` and `"da9a9c0b-…"`; an end-to-end breakpoint on the
  Delay's authored id yields `PauseRequestCount == 1`.
- **CF-3 (reconcile + cleanup):** update the probe/step **count** tests (old→new documented); gate the canvas
  breakpoint toggle to DebugMap-eligible (exec) nodes so there are no silent dead breakpoints; remove the
  temporary `DiagLog`/`bp-diag.log` diagnostics.

---

## Open question CF-1 settles

Whether "Add" (`…0003`, a `FunctionCall`) is **pure** (data, inlined) or **impure** (exec). If pure, it is
correctly non-breakable (standard visual-scripting semantics) and CF-3 will disable its toggle with a "data node"
tooltip. CF-2's tasks are written to handle either outcome.

---

## Uncommitted working-tree changes

```
M FDP/Toolkits/Fdp.Toolkits/Blueprints/Systems/BlueprintTickSystem.cs   (delta guard — keep)
M Hrot/.../BlueprintEditorModule.cs                                     (Full Rebuild debug-map load — keep)
M Hrot/.../SequenceEmitIntegrationTests.cs                              (test adjusted for delta guard — keep)
M Hrot/.../EditorSubsystem.cs                                           (Attach call — keep)
M Hrot/.../BlueprintDebugSession.cs                                     (TEMP diagnostics — CF-3 removes)
?? bp-diag.log                                                          (TEMP — CF-3 deletes)
```

The 4 "keep" fixes are independently correct and can be committed now. The diagnostics in `BlueprintDebugSession.cs`
(`DiagLog`, `_diagCount`, `_diagLogPath`, and their calls in `SetBreakpoint`/`OnNodeEnter`) plus `bp-diag.log` are
intentionally left in to aid CF-1/CF-2 and are removed by CF-3 before the final commit.
