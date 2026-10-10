<!--STATUS
state: LIVE
updated: 2026-10-05
current-answer: the whole file — a procedure. §2 is the scenario table; §3 is per scenario.
stale-below: nothing
known-rot: none
related-designs:
  - docs/DESIGN_Utility_AI_Demo_Scenarios.md — OWNS the scenarios, what each must show, and the build items behind them (§4, §6).
  - docs/RUNBOOK_Cluster_Debugging_Over_Http.md — OWNS launching and reading a --mode all cluster; this runbook assumes it.
-->

# RUNBOOK — the utility AI demo scenarios on `ClusterRunner --mode all`

Each scenario is a **demo** (watch it in the editor or over HTTP) and an **E2E test** (`scripts/utility-demo-check.py`
exits 0 on PASS, 1 on FAIL). The design is [`DESIGN_Utility_AI_Demo_Scenarios.md`](DESIGN_Utility_AI_Demo_Scenarios.md).

## 1. Run one

```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner                          # once
python3 scripts/utility-demo-check.py --launch ua-posture             # ⭐ starts a FRESH cluster, runs, stops it
```

⭐ **Several scenarios in one cluster process work since `CE-295` (`2026-10-05`)** — a load from Live or Edit unloads
first, and since `CE-3075` a scenario that names NO terrain (the `hill-attack*` set) unloads the previous one. The
script still verifies the loaded cast against the scenario file. To watch a run by hand instead:

```bash
HROT_DEBUG_API_PORT=8111 setsid nohup xvfb-run -a dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all > /tmp/cluster.log 2>&1 & disown
grep -m1 "Debug API listening" /tmp/cluster.log                       # wait for it; it also lists the seeded scenarios
python3 scripts/utility-demo-check.py ua-posture                      # against the running cluster (its FIRST load)
```

