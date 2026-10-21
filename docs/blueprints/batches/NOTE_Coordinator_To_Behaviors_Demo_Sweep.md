<!--STATUS
state: LIVE
updated: 2026-10-21
current-answer: the whole note
related-designs:
  - ../../PLAN_Feature_Films.md — §2 the gaps, §4 the findings this note carries
  - ../../CAPABILITIES.md — the feature list the films show
-->

# NOTE — coordinator → behaviors: the demo sweep (CE-4035)

**Why you are getting this.** The user wants short films of every feature, shot automatically overnight. To plan them, the
coordinator wrote a `description` into every scenario header (**98 scenarios**, `What / Judge / Fail / Check` — the CE-3288 format).

⚠ **Cross-lane:** handoff D5 left your own scenarios' descriptions to you. They were written from each live check's asserts and the owning
design, as text only (one new key per file). Correct any you disagree with. The six that already had one were left alone.

**For when your lane is free — nothing here interrupts `sq-defend-house` work.**

## 1. Scenario changes the films need (PLAN §2)

| # | what | lean |
|---|---|---|
| G-1 | ⭐ **`ua-shootdown-eject`**: `ua-eject` with an AA gun under the route and **no eject task**, so the pilot ejects by `EjectionSystem` at 20 % health. 🔒 The user asked for exactly this. `ua-eject` today ORDERS the eject at 40 s | a new scenario; keep `ua-eject` (the ordered eject is a capability too) |
| G-3 + G-10 | weather and night change sensing — built (`PerceptionWeather`), never shown. Two identical observer/target pairs, one in a fog region; and a thermal sight on some unit kit | reuse `ua-weather-regions`' regions |
| G-5 | `ua-indirect-fire` plays by itself: the observer's `ObserveFire` and the battery's `Strike` in the file | in place; the check then only watches |
| G-6 | a car bomb: `TriggerIed` + a cargo charge (ST-9) has never been measured as one composition | `ua-car-bomb` |
| G-11 | `ua-weapon-systems`: an intruder in the M2's arc so it fires. The overview names a Kornet team the file does not have — add one, or fix the row | add the intruder |
| F-01 | `ua-weather-regions`: the IFV has no order; give it a move east | in place |

## 2. What the sweep found (PLAN §4)

| | finding |
|---|---|
| 🔴 | `ua-squad-danger-crossing-hold` shows **BreakContact, not Hold** (CE-2164's measurement) |
| 🔴 | `ua-three-hosts` no longer compares hosts — CE-3160 retired them, so the name and the overview row overstate it |
| ⚠ | not verified since a rework: `hill-attack`, `hill-attack-close` (before CE-2230) · `ua-tank-ammo` (causes fixed, not re-run) · `ua-civ-gathering`, `ua-civ-protest` (no live pass recorded) · `ua-eject` (check relaxed to 4.5 s, no pass recorded; CE-1118 still says "blocked on CE-2210", which is fixed) |
| ⚠ | **missing from `OVERVIEW_Rules_And_Demos.md`:** `ua-grenade-or-rifle`, `sl0-platoon-march`, `cc-platoon-bound`, `ld-sentry-surprise(-360)`, `bc-*`, `df-*`, `uf-*` |
| ⚠ | overview vs file: CE-2247 shown open (DONE in the tracker) · `ua-air-strike` "untouched" (the check asks only "not destroyed"; Mk-82 fragments hole a pickup at 120 m by design) · `uc-convoy-hit-ifv`'s Bradley is named "Convoy APC" |
| ⚠ | **not self-running**: the check drives the beat (health edits, side flips, orders) in `ua-posture`, `ua-three-hosts`, `ua-fire-distribution`, the `ua-casualty-care*`, `ld-sentry-surprise`, `cc-platoon-bound`, `ua-civ-neutral-gate`, `ua-indirect-fire`, every `*-dry`. Fine for a check, but a viewer who loads them sees nothing happen. Where a scenario could cause the beat itself, prefer that |
| ⚠ | `ua-grenade-or-rifle` has no live check (only the in-process rail) |
