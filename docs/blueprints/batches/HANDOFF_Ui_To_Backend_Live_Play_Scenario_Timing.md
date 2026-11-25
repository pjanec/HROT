# Handoff: ui → backend — a bomb that misses when played live; scenarios that pass their regression but not real-time play

| | |
|---|---|
| **From** | `ui`, 2026-11-25 |
| **For** | the `backend` lane |
| **Dispatched at** | `ui` (see the commit that adds this file) |
| **Id** | **CE-11057** (ui block, allocated here; the row is yours for the duration) |
| **User, verbatim** | *"I would like to give this issue to a backend lane. I think it is extremely sensitive to the bomb release time and frame time stepping and might need some cheating like slightly steering the bomb to desired target point to eliminate the issues."* |
| **Found by** | `unreal-ig`: [NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md](NOTE_UnrealIg_To_Ui_How_We_Run_Scenarios.md) (`unreal-ig` `ce3f3c3dc`) — their exact recipe |

## 1. The symptom — measured by `ui` on HROT `ui` `5c3a40aea`, Linux, Debug, no gateway

| `demos/weapons-and-fires/fl-napalm-strike` | regression (`scripts/scenario-regression.sh`) | live (recipe below) | `unreal-ig`, live, Windows Release `037e62a8d` |
|---|---|---|---|
| the line men set alight | 27.5 s | **~37–38 s** | not by 60 s |
| the line men dead | by 32 s | ~38 s | alive at 60 s |
| the Late Man hurt by the GROUND fire | by 84 s | **never** (through 105 s) | never |
| verdict | PASS (every row; ⚠ golden warning: the A-10's position at t = 50 s differs) | would FAIL rows 3, 4, 6 | would FAIL |

`demos/platoons-and-vehicles/va-pop-smoke` (checks run 60 s: the Bradley's TOW, the tank's smoke within 3 s): live, the tank's health was
unchanged through 120 s and it was **destroyed and burning at 128.5 s** — the attack came more than a minute late and the screen did not save it.
`unreal-ig`: no TOW in 130 s. ⚠ Only health was polled: the launch time and whether smoke was popped are NOT measured.

⛔ **Not measured — the first thing to do:** the A-10's release time, its position and velocity at release, and the firebomb's burst point,
in BOTH runs. The first divergent event names the cause.

## 2. The two ways of running

| | regression `--verify` (`CommandLineVerify`) | live (the recipe) |
|---|---|---|
| step | fixed 1/60 s frames, turbo (9001 steps for 150 s, 55 s wall) | real time, the step = the frame's dt (varies per machine, build, load) |
| load | the editor's verify load | `POST /scenario/load/live` → `OperatingLive`; `/status` shows `"scenario": null` |
| threading | threaded node + parallel sim (settings) | the same |

**The recipe** (Linux): `HROT_DEBUG_API_PORT=8111 HROT_HIDDEN_WINDOW=1 dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode editor --headless --domain 7`;
`POST /scenario/load/live {"name":"demos/weapons-and-fires/fl-napalm-strike","waitForReady":false}` (retry until ok); wait for `clusterState`
`OperatingLive` (⚠ every answer is wrapped in `{ok, data}`); `POST /sim/play {}`; poll `GET /entities/{networkId}` (Health, `EffectState.BurningUntil`).
The sim keeps real time (15.0 s per 15 s polled).

## 3. Where to look (measured seams)

| seam | what it does |
|---|---|
| `BombBallistics.ReleaseNow` / `Cue` (`FDP/Toolkits/Fdp.Toolkits/Combat/BombBallistics.cs:57, :116`), called by `AimAndFireExecutor` (`:769-785`) | the weapon brain's release cue — a predicted impact checked once per brain step: with a large or irregular step the cue fires late (the plane flew on) |
| `FireProcessingSystem.cs:309-330` (CE-2180, Air Ops §8a) | the bomb leaves the carrier as it was when the brain RELEASED it, flown forward to now (`LateSteps`, `FlyFor`) — the existing fix for the brain→node trip |
| `AirPlatformStrikeBehavior` (`Hrot.AI.Behaviors/Brains/AirAttackBehaviors.cs:1675`), LevelBomb (§9c) | the run-in that brings the plane over the point |
| `NapalmSystem` (CE-3487, `DESIGN_Flame_Weapons.md` §7b) | the fireball and the ground fire on the cell record |

📄 Designs: [DESIGN_Air_Operations.md](../../designs/air-attack/DESIGN_Air_Operations.md) §8 / §8a (the bomb), §9c (LevelBomb);
[DESIGN_Flame_Weapons.md](../../designs/weapons/DESIGN_Flame_Weapons.md) §7b (napalm).

## 4. What is asked

1. **Measure** §1's ⛔ row; find the first divergence (release time / point, the cue, the run-in, the burst, the fire's placement).
2. **Make the strike land where it was aimed regardless of the step** — the user's lean: *"slightly steering the bomb to desired target point"*
   (a small terminal correction of the falling bomb toward the aim point, bounded so it stays plausible), or the same effect by a
   step-independent release (sub-step interpolation of the cue crossing). Yours to choose — argue it in the design (§8a), with the rejected one.
3. **va-pop-smoke**: measure why the Bradley's TOW comes > 60 s late live; fix in the same spirit if it is the same class (a cue / timing that
   depends on the step), else report it.
4. **So the machine catches this class**: a feature scenario must pass when PLAYED LIVE too. Check whether `TIER=cluster` (real time, `--mode all`)
   already shows it; if not, propose the cheapest regression tier that plays at real time with an uneven step.

## 5. Acceptance

- `fl-napalm-strike` and `va-pop-smoke` pass `scripts/scenario-regression.sh` AND the live recipe (all expect rows by their times), on this
  VM; ideally also under an artificially uneven step (e.g. a frame-time jitter knob if one exists).
- The rails of the bomb (`BombBallisticsTests`) and napalm stay green; the design records the as-built (§8a).
- Reply note to `ui` (we answer `unreal-ig`).
