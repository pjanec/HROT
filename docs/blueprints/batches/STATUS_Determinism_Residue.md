<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: the item table, then each item's section and the gate table
stale-below: none
related-designs:
  - docs/DESIGN_Parallel_Simulation.md — §18.2c owns the rules under the brain phase (D-1 as-built) and the per-entity rule
  - docs/DESIGN_Semantic_Value_Types.md — §2.4 owns SimInstant / SimDuration, incl. their non-finite JSON (D-2 as-built)
  - docs/DESIGN_Entity_Control.md — §8e owns the operator's fire press (D-3: a press during a reload is dropped, CE-4344)
-->

# STATUS — Determinism residue (CE-3448 · CE-3441 · CE-3447 · CE-3443 · D-5 · CE-6004)

Handoff: [`HANDOFF_Coordinator_To_Backend_Determinism_Residue.md`](HANDOFF_Coordinator_To_Backend_Determinism_Residue.md), dispatched at
`885b083c7`, scope frozen there. Branch `backend`, started at `5d8285c0c` (backend = `coordinator@d01ed2df5`).

| item | tracker | state | result |
|---|---|---|---|
| D-1 | CE-3448 | ✅ `65dda2325` + `34f7e9fc9` | the 26: **0 designed lag · 9 ② lost refusal reason · 17 ① harness defect**; the survey then found 5 verbs writing another entity and 9 undeclared rule reads — fixed. Rule suites 190/190 serial, parallel, parallel + read check |
| D-2 | CE-3441, CE-1471 | ✅ `6b3046960`, `78ce1b04c` | `"-inf"` / `"+inf"` / `"nan"` markers, round-trip rail; none of the 12 combat / squad scenarios stops on ±∞ any more |
| D-3 | CE-3447 | ✅ `cce190bd0` | not a regression: the rail was stale since CE-4344 (a press during a reload is DROPPED, user 2026-11-08); proven by mutation, rail re-pointed |
| D-4 | CE-3443 | ✅ `65dda2325` | `ParamUsage.Rename` sets a new instance; rail red-proved |
| D-5 | CE-3450 | see below | |
| D-6 | CE-6004 | see below | |

Ids allocated: **CE-3449** (five goldens stale at t = 0), **CE-3450** (the D-5 survey findings).

## D-1 — the rules under the parallel brain phase (CE-3448)

| group | n | cause | fix |
|---|---|---|---|
| ② refusal reason | 9 | `ActionFailure.Set` in the phase deferred the add; the trigger read it in the same step ⇒ *"an action failed"* | a per-thread pending reason `TryGet` serves until the add lands — one path for every verb |
| ① harness | 17 | `ObjWorld` advanced the world version only `WithHistory` ⇒ the last-tick view (copied by changed chunk) stayed at tick one | the harness ticks the version and takes the view each tick, as the kernel does |
| survey (`HROT_READ_CHECK=2`) | — | 9 rule types read on other rules undeclared; `AddToCounter`, `SetObjectiveActive`, `BeginPhase`, `EndExercise`, `SetSkillAmplifier` wrote another entity in place | declared in `AiBrainReads`; `RuleAfterPhase.Run` — the one serial path (now, or a follow-up in batch order on the live state); `Message` uses it too; the rules' tree prepared by `AiBrainReads.Contribute` for every host |

⭐ No case was a designed one-tick lag: once the harness was right, every rail holds its serial assertion under the phase. Permanent rail:
`DrillsUnderTheReadCheckTests.TheRules_AsTheParallelPhase_MakeNoUndeclaredCrossReadOrWrite` (a child run with the check on).

## D-3 — the CE-1078 press rail (CE-3447)

Proof instead of a bisect (one build, not ~10): CE-4344's drop disabled at HEAD ⇒ the old rail **green**; CE-4344 is in **both** parents of
the coordinator merge ⇒ the merge was not the cause; red with every threading switch off ⇒ not a parallel defect. The rail now asserts the
user's CE-4344 rule.

## D-2 — the regression on the 12 combat / squad scenarios

`scenario-regression.sh` (Debug, defaults ON) at `cce190bd0`: **none stops on ±∞**. 3 pass (`cc-platoon-bound`, `pc-attack-dry`,
`uc-a10-zu23-dry`); 5 pass every check but differ from their golden at t = 0 (`bc-house-clear`, `bc-room-clear`, `bt-grenade-posture`,
`bt-mortar-roof`, `df-civilian-ordered` → **CE-3449**); 4 fail checks — the same with every threading switch off, so not parallel, each already
a row: `bc-house-defended` (CE-2261), `bt-doors` (CE-3113 / CE-3369), `bt-window-duel` (CE-3342), `pc-ambush-bypass` (CE-2284 / CE-2352).

