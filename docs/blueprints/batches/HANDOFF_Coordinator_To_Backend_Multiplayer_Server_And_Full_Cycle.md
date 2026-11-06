<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-06
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Player_Join.md — Deploy S1–S3 (yours, built); §9.4 S1-d (a slot whose station leaves is disposed) and the §9.5 open item (a Playing unit released with Esc keeps its reservation)
  - docs/DESIGN_Cluster_Membership_Use_Cases.md — §2.1 UC-17…UC-32, the multiplayer acceptance, measured on Linux 2026-11-05
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — launching one node per process over HTTP, the traps
-->

# HANDOFF — coordinator → backend: the multiplayer server comes up on Windows, and the FULL player cycle is tested (CE-4269 + a new acceptance)

**For:** the backend lane (`backend`). **From:** the coordinator lane. **Dispatched at** the commit that adds this file. ⭐ Scope FROZEN
there; later documents are FYI. A later document that invalidates an item ⇒ STOP that item, report it (R-106), keep the rest. Questions ⇒
a NOTE to coordinator. Merge `coordinator` first.

## Why

🔒 User, `2026-11-06`: *"I need to solve the multiplayer server not wanting to come up and to test the full cycle of players joining the
server and playing and leaving. Maybe backend could take it."* ⭐ Every multiplayer use case (UC-17…UC-32) passed on the Linux cloud VM;
the user's first try on **Windows** died at server start (CE-4269). ⚠ You cannot run Windows — so the work is: make the failure reproducible
here, fix it, and hand the user a **one-command full-cycle check** they run on their Windows host.

## The items

| # | item | basis | done when |
|---|---|---|---|
| **①** | 🔴 **CE-4269 — `run_Server.bat` (`Hrot.ClusterRunner.exe -d 0 -m server`) exits shortly after start on Windows**, no managed exception printed; the last console line is raylib's `AUDIO: Device initialized successfully` (miniaudio / WASAPI). ⭐ **Reproduce here first**: the cloud VM has NO audio device, so `InitAudioDevice` fails quietly and the code after a SUCCESSFUL init never runs — give the VM an audio sink (e.g. PulseAudio with a null sink, or ALSA's dummy) and run `-m server`; also try the other difference a Windows host has (a real display: `-m server` composes several subsystems' windows / GL contexts in one process). ⭐ If it does not reproduce, add what would have told us: an unhandled-exception + native-crash hook that writes the reason to the log (`AppDomain.UnhandledException`, the module host's swallowed system exceptions, a last-words line before exit), and ask the user for the Windows Event Viewer entry (faulting module) — the row lists both | tracker CE-4269 | `-m server` stays up with an audio device present (a run here, + the user's run on Windows); whatever killed it has a rail, or the log names the cause if it recurs |
| **②** | ⭐⭐ **The FULL player cycle as ONE scripted acceptance** — `scripts/multiplayer-cycle.py` (or extend the existing HTTP tooling; ⛔ not a second harness — R-174), driving a server + two Players over HTTP on `localhost`, **runnable unchanged on Windows** (no `xvfb-run`, no `killpg` — start the processes with plain `subprocess`, or `--attach` to processes the user started from the `.bat` files): server up → P1 joins (deploy screen, Waiting → Choosing) → P1 deploys into a placed unit, drives it → P2 joins late and sees it moving → P2 asks for the same unit (refused) and takes a role instead → P2's man dies → respawn after the delay → **P1 LEAVES by Esc** (unit released, task resumed, slot free) → **P1 QUITS the process** (slot disposed on every node, S1-d) → P1 **rejoins** and deploys again → P2 quits → server keeps running → a final state check (no orphan slot, no orphan reservation, no ghost entity on the wire — reuse CE-4000/4007's check). Prints PASS/FAIL per step with the reason | UC-17…32; Player Join S1-d, §9.5 open item | the script passes on the Linux VM, its report in the Use Cases doc; the user can run it on Windows with one command (state it in the RUNBOOK) |
| **③** | **Close Player Join §9.5's open item**: a `Playing` slot whose unit is released with Esc keeps its reservation until the station leaves — decide the transition (lean in the design: Esc returns to the deploy screen and frees the unit; say if you disagree) and build it, as part of ②'s "leaves by Esc" step | Player Join §9.5, D-7 | the design records the transition; ② proves it |

⚠ Fences: the orchestrator stays untouched except what CE-4269's cause demands (say it if so); every gameplay behaviour stays a switchable
Node Feature (R-4108); hot path R-2084. Fold the as-built into Player Join and re-measure the UC rows you touch.

## Gates (report these rows)

The touched projects' suites, `--no-build` after building the TEST project · new rails red-proved · ②'s full run on the VM (its output in the
report) · the cluster integration tests covering possession / late join (named, filtered if the batch is slow — CE-3369) · every red confirmed
pre-existing against the base sha · `tracker-counts.py --check` · the ids you allocate.

---
_coordinator lane, 2026-11-06_
