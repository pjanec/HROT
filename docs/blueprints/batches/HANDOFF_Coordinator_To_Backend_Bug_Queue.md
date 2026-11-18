<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - PROTOCOL_Autonomous_Lanes.md — ⭐ HOW you work: the loop, the rate-limit rules, new handoffs, decisions skipped and written, notes
  - ../PLAN_Bug_Priorities.md — WHY this order (P1…P5 the user's priority, then the second tier)
-->

# HANDOFF — coordinator → `backend`: THE BUG QUEUE (22 rows)

**For:** backend. **From:** coordinator. **Dispatched at the commit that adds this file.** **When:** after your current handoff (bows / birds / outfits — part E on `backend-wip-part-e`) and the user-observation row CE-1958.

🔒 User, `2026-11-18`: *"I would like to focus on the most serious bugs"* · *"I need the handoffs to contain more bugs so the lanes keep themselves busy
for longer … sync with others … aware of token rate limit … watching for new handoffs … skipping stuff needed decisions (reporting those in written)"*.

## ⭐⭐⭐ Read first: [`PROTOCOL_Autonomous_Lanes.md`](PROTOCOL_Autonomous_Lanes.md)
It is the HOW for every row below: push after every row, the status file `STATUS_Backend_Queue.md` always resumable, the loop after each row
(fetch · take new handoffs addressed to you · read notes · merge the others · next row), decisions SKIPPED and written to
`DECISIONS_Backend.md` with your lean, notes to other lanes, and what to take when the queue runs dry.

⭐⭐ **First action of the run: create your hourly SELF-WAKE-UP** (protocol §1a — `backend self-wake-up`, into your own session, unless it already exists), so a rate-limit stop does not end the run; delete it when the queue is empty.

## The queue — in order (P = the user's priority, T2 = the second tier; ⭐ each row: reproduce · root-cause · fix with a rail · its scenario passes)

| tier | row | the bug (from the tracker — the row there is the truth) | note |
|---|---|---|---|
| P1 | **CE-3458** | OPEN `2026-11-16` (backend; found by `ua-animal-flock-flush`, `-guard-dog`, `-village-fight`) — AN UNREACHABLE MOVE NEVER FAILS: THE UNIT STANDS STILL FOREVER, SILENTLY. | Moves that cannot finish must FAIL, loudly — one principle for the three (ruling to record if the design has none) |
| P1 | **CE-3452** | OPEN `2026-11-16` (backend; found by the feature scenario `ua-moto-parked-bomb`) — A BIKE STARTING ON A ROAD FROM A STANDSTILL REPORTS `FailedBlocked` WHILE IT MOVES. | Moves that cannot finish must FAIL, loudly — one principle for the three (ruling to record if the design has none) |
| P1 | **CE-9023** |  · `convoy` → behaviors 📐 — `uc-urban-battle`'s *"and the convoy moves on"* row was a FALSE GREEN: the IED-immobilised APC kept "running" `FollowRoute` for good, which satisfied the row the instant a  | …the disabled vehicle that "runs" its route forever — ⭐ with behaviors (CE-9023 is filed to them: the play's half); you own the locomotion result |
| P5 | **CE-3457** | OPEN `2026-11-16` (backend; found by `ua-animal-herd-road`) — A VEHICLE NEVER STOPS OR YIELDS FOR ANIMALS ON ITS ROAD. | Vehicles yield to animals on their road |
| T2 | **CE-3468** | OPEN `2026-11-17` (backend; found building `bw-archery-contest`'s score) — IN THE LIVE EDITOR WITH THE PARALLEL / THREADED KERNEL (THE DEFAULT), A `Points` ROOT AND A `Best` SEE NO SCORED CHILDREN. | the threaded editor (the DEFAULT): scoring roots see no children · six rails red only under the parallel env |
| T2 | **CE-3469** | OPEN `2026-11-17` (backend; confirmed pre-existing for the bows/birds/outfits gate) — SIX `Hrot.SimHost.Tests` RAILS RED ONLY UNDER THE PARALLEL ENV. | the threaded editor (the DEFAULT): scoring roots see no children · six rails red only under the parallel env |
| T2 | **CE-2440** | OPEN `2026-11-16` (behaviors, found by the AU-6 stepped-hash gate) — `cc-battalion` is NOT thread-count independent, INTERMITTENTLY: `BlueprintBlackboard1024` differs between 1 and 8 threads in 1 of 5 eight-thread runs. | determinism |
| T2 | **CE-3445** | OPEN `2026-11-13` (backend, from CE-4503 / CE-4509) — a RARE mode-3 stepped divergence: 1 of 36 runs at 4 threads, every phase on (`HROT_PARALLEL_SIM=1`), parted from the 1-thread world (17 types; the rows the centroid bifurcation | determinism |
| T2 | **CE-2438** | OPEN `2026-11-15` (behaviors, measured) — THE EDITOR'S PAUSED-PREVIEW + RUN-TO RUN DOES NOT REPEAT ON `pc-defend-house`. | determinism |
| T2 | **CE-2448** | OPEN `2026-11-17` (behaviors, found re-capturing the checkpoint goldens) — `cp-vbied`'s GOLDEN DOES NOT REPEAT. | determinism |
| T2 | **CE-3449** | OPEN `2026-11-14` (backend, found by D-2 of the determinism residue; owner: the scenarios' owner, behaviors) — FIVE SCENARIO GOLDENS DIFFER FROM t = 0 WHILE EVERY CHECK PASSES. | determinism |
| T2 | **CE-3424** | OPEN `2026-11-10` (backend, L-4 scale rig) — ON scale-1 AT 1 000 MEN ONLY 1 OF 12 SAMPLED MOVERS MOVED | scale — the movers that do not move first |
| T2 | **CE-3425** | OPEN `2026-11-10` (backend, L-4 scale rig) — THE FRAME AT 1 000 MEN ON scale-1, AFTER THE LOAD AND GARBAGE FIXES: | scale — the movers that do not move first |
| T2 | **CE-4395** | OPEN `2026-11-10` (coordinator → backend) — THE BRAIN'S COST AT 1 000 MEN: | scale — the movers that do not move first |
| T2 | **CE-3460** | OPEN `2026-11-16` (found by item 7 of the regression suite — `sr-objectives-march`'s new `checks.json`, editor tier; owner: rules (backend)) — THE ROOT OBJECTIVE NEVER AGGREGATES ITS CHILDREN. | rules · animals |
| T2 | **CE-3471** | OPEN `2026-11-17` (backend; found reproducing CE-1958) — A PIG WALKED THROUGH ITS PEN'S BRICK WALL: | rules · animals |
| T2 | **CE-1038** | `Hrot.SimHost.Tests` reports a different number of executed tests per full run (1180 / 1161 / 1157 of 1180 listed); the unreported ones pass when run alone. | tests and tools |
| T2 | **CE-1046** | `LiveFromReplayTests.AfterBranch_SimGroupsReEnabled` FAILS UNDER MACHINE LOAD, and the SimHost suite sometimes ABORTS. | tests and tools |
| T2 | **CE-1473** | OPEN `2026-11-10` (ui; found at the gate, PRE-EXISTING at `14194f2cc`) — `Hrot.ClusterRunner.Tests` DOES NOT BUILD: | tests and tools |
| T2 | **CE-3451** | OPEN `2026-11-14` (backend; found gating the HTTP API batch) — SIX `Hrot.ClusterRunner.Tests` RAILS DRIFTED WHILE THE PROJECT DID NOT BUILD (CE-1473); it builds again now. | tests and tools |
| T2 | **CE-1475** | OPEN `2026-11-10` (ui; found at the gate on the user's Windows machine, Czech locale) — `SumoNetPlacementTests` F5 / E1 FAIL ON A NON-ENGLISH WINDOWS: | tests and tools |
| T2 | **CE-167** | [`RUNBOOK`](../RUNBOOK_Cluster_Debugging_Over_Http.md) §4 says verbatim *"Read the entity on BOTH nodes before concluding anything about 'the cluster'"* — I wrote that section and then did not do it. | ⚠ re-check first (September) — close it if stale |

## Always available (when everything above is done, skipped or blocked)
Open tracker rows of your area marked `RW-M` or higher that no handoff holds — name each in your status before you start it.