## D-5 — the survey and the stepped gate on `showcases/city-battle` (CE-3450)

| | result |
|---|---|
| survey (`HROT_READ_CHECK=2`) — fixed `a0c863892` | staff casevac's `Health` / `InjuryThresholds` (stable reads of the staff phase); `PhysicsCollider` (hull radius) and `Gesture` in `BrainReads` |
| `Gesture` (the handoff's question) | ⭐ read on another man by `ThreatDanger.ShowsHandsUp` (threat ranking, the weapon's hold, two crowd brains), written in the phase by `CivilianBehavior` and the weapon's warning ⇒ declared |
| left in the survey | `ObjectiveStatus` / `EntityGuid` / `RefKey` through the DECLARED path for an entity with no row in the tick-start view (created this tick) — the known live fallback D-X3b, not an undeclared read |
| stepped gate 1,4,8 | 1 vs 4 EQUAL but `ResolvedStyle` (the editor's display style — not sim state); **1 vs 8 DIFFERS** from the first 10 steps (`UnitRoster`, `UnitSubordinate`, `SquadLeadership`, `PartMetadata`, `IsEmbarkedTag`, sensors; 23 types, 1 636 positions at 120 steps); **8 vs 8 EQUAL** ⇒ thread-count dependent, deterministic, changes between 4 and 8 (4-core VM) — ⏳ open, CE-3450 |
| ⚠ stale-binary trap | the gate runs `bin/Release`; the first run used a `2026-11-13` build (every step faulted on the X-6 defect) — void, rebuilt; ⭐ rebuild Release before any gate run |

## D-6 — CE-6004 measured, R-244 untouched

`roadblock-blast`, 60 s of sim time (the default 2 s never reaches the blast), the scripted orders removed: **1 / 4 / 8 EQUAL in 2 rounds**
(6 runs; the barrier removed in each). Caveat: the hash covers unmanaged components; the rebake's navmesh is outside it (its effect — positions —
was equal). Written into the CE-6004 row with the lean (sim time) for the user.

## Gates

| gate | command | result |
|---|---|---|
| rule suites (T-1 for D-1) | `dotnet test Hrot.SimHost.Tests --no-build --filter "…Objective\|…Rule\|…Trigger"` serial / `HROT_PARALLEL_SIM=1` / `+HROT_READ_CHECK=1` / `=2` survey | 190/190 · 190/190 · 190/190 · survey empty |
| D-1 red-proofs | `ActionComponents.cs` reverted ⇒ 9 refusal reds + the child rail red · a direct write in `AddToCounter` ⇒ `TheRules_*` red | ✅ both, restored, green |
| D-2 | `SimTimeJsonTests` (7) · `DebugApiNonFiniteTimeTests` (1) | 7/7 · 1/1; red-proof (marker dropped) 4 red |
| D-3 | `DebugApiOrdersTests` (Hrot.ClusterRunner.Integration.Tests) | 17/17; red-proof (CE-4344's drop off) ⇒ the rail red (28 vs 29) |
| D-4 | `ParamUsageTests` | 6/6; red-proof (no copy) ⇒ CE3443 red |
| D-5 code | `Fdp.Toolkits.Tests` Staff\|SquadCoordination serial + parallel · `Hrot.SimHost.Tests` DrillsUnderTheReadCheck\|EmbarkNodes\|ParallelBrainsDeterminism | 24/24 · 24/24 · 32/32 |
| scenario regression | `scenario-regression.sh <12>` | 3 pass / 5 golden-only (CE-3449) / 4 pre-existing rows |
| tracker | `python3 scripts/tracker-counts.py --check` | OK |
| allocation | no new per-frame allocation: `RuleAfterPhase` allocates a closure per verb RUN (a rule's edge), never per frame | — |
| not run | full `Hrot.SimHost.Tests` / `Fdp.Toolkits.Tests`, the E2E system suite | T3, nightly |

⚠ Pre-existing, not this batch's: `DrillsUnderTheReadCheckTests` takes ~15 s per child run (it is a child `dotnet test`).
