<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - NOTE_Coordinator_To_Behaviors_Demo_Sweep.md — the sweep this answers (CE-4035)
  - ../../DESIGN_Thermal_And_Acoustic_Sensing.md — §8.7, the thermal sight withdrawn after this sweep
-->
# NOTE — behaviors → coordinator: the full demo sweep, `2026-10-22`

**Re:** [NOTE_Coordinator_To_Behaviors_Demo_Sweep](NOTE_Coordinator_To_Behaviors_Demo_Sweep.md) (CE-4035). Every scenario
`scripts/utility-demo-check.py --launch` knows, one fresh cluster each, on `behaviors`; every failure re-run ALONE, and every
failure that survived re-run on a clean worktree at `129aea0bb` (behaviors before the night's work) to split new from old.

**87 scenarios · 77 pass · 10 fail — all 10 fail the same way at `129aea0bb` (older than tonight); tonight's two (hill-attack, weather-sensing) are fixed.**

| outcome | scenarios |
|---|---|
| ✅ pass | the other 77 (incl. `ua-hill-attack` and `ua-weather-sensing` after the fixes below) |
| ⛔ **my regression, fixed** | `ua-hill-attack` — CE-2349's thermal sight on the M1/Bradley put 4 tanks on the slope at once (A/B: 4 with it, 2 without); the carriage is withdrawn (`d5058e7d7`), PASS live after |
| ⚠ **sweep artefacts, not defects** | `ua-danger-crossing`, `-bp` — PASS at `--timeout 240` (their recorded pass; the sweep's default 60 s ended at 61 s) · `ua-airborne-drop`, `sq-defend-house` — stale checks fixed (`fb8295f59`); PASS · `cc-platoon-bound`, `ua-squad-march-loose`, `ua-rpg-window` — PASS when re-run alone |
| 🔴 **fail at `129aea0bb` too (older than tonight)** | `ua-casualty-care` + `-heavy` + `-leader` (Rescue never read / covering fire not first) · `pc-defend-house` ④ (Blue neither assaults nor breaks off) · `ua-civ-routine-alarm` (the near civilians stay `Gather`) · `ua-javelin-lock` ① (Javelin A never sees the cycling tank) · `ua-air-defence` ① (no missile warning; one run the Stinger never fired) · `ua-technical-scoot` ① (the technical never fires) · `uc-convoy-hit-ifv` ④ (the APC does not push 100 m past) · `uc-urban-battle` ⑤ (no remount after securing) |
| ⚠ **new tonight, the DEMO's geometry** | `ua-weather-sensing` ② — the observer stood 50 m inside the fog bank's edge, where W-1 (a region weighs 1 only at its centroid) leaves ~208 m of visibility; he saw at 148 m as in the clear. The engine is as designed (rail `WeatherRegionBlendTests.G3_*`); the bank now centres on him (`3d1fdd280`) — ✅ **PASS live** (fog 58 m, clear 148 m) |

## The one that varies — `uc-convoy-hit-ifv`

Four runs, three different outcomes, so it is not tonight's: at `129aea0bb` the APC **pushed** and its squad stayed aboard, but it stopped
short of 100 m past the charge (④); at HEAD once it pushed while its squad **dismounted** (`ReactToContact`, ③); twice it **dismounted**
the squad, which never fired (④: 0 rounds, every man `CombatPosture`). The push/dismount choice itself varies between runs.
