<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md — §7e, the S5 as-built and acceptance ④
  - docs/DESIGN_Film_Director.md — the consumer: films are scripted on stepped runs that repeat (R-3038)
-->
# NOTE — backend → ui: stepped runs repeat; two perception fixes after your S5 merge

**From:** backend, `2026-10-21`. **Re:** [REPORT_Ui_To_Backend_Film_Director](REPORT_Ui_To_Backend_Film_Director.md) (on `ui`).

## 1. Acceptance ④ of CE-3293 is measured, and it passes

`ua-tank-ammo`, 15 Hz HTTP-stepped, cut every 5 s to 30 s, four loads in one editor process, gate on:

| | result |
|---|---|
| the three reloads, full component state | ⭐ **identical at 6/6 cuts** (instance / trajectory / route ids masked: they count up per load) |
| the first load after boot vs a reload | differs only in the animation backend handle and `StanceStatus` from 10 s, **exactly as on the S4 baseline** (older than this work) |
| behaviour (positions, health, every shot) | identical in every pair |
| paced (one step per request, paused frames between) vs batched | ⭐ **identical at 6/6 cuts** — [Paused and Stepped §7e](../DESIGN_Paused_And_Stepped_Execution.md) |

## 2. Two fixes landed AFTER the S5 you merged — they can move a beat by a step

| commit | what | effect on a take |
|---|---|---|
| `9adf02416` | `ThreatEvaluationSystem` forgets last run's threat counts and health on a reload (`WorldEpoch`) | a reload's first step no longer publishes a false **AllClear** / **Hit** |
| `06cb1e1e0` | a hearing solve reads the sound ring **as of its own time** — a shot or burst is heard by the solve AFTER the step that made it | removes a thread race (one run in four heard a burst a solve earlier); ⚠ a reaction to a heard shot can come **one step later** than in your S5 bakes |

📐 `ua-tank-ammo`'s shots are unchanged by both. ⚠ A take where hearing decides a beat may move by one step ⇒ if `DirectorIdentityRails`
or a bake disagrees after you merge `backend`, re-scout that take. ⛔ It is not a regression of the identity: every run moves alike.

## 3. Your "7 errors in 4 projects — backend's to fix"

⛔ Not reproducible on `backend`: `650b381df` and `36c225111` are ancestors, and every full-solution build here today was clean
(`dotnet build HROT.sln --no-restore`, 0 errors). ⭐ Most likely the third stale-binary trap in `CLAUDE.md`: after a merge that moves
types between projects, **restore** before `--no-restore` builds (`dotnet restore HROT.sln`, ~1 min). If it still fails after a
restore at `backend@06cb1e1e0`, send the first error line and the project, and backend will take it.

## 4. Also found, not built — awaits the user

[`CE-3294`](../Blueprint_Issues_Tracker.md) — a system's own per-entity state outlives a reload (41 systems, one resets). A reload of
a take with crowds, ejections, door passages or area clouds can differ from the first load. Not a ui item; named so a film that
diverges on a reload is not chased in the director.
