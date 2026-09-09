# RESUME — macro design deep-dive (start here after compaction)

## Where things stand

| | |
|---|---|
| **Batch 27** | dispatched, frozen at `d462b5c0`, **in flight** — do not touch the tracker or detail docs (rule 6) |
| **Macro design** | ⭐ **already closed** — [Q25](Architect_Question_25_Macros.md), all five sub-questions answered 2026-08-07, self-researched, with a verification log |
| **Build plan** | rows **BP-79 → BP-83** already exist, in that order. **BP-79 lands first** because it closes the two *silent*-failure holes |
| **BP-77** | the My Blueprint "Macros +" button is **live and does nothing** — a real hole today |

## What "go deeper" means here

Q25 settled *what a macro is*. What does **not** exist yet is the **implementation-level design per
slice**. That is the work:

1. **BP-79** — `GraphKind.Macro` + the fail-loud net. ⚠ Read Q25-E first; the guard rails are the point
   of doing this one first.
2. **BP-80** — the authoring surface (create / rename / delete a macro graph).
3. **BP-81…83** — expansion, guard enforcement, tooling. Confirm the current row text before designing;
   it was written before Batches 20-26 landed.

## Carry these in

- ⭐ **Q25-C decided asset-local macros first**, because the compiler sees sibling **signatures, not
  bodies** (`BlueprintSignature` has no `Nodes`/`Links`) — cross-asset inlining needs bodies. Verified.
- ⭐ **Expansion is a new pipeline stage.** `Stage3_Normalize.Run` already returns a *new* asset, so a
  node-set-rewriting stage has a precedent to copy. Verified.
- ⭐ **Inlined latent nodes work.** `WaitLowering_Instance.Apply` keys off suspend blocks and chains N
  of them; it does not care where a node was authored. Verified — this is why macros can contain latents.
- ⚠ `IrDebugAnnotation.OriginNodeId` is a **fallback**, not the primary (`CSharpEmitter:45,53`
  `debug?.NodeId ?? debug?.OriginNodeId`). Matters for breakpoints inside expanded macros — Q25-E.

## Process reminders

- **The coordinator allocates no ids** (rule 3, three collisions). Describe findings; the implementation
  session numbers them.
- **Never amend a dispatched handoff** (rule 1). Batch 28 is a new file.
- ⭐ **Re-read any handoff against the rules immediately before stamping** — the last two pre-dispatch
  reviews each caught a real defect (a backwards `TreatWarningsAsErrors` claim; a missing rules section
  plus a stale baseline that would have had warnings silenced).
