<!--STATUS
state: LIVE — RE-DISPATCHED 2026-11-06 (rule 1a: the first dispatch `ef9fc92dd` was never merged by backend); scope frozen at the new stamp
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/designs/packs-1/DESIGN.md — OWNS the pack model: "an All-In-One editor installs both Brain and Muscle logic packs without Translator Packs" — the fused host is that composition PLUS the network
  - docs/DESIGN_Role_Affinity_Ownership.md — the editor's union role (Brain | MuscleGround | Perception | NavigationSolver) is legal in ONE world
  - docs/DESIGN_Player_Join.md — Deploy S1–S3 (yours, built); §7b the user's -m server ruling; §9.4 S1-d; §9.5 the open Esc item
  - docs/DESIGN_Cluster_Membership_Use_Cases.md — §2.1 UC-17…UC-32, the multiplayer acceptance
  - docs/designs/audio/DESIGN_FPS_Audio.md — OWNS the audio layer whose per-host device crashed the server (CE-4269)
-->

# HANDOFF — coordinator → backend: a FUSED multiplayer server host, one audio device per process, and the full player cycle (CE-4270, CE-4269)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at `194705487`** (re-dispatch; the first stamp `ef9fc92dd` was never merged). ⭐ Scope FROZEN
there; later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒
a NOTE to coordinator. Merge `coordinator` first.

⚠ **This REPLACES the first version of this handoff (`ef9fc92dd`, never started) and folds in `NOTE_Coordinator_To_Backend_CE4269_Evidence.md`
(now SUPERSEDED).** 🔒 User, `2026-11-06`: *"it looks like we need a special host type, something like an editor, combining brain and muscle
and orchestrator and network into a single host"* · *"backend haven't started. you can replace its handoff and notes with this new job"*.

## What is measured (the basis)

| fact | where |
|---|---|
| **CE-4269 root cause CONFIRMED by the user on Windows:** `-m server` died of a NATIVE access violation in `raylib.DLL` (Event Viewer: `c0000005`, faulting module `raylib.DLL`); with `HROT_AUDIO=0` the server **stays up** (bootstrap complete, TKB join sync on every node). Cause: `AudioLayer.DefaultOutput()` (`AudioLayer.cs:101-104`) makes ONE `RaylibAudioOutput` per map host (`MapInteractionPack.cs:611`), each `Open()` calls `InitAudioDevice` (`AudioOutput.cs:154`) and `Dispose` calls `CloseAudioDevice` (`:285`) — raylib has ONE device per process. The Linux VM has no audio device, so it never ran there | the CE-4269 row |
| `-m server` today = FOUR nodes in one process — `ServerNodes = { orchestrator, simhost, cgf, traffic }` (`HrotRunnerConfiguration.cs:290`), each its own world, brain ↔ muscle over DDS inside the process | code |
| the editor ALREADY fuses brain and muscle in ONE world: `EditorNodeBootstrapper.cs:261` (`SimHostCoreLogicPack`) and `:278` (`CgfLogicPack`), runs the orchestrator core in-process (`TheEditorRunsTheOrchestratorCoreTests`), and has NO network (`OfflineNetworkFactory`) | code; packs-1 |

## The items

| # | item | basis | done when |
|---|---|---|---|
| **①** | ⭐⭐ **CE-4270 — DESIGN, then build, a FUSED server host**: the editor's composition (Brain + Muscle logic packs in ONE world, perception, navigation) **plus the network translator packs** so it serves Players, plus the orchestrator, the gameplay server pack (Deploy) and traffic; headless or with the game-master UI (R-4107). ⭐ It is a COMPOSITION of existing packs (R-174), ⛔ not a second engine path, and ⛔ not the editor itself (no authoring UI, no edit-mode semantics). ⚠ **Measure first, the one unmeasured premise:** a world that both SENDS and RECEIVES the same descriptors must not ingest its own samples (an echo) — find how ingress drops own-node samples today, and whether every translator does. Write the design in `docs/` with the class, sequence and module diagrams (who registers each pack, who ticks it each frame — draw the dead edges); then `-m server` composes it (or a named new mode — your call, say it). `-m all` (separate nodes per process) STAYS as the distributed test bed | packs-1; Role Affinity (union role legal); Player Join §7b; R-4102, R-4107, R-4108 | the design is READY-TO-BUILD and the user approves it before the build (it is a new host kind — large blast radius); then UC-17…UC-32 pass against the fused server |
| **②** | 🔴 **CE-4269 — ONE process-wide audio device**: a shared, refcounted owner of raylib's audio device that every map host's `AudioLayer` uses (opened once, closed when the last host lets go). ⭐ Still needed with ①: `-m all` and any other multi-host process keep several map hosts. Reproduce here first by giving the VM an audio sink (PulseAudio null sink / ALSA dummy) — the crash should then appear with two map hosts in one process | the CE-4269 row | a rail with two map hosts in one process (open, both play, one disposes, the other keeps playing); the user's `run_Server.bat` stays up WITHOUT `HROT_AUDIO=0` |
| **③** | ⭐ **The FULL player cycle as ONE scripted acceptance** (`scripts/multiplayer-cycle.py`, or extend the existing HTTP tooling — ⛔ not a second harness), against the fused server, **runnable unchanged on Windows** (plain `subprocess` or `--attach` to processes started from the `.bat` files; no `xvfb-run`, no `killpg`): server up → P1 joins, deploys into a placed unit, drives it → P2 joins late, sees it moving, is refused P1's unit, takes a role → P2 dies, respawns → **P1 leaves by Esc** → **P1 quits** (slot disposed, S1-d) → P1 rejoins and deploys → P2 quits → server keeps running → final check: no orphan slot / reservation / ghost entity on the wire (reuse CE-4000/4007's check). PASS/FAIL per step with the reason | UC-17…32; Player Join S1-d | passes on the VM (report in the Use Cases doc); the one command for Windows is in the RUNBOOK |
| **④** | **Player Join §9.5's open item** — a `Playing` unit released with Esc keeps its reservation; decide the transition (lean: Esc returns to the deploy screen and frees the unit) and build it as ③'s "leaves by Esc" step | Player Join §9.5, D-7 | the design records it; ③ proves it |

⭐ Order: ② and ④ need no approval — build them while ①'s design waits for the user. ③ runs against `-m server` as it is first, then
against the fused host once ① is built.

⚠ Fences: the orchestrator's protocol stays as is unless the design argues a change; every gameplay behaviour a switchable Node Feature
(R-4108); new recorded components unmanaged / immutable (the record rail); hot path R-2084. Fold every as-built into its design.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · new rails red-proved · ③'s full run on the VM (its output in
the report) · the cluster integration tests covering possession / late join (named, filtered if the batch is slow — CE-3369) · every red
confirmed pre-existing against the base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-11-06_
