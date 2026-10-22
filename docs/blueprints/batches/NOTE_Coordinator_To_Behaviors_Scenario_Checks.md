<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: the whole note — §2 is who does what
related-designs:
  - ../../DESIGN_Scenario_Regression.md — the design: checks.json (§3), goldens (§4), which scenarios get one (§5), what it measured (§7)
-->

# NOTE — coordinator → behaviors: every scenario gets machine-checked conditions; who fixes what

🔒 **User, `2026-10-22`:** *"You are the lane to write checks (scenarios don't belong to a concrete lane). Note behaviors lane goes over
scenarios and fixing bugs, you might want to write a note for them about what you are doing and what you expect from them to avoid dual
fixes."* (R-4065, R-4066, [CE-4042](../Blueprint_Issues_Tracker.md))

## 1. What exists now (built, on `coordinator`)

| | |
|---|---|
| **`scenarios/<s>/checks.json`** | the scenario's Judge / Fail points as rules the engine judges on a stepped run in the editor — the take anchor grammar plus `field` (any component value), `where` on a shot/blast row, windows, `never`, `atEnd`, and `do` (the old live checks' hand edits). 📄 [design §3](../../DESIGN_Scenario_Regression.md) |
| **`scenarios/<s>/golden.json`** | a FEATURE scenario's recorded run (positions, health, a hash per component, every round and burst). ⛔ A DEMO has none (`"golden": false`) |
| **run one** | `dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode editor --headless --verify <s>` (exit 0 = pass) · View › Director › **Verify** · `POST /director/verify {scenario}` |
| **run all** | `scripts/scenario-regression.sh` — each scenario in a fresh process, `junit.xml` + a log each |
| the pilot | `ua-tank-ammo`, `bt-grenade-posture`, `ua-civ-melee`, `ua-civ-routine-alarm` (golden), `pc-defend-house` (demo) — all pass; the checks for the other ~98 are being written now |

## 2. ⭐ Who does what — so nothing is fixed twice

| | **coordinator** | **behaviors** |
|---|---|---|
| `checks.json` | ⭐ **writes and owns it**, from the scenario's header Judge/Fail and its live check | ⛔ do not edit it — if a condition is WRONG (it asserts what the design does not want), tell coordinator (a NOTE, or the CE-4042 row) |
| a red verdict | ⭐ files it (a NOTE to you naming the scenario, the condition, the time) and **does not touch the behaviour** | ⭐ **fixes the behaviour** — and says so in the commit (`verify <s> green`) |
| `golden.json` after a deliberate behaviour change | — | ⭐ **re-record it in the SAME commit as the change**: `--verify <s> --verify-capture`, then **read the diff** (TESTING_Harness_And_Goldens §4). ⛔ A golden re-recorded without its change is a lie |
| `golden.json` that went red and you did not mean it | — | ⭐ that is the regression the golden exists to catch — run it down (`"golden": {"every": 5, "full": true}` in a scratch copy shows every value) |
| a scenario's `scenario.json` | edits only a scenario's `description` | ⭐ owns the content, as today |
| new scenario | ⭐ writes its checks once you tell it exists | add the Judge/Fail text to the header (the checks are written from it) |

⚠ **One verify before you push a behaviour change** is cheap: a feature scenario verifies in **7–33 s** headless. The scenarios a change
touches are the ones whose `checks.json` names its units or behaviours.

## 3. What I will send you

Red verdicts as I write the checks, batched per NOTE, each with the condition, the time it held (or `heldOutsideItsWindowAt` — it
happened, but late), and the first golden difference. ⭐ **Already measured, nothing for you yet:** `pc-defend-house`'s Red now opens
fire at **86.9 s** (the film take baked 11.2 s before today's base-of-fire changes, R-2066) — if 86.9 s is intended, nothing to do; if
not, it is yours. The verifier also found and fixed **CE-4043** (`MissionAdapterState.LastPlanVersion` folded `string.GetHashCode()`,
which .NET randomises per process — cross-lane, one line in CGF).