| ⚠ trap | |
|---|---|
| `127.0.0.1` | every route 404s — the listener binds the `localhost` HOSTNAME (HTTP runbook §2) |
| a proxy | the script never uses one; with `curl` pass `--noproxy '*'` |
| `hill-attack*` after another scenario | ⚠ ends differently than on a fresh cluster (`CE-3076`, open) — run it with a fresh cluster |
| network ids | a load renumbers entities (the Rifleman is `1000`, not the scenario's `7101`) — the script finds them by NAME via `GET /entities` |
| stopping | `pgrep -f 'ClusterRunner[.]dll'`, then `kill` the ids in a SEPARATE command — `pkill -f` matches its own shell |
| a scenario not found | only folders under `scenarios/` are seeded in `--mode all`; the `tt-*` recipes are seeded by the editor only |

## 2. The scenarios

| scenario | terrain | shows | status |
|---|---|---|---|
| `ua-posture` (U1) | test-town | CombatPosture: fight when healthy, defend when hurt, back to fighting when healed; no flicker | ✅ PASS ×2 on fresh clusters, `2026-10-05` |
| `ua-threat-ranking` (U2) | test-town | ThreatRanking: armed + visible + near ranks first, unarmed and far below, hidden never | ✅ PASS ×2 on fresh clusters, `2026-10-05` (after `CE-3073` + `CE-3074`) |
| `ua-danger-crossing` (CE-3079) | test-town | the danger-area sensor: hold short of the WATCHED crossing, run across the unwatched one; the watcher's own two-task mission ends the threat — no HTTP intervention | ✅ PASS ×3 on fresh clusters at the 145 m Sentry, `2026-10-06` (runs 1–2 found the L-Block layout and a too-short window; run 4 the 125 m deadlock) + the in-process twin |
| `ua-danger-crossing-bp` (CE-3079 B7) | test-town | the SAME demo, the rifleman's task the BLUEPRINT `DangerCrossingBp` (built only from the per-kind sensor nodes, CE-3078) | ✅ PASS on a fresh cluster, `2026-10-06` + the in-process twin |
| `ua-three-hosts` (U3, CE-3082/3083) | test-town | ONE decision on THREE hosts: BTree `CombatPosture`, HSM `CombatPostureHsm`, blueprint `CombatPostureBp` — the same Health edits, the same choice | ✅ PASS, `2026-10-06` — identical winners on all three at every step |
| `ua-attack-approach` (U4, CE-3084) | test-town | `AttackApproach` nested in the advance: out of sight of an identified hostile ⇒ Flank / FiringPosition, then fire from there | ✅ PASS, `2026-10-06` (run 1 found the hostile dying in 5 s — 1000 HP now) |
| `ua-weapon-choice` (U5, CE-3089) | basic-desert | a Bradley fires the 25 mm at infantry and the TOW at a T-72 (`WeaponSelection` per shot) | ✅ PASS, `2026-10-06` (after four G7 defects, design §12 W6–W9) + the in-process twin |
| `ua-fire-distribution` (U6, CE-3088) | basic-desert | a leader + 4 riflemen: fire spread over the targets, ≤ 2 per target, every member fires (`/entities/{id}/squad`) | ✅ PASS ×3 for the distribution, `2026-10-06`; ✅ the hurt-member step PASSES `2026-10-07` (behaviors): at 10 HP `HoldProne` 0.975 and `StanceIntent` Prone (`CE-3090`, `CE-2121`) |
| `bt-doors` (CE-3104, buildings 5d) | bt-range | doors on the way: House A's LOCKED front is routed round; the Visitor stops at the CLOSED back door (set by the scenario's `TerrainObjects` section), opens it, walks through the hall into the west room; the Locksmith (curated `DoorLocksmith`, 5d-2) walks to the front, unlocks and opens it, and walks in through it — `DESIGN_Building_Interiors.md` §3j "5d-3 / 5d-4 as built" | ✅ PASS on a fresh cluster, `2026-10-08` — with the Locksmith after three more fixes (no `CanInteract` on the built-in humans, a node's finished walk left on the channel, a lintel taking the ground away) (runs 1–3 found four defects: the unregistered transient-spawn tag, the 2 m ground strip along thin walls, House A's door widths/placement, infantry planned on the vehicle layer — `CE-3112`) |
| `bt-window-duel` (CE-3136, P-8) | bt-range | peek and fire: A at House A's upstairs windows (prone under the sill, up, aim only at what it sees, else a blind burst at the freshest evidence, another window after a few exposures); B behind Van 1 steps out, fires, suppresses and bounds to Van 2 — `DESIGN_Peek_And_Fire.md` §8 / P-8 row | ✅ PASS on a fresh cluster, `2026-10-09` (both fire, A at both upstairs windows, B at Van 2 by t≈37 s) — after the readout fix (`CE-3144` ⑥: a `-Infinity` in the behaviour block made every `/entities` read 500, so earlier runs looked like the duel stopped at ≈ 60 s) |
| U7 `ua-squad-maneuver` | basic-desert | squad maneuver | ⏳ waits for the CE-507 D2/D3 decisions (design §8 Q5) |

## 3. Per scenario

### 3.1 `ua-posture` (U1)

**Cast:** a Rifleman (TKB 2002) running `CombatPosture` toward (325,300); one Armed Hostile (2002) at (360,320) — the
hostile does not fight back, so the script drives the rifleman's Health over HTTP.

**What to watch** — `GET /entities/1000/utility` on the `Scenario` perspective, after `POST /trace/observe {"networkId":1000,"on":true}`.
📐 **Measured `2026-10-05`, two fresh runs, identical:**

