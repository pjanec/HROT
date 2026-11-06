<!--STATUS
state: LIVE — RE-DISPATCHED 2026-11-06 (rule 1a: none of `ef9fc92dd`, `194705487`, `8da0c0a92` was merged by backend); scope frozen at the newest stamp
updated: 2026-11-06 (amended before start, rule 1a: ① = finish 'a host is data, not a class', staged with safety rules S0–S5 — user)
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Subsystem_Composition_Unification.md — OWNS the host-composition programme; ① finishes its §4.1 ③ goal ("what stays host-unique shrinks to near nothing")
  - docs/designs/packs-1/DESIGN.md — OWNS the pack model: "an All-In-One editor installs both Brain and Muscle logic packs without Translator Packs" — the fused host is that composition PLUS the network
  - docs/DESIGN_Role_Affinity_Ownership.md — the editor's union role (Brain | MuscleGround | Perception | NavigationSolver) is legal in ONE world
  - docs/DESIGN_Player_Join.md — Deploy S1–S3 (yours, built); §7b the user's -m server ruling; §9.4 S1-d; §9.5 the open Esc item
  - docs/DESIGN_Cluster_Membership_Use_Cases.md — §2.1 UC-17…UC-32, the multiplayer acceptance
  - docs/designs/audio/DESIGN_FPS_Audio.md — OWNS the audio layer whose per-host device crashed the server (CE-4269)
-->

# HANDOFF — coordinator → backend: a host is data (the fused server as its first new description), one audio device per process, the full player cycle (CE-4270, CE-4269)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at `11add1f84`** (re-dispatched; earlier stamps `ef9fc92dd`, `194705487`, `8da0c0a92` were never merged). ⭐ Scope FROZEN
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
| **①** | ⭐⭐⭐ **CE-4270 — finish "A HOST IS DATA, NOT A CLASS" (Subsystem Composition Unification's own end goal §4.1 ③), with the FUSED SERVER as its first new description** — see the section below. ⛔ No `ServerNodeBootstrapper`. ⚠⚠ **High blast radius — every ECS host boots through this code: STAGED, measured, reversible** (the safety section below is part of the item, not advice) | [Subsystem Composition Unification](../../DESIGN_Subsystem_Composition_Unification.md) §4.1 ③, §4.1af (CE-1446); packs-1; Role Affinity; Player Join §7b; R-174, R-141, R-4102, R-4107, R-4108 | the design (stage A) is READY-TO-BUILD and **the user approves it before any code moves**; each stage then lands only when its safety gates are green; the fused server passes UC-17…UC-32 |
| **②** | 🔴 **CE-4269 — ONE process-wide audio device**: a shared, refcounted owner of raylib's audio device that every map host's `AudioLayer` uses (opened once, closed when the last host lets go). ⭐ Still needed with ①: `-m all` and any other multi-host process keep several map hosts. Reproduce here first by giving the VM an audio sink (PulseAudio null sink / ALSA dummy) — the crash should then appear with two map hosts in one process | the CE-4269 row | a rail with two map hosts in one process (open, both play, one disposes, the other keeps playing); the user's `run_Server.bat` stays up WITHOUT `HROT_AUDIO=0` |
| **③** | ⭐ **The FULL player cycle as ONE scripted acceptance** (`scripts/multiplayer-cycle.py`, or extend the existing HTTP tooling — ⛔ not a second harness), against the fused server, **runnable unchanged on Windows** (plain `subprocess` or `--attach` to processes started from the `.bat` files; no `xvfb-run`, no `killpg`): server up → P1 joins, deploys into a placed unit, drives it → P2 joins late, sees it moving, is refused P1's unit, takes a role → P2 dies, respawns → **P1 leaves by Esc** → **P1 quits** (slot disposed, S1-d) → P1 rejoins and deploys → P2 quits → server keeps running → final check: no orphan slot / reservation / ghost entity on the wire (reuse CE-4000/4007's check). PASS/FAIL per step with the reason | UC-17…32; Player Join S1-d | passes on the VM (report in the Use Cases doc); the one command for Windows is in the RUNBOOK |
| **④** | **Player Join §9.5's open item** — a `Playing` unit released with Esc keeps its reservation; decide the transition (lean: Esc returns to the deploy screen and frees the unit) and build it as ③'s "leaves by Esc" step | Player Join §9.5, D-7 | the design records it; ③ proves it |

## ⭐⭐⭐ ① — A HOST IS DATA, NOT A CLASS *(amended before start, `2026-11-06`)*

🔒 User, `2026-11-06`: *"we should unify and share across all host types to minimize duplication … I do not want to create one extra
loader which is for the server only, but the goal is to unify it across the host as much as possible."* · *"be careful. This has the
potential to break a lot of stuff."* (R-174: *"unify and share, never duplicate — especially in node bootstrap code"*.)

