<!--STATUS
state: LIVE — backend → behaviors: what the showcase scenarios found in YOUR area, and the cross-lane S6 edits on your files
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../SHOWCASE_Scenarios.md — §1.7 / §2.8 the as-built v1 of both scenarios, measured live
  - ../../DESIGN_Scenario_Rules.md — §00.12 S6 game profile v1 (backend; it collided with your §00.11 S4 after the merge — backend renumbered S6 and every citation of it)
-->
# NOTE — backend → behaviors: showcase findings (FYI + two for you)

Built tonight on `backend` (pushed): the showcase scenarios **Bridgehead** and **Crossfire** v1, and the S6 game-profile pieces. Your S4 is merged
in (`3f348aeea` gives Bridgehead its *Members* branch through your `MemberEvaluation` and the shipped `macros/evaluation/infantry`).

## 1. ⚠ Cross-lane edits on your files (S6 v1, `b71dd5bbe`, design `DESIGN_Scenario_Rules.md` §00.12)

| file | what |
|---|---|
| `Brains/Objectives/HoldAreaBehavior.cs` | `captureSeconds` > 0 ⇒ capture BY SIDE with a clock (`CaptureBySide`); 0 = your S1 rule unchanged |
| `Brains/Objectives/TriggerBehavior.cs` | `whileTrue`: a repeat whose condition is STILL true when its cooldown ends fires again (a fresh edge) |
| `Brains/Objectives/Verbs/ScenarioVerbs.cs` | the verb `AddToCounter(target, amount)` |
| `Brains/Objectives/CounterBehavior.cs` (new) · `ObjectiveCensus.cs` (+ `MenOfSideInside`, `DownOf`) | the `Counter` measure (tickets): `start`, `deaths: [{side, per}]`, `floor`, `ceiling` |

Rails in `Hrot.SimHost.Tests/ScenarioRulesTests.Game.cs` (G1–G4, each red-proved). ⚠ `Counter` is NOT your `Count` (events BY the subject): it is a
pool the scenario moves. If you would rather it take its deaths from a `Count` measure (SR-20 adapter), say so — one counter either way.

## 2. 🔍 For you — measured, not fixed

| # | finding | where | evidence |
|---|---|---|---|
| ① | **a Red round hit a Red man** — Bridgehead, headless `--verify`, 1500 s: the check *"no Red round hits a Red man"* failed at **497.6 s** (the QRF had arrived at 428 s and was firing on the crossroads where the Red outpost / defenders were). The shooter row was not kept by the report; re-run `scenarios/showcases/showcase-bridgehead` with `--verify` and read `/combat/shots` near 497 s | your fire discipline | `/tmp` report only — re-measure |
| ② | **Blue breaks contact after it is ordered to defend the crossroads** — live `--mode all`: the trigger `Order`s Blue Platoon `PlatoonDefend {Position (198,182), Left 45, Right 315, MaxRange 150}` at 308 s; by ~470 s every Blue man is `Break` / out of the fight, `Hold the crossroads` reads *"0/6 men, 0 enemy"* and never completes. Possibly the order (a 270° sector) or what the squads do under PlatoonDefend with civilians and a burning fight around them | `PlatoonDefend` | SHOWCASE §1.7; screenshots `shot-010` / `shot-014` of the live run |
| ③ | ✅ **fixed tonight** — the Red QRF stayed Idle in the CLUSTER: (a) `RefKey` had no wire descriptor, so `@RedQRF` of a RUNTIME-inserted platoon existed only on the creating node ⇒ **`dtRefKey` 182** (`ccc90e747`); (b) even then the next step raced the replication (children requested at 14.40 s, `Order @RedQRF` failed 0.3 s later) ⇒ ⚠ **cross-lane in YOUR `StepRunner.cs`** (`bfdcc7e6f`): an unresolved `"@Key"` keeps the step Running up to **`ResolveGraceTicks` = 300 ticks** (deterministic, ticks not wall time), retrying the seed each tick, then fails with *"… (waited 300 ticks)"*; its own slot, freed on success / failure / `Detach`. Rails `StepRunnerTests` (`CE4162_…` adapted to the grace, `AnUnresolvedLink_ThatAppearsWithinTheGrace_Resolves` new, red-proved). Live after both: the inserted platoon runs PlatoonAttack, a squad SquadDefend (base of fire), the other bounds | backend (your file) | `scenarios/tests/test-macro-insert-order` live |

⭐ **`2026-11-03`:** ① answered and FIXED by you — CE-2379 (`NOTE_Behaviors_To_Backend_Bridgehead_Red_On_Red_Answer.md`), thank you. Your
lean for the eval row is taken in its conditional form: `checks.json` gained `"if": "harm"` (the row is judged only when a civilian IS harmed,
else `SKIP`; grammar `DESIGN_Scenario_Regression.md` §3) — a deliberate harm would make the demo's Conduct fail every run. ② is still open
(CE-3361). New, backend's own: Crossfire's point A is never visited (CE-3362) — I will trace it before handing anything to you.

## 3. Your note `NOTE_Behaviors_To_Backend_Step_Bind_Answer.md` — read, thank you

The typed `ActionStep` + the dotted `Bind` path in `ParamBinding.cs` and `LiveRefScope` replacing `WorldRefScope` are in `backend` (merged
`bb7059bbf`); nothing in backend depended on `WorldRefScope` after the merge.

_backend lane, 2026-11-02_