| step | winner (scores after hysteresis) | why |
|---|---|---|
| start | `AdvanceAndAttack` until the hostile is seen, then `Suppress` (0.93 vs Advance 0.74) | a matched, armed enemy (strength 0.5) ⇒ suppress, not advance (CE-2072) |
| Health 40 | `Flee` 0.87 vs `TakeCover` 0.69 | hurt; here the retreat answer beats the cover answer |
| Health 10 | `Flee` 0.98 (incl. +0.08 held) vs `TakeCover` 0.88 | the held posture keeps +0.08 |
| Health 100 | `Suppress` 0.93 | healthy again |
| any held Health | `switchCount` does not move | no flicker |

⚠ WHICH defensive posture wins depends on where the rifleman stands when hurt (the cover and retreat EQS answers) — an
earlier run that had advanced further chose `TakeCover` at 40 HP and HELD it at 10 HP by the +0.08 bonus (TakeCover 0.96 vs
Flee 0.90). The script therefore asserts the CLASS (fight / defend) and prints the run's own sequence; the exact hysteresis
arithmetic is pinned by `UtilityScorerTests.CE3069_*`.

`lastPass` lists every consideration of the latest posture pass: `input`, `raw`, `curved`, `weight` per option.

**A failure means — look here first:** no `decisions` ⇒ not observed (re-arm) or not the `Scenario` perspective ·
`Suppress` never comes ⇒ the hostile is not in the rifleman's memory (`GET /entities/1000` → `TargetMemory`) ·
`TakeCover` never comes ⇒ no cover answer (`ranked` TakeCover 0 — the EQS cover sensor) · a Health edit refused ⇒ the
script prints the 400 (CE-3003: the edit must reach the owner).

### 3.2 `ua-threat-ranking` (U2)

**Cast:** the Rifleman; Civilian Near (1001, unarmed) 21 m; Armed Near (2002) 43 m; Armed Far (2002) 96 m; Armed Hidden
(2002) behind the Tower.

**Expect:** the `Threat ranking` decision's `ranked` list puts Armed Near first, the civilian below it, Armed Far below it,
and never the hidden one.

📐 **Measured `2026-10-05`, two fresh runs:** Armed Near 0.043–0.052 · Armed Far 0.042–0.050 · Civilian 0.013–0.016 · the
hidden one never ranked (scores rise with time in view — the check waits until the top is above 0).

🔴 **This scenario found two production defects on its first runs, both fixed:** `CE-3073` — the unit remembered only
ONE of several contacts seen in the same frame (`ActiveSensorTracks.Count` 1); `CE-3074` — ThreatRanking scored every
HEALTHY contact 0, so an unarmed civilian with no Health component ranked first.

### 3.4 U3–U6 — run, and what to read when one fails

```
python3 scripts/utility-demo-check.py --launch --timeout 120 ua-three-hosts       # U3
python3 scripts/utility-demo-check.py --launch --timeout 120 ua-attack-approach   # U4
python3 scripts/utility-demo-check.py --launch --timeout 120 ua-weapon-choice     # U5
python3 scripts/utility-demo-check.py --launch --timeout 120 ua-fire-distribution # U6
```

| scenario | what fails | look first at |
|---|---|---|
| U3 | the hosts disagree | `GET /entities/{id}/utility` on each — ⚠ the HSM switches ONE tick later (behaviors' G4 note); a defensive winner depends on where each unit stands (the EQS answers) |
| U4 | never flanks | the approach decision in `/utility` (`Attack approach`); it needs a LIVE identified target out of SIGHT (`ThreatInSight`); a killed target no longer counts (CE-2120) |
| U5 | the gun fires at the tank | ⭐ `GET /entities/{bradley}/weapons?target={tank}` — every mount's inputs and the choice; `rangeFit` 10 = unknown range, `readiness` 0 = reloading |
| U6 | no assignment | ⭐ `GET /entities/{leader}/squad` — members, assigned targets, the merged pool and `lastMergeTick` |

### 3.3 `ua-danger-crossing` (CE-3079)

📄 Design: [`DESIGN_Utility_AI_Demo_Scenarios.md`](DESIGN_Utility_AI_Demo_Scenarios.md) §10.3–§10.7.

```
python3 scripts/utility-demo-check.py --launch --timeout 240 ua-danger-crossing   # the walk is 1.5 m/s: ≈ 250–285 s of sim time
python3 scripts/utility-demo-check.py --launch --timeout 240 ua-danger-crossing-bp   # B7: the same, the task the blueprint DangerCrossingBp
```

⭐ **`ua-danger-crossing-bp`** is the same file with ONE change: the rifleman's task is `DangerCrossingBp` with `{"Objective":[285,220,0]}` (one param, where the BTree's order gives the objective twice). Same cast, same watcher mission, same check. ⚠ The blueprint rushes straight to the far handle and releases its hold at < 0.5 (no hysteresis) — design §10.4a H7 row. In-process twin of both: `PostureScenarioTests.CE3079_DangerCrossing_…` (a theory over the two scenario names, ≈ 4.5 min each).

