<!--STATUS
state: LIVE — the overnight build is RUNNING (branch exp/threaded-build)
updated: 2026-11-12
current-answer: the morning report (top) · then NEXT (the resume point after a compaction)
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — §8 the decision list; §1 UI writes; §2 run controls; §3 HTTP; §4 gizmo move; §7 the review
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §13 shared state, §14 target picture, §14.6 gizmo block
  - ../../DESIGN_Parallel_Simulation.md — §18–§18.2 the per-entity rule
  - HANDOFF_Coordinator_To_Backend_Overnight_Threaded_Build.md — the handoff (dispatched at e9638d40b, re-stamped f28f53609)
-->

# STATUS — the overnight threaded build (branch `exp/threaded-build`)

**Branch:** `exp/threaded-build` from `exp/integration@406659433`, `coordinator@f28f53609` merged (the handoff, R-4216 / R-4217, inventory §8.2a).
Started marker `1cd3c2dbc`.

## 1. DONE

| item | state | commit |
|---|---|---|
| N-1 `CE-4463` the one sim job queue | 🔄 seam built + rails (`SimJobQueue`, `SimJobs.Run`, `SimLink`); editor wiring next | (this commit) |
| N-3 `CE-4465` two staging buffers + publish queue | 🔄 seam built + rails (`StagingWorlds`); editor wiring next | (this commit) |

## 2. GATES

| gate | command | result |
|---|---|---|
| N-1 + N-3 rails + TwoWorlds | `dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter "FullyQualifiedName~SimJobQueueTests\|FullyQualifiedName~StagingWorldsTests\|FullyQualifiedName~TwoWorldsTests"` | 14 / 0 / 0 |
| red-proofs (each a compiling mutation, reverted) | `scratchpad/redproof.sh` | ① torn tick: publish into the pulled buffer, no claim ⇒ red · ② drain only the pulled tick's packet ⇒ red · ③ spin instead of skip ⇒ red · N-1 LIFO drain ⇒ red |

## 3. DECISIONS

| # | problem | options | chosen | why | where |
|---|---|---|---|---|---|
| D-1 | where the seams live (R-4215: no host handled differently) | in `EditorSubsystem` (the prototype) · in `Fdp.Core` shared by every host | ⭐ `Fdp.Core.Threading` (`SimJobQueue`, `StagingWorlds`) — any host / kernel instantiates them; one queue per kernel (N-13) | built ONCE, reachable from every host and every UI write site (they hold a world, nothing else) | `FDP/Engine/Fdp.Core/Threading/` |
| D-2 | how a UI write site reaches the queue with no new plumbing | pass the queue to 133 sites · a static · ⭐ a link on world A | ⭐ `SimLink` — a node-local managed singleton on A; `SimJobs.Run(world, job)` posts when A carries it, runs at once otherwise | a call site already holds A; switch OFF ⇒ no link ⇒ today's code path, unchanged | `SimJobQueue.cs` |
| D-3 | events / replies of a publish the UI never pulls (it pulls only the LATEST buffer) | keep them in the buffer (lost when skipped over — the first cut did, found while writing the rail) · ⭐ tick-tagged packets in their own FIFO | ⭐ packets: the UI drains every packet up to the tick it pulls | "every event once, in order, skipped ticks included" (handoff N-3) | `StagingWorlds.Pull` |
| D-4 | a row copy stamps the destination chunk with ITS OWN version — with two alternating sources an equal version from the other source would make the table sync skip a stale chunk | force-copy chunks · a sentinel version · ⭐ rows copy only types OUTSIDE the table mask | masked types arrive whole with the table sync; out-of-mask types are never table-synced, so their versions decide nothing | `EntityRepository.SyncRowsFrom(…, skipTypes)` |
| D-5 | `SimLink` needs a component id | — | `GlobalComponentIds.SimLink = 475` (free in every lane and in every test) | — | `GlobalComponentIds.cs` |

## 4. BUGS FIXED

| bug | test that catches it |
|---|---|
| a ROW of a type the destination world never registered was silently dropped ("the next table sync brings it" — never, for an out-of-mask type) | `StagingWorldsTests.RowsCopyOnlyTypesOutsideTheMask_…` |

## 5. OPEN
(none yet)

## 6. NUMBERS
(N-16, later)

## NEXT (the resume point)
N-2 (tick context + current entity on pool batches) → wire N-1 + N-3 into the editor's mode-3 worker loop (drain jobs between ticks, publish after
each tick, pull at the UI frame start, remove `ThreadedKernelGate`'s world lock from that path) → N-4 orchestration bus → N-5 run controls → N-6 …
