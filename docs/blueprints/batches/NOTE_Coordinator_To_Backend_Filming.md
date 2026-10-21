<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../PLAN_Feature_Films.md — §3 the pipeline (P-1…P-7) and its leans
  - ../../RUNBOOK_Filming.md — film.py as built (CE-3289)
-->

# NOTE — coordinator → backend: the overnight film run (CE-4034, CE-4037)

🔒 **User:** *"so the filming can take place automatically overnight from an already prepared description what to show and how (filming
takes long, each frame few secs -> ideal for overnight)"* · *"the clips … should use camera cuts to show the interesting parts from the
best view … be the director."*

**What exists now:** [`docs/films/clips.json`](../../films/clips.json) holds **55 clips with 176 shots** (~11 min of film, ~6–7 h in
`--mode editor`). Each clip is a director's shot list: establish → set-up → the moment → consequence → an optional slow replay
([PLAN §1a](../../PLAN_Feature_Films.md)). Times are **anchors** (`"fire-0.5"`), not numbers, because the docs give few exact times.

**What `film.py` / `film-events.py` need** — full table in [PLAN §3](../../PLAN_Feature_Films.md):

| # | piece | lean |
|---|---|---|
| P-2/P-3 | resolve anchors (`shot`, `hit`, `blast`, `task`, `health`, `side`, `embark`, `near`, `gesture`; `*Name` = any entity whose name starts so) with a scout pass in the editor — deterministic, so the times are frame-exact for the film pass | the probes already exist in `utility-demo-check.py` |
| P-4/P-5 | `film-batch.py clips.json --out <dir>`: one process per clip; scene scores + a 9-frame contact sheet; `REPORT.md` with ✅ filmed / ⚠ flat (nothing visible moved) / ⛔ beat missing | a failing clip never stops the night |
| P-6 | the live check runs before the film; red ⇒ skip and report | worth the ~2 min |
| P-7 | `to` (eased per-frame camera move: push-in, pan, zoom) · `speed` (0.5 = slow motion) · `replay` (a shot back in time: a second deterministic pass, spliced) · caption card + 0.3 s cross-fades in post | all in `film.py`; it already computes the camera per frame |

⭐ **The scout pass doubles as a health check:** a beat it cannot find means the scenario is broken — that is a finding for the morning
report, not a reason to guess a time.

**Also (CE-4037):** G-4 — the `demo-city` recipe has no shooter, so traffic → frightened civilians is never seen; add a short `ShootAt`
at ~20 s. G-8 — re-measure CE-2160 (an authored `Roe` crashed the cluster; not reproduced 2026-10-14), since the casualty demos fake
their fall because of it. And CE-4033 (cold demo-city navmesh bake ejects SimHost) blocks any demo-city clip on a fresh cluster.
