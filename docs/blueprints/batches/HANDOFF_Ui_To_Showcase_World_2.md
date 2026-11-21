<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — THE design (§11 the rows, §12 the checks)
  - HANDOFF_Ui_To_Showcase_World.md — round 1 (CE-6001…CE-6011, mostly BUILT on `showcase-world`); this file continues it
  - HANDOFF_Ui_To_Showcase_Brains.md — the brains lane (same base, different files); CE-6018 ② ③ are its rows
  - NOTE_Coordinator_To_Ui_Threaded_Editor_Limitations.md — the switch settings every automated test uses
  - ../../DESIGN_Peek_And_Fire.md — §9.9 obstacle removal as built (CE-6003)
-->

# HANDOFF — ui → `showcase-world`, round 2: the world meets the parallel checks, and the findings of round 1 (CE-6000)

**For:** a new session on a VM (round 1's session has ended). **From:** ui. **Dispatched at `8b87f08a6`** (+ the commit carrying this file); ⛔ scope
FROZEN there — later documents are FYI; one that invalidates an item ⇒ stop THAT item (R-106).
🔒 User, `2026-11-13`: *"i also need the handoff and tasks to be prepared so well that they can be run overnight autonomously with NO QUESTIONS"*.

## 0. Start

| # | step |
|---|---|
| 0.1 | `git fetch origin showcase-world exp/threaded-build ui` · `git switch showcase-world` (track `origin/showcase-world`, head `45233b5e9` or later) · **`git merge origin/exp/threaded-build`** (≥ `a9955908e`) — ⭐ this merge IS CE-6012's first step; conflict rule §1a · `git merge origin/ui` · push · empty commit `chore: started showcase-world round 2 at <sha>` |
| 0.2 | ⭐ from now on this branch merges `exp/threaded-build` as it moves (⛔ SUPERSEDES round 1's "do not merge it") — the checks live there. Never rebase |
| 0.3 | ⛔ do NOT merge `showcase-world-roads` (CE-6020) unless its row says the user approved stage 2 |
| 0.4 | read RULINGS.md, the design, the coordinator note §3; your rows: Area S-W on `showcase-world` (CE-6000…6019 are there), new ids from `CE-6023` (`CE-6020`…`6022` are taken) |

## 1. Rules

- `.claude/CLAUDE.md` "BEHAVIOUR CODE RUNS IN PARALLEL" + the switches IN EVERY COMMAND (coordinator note §3): rails `HROT_PARALLEL_SIM=1` AND `=0`, with
  `HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`; anything HTTP / stepped / scenario: `HROT_THREADED_KERNEL=0 HROT_PARALLEL_SIM=1`; determinism:
  `python3 scripts/stepped-hash.py --kernel 0 --threads 1,4,8 <scenario>`. ⛔ Never work around the threaded editor's L-1…L-4.
- Hot path zero-alloc (R-2084) · new components unmanaged when recorded · T-1: the feature's own suite first · obligation ⑤ · no UI · no screenshots in git.
- ⭐ **Before each item** re-check: `git fetch origin 'refs/heads/exp/*:refs/remotes/origin/exp/*'`; a file you are about to edit that a NEWER `exp/*`
  branch (not yet in `exp/threaded-build`) also changes ⇒ that item is `⏸ blocked: <branch> changes <file>`, next item.

## 1a. AUTONOMY — NO QUESTIONS

⛔ Never ask, never pause. The ladder: ① the defaults below → ② the designs → ③ mirror the nearest existing thing → ④ the smaller blast radius →
⑤ record a lane default (`R-60xx`, *"lane default, user may override"*, + the design §10) and carry on.

| situation | do |
|---|---|
| a premise false | measure, fold into the design, continue |
| a user ruling would have to change (R-244 …) | ⛔ do not change it: build nothing for that part, write the measured case + the proposed fix in the row as **"for the user"**, next item |
| a red not yours | prove it pre-existing on a clean worktree at the base, note, continue |
| stuck: 3 hypotheses or ~90 min | a row with the measurements, `⏸ stuck`, next |
| merge conflict with `exp/threaded-build` | their side in their files; yours in `scenarios/showcases/`, `assets/terrain*`, your new files, Area S-W rows; a semantic clash in a shared file ⇒ keep theirs, re-apply yours on top, rails decide |
| a new id | the next free in the same range across every lane (`git grep` over `origin/*`) — permanent |

⭐ Push after each green item with its row updated; morning report at the end of `STATUS_Showcase_World.md`: done / blocked / stuck + every lane default.

## 2. The items — in order

| # | row | frame | decided defaults → done when |
|---|---|---|---|
| **1** | **CE-6012** the checks | after 0.1's merge: CE-6003's and CE-6005's rails, and every `scenarios/showcases/city-battle/features/*` rail, with the switches; stepped-hash on each feature scenario and on `showcases/city-battle/city-battle` | all green both ways; a red the checks raise is FIXED here (declare the read, move the write to the owner, a command) — it is this lane's code |
| **2** | **CE-6004** the debounce | `StaticObstacleBake.cs:52,167` (R-244, wall clock) — stepped-hash on `roadblock-blast` at 1,4,8, three runs each | EQUAL ⇒ close with the numbers. UNEQUAL ⇒ ⛔ R-244 is the user's: the measured case + the lean (stepped mode counts sim ticks) as "for the user", close as measured |
| **3** | **CE-6016** lethality knobs | the row's lean: ONE unmanaged recorded World Environment component `LethalitySettings { DroneEnduranceScale, ArmourScale }` (1.0 = TKB), read at `AirAttackBehaviors.cs:534` and the armour lookup, set by a scenario value and a trigger verb beside `SetSkillAmplifier`; the id in `GlobalComponentIds.cs` by the id rule | a feature rail: scale 0.5 halves the FPV's endurance and doubles damage taken |
| **4** | **CE-6013** an obstacle is not a man | the row's lean: `TerrainObstacles.IsObstacle` excludes it in `ObjectiveCensus.IsMan` (:24), `ManDowned`, and the hit tally `MeasuresEvidence.cs:90` — ⚠ a cross-lane edit of behaviours code: name it in the commit | blasting a roadblock leaves the objective measures and the civilian-harm count unchanged |
| **5** | **CE-6014** a vehicle swerves through a wall | the row's lean: RVO avoidance (`CarKinematicsSystem.ApplyCollisionAvoidance`) clamped to the vehicle's walkable nav layer — the same rule for every ground vehicle | `RoadblockBlastScenarioTests`: 3 / 3 runs the IFV passes the gate and never crosses the wall line |
| **6** | **CE-6018** ① ④ the scripted top | ① two trigger predicates: a side's strength below a share (census) and a sector quiet for N s (no shot heard) — new condition types beside `OnEntity` / `ScenarioTime` / `Compound` / `CountMatching`; ④ an `Order` whose `@Key` never resolves fails at 10 s as documented. ② ③ are the brains lane's (CE-7014 / CE-7028 / CE-7002 / CE-7010) — say so in the row | each predicate fires in a test scenario; the bad key fails at 10 s; the skeleton's time fallbacks for phase 6 replaced by the census predicate |
| **7** | **CE-6015** the hierarchy on SimHost and the Player | measure first who owns `EntityInfo` for a CGF-built hierarchy (`EntityInfoEgressTranslator.cs:124-160`); fix so `CommanderId` arrives once the commander is mapped — ⚠ cross-lane (network): name it | `pc-attack-dry` at 8 s: the links exist on all four hosts (15 / 15) |
| **8** | **CE-6017** a failed load reaches Live | measure the master after a failed prepare round; the twin helper (`ScenarioTwin.Live`) asserts a non-empty world — ⚠ cross-lane (backend's master): name it; if the master is wrong, fix it to stay out of Live and say so | the GUID-miss skeleton: the twin reports the failure, never 60 s on empty worlds |
| **9** | **CE-6019** speed at size | measure only: `showcases/city-battle/city-battle`, Release, per node, `HROT_THREADED_KERNEL=0 HROT_PARALLEL_SIM=1` at 1 / 4 threads, recording ON; append the numbers to the row for the parallel programme | numbers in the row; ⛔ no optimisation here |

⏸ **Not this run:** **CE-6020** stage 2 (regenerate the town from the plan traced from Týn, on `showcase-world-roads`) — ⛔ waits for the user's approval,
file its row at the end of S-W if it has none · **CE-6022** — the Windows session's (`HANDOFF_Ui_To_Windows_OSM_Roads.md`).

## 3. Report — the gate contract

Per item in its row and `STATUS_Showcase_World.md`: verbatim commands WITH switches · counts · `--no-build` (build the TEST project) · the feature's own
suite first · stepped-hash per thread count · reds proved pre-existing at the base sha · clean tree · `tracker-counts.py --check` · ids · design updated.
⛔ No broad re-runs; no full-solution build in the loop; slow runs in the background, never polled.
