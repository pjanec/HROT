<!--STATUS
state: LIVE — QUEUED: start when the animals batch (CE-4623) is reported — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §3 the checks grammar, §4 the golden, §9 how the grammar grows; `scripts/scenario-regression.sh`
  - ../../designs/animals/DESIGN_Animals.md — the animals (AB-1…AB-10 as built, §9)
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — the plays IM-1 / IM-2 / IM-5 (§9 as built), the dash (§9a)
-->

# HANDOFF — coordinator → `backend`: FEATURE-TEST SCENARIOS for animals and motorcycles (R-4277; CE-4623, CE-4621)

**For:** the `backend` lane, AFTER its animals report. **From:** coordinator. **Dispatched at `<STAMP>`**; ⛔ scope FROZEN there.

🔒 User, `2026-11-16`, verbatim: *"Without the scenario passing, i don't believe that the feature works because in the past every such scenario
development revealed a lot of issues. Those scenarios should work like a regression test - their description should clearly describe what they
are all about and what is expected. Maybe event better if this is expressed as objectives so it is testable by machine"* — R-4277, CLAUDE.md
*"A FEATURE WORKS ONLY WHEN ITS SCENARIOS PASS"*.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait; decide by the claim table and record an `R-3xxx` lane decision; a failed premise stops ONE scenario, never the batch
(R-106); ⭐ **a scenario that fails because the FEATURE is wrong is the point of this batch — fix the feature where it lives, with a rail in its
own suite, and say so**; commit, push and update `STATUS_Backend_Feature_Scenarios.md` after each scenario.

## 0. Start
Merge `origin/coordinator`; `dotnet restore` the runner; push `chore: started feature scenarios at <sha>`.

## 1. Every scenario
`scenarios/demos/<area>/<name>/` with `scenario.json` (a `description`: **What** happens · **Judge**: the numbered expectations), `checks.json`
(one `expect` row per Judge line — `field` / `near` / `task` / `health` / `shot` / `blast` cues, `after`, `never`, `atEnd`), `golden.json`.
DONE = `scripts/scenario-regression.sh <name>` passes three seeds in a row and is equal at 1 / 4 / 8 threads. A cue the grammar cannot say ⇒
extend the grammar (§9), ⛔ never a check read from a log. Mark each in `docs/CAPABILITIES.md` (§3a, §9) with ✅ / ⚠.

## 2. Build first — the pillion's THROW
`DriveBy` (IM-1) fires only; the design says *"fires a burst (or throws a grenade)"*. Add the throw variant (a `DriveByParams` choice) through the
existing throw path (`HiddenTargetThrow`, the grenade arc), with a rail in `DriveByBehaviorTests`.

## 3. The scenarios
| name | What · the Judge lines (each an `expect` row) |
|---|---|
| `ua-moto-drone-dash` (+ `-offroad`) | bikes dash across open ground past two FPV operators · riders break hard when a drone dives · misses land outside the lethal radius in the measured share (R-4275) · survivors dismount at the far edge |
| `ua-moto-drive-by` | a two-up bike passes a sentry post as traffic · the pillion fires a burst while passing · the post is hit · the bike escapes and is past 300 m within N s |
| `ua-moto-drive-by-grenade` | the same with a throw · one `blast` at the post · the bike escapes |
| `ua-moto-parked-bomb` | a bike parks at a market · the riders walk away beyond the blast radius · the charge goes off on its timer · the riders unhurt |
| `ua-moto-swarm-raid` | six bikes close on a post from six bearings · all arrive within the phase window · fight · scatter on divergent bearings to the rally point |
| `ua-animal-guard-dog` | a patrol enters a farmyard · the dog barks at the first man on its ground · bites one who comes close · is marked Dangerous · (ROE-dependent) shot or avoided |
| `ua-animal-horse-rider` | riders fire from horseback at a gallop · hits on the target · the horse stays under its rider |
| `ua-animal-flock-flush` | a hidden ambush party moves through an orchard · the flock lifts off and the birdsong stops BEFORE the first shot · the patrol's hearing registers it |
| `ua-animal-herd-road` | a herd on the road stops a convoy and SUMO traffic · a tank's approach makes it flee · the road clears · the convoy goes on |
| `ua-animal-village-fight` | a firefight among livestock · animals hit are drawn dead (no wreck) · animals and civilians panic together · no animal is ever a target |

## 4. Gates and report
Each feature's own suites stay green (T-1); `stepped-hash.py --threads 1,4,8` on each scenario; the report names every defect a scenario found and
where it was fixed (design folded, obligation ⑤), the gate-report contract (rule 8), ids from your block.
