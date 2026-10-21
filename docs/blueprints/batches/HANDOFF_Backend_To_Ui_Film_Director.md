<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole handoff
related-designs:
  - ../../DESIGN_Film_Director.md — THE DESIGN: §0 (R-3038) first, then its UML (§1 module view, §2 classes, §3 sequences), formats and routes (§4), decisions (§5), phases (§6)
  - ../DESIGN_Paused_And_Stepped_Execution.md — backend's refactor (CE-3293) that makes stepped runs repeat; the four touch points below come from it
  - HANDOFF_Coordinator_To_Backend_Film_Director.md — the original dispatch to backend: its item table is re-used here; this handoff supersedes it
  - ../../PLAN_Feature_Films.md — what the takes film; ../../films/clips.json is the seed
  - ../../RUNBOOK_Filming.md — film.py as built (CE-3289), which becomes a client
-->

# HANDOFF — backend → ui: build the film director (CE-4038)

**Dispatched at `687208f56`** (backend). ⛔ **Your scope is FROZEN at this sha.** Documents that change after it are FYI only. If a later
document invalidates an item, stop and report it; do not adapt.

🔒 **User, `2026-10-21`:** *"Can we afford handing this design implementation to different lane while we will be implementing the big
scale refactor?"* · *"it needs to rely on determinism, otherwise we could not specify when what cut should take place and at which time the
camera should be at what direction or not."* · *"Yes, write it and write a handoff to the UI line."* ⇒ **R-3038.**

⭐ **Why you, and why now:** backend is building the paused/stepped execution refactor
([`DESIGN_Paused_And_Stepped_Execution.md`](../DESIGN_Paused_And_Stepped_Execution.md), CE-3293). The director touches the editor, the
film camera, routes and a panel, not the kernel, so the two can run side by side. ⚠ **This supersedes the backend dispatch**
([`HANDOFF_Coordinator_To_Backend_Film_Director.md`](HANDOFF_Coordinator_To_Backend_Film_Director.md) and its STEER). Backend built nothing of
it except the sized step (`ITimeCommands.StepBy`, `/sim/step?dt=`), which is merged and yours to use.

## ⭐⭐⭐ Read first: the design's §0 (R-3038)

A take is **scripted in time** on a stepped run that **repeats**: the scout bakes anchor times, the film cuts at them, and live cues only
verify. ⛔ **The fifth pass's live-cue design (R-4064) is superseded for stepped runs.**

### ⭐⭐ The stepping rule — from day one, in every run that must repeat

> **Exactly one step per rendered frame — never zero, never two.** Real time is kept by **holding the frame** until the step is due, not
> by skipping or doubling steps. The scout, the live show and the capture all step this way. One take keeps one step size (60, or 20 / 15 Hz
> per R-4063) across its scout and every film of it.

⛔ This replaces D-1's *"at most 2 per frame"*. Why (design §0; execution design §10 F1): a system that runs every frame reacts on the frame
after a step, so a step that comes right after a step and one that comes after a paused frame give different runs.

## ⭐⭐ The four places your work meets the refactor

| # | touch point | what to do |
|---|---|---|
| **1** | **reading events.** Refactor slice S3 forbids world-bus reads (`repo.Bus.Read…`) outside the framework | the `AnchorBook` reads precursors from **component state** (as §3d already does for aiming: `ActionStatus.Weapon`) and events through the **view** (`view.ReadEvents<T>()`) — never `Bus.Read` |
| **2** | **every system declares when it runs.** After S1, registering a system without `[TickMode(...)]` throws | a system you add carries its mark from the start: a director or camera system is `EveryFrame` (it works while paused). Until S1 lands, write the intended mode in its doc comment so the mark is a one-line change |
| **3** | **behaviour changes at S5.** Step-only systems stop missing events | a beat time measured before S5 can move after it ⇒ ⚠ **re-scout every seeded take after S5**; never treat a pre-S5 scout as final |
| **4** | **"filmed = scouted" waits for S5.** Until then stepped runs are not guaranteed to repeat | build and test everything on single runs; the two-run identity is a REPORT now and a GATE after S5 (B0′ below) |

## What to build — the design's phases 1–3

⭐ **Build against the diagrams in [DESIGN_Film_Director.md](../../DESIGN_Film_Director.md); do not redraw them here.** Report the match per
design obligation ③: *"the design has N classes and M sequences; what I built matches, or deviates HERE because…"*. A deviation goes back
INTO the design (marked SUPERSEDED), not only into your report.

Items **B1, B2, B4, B5, B6, B9, B10, B11, B12** are exactly as in
[the backend dispatch](HANDOFF_Coordinator_To_Backend_Film_Director.md) (same design basis, same acceptance). These change under R-3038:

| # | item | accepted when |
|---|---|---|
| **B0′** | ⭐ **a two-run identity rail**: load one seeded take twice, stepped once per frame, compare the world at every cut (positions, health, every shot). It replaces the withdrawn B0 | **now:** it runs and REPORTS the differences · **after CE-3293 S5:** it is a GATE that passes at 60 Hz and at the take's step size |
| **B3** | `PacedClock` + `DirectorRunner` as in the backend dispatch, ⭐ **with the stepping rule**: one step per rendered frame, the frame held to real time | a live play of a reel never steps twice in a frame (assert it) and stays within 5 % of real time on a desktop |
| **B7** | `Scout` + `AnchorBook` as in the original B7: **bake** the anchor times from one run stepped like the film, **verify** them live during capture, re-scout on a scenario hash, step-size or build change | a take's every anchor is found, or the missing one is named; a capture whose anchor drifts from its baked time reports it as a **determinism finding** with both times |
| **B8** | capture as in the backend dispatch; ⛔ **no retakes** as a remedy for a missing beat | a reel of several scenarios renders to one movie on the cloud VM (Xvfb); a missing beat is reported, not re-shot |

⛔ **Out of scope:** instant replays (R-4061), `/director/draft` (phase 4), `--mode all` hosts (D-8), recording-based editing (§3e, phase 5).
⭐ **R-106:** if a measurement breaks a lean (B3's real-time factor, B9's gain), stop THAT item, report it and finish the rest.

## Gates — report these rows (the dispatching lane does not re-run them)

| # | report |
|---|---|
| 1 | per gate: the verbatim command · pass / fail / skip · the delta against the dispatch sha |
| 2 | ⭐ **T-1, the feature's own suites first:** `Hrot.Presentation.Tests` `FilmTests` + `FilmFrameRail`, then the editor's load rails (`DeterminismRails`, the `HandleScenarioLoad` readiness cases) through the extracted probe |
| 3 | every new rail named, each with an inverse-edit red-proof where it guards a rule (B3's one-step-per-frame, B10's never-written, B11's FPS-always) |
| 4 | the integration row: one live run of `POST /director/play {reel}` in the editor, **and** one capture of the same reel on the cloud VM; the scout table of the 55 seeded takes: found / missing beat / name not found |
| 5 | the measurements: the real-time factor of live play on the cloud VM and a desktop, per-frame time with panels on and off, and B0′'s difference report |
| 6 | the build: the affected projects with `--no-restore`, the TEST projects built (the stale-binary trap); one full-solution build at the end |
| 7 | the working tree clean after the suites; `tracker-counts.py --check`, `rulings-check.py`, `design-digest.py --check`, `mermaid-check.mjs` on the design |
| 8 | every id allocated (`CE-1xxx`, `R-1xxx` from the ui blocks) and the design sections you updated to the as-built |

⭐ **Started marker (rule 1b):** push an empty `chore: started CE-4038 at <sha>` commit right after you merge `backend@687208f56`, before
any code.