**Cast:** the Rifleman (2002) at (100,60), mission `DangerCrossing` to (285,220); the Watcher (2002, Hostile) at (370,212),
mission `Sentry` (ends itself once a contact is within 145 m and 15 s have passed) → `MoveToLocation` (395,290), north-east
behind the Tower. 📐 Layout measured on the REAL footprints (`2026-10-06`): L-Block is an L, so a post at (310,222) saw BOTH
crossings (first live run); from (370,212) the line to Cross Street crosses ≥ 25 m of building and the line to Main Street
is clear; along the walk to (395,290) the last point the rifleman sees has ≥ 10 m of building between it and the crossing. ⭐ **Nothing is written over HTTP** — the check only reads.

**Expect, in order:** ① `GET /entities/{rifleman}/sensors` shows a `DangerArea` sensor (route source `ToPoint`) with TWO
crossings — Cross Street at ≈ y 150 first, Main Street at ≈ x 255 second · ② once the rifleman has seen the watcher (≈ 40 m
before Main Street), the Main Street crossing rates ≥ 0.5 and Cross Street stays low (L-Block hides it from the watcher) ·
③ the rifleman runs across Cross Street without stopping and HOLDS within 4 m of Main Street's near handle for ≥ 10 s ·
④ the watcher's mission moves to task 2 and it walks north; the rating falls below 0.4 as soon as its LAST-KNOWN position
no longer sees the crossing · ⑤ the rifleman crosses and arrives.

| what fails | look first at |
|---|---|
| ① no sensor, or `answer.ready` false | the solver node: `/diagnostics/architecture` `DangerAreaResult` `sentSamples` (SimHost) / `receivedSamples` (CGF); the config's `SolverNodeId` must be a node with `Perception \| NavigationSolver` |
| ① one area where two are expected | the navmesh route bent through the intersection (one `Intersection` area) — the layout assumption failed; re-measure the route |
| ② never threatened | the rifleman never SAW the watcher (its `TargetMemory`); or sight from the watcher's last-known position is blocked — `ThreatOn` uses `TerrainWorld.SegmentBlocked` |
| ③ holds at the wrong place / oscillates | the sensor's route source is not `ToPoint` (with `OwnMove` the hold's own move hides the area — §10.4) |
| ④ never clears | the watcher's task 1 never ended — `CE-2112` (the BTree `Wait` never completing) is the known cause; or the rifleman lost sight of it while its last-known position still saw the crossing (memory keeps it ≈ 0.99 fresh for minutes) |

📐 **Measured `2026-10-06` (PASS, fresh cluster, sim 0 → 284 s):** areas `StreetCrossing` at 133 m and 198 m along the route ·
only the Main Street one rated ≥ 0.5 · the rifleman held at (247.6, 187.7) ≥ 10 s · the watcher's mission advanced by itself
and it walked to (395.4, 291.1) · the rating cleared · the rifleman crossed and arrived. 🔴 **The first run found that L-Block is
an L** (the old post saw both crossings) — the layout is now measured on the real footprints, not bounding boxes.

