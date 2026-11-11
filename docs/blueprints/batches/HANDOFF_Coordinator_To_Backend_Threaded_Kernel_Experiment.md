<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-11
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS the concept (R-4205): §2 the decision, §3 diagrams, §4 rules, §7 the PoC, §8 verification — ⭐ §11 THE CRITICAL REVIEW (the 23 findings this experiment answers)
  - ../Architect_Question_111_Threaded_Kernel_Details.md — the approved details (R-4206); Q111-B, -E, -F are flagged for revision by §11
  - ../../../FDP/Engine/docs/FDP-GDB-SoD-unified.md — FDP-DES-012, the kernel's snapshot model; §8 the self-paced reader
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — §11h ui's Windows measurement of the editor's preview frame (what this is for)
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — make the threaded kernel work in the real editor, deciding by your own judgement

**For:** backend. **From:** coordinator. **Dispatched at `74456b8c9`**, scope frozen there. ⭐ **This is an EXPERIMENT** — 🔒 user, `2026-11-11`:
*"explain them the situation including all the weak problems and ask them to try to find a solution autonomously overnight, taking decisions using
their best judgement, as an experiment. we will see what comes out of that."*

⛔⛔ **Autonomous — never stop for a question** (R-4156). Every decision is YOURS: take it, record it with its claim table (code — how it is; design —
how it was meant) in your status file, and move on. A premise that fails stops THAT item only (R-106). ⭐ You may revise or overturn anything in the
design — including the approved R-4205 / R-4206 details — when the code shows a better way; say so plainly with the evidence. The user will judge
the result in the morning.

## Where to work

