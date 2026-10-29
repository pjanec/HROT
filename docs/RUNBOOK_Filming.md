<!--STATUS
state: LIVE
doc-type: runbook (how to film the 3-D map — not a design; the design and its UML are Map 3D Mode §6ab)
updated: 2026-10-29 (§0b record a live run in the editor, film it in the Replay Browser — the HTTP sequence, measured CE-4102) · 2026-10-21 (§0a the film director, CE-4038 — takes, reels, /director/*, film.py --take; the shot math is the engine's) · 2026-10-21 (§0 --mode editor; R-3034 — runs are not repeatable, load again for each)
current-answer: this whole file — ⭐ §0a the film director (takes, reels, live or to film); ⭐ §0b record an exercise in the editor and film it in the Replay Browser; §0 film in --mode editor; §1 the quick start, §2 shots, §3 a plan with cuts, §4 finding when things happen, §5 checking the
  film, §6 the traps.
stale-below: nothing.
known-rot: none.
related-designs:
  - DESIGN_Film_Director.md — the designed successor: takes and reels played in the editor live or to film; film.py becomes its client (D-9)
  - PLAN_Feature_Films.md — OWNS what to film (the clip list, docs/films/clips.json) and the overnight pipeline proposal (CE-4034)
  - docs/DESIGN_Map_3D_Mode.md §6ab — OWNS the design (the director camera, the scene capture, the routes; UML; as-built and measured limits §6ab-i)
  - docs/Editor_Headless_Xvfb.md — OWNS the headless launch (Xvfb + Mesa) the films run under
  - blueprints/Architect_Question_108_Run_Mode_Picker.md — OWNS the armed mode (Preview / Live / AAR), ▶ and ■ (§2.8, §2.14, D22)
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — OWNS the HTTP debug API and its traps (the localhost HOSTNAME, a paused cluster)
-->
# RUNBOOK — filming the 3-D map

Short videos of the 3-D map: **the scene alone** (sky, terrain, buildings, vehicles, people, flashes, blasts, smoke, rounds) — ⛔ no
panels, no gizmos / cards / HUD. Works in a cloud session (Xvfb + Mesa, no GPU) from any lane. `CE-3289`, ruling `R-3032`.

```mermaid
graph LR
  A["film-events.py: WHEN things happen"] --> B["write a plan: shots at sim times"]
  B --> C["film.py: pause, shot, capture, step"]
  C --> D["look at frames / scene scores"]
  D -->|"adjust angles or times"| B
  C --> E["MP4"]
```

*Caption:* filming is a loop — find the moments, place the cameras, film, look, adjust. A film costs minutes, so look at a few frames of
a short take before filming the whole plan.

## 0. ⭐ Film in `--mode editor` — the preferred host for demos *(user, `2026-10-21`)*

> 🔒 *"pls try the filming on the clusterrunner --mode editor (this is preferred for demos — might hide some network related engine
> bugs)"* · *"stepping in the editor is more 'deterministic' (no network included, faster)"*

| 📐 measured on `ua-tank-ammo`, `2026-10-21` | `--mode editor` | `--mode all` |
|---|---|---|
| one world / network | ⭐ one world, no wire | five nodes over DDS |
| a cut | ⭐ **STEPS** to the next `at` — frame-exact in sim time | plays in real time, overshoots ~0.1 s |
| a film frame | ⭐ **~0.8 s** | ~1.85 s |
| stepping to a cut | ~20 s per sim second skipped | (plays in real time) |
| the M1's shots on the T-72 | **8.85 s, 17.58 s** (`film-events.py --mode editor`) | ~10, ~20, ~30 s |

```bash
python3 scripts/film-events.py ua-tank-ammo --mode editor --seconds 35      # exact shot times (each shot's own firedAt)
python3 scripts/film.py ua-tank-ammo --mode editor --plan plan.json --out /tmp/film.mp4 --keep-frames   # background
```

⚠ **Two runs are NOT the same run** (R-3034, measured CE-3291: the first load after boot plays differently, later loads drift) — so
`film-events.py`'s times are a GUIDE for the next run, not a promise. Load again for every run; never Stop-and-rewind a preview to
rerun it (a rewind leaks brain and perception state). Look at the frames and adjust. 📐 The first editor film caught the second round's impact on the T-72 on the frame the plan
aimed at (`at` 17.3 for a shot fired at 17.58). ⚠ A muzzle flash shows ~0.1 s after `firedAt` (the effect spawns a few frames later).
⚠ An editor result says nothing about the wire — a demo that must prove the CLUSTER films `--mode all`.

## 0a. ⭐ The film director — takes and reels, live or to film *(CE-4038, built `2026-10-21`)*

⭐ A scenario's camera script is a **take** (`scenarios/<s>/takes/<take>.take.json`; 55 seeded from `docs/films/clips.json`), and a
**reel** (`films/<name>.reel.json`) plays several as ONE movie. The editor plays them — 📄 [DESIGN_Film_Director.md](DESIGN_Film_Director.md)
§8 as built. ⭐ **One step per rendered frame** (R-3038), the frame held to real time.

| you want | do |
|---|---|
| watch a reel on your machine, live | View › **Director** → Reel → Play (Esc stops) · or `POST /director/play {"reel":"highlights"}` |
| one take again | the Director panel's row → Replay · `POST /director/play {"scenario":"ua-tank-ammo","take":"main"}` |
| bake a take's beat times (scout) | panel → Prepare · `POST /director/scout {"scenario":"ua-tank-ammo"}` — ⚠ re-scout every take after CE-3293 S5 |
| film a reel overnight (cloud VM, Xvfb) | `POST /director/play {"reel":"features","capture":{"dir":"/tmp/films/features","fps":30,"size":"1280x720"}}` → one MP4 with chapters and sound |
| film one take from the shell | `python3 scripts/film.py ua-tank-ammo --take main --out /tmp/tank.mp4` (launches `--mode editor`) |
| the status | `GET /director/status` — the phase, the shot, the realtime factor, and per take: missing beats, drifted beats, the world at every cut |
| the map's layers / the sound, as the user's clicks | `GET/POST /map/layers` · `GET/POST /audio` — ⚠ a TAKE's layers are transient and never saved |

⚠ `film.py`'s own `--plan` / shot flags still work, but the camera math is the engine's now (`POST /film/shot {spec, t}`, D-9).

## 0b. ⭐ Record an exercise in the editor, film it in the Replay Browser *(CE-4102, measured `2026-10-29`)*

🔒 *User: "the goal here is to make it possible to easily record exercise so we can prepare movie clips using director in replay browser"*.
Two processes, ONE staging root: the editor records a LIVE run to disk, the Replay Browser lists it, opens it and the director films it.

```mermaid
sequenceDiagram
  participant E as editor :8120
  participant D as disk FDP_STAGING_ROOT
  participant R as Replay Browser :8121
  E->>E: POST /scenario/load/edit · POST /run-mode armed live
  E->>D: POST /scenario/load/live — runs, records (Record live runs ON)
  E->>E: POST /sim/stop — ■ back to editing, Live stays armed
  D-->>R: nodes/node-1/exercises/ID/node_1.fdp + .meta.json
  R->>R: GET /exercises · POST /replay exerciseId
  R->>R: POST /director/play scenario + recording + take → GET /director/status until Done
```

*Caption:* the recording is the only thing the two processes share — the Replay Browser needs no cluster, only the folder.

**① Launch both, the same `FDP_STAGING_ROOT`** (build `Hrot.ClusterRunner` first):

```bash
D=/home/user/HROT/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0
launch() { (cd $D && FDP_STAGING_ROOT=/tmp/films-staging HROT_DEBUG_API_PORT=$2 setsid nohup \
  xvfb-run -a --server-args="-screen 0 1600x1000x24" dotnet Hrot.ClusterRunner.dll -m $1 --no-wait > /tmp/$1.log 2>&1 < /dev/null &) ; }
launch editor 8120          # up in ~15 s
launch replaybrowser 8121   # up in ~9 s — may start later, it scans the folder when asked
c() { if [ "$1" = POST ]; then curl -s --noproxy '*' -m 150 -X POST "http://localhost:$2$3" -H 'Content-Type: application/json' -d "${4:-{\}}"
      else curl -s --noproxy '*' -m 30 "http://localhost:$2$3"; fi; echo; }   # usage: c GET|POST <port> <route> [json]
```

**② Record a live run in the editor:**

```bash
c POST 8120 /scenario/load/edit '{"name":"hill-attack","waitForReady":true}'
c POST 8120 /run-mode '{"armed":"live"}'                       # the indicator's move; GET /run-settings shows the cluster's armed mode
c POST 8120 /scenario/load/live '{"name":"hill-attack","waitForReady":true}'   # OperatingLive, running, recording:true in GET /status
sleep 60                                                       # the run; GET /status → simTime
c POST 8120 /sim/stop                                          # ■: back to editing at the start, Live stays armed, the exercise is on disk
```

**③ Film it in the Replay Browser:**

```bash
c GET  8121 /exercises                                         # {exerciseId, scenario, durationSeconds} — newest first
c POST 8121 /replay '{"exerciseId":"<id>"}'                    # OperatingReplay, the recorded entities
c GET  8121 /director/moments                                  # combat beats to anchor shots on (empty if nothing fired)
c GET  8121 /entities/<networkId>                          # Components.SimTransform.Position — where to point the camera
c POST 8121 /director/play '{"scenario":"hill-attack","recording":"<id>","speed":4,
  "take":{"title":"…","shots":[{"at":2,"seconds":6,"lookAt":[668,522,0],"distance":150,"yaw":45,"pitch":-35,"orbit":3},
                               {"at":10,"seconds":6,"lookAt":[668,522,0],"distance":40,"yaw":225,"pitch":-15}]}}'
c GET  8121 /director/status                                   # poll: phase Playing → Done; reports[0]: status ok, problems [], cuts per shot
```

⭐ To FILM to disk add `"capture":{"dir":"/tmp/films/x","fps":30,"size":"1280x720"}` to `/director/play` (§0a). ⭐ A take may be inline (as
above, played and never saved), a saved name (`"take":"main"`, `scenarios/<s>/takes/main.take.json`), or drafted on a beat
(`POST /director/draft {lane, eventIndex}` — needs `/director/moments`).

| ⚠ trap — measured | |
|---|---|
| ⛔ **`POST /sim/play` from Edit does NOT start Live** — routes are raw (AQ108 D22): from Edit it is the Preview dry run, which records nothing | start the live run with `POST /scenario/load/live` (the UI's ▶ with Live armed does the same load) |
| ⚠ **`POST` with no body answers `411 Length Required`** | always send a body — `-d '{}'` (the `c` helper does) |
| ⚠ **the two processes must share `FDP_STAGING_ROOT`** — the editor writes `{root}/nodes/node-1/exercises/<id>/`, the Replay Browser scans `{root}` | set it on BOTH launches; ⛔ the default is `$TMPDIR/FDP_Temp`, shared with every other run on the box |
| ⚠ **`/director/moments` is EMPTY when nothing fired** (`hill-attack`, 103 s: 0 beats) | anchor shots on plain seconds (`"at": 2`) and point them at an entity's recorded position |
| ⚠ **slow under Xvfb + Mesa**: a 16 s take at `speed: 4` took **82 s** (realtime factor ~0.2) | poll `/director/status`; don't block on it |
| ⚠ before any scenario is opened the editor's `/status.armedMode` (its own default, Preview) may differ from the cluster's `/run-settings.armed` (Live) | they agree from the first open / arm (AQ108 §2.14) |
| ⚠ `GET /exercises` listed `scenario: null` before `3f7785c24` | fixed: the scenario comes from the recording's sidecar |
| ⚠ stop both processes after: `pkill -f 'ClusterRunner.dll -m editor'` · `… -m replaybrowser` (and their `xvfb-run` parents) | a leftover holds its port |

## 1. Quick start

```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore        # film.py refuses a runner older than the source (or HROT_ALLOW_STALE_RUNNER=1)
which ffmpeg Xvfb                                                # both are in the cloud image
python3 scripts/film.py ua-tank-ammo --follow Tank --distance 28 --pitch -14 --yaw 230 --orbit 4 \
        --warmup 8 --seconds 6 --out /tmp/tank.mp4               # ⚠ ~1.85 s per frame: 6 s of film ≈ 6 min — run it in the BACKGROUND
```

`film.py` launches `ClusterRunner --mode all` under Xvfb on port 8111, loads the scenario, runs it to `--warmup`, **pauses**, then per
film frame: holds the camera (`POST /film/shot`), writes the scene (`POST /film/capture`), steps the sim one film frame
(`POST /sim/step`). ffmpeg joins the PNGs. `--keep-frames` keeps them beside the MP4 (`<out>-frames/`). `--size 1280x720`,
`--fps 30`, `--supersample 2` (smoother edges, 4× the render cost).

## 2. Shots

| shot | flags | what it does |
|---|---|---|
| **follow** | `--follow NAME\|ID --distance --yaw --pitch [--orbit deg/s] [--lift]` | looks at the unit (every frame, it moves) from that distance and angle; `--orbit` circles it |
| **over the shoulder** | `--over NAME --toward NAME2 --back --height [--ahead] [--lift]` | behind NAME on the line to NAME2, `--back` m behind, `--height` up, looking `--ahead` (share) of the way to NAME2 |
| **fixed point** | `--look-at X,Y,Z --distance --yaw --pitch [--orbit]` | a fixed point (a wide shot, a place) |
| **any shot** | `--fov DEG` | the lens: 45° default; ⭐ **narrow = telephoto** — a target 200 m away fills the frame at ~10° |

| ⭐ the angles | |
|---|---|
| **yaw** | the direction the camera LOOKS, degrees from **east, counter-clockwise**: 0 looks east, 90 north, 180 west, 270 south. ⇒ to see a unit from its south, look north: yaw 90 |
| **pitch** | negative looks down: −10 a low side view, −30 a high one, −89 straight down |
| **positions** | HROT metres, Z up; `film-events.py` prints every named entity's start position |

## 3. A plan — several shots with cuts

```json
[
  {"at": 9.4,  "seconds": 1.8, "over": "Tank", "toward": "Enemy Tank", "back": 14, "height": 4.5, "ahead": 0.2, "lift": 2},
  {"at": 18.5, "seconds": 2.6, "follow": "Enemy Tank", "distance": 30, "yaw": 180, "pitch": -10, "fov": 25},
  {"at": 29.9, "seconds": 2.6, "look_at": "300,400,2", "distance": 190, "yaw": 180, "pitch": -9}
]
```

```bash
python3 scripts/film.py ua-tank-ammo --plan plan.json --out /tmp/cut.mp4 --keep-frames     # background
```

Each segment starts at sim time `at` (seconds of the scenario) and lasts `seconds`; keys are the flags without dashes (`look_at`,
`fov`). Between segments the sim **plays in real time** to the next `at` — a cut. ⚠ See §6: a cut overshoots.

## 4. Finding WHEN things happen

```bash
python3 scripts/film-events.py ua-tank-ammo --seconds 40      # ~1.5 min; prints positions, then "sim 10.2: Tank -> Enemy Tank InFlight"
```

📐 ua-tank-ammo: the M1 (1000, at 300,280) fires on the T-72 (1001, at 300,520) at about sim **10**, **20** and **30** s; the third
kills it (burning). ⚠ Shots only — a scenario whose action is movement (an air assault, a crowd) needs its own probe: poll
`GET /entities/{id}` positions the same way.

## 5. Checking the film without watching it

```bash
# where the action is: frames that differ most from the one before (flash, blast, cut)
ffmpeg -i film.mp4 -vf "select='gt(scene,0.003)',metadata=print:file=-" -f null - 2>&1 \
  | grep -oE "pts_time:[0-9.]+|scene_score=[0-9.]+" | paste - - | head
```

Then open a few PNGs from `<out>-frames/` (an agent can read them as images) — the frame at each event, and one per segment for the
composition. 📐 A muzzle flash scores ~0.01, a cut ~0.07–0.17.

## 6. The traps — each one measured

| trap | what to do |
|---|---|
| ⚠ **~0.8 s per film frame in the editor, ~1.85 s in `--mode all`** (1280×720, llvmpipe) — each sim step is a full frame of the window | run film.py in the background; film a 1–2 s take of a shot first and look at it |
| ⚠ **a cut overshoots ~0.1 s in `--mode all`, and NO host repeats a run exactly (R-3034)** | film in the editor (§0); put `at` ≥ 0.5 s before the event and check the frames (📐 the first plan's cut landed just AFTER the T-72 was hit) |
| ⚠ **far is tiny** — 45° makes a tank 240 m away a speck | `--fov 10…25` (telephoto) or a closer camera |
| ⚠ **the first frame of a new shot has the previous camera's sky** | film.py throws it away (a 64-px capture) — keep that if you script the routes yourself |
| ⚠ **buildings between camera and subject** are drawn | move the camera (yaw / distance) — 📐 a wide shot east of ua-tank-ammo's T-72 crossed a roof |
| ⚠ **`--mode all` films the map ON SCREEN** — the perspective shown (the default Scenario) | `/combat/shots` and friends switch perspective; do not poll them while filming |
| ⚠ **504 "no map drew a frame"** | no window (a headless runner, no Xvfb) or the map not visible |

## 7. Driving it by hand (any HTTP client)

| route | body |
|---|---|
| `POST /film/shot` | `{follow, distance, yawDeg, pitchDeg, lift, fovDeg}` · `{eye:[x,y,z], target:[x,y,z]}` · `{lookAt:[x,y,z], distance, yawDeg, pitchDeg}` |
| `DELETE /film/shot` | gives the camera back |
| `POST /film/capture` | `{path, width?, height?, supersample?}` → `{path, width, height, simTime}` |
| `POST /sim/pause` · `POST /sim/step {count}` | one sim step = 1/60 s; 2 per frame at 30 fps |

⭐ `--mode editor` films the same way (§0) — every map host carries the film layer. The code:
`Hrot/Engine/Hrot.Presentation/Map3D/Film.cs`, `MapCanvas.DrawScene`, the routes in `DebugApiHost`; rails `FilmTests`, `FilmFrameRail`.
