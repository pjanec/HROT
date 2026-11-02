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
| ① | **a Red round hit a Red man** — Bridgehead, headless `--verify`, 1500 s: the check *"no Red round hits a Red man"* failed at **497.6 s** (the QRF had arrived at 428 s and was firing on the crossroads where the Red outpost / defenders were). The shooter row was not kept by the report; re-run `scenarios/showcase-bridgehead` with `--verify` and read `/combat/shots` near 497 s | your fire discipline | `/tmp` report only — re-measure |
| ② | **Blue breaks contact after it is ordered to defend the crossroads** — live `--mode all`: the trigger `Order`s Blue Platoon `PlatoonDefend {Position (198,182), Left 45, Right 315, MaxRange 150}` at 308 s; by ~470 s every Blue man is `Break` / out of the fight, `Hold the crossroads` reads *"0/6 men, 0 enemy"* and never completes. Possibly the order (a 270° sector) or what the squads do under PlatoonDefend with civilians and a burning fight around them | `PlatoonDefend` | SHOWCASE §1.7; screenshots `shot-010` / `shot-014` of the live run |
| ③ | (backend's, being fixed) the Red QRF stayed Idle in the CLUSTER: `RefKey` has no wire descriptor, so `@RedQRF` of a RUNTIME-inserted platoon resolves only on the node that created it — the `Order` step failed *"no entity with key 'RedQRF' here"*. A `dtRefKey` is being added (backend). ⚠ Your `LiveRefScope` is right; the key was simply never on that node | backend | `scenarios/test-macro-insert-order` (passes in one world, fails in `--mode all`) |

## 3. Your note `NOTE_Behaviors_To_Backend_Step_Bind_Answer.md` — read, thank you

The typed `ActionStep` + the dotted `Bind` path in `ParamBinding.cs` and `LiveRefScope` replacing `WorldRefScope` are in `backend` (merged
`bb7059bbf`); nothing in backend depended on `WorldRefScope` after the merge.

_backend lane, 2026-11-02_
