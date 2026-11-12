<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
supersedes: HANDOFF_Coordinator_To_Backend_POC_Staging_Worlds.md · _POC_Write_Path.md · _POC_Brain_Rule.md (withdrawn unstarted)
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — ⭐ §8 the decision list (R-4216 two staging worlds, D-5 UI writes on the sim thread), §1 the 133 UI write sites, §7 the review
  - ../../DESIGN_Parallel_Simulation.md — ⭐ §18–§18.2 the per-entity rule, the redirect, the declared mask; §11.2 the brain's ~36 ms serial walk
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §14 the target shape, §14.1 the anti-race rules, §14.6 the whole gizmo block
-->

# HANDOFF — coordinator → backend (VM): ONE COMBINED POC — the whole threaded + parallel architecture working TOGETHER

**For:** one backend VM session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen at that sha; later documents are FYI only.
🔒 User, `2026-11-12`: *"we need to measure first, and once we are sure, then we will start the overnight batch"* · *"We need to prove that it
will work all together, not each separately."* ⛔⛔ **Autonomous** (R-4156) — a blocked item stops THAT item, never the run (R-106).

## Goal
Prove, in ONE build, that the pieces the overnight batch will build work **together**: the threaded editor with two staging worlds, UI writes
executed on the sim thread, and the per-entity rule running the brain in parallel — so the overnight build meets no unproven premise. Prove or
disprove each, and say which.

## Where to work
A NEW branch **`exp/poc-combined`** from **`exp/integration`**. Status `docs/blueprints/batches/STATUS_POC_Combined.md` on it, updated per item.
Every switch default OFF (0 B per steady frame with them off — R-2084).

## The order — shared seams FIRST, built ONCE
⭐ One owner per seam. ⛔ Never a second queue, a second tick context or a second copy path.

| # | build / prove | done when |
|---|---|---|
| **C-1** | ⭐ **the ONE sim job queue** — thread-safe, drained by the sim thread at a fixed point between ticks, in post order. It carries UI writes (D-5), run controls, HTTP work (inventory §3) AND the orchestration bus's UI side (the K-1 "Collection was modified" race) | a rail: posted from N threads, applied on the sim thread, in order, once; the K-1 repro 0 of 5 |
| **C-2** | ⭐ **the tick context on the pool** (F-1) — every pool batch carries the tick and the current entity; an access with no current entity inside a parallel phase throws in Debug | a rail, red-proved |
| **C-3** | ⭐ **two staging worlds** (R-4216) — after each tick the sim copies the masked state (tables, selected rows, singletons — §13 / §14.1) into B1 or B2, never waiting; an atomic "latest complete" index; the UI pulls the newest into A at its frame start only when newer; the old lock REMOVED. Events, logs (shots, detonations, squad reports) and edit outcomes ride with each staging world and drain into A in order, none lost or doubled when the UI skips ticks | the lock-acquire time ≈ 0; a rail: ticks published faster than the UI pulls ⇒ every event exactly once, in tick order, red-proved; MB per staging world at 1 000 men (`HROT_MEMORY_CENSUS=1`) |
| **C-4** | ⭐ **UI writes on S through C-1** — three kinds end to end while PLAYING: ① a drag (a ghost in A while dragging, the drop one job, SNAP-BACK when refused — U-3); ② an inspector field edit through `RepositoryAdapter` (U-4); ③ scenario new / load (the clear on S, then a full resync of both staging worlds and A) | a rail per kind; the refused drop snaps back |
| **C-5** | ⭐ **undo / redo on S** — `WorldUndoRecorder` on S (U-1); the before-snapshot taken on the sim thread (a drag's "before" = S at the drop); undo / redo as C-1 jobs | undo → redo → undo of each C-4 kind, the world hash as expected |
| **C-6** | ⭐ **the 133 UI write sites classified** — every caller that reads a result AT ONCE (`EditTicket` readers `ScenarioClockEdit:49`, `FormView:504`, `KnowledgeOrders:17`, the gizmo snap-backs …): copes a tick later / needs a pending state / needs a redesign | the table with `file:line`; ⭐ the count needing more than mechanical work = the overnight track's real size |
| **C-7** | ⭐ **the per-entity rule in the repository** (R-4212 / R-4214) — a read of ANOTHER entity's in-phase-written type served from the masked LAST-TICK copy taken serially at the phase start; a write to another entity captured as a field-level / append command (F-5), merged in fixed order; structural changes deferred (F-6); `Bus.Publish` inside the phase to the batch's list (F-2) | a rail per path; 0 B per steady tick |
| **C-8** | ⭐ **BrainTick's serial walk per entity on the pool** under C-7 — the census conversion list (child sensors deferred + idempotent while pending, F-8; callouts / order journal as commands; per-thread scratch; the shared statics) | the same world hash over N steps at 1 / 2 / 4 / 8 threads (stepped mode); the guard silent; red-proved by removing one deferral |
| **C-9** | the predicted behaviour changes, MEASURED on a contact-onset run: callout chorus (F-10's de-dup at playback), cover ties (F-9), squads per squad (F-11) | counts before / after; one "Contact" per burst within 10 m / 2 s |
| **C-10** | ⭐ **`--mode all`** (R-4214 ②): every kernel of the process on the ONE sim worker in sequence, one C-1 queue per kernel, the shared statics (`ParallelEntities.Mode`, `FdpWorkerPool`, timers) not contended — with C-8 on | 2 min at 1 000 men, 0 faults; each node's tick period |
| **C-11** | ⭐⭐ **THE COMBINED RUN — the proof the overnight batch needs.** The threaded editor (`HROT_THREADED_KERNEL=3`) + staging worlds + the brain parallel at 4 threads, the 1 000-man scripted session (`threaded-poc2-measure.py --mode 3`) WHILE PLAYING: drags (one refused), an inspector edit, undo / redo, a scenario load mid-run, play / pause / step | green twice, 0 faults; the tick period and the UI frame; the 1/2/4/8 hash equal with the UI edits replayed at the same ticks (stepped) |
| **C-12** | side task, no code: the editor's ~1.2 GB unattributed (CE-4456 N-1) — `dotnet-gcdump`, top 10 owners by retained size | the top 10; which are fixable |
| **C-13** | BrainTick and the whole frame at 1 / 2 / 4 threads on the VM (a trend) + ONE Windows script for the combined run at 1…12 threads | the VM trend; the script for the Windows lane |
| **C-14** | ⭐ **the verdict** — what is proven, what failed and why, what the overnight build must do differently | top of the status file; folded into inventory §8 and Parallel Simulation §18 |

## Rules
- Sub-agents may build items that touch no shared seam in parallel — ⛔ C-1, C-2, C-3 and C-7 have ONE author.
- The touched features' OWN rails only; ⛔ no broad suites, no scenario sweeps. Slow things in the background, never polled. ⛔ No screenshots in git.

---
_coordinator lane, 2026-11-12_
