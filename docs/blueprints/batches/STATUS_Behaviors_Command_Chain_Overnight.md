<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: §1 the items · §2 the gates · §3 decisions
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Command_Chain_Overnight.md — the batch (dispatched at d6a58138a)
  - STATUS_Behaviors_Command_Chain_Prereqs.md — part A's items, row by row
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Battalion_Test.md — CE-4615, the scenario this batch proves
-->

# STATUS — `behaviors`, the command chain overnight (CE-4615)

Scope frozen at `d6a58138a`. Started at `bb9366695` (merged `coordinator`), marker `b64adc669`.

## 1. The items

| part | row | state | where |
|---|---|---|---|
| A | CE-4614 `Strength` / `QuietArea` objectives | ✅ | Scenario Rules §10.2 · R-2093 |
| A | CE-4589 the battalion's reserve (`SupportCommand.Reserve`, order by echelon) | ✅ engine · HQ-15 showcase wiring → CE-2434 | Command Chain §8e as built · R-2094 |
| A | CE-4610 · CE-7037 · CE-4605 · CE-4606 · CE-4607 · CE-4608 · CE-7038 · CE-7035 civilian half | ⏳ | — |
| B | CE-7033 dynamism measure | ⏳ | — |
| C | CE-4615 generator + 3 scenarios + `run_cc_battalion` | ⏳ | — |
| D | live runs ×3 per variant, thresholds X / Y | ⏳ | — |

## 2. The gates

| command | `--no-build` | result |
|---|---|---|
| `dotnet build Hrot.SimHost.Tests / Fdp.Toolkits.Tests --no-restore` | — | 0 errors |
| `dotnet test Hrot.SimHost.Tests --filter ScenarioRulesTests` plain · `PAR=1 READ_CHECK=1 WORLD_GUARD=1` | ✓ | 141 / 141 · 141 / 141 |
| `… --filter SquadManeuverTests\|StressSystemTests\|CompanyBehavior\|SquadReport\|ScenarioRulesTests` `PAR=0` | ✓ | 423 / 423 |
| the same `PAR=1 + READ_CHECK + WORLD_GUARD` | ✓ | 422 / 423 — `StressSystemTests.CE2272_…` PRE-EXISTING (red at `0a4a4d364`, prereqs status §2) |
| `dotnet test Fdp.Toolkits.Tests --filter S0IdTypesRail` | ✓ | 16 / 18 — the duration rail FIXED here (8 bare floats, see §4); the two left (`NodeId`, `ExerciseId` in Orchestration / ReplayBrowser types) are not this lane's and predate this run |
| `… --filter ClearObstacle\|DroneOperator\|ShippedTree\|Golden` (after the SimDuration change) | ✓ | 36 / 36 |
| red-proofs | — | CE-4614: burst branch off · `Met` wrong · no deadline ⇒ each red. CE-4589: slot mask ignored · `DepthOf` one level ⇒ both red. All green after |
| `tracker-counts.py --check` | — | OK |

## 3. Decisions (lane decisions — for the user's review)

| id | decision | would change it |
|---|---|---|
| R-2093 | `Strength` takes a UNIT (a side = its top HQ); `QuietArea` counts a shot by the shooter's position, a hit by the victim's, a burst by where it lands | a side with no single HQ; fire passing over a sector meant to count |
| R-2094 | CE-4589's showcase wiring (HQ-15) deferred to CE-2434 — the defender-company macro has no company commander; the reserve is proved on `cc-battalion` | the user wanting the showcase wired in this lane |

## 4. Defects fixed on the way

| what | fix |
|---|---|
| `S0_EverySecondsTimeoutCooldownMember_IsNotABareFloat` red (8 members from recent behaviour work) | runtime `ClearObstacleParams.PushSeconds` / `DroneOperatorParams.IntervalSeconds` → `SimDuration` (same size); the six JSON-authored forms (`CompanyAttack.DeadlineSeconds`, `*Defend.ShootAndMoveSeconds` ×3, the two orders) added to the authored-form list |

## 5. Ids allocated

CE-2434 (HQ-15 showcase wiring). Rulings R-2093, R-2094 (recorded in their designs).
