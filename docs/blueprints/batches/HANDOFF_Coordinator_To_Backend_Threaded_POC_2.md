<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: ⭐ §14 the target shape (img/threading-architecture.svg) · ⭐ §14.1–14.2 the anti-race strategy and the gizmo split (img/threading-anti-race.svg) · §13 the shared-state classification · §12 (on exp/threaded-kernel) the experiment's world inversion
  - STATUS_Threaded_Analysis_Instruments.md (on exp/threaded-analysis) — A-1…A-5: the evidence this POC builds on (the masks, the costs, the A-4 dependencies)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_2.md — the OTHER experiment (simulation throughput); independent, combined later
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — POC 2 of the threaded editor: build the TARGET SHAPE and prove it

**For:** a backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there. ⭐ **Still an EXPERIMENT** — 🔒 user,
`2026-11-12`: *"looks good at least for next version of the POC to verify it"* · *"write the handoffs for the next POC — still experimental."*

⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table in the status file, continue; a failed premise stops
THAT item (R-106). You may change the design where the code shows a better way — say so with evidence and fold it into §14 (as-built rule).

## Where to work

⭐ A NEW branch **`exp/threaded-poc-2`** from **`exp/threaded-analysis`** (it carries the threaded prototype, `ThreadedKernelGate`, `PublishProbe`,
the access tracer `HROT_ACCESS_TRACE` and the reach tool), with **`coordinator`** merged in (§14 and both pictures). Push as you go. Status file
`docs/blueprints/batches/STATUS_Threaded_POC_2.md` ON THAT BRANCH. ⛔ Nothing merges into a lane; ⛔ do not push to `backend`.
Everything behind a switch, **default OFF** (`HROT_THREADED_KERNEL=2` for this shape) — OFF, the editor is byte-for-byte today's.
The cloud VM proves correctness; leave a one-command measurement script for the user's Windows machine (8 cores, every window HIDDEN —
`HROT_HIDDEN_WINDOW=1`).

## What to build — the target shape of §14, in this order

| # | item | design basis | done when |
|---|---|---|---|
| **P-1** | ⭐ **reads are reads**: a managed-component read and a singleton read stop stamping the chunk version / stop returning a writable ref (A1-2; `ManagedComponentTable.cs:181-196`, `GetSingletonUnmanaged`) — a read-only path the readers use | §14.1 last row | a rail: N reads leave every chunk version unchanged; the tracer's "get-by-ref on both sides" rows for read-only callers disappear. ⭐ This one also helps TODAY's recorder / egress — measure its effect on dirty chunks (`HROT_WRITE_STATS` from `exp/parallel-sim` if you take it) |
| **P-2** | the **3 unsafe statics** (`RunModeChoice.PresetCache`, `UtilityInputNames._names`, `UtilityDecisionCatalog._shared`) | A-2 | each with a rail that races two threads |
| **P-3** | ⭐ **S-1: the UI work leaves the tick** — split `GizmoInteractionModule` (its world writers — action dispatch, effect / wreck spawn, lifespan — stay in the tick); the tool gizmos, tool activation, centre-on-entity, the selection systems and the 4 afterKernel objects run in a presentation step on the main thread (A-4's per-item table says what each relies on) | §13.5, §14.2, A-4 | with the switch OFF nothing changes; with it ON but STILL SINGLE-THREADED the editor behaves the same — the editor's feature rails and the scenario regression subset green |
| **P-4** | ⭐ **two worlds**: the node frame on S on the sim thread; the presentation keeps World A (§12's inversion — A-4 ⑤: UI state is keyed by the world instance) | §14 picture ① | the editor runs threaded on the 1 000-man rig, 0 faults over load → play 120 s → pause → step → edit → save |
| **P-5** | ⭐ **the S→A sync**: TABLE masks (map, 3-D, overlays — from A-1's per-reader sets) + a NEW **ROW copy** for the selected / inspected entities; the inspector a ROW reader (never widens the mask); singletons per §13.1 (copy · rebuild `NetworkEntityMap` · immutable views for `ClusterUiCache` / clock · share / swap the inside · lease roads); logs and events forwarded; one short lock on A | §14.1, §14.2 rows 1–9 | measured per sync at 1 000 men (A-3 said ≈ 1.3 ms tables + µs rows); a rail per mechanism; A has no reader that sees a half-copied state |
| **P-6** | the **gizmo frame** published (the experiment's triple buffer) and the X4-2 / A-4 ⑥ race fixed (`DebugGizmoLayer.HandleInput` / `PickEntity` must read the published frame); the MIXED gizmos' pick boxes and the pickers on the main thread together | §14.2 gizmo split | picking works on a moving unit while playing; a rail |
| **P-7** | ⭐ **UI writes as commands** — at least: delete, move (drag = a ghost in A, the drop one command), spawn / place, an order, play / pause / step; refused ⇒ the edit outcome (Q111-C); a Debug guard throws on a write to S off the sim thread | §14.1 command rows | each with a rail; the guard ON in every test run of the branch, and every hit it finds listed (the remaining writes = the cost to finish) |
| **P-8** | ⭐ **the verdict** — playing vs paused frame threaded / not (VM; the Windows script left); what broke; the guard's list; what is still unknown; the real cost to finish | — | at the top of the status file; the as-built folded into §14 |

## Rules

- ⛔ Default OFF; 0 B per steady frame with the switch OFF (R-2084).
- **Measure before claiming**; every "nothing else writes S" backed by the guard's run, not by grep.
- A red in the editor's feature rails or the scenario regression subset is a finding; name the base sha for every pre-existing red.
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** P-8 is written — however far P-1…P-7 got.

---
_coordinator lane, 2026-11-12_