⭐ **On a NEW experimental branch `exp/threaded-kernel`**, from `coordinator` (which carries the design) — ⛔ not on `backend`, so your lane stays
clean. The proof of concept is on `poc/threaded-kernel` (`FDP/Examples/Fdp.Examples.ThreadedKernel`, its README) — merge or copy from it as you like.
Push the experimental branch as you go (it must survive the container). On `backend` itself push only: `chore: started …` and the status file
`STATUS_Backend_Threaded_Kernel_Experiment.md` (updated as you go, with the experiment branch's sha). ⛔ Nothing from the experiment merges into a
lane without the user's word.

## The situation

**What hurts** (Evaluation Cost §11h, ui on the user's Windows machine): at 1 000 men a preview frame is **98.8 ms playing vs 32.4 ms paused** —
63 ms of simulation on the drawing thread; every input waits a 100 ms frame, often two.

**The concept** (R-4205, the design's §2–§4): the ONE kernel ticked on a worker thread; the windowed host's presentation (map preparation,
interaction, input, ImGui panels) a PULLED module run by the render loop on the latest published, leased snapshot; the UI writes only validated
commands; events queue per reader; one code path, two schedulings (inline for tests / headless, threaded for the windowed editor and player).

**The proof of concept** (§7): the real kernel on a worker with a toy world kept the UI at 93 fps under a 408 ms tick (today's shape 2.5 fps);
a publish cost ~1 ms at 10 000 light entities. ⚠ **A toy world: 5 component types, no singletons, no managed tables, no editor frame.**

**The critical review** (§11 — READ IT WHOLE; ✔ = re-checked in code) found 23 problems the toy world could not show. The heaviest:

| # | the problem |
|---|---|
| F-1 | ✔ the snapshot copies only 12–13 singletons hand-picked for EQS (`EntityRepository.Sync.cs:112-129`); the presentation reads ~83 singleton sites (`NetworkEntityMap`, `ITkbDatabase`, traces, `IOrderForm` …); mutable classes default to `NoPreview` (`EntityRepository.cs:899`) — effects, `RoutePlan`, `ActiveMissionPlan` absent ⇒ silent blanks |
| F-2 | borrowed singletons are the LIVE table (`Sync.cs:177-190`) — `TerrainWorld`, `ICoverProvider`, `SpatialGridData` rebuilt every tick ⇒ torn reads |
| F-3 | the worker must own the WHOLE node frame (`TickNodeFrame`: load phases, kernel, afterKernel hooks, `StepTimeline`, `SwapBuffers`) plus `_orchestratorCore.Tick`, the time transport and Run-to (`EditorSubsystem.cs:2799-2868`); ✔ `MasterSyncController` has NO locks |
| F-4 | ✔ UI bus publishes race the swap — `NativeEventStream.Write` is lock-free, `Swap` locks what `Write` never takes (`NativeEventStream.cs:89-97, :149-160`) |
| F-5 | the publish is effectively a FULL copy per tick (any `GetRefRW` stamps its 64 KB chunk; 1 000 men = 1–8 chunks per table; the entity index copied whole; ✔ `SnapshotPool.Return` → `SoftClear` makes every reuse cold) — the real cost is UNKNOWN |
| F-6 | paused / Edit mode: no worker cadence — creation is a multi-frame pipeline of `EveryFrame` systems |
| F-7 | ✔ undo captures and restores World A on the main thread (`WorldUndoRecorder.cs:50-73`) |
| F-8 | live widgets (`DragFloat3`, `SliderFloat`, `InputText`) write every frame from a stale snapshot |
| F-9 | ✔ `ParamUsage` rewrites a shared managed tree in place (`ParamUsage.cs:121`) — a write into World A no guard catches |
| F-10…F-23 | toggle commands that cancel (✔ `EditorSubsystem.cs:1653`); Run-to / stills counting frames; culling / styling / layer assignment inside the kernel; 44 world-keyed side tables (incl. `EditOutcomeLog`); 51 fields + 142 getters + 37 casts binding World A; the HTTP API's main-thread routes; shared UI state without locks; hot reload; possession input edges; group commands; save with pending edits; pacing, thread priority, the profiler; `--mode all`, the Replay Browser, Stride; a verification gap; allocations per publish |

## The experiment — in this order, as far as the night goes

| # | what | done when |
|---|---|---|
| **X-1** | ⭐ **MEASURE the real publish** — the editor's REAL world (the scale rig `scale-rig.py --terrain scale-1 --men 1000 …`, and 5 000 / 10 000 if the rig allows), the snapshot as the presentation would need it (§11 F-1: all component tables it reads + its singletons), warm and cold, with and without `SoftClear`: ms per publish, bytes, allocations | the numbers in your status file — ⭐ they decide everything after |
| **X-2** | **the INVENTORY** — everything the presentation READS (components, singletons, side tables, fields / getters / casts) and WRITES (bus publishes, the routed paths, direct writes, in-place managed mutation), and everything on the main thread besides the kernel that touches World A or the clock | a table per category with counts and `file:line` |
| **X-3** | **your SOLUTION** to the findings — by your judgement: e.g. a full snapshot vs a NARROW published view (only what the map and panels draw), how the node frame moves, the command shape (atomic, deltas, absolute), undo, widgets, paused cadence, the HTTP split, the hosts. ⭐ Write it as a new §12 in the design (diagram first, a claim table per decision, the rejected alternatives one line each), saying which R-4205 / R-4206 points you keep, change or overturn | §12 written, Mermaid parses |
| **X-4** | ⭐ **a PROTOTYPE in the real editor** on `exp/threaded-kernel`, behind a switch (default OFF): the kernel / node frame on a worker, the presentation pulled from the published view, the UI's writes as commands — as much of the real editor as the night allows (the map and a few panels is enough to learn from); every shortcut named | the editor runs threaded on the 1 000-man rig (hidden window, `HROT_HIDDEN_WINDOW=1`, `xvfb-run -a` on the cloud VM); the frame playing vs paused measured both ways (`HROT_FRAME_STATS`, `KernelFrameTimings`); what broke and how it was found |
| **X-5** | **the verdict** — what works, what does not, the real cost to finish, and your recommendation (go / change the shape / stop) | a short section at the top of the status file |

## Rules

- ⭐ **Decide, record, continue.** No item waits for an answer; a decision you are unsure of is still taken — say what would change it.
- **Measure before claiming** (CLAUDE.md): every number from a run, every "X does not exist" from `search_graph` + grep.
- **Run anything slow in the background** and never poll it (CLAUDE.md).
- The experiment branch may break things — but the switch default OFF keeps the normal editor working; run the editor's feature rails and the
  kernel tests at the end on that branch and report them (a red is a finding, not a failure of the experiment).
- ⛔ No screenshots or recordings in git; numbers and stills to the status file and the scratchpad.

**Done when:** X-5 is written — however far X-4 got.

---
_coordinator lane, 2026-11-11_
