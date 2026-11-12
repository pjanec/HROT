<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — §1 the 133 UI write sites (U-1…U-4), §8 D-5 (UI writes executed on the sim thread), CE-4455
-->

# HANDOFF — coordinator → backend (VM): DAYTIME POC — UI writes executed on the simulation thread, and the editor's memory

**For:** a backend VM session. **From:** coordinator. **Dispatched at `a0cc3067e`**, scope frozen.
🔒 User, `2026-11-12`: *"we need to measure first, and once we are sure, then we will start the overnight batch"*. ⛔⛔ **Autonomous** (R-4156).

## Where to work
A NEW branch **`exp/poc-writes`** from **`exp/integration`**. Status `docs/blueprints/batches/STATUS_POC_Write_Path.md` on it. Switch default OFF.

| # | prove | done when |
|---|---|---|
| **P-1** | ⭐ ONE sim job queue (the same the HTTP split will use, inventory §3) that the sim thread drains between ticks; UI writes POSTED to it with their existing code retargeted from A to S (D-5) | the queue + a rail (posted from the main thread, applied on the sim thread before the next phase, in order) |
| **P-2** | ⭐ three representative kinds end to end in the threaded editor: ① a DRAG — a ghost in A while dragging, the drop one job, SNAP-BACK when refused (today the gizmo reads the route at once — U-3); ② an INSPECTOR field edit through `RepositoryAdapter` (today a direct write of any component — U-4); ③ SCENARIO NEW / LOAD — the clear on S, then a FULL resync into A (today it clears A — inventory §1) | a rail per kind while PLAYING; the refused drop snaps back |
| **P-3** | ⭐ UNDO / REDO on S: `WorldUndoRecorder` installed on S (today on A — U-1); the bracket's before-snapshot taken on the sim thread (a drag's "before" = S at the drop, the ghost never touched S); undo / redo as sim jobs; A sees the result at the next pull | undo → redo → undo of each P-2 kind, the world hash equal to the expected state |
| **P-4** | ⭐ the callers that read a result AT ONCE — `EditTicket` readers (`ScenarioClockEdit:49`, `FormView:504`, `KnowledgeOrders:17`) and the gizmo snap-backs: list every such caller across the 133 sites (inventory §1) and classify: copes with a result a tick later / needs a pending state / needs a redesign | the table with `file:line`; ⭐ the count that needs more than mechanical work — the overnight track's real size |
| **P-5** | the editor's ~1.2 GB unattributed memory (CE-4456 N-1: 4.24 GB at 1 000 men): `dotnet-gcdump` of the editor, top owners by retained size | the top 10 with sizes; which are fixable |
| **P-6** | ⭐ the verdict — is D-5 (execute on the sim thread) viable for all 133 sites; what the overnight build must do differently | top of the status file; folded into inventory §8 |

Rules: the touched features' OWN rails only, no broad suites; slow things in the background, never polled; ⛔ no screenshots in git.

---
_coordinator lane, 2026-11-12_