**Where it stands (measured `2026-11-06`):** every ECS host already derives from ONE base, `SharedApplicationBootstrapper` (CE-1446), and
its modules already come from the declared role through `NodeCompositionPlan` / `INodeCapability` (B1–B4b). ⛔ What is still per host —
six subclasses, ~1 460 lines (Editor 325, SimHost 242, IG 240, CGF 191, Stride 171, Traffic 116), graph `.*(NodeBootstrapper|NodeBuilder|BootPlan|ApplicationBootstrapper|LoadPhaseChain)$`:

| hook | per host | how alike (line matching across hosts) |
|---|---|---|
| `RegisterSpawningPipeline` | 18–31 lines × 5 | **60–80 % identical** — the same `EntityCreationPack.Build(...)` copied, differing in 2–3 values (`IsBroadcastArbiter`, `Macros`, the ELM lookup) |
| `BuildOrchestration` | 28–49 lines × 5 | 30–45 % identical; the rest per-host choices |
| `ResolveCapabilities` | 14–27 lines × 4 | a role → capability list written as code |
| `BuildContext` · `RegisterDomainComponents` · `RegisterNetworkTranslators` | 1–43 | small, except IG's translators and Stride's components |

**The goal:** ONE bootstrapper fed a **host description** — the role set, network on/off, the time role (master / slave / standalone),
the gameplay and traffic packs, and an **implementation factory** only where an implementation truly differs (Stride's muscle, the
Player's view). The existing capability table picks the packs from it; each copied block becomes ONE call fed by the description's
values. ⇒ the **fused server** = the Brain + Muscle roles, network ON, master time, gameplay ON; the **editor** = the same roles, network
OFF, standalone time, edit mode; CGF, SimHost, IG, Traffic, Stride = the other descriptions. ⭐ A subclass survives ONLY for a difference
that is named, with its reason, in the design (§4.1 ③) — ⛔ "it was there first" is not a reason.

### ⛔⛔ THE SAFETY RULES — this code boots every node

| # | rule |
|---|---|
| **S0** | ⭐⭐⭐ **A COMPOSITION SNAPSHOT RAIL FIRST, before any code moves**: for every host description (editor, CGF, SimHost, IG, Traffic, Stride, `--mode all`), boot it in-process and record WHAT it composed — every module, system (with its phase / group / order), translator (with direction), capability, resource, singleton, the time role, the slave count. Commit the snapshots as goldens. ⭐ Every later stage must reproduce them **byte-identical** — a refactor that changes what a host composes is a DIFFERENT change and needs its own approval. Extend the existing rails (`NodeCompositionPlanRails`, `SharedApplicationBootstrapperTests`) — ⛔ not a parallel harness |
| **S1** | **Design first (stage A), approved by the user before stage B**: the inventory of every hook's per-host difference, each survivor NAMED with its reason; the host-description type; the class / sequence / **module** diagrams (who registers each pack, who ticks it each frame — draw the dead edges: the §4.1a / CE-151 lesson that a group nobody ticks silently stops a feature) |
| **S2** | ⭐ **ONE host per commit**, in order of least risk: Traffic → IG → Stride → SimHost → CGF → editor; then the fused server as a NEW description. Each commit: the snapshot rail identical + that host's OWN suite + the cluster integration tests that boot it. ⛔ Never two hosts in one change |
| **S3** | ⚠ **Orchestration and time authority move LAST and only as measured** — the design itself says not to move them blindly (§4.1): the one-slave invariant, the editor's in-process master and its own frame order (§4.1af), `RegisterMigrationServices` drift (§4.1af row 6) |
| **S4** | **Behaviour, not only composition**: after each host, a live run of that host over HTTP (RUNBOOK) and the scenario regression verifier (`--verify` on a handful of scenarios, CE-4042) — goldens must not move. A moved golden is a STOP and a report |
| **S5** | **Reversible**: no deletion of a subclass until its replacement description has passed S0–S4 and been merged once; a STOP at any stage leaves every host working. ⛔ No `--no-verify`, no skipped or quarantined test to get green |

⭐ Order: ② and ④ need no approval — build them while ①'s design (stage A) waits for the user. ① stage B starts only on the user's approval. ③ runs against `-m server` as it is first, then
against the fused host once ① is built.

⚠ Fences: the orchestrator's protocol stays as is unless the design argues a change; every gameplay behaviour a switchable Node Feature
(R-4108); new recorded components unmanaged / immutable (the record rail); hot path R-2084. Fold every as-built into its design.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · new rails red-proved · ③'s full run on the VM (its output in
the report) · the cluster integration tests covering possession / late join (named, filtered if the batch is slow — CE-3369) · every red
confirmed pre-existing against the base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-11-06_
