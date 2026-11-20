<!--STATUS
state: LIVE
updated: 2026-11-20
current-answer: §1 where we are · §2 open items with leans · §3 how to work
stale-below: nothing
related-designs:
  - batches/STATUS_AggBehaviors.md — the lane's running status table (the realism rows at its tail)
  - batches/DECISIONS_Behaviors.md — D-13 (prone on a dispersal spot), D-14 (one line-of-fire rule), D-15 (suppress)
  - ../DESIGN_Decision_Layer.md — §3.3g′ the suppressor (D-15, CE-8311/8313/8314)
  - ../DESIGN_Peek_And_Fire.md — §11.6b the blind aim through a building's opening (CE-8315)
  - ../DESIGN_Scenario_Regression.md — §3 the cue grammar (`upright`, CE-8316)
  - ../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — D-2m coverage eye (CE-8312), §8f D-2n prone
-->

# RESUME — `exp/agg-behaviors`: realism of the infantry fight scenarios

⭐ Run `RELEARN` first (`bash scripts/session-design-brief.sh`), then read this. Lane `exp/agg-behaviors`; ids from **CE-8300…8599**, next free **`CE-8317`** (the tracker header row).

## 1. Where we are *(HEAD `3f547d727`, 2026-11-20)*

**The ask (user):** *"assess the realism of the soldier behaviors in the scenarios and fix the issues … then we can re-record the goldens."*

**📐 The 24-scenario fight set: 20 pass.** Every regression D-15 introduced is fixed. The four reds all predate D-15 (§2).

| built | what | sha |
|---|---|---|
| D-13 prone half | a man on a dispersal (open) post lies prone | `20f2e6e8e` |
| CE-8308 / D-14 | one friend/civilian line-of-fire rule, with the spread | `0c9b89871` |
| CE-8309 | a parked car stays in the navmesh (walkers on car roofs) | `96ca1a82f` |
| CE-8310 | a weapon far past its range is no choice (pistol at 112 m) | `8545c269f` |
| CE-8312 | the guard's coverage fan looks from a frame post's opening | `da2b9a957` |
| **D-15 / CE-8311** | a suppressor runs crouched to a FIRING POSITION on the enemy's last-known spot, then takes the LOWEST stance whose line reaches the round's aim point; never stops firing | `6e8c57ad3`, `885dc6637` |
| CE-8313 | a POSTED man suppresses from his post, in its stance | `adceb42a2` |
| CE-8314 | ONE placement (`PostureNodes.PlaceSuppressor`) for both suppressors — the posture and the squad-ordered `SuppressContact` (8 m reach) | `21b5a581c` |
| CE-8315 | `AimPoint.BlindAt`: a blind round at an unseen man inside a building goes through the nearest opening on his storey (cross-lane, FDP Combat) | `21b5a581c` |
| CE-8316 | cover cue `"upright": true` — a prone man is not "standing in the open" (cross-lane, the verifier) | `0ab835ac9` |
| content | bt-doors, bc-room-clear, ia-house-hunt, ts-house-defenders, uf-alone-under-fire, pc-ambush-bypass checks/scenario fixed to the model | `9c18f70b2`, `79cb61eb0`, `f89771dee` |

⛔ **D-15's history, so nobody repeats it:** cut 1 used the take-cover cycle — it fires blind only on evidence ≤ 5 s old and **silenced every suppressor**. Cut 2 ran to COVER crouched — the cover hid him from the spot and **his rounds struck it**. Cut 3 (built) uses a firing position + the lowest clearing stance, judged on the round's OWN aim point.

## 2. Open — each with a lean

| item | what is measured | ⭐ lean |
|---|---|---|
| **Goldens** | 20 passing scenarios show GOLDEN "different" (expected after these changes) | ⭐ the user decides; lean: re-record the 20 green now (`--verify-capture`, each must come back SAME in a second fresh process), leave the 4 red |
| `ia-area-defend` row (1) | the guard fires on Blue East only at ~231 s, after he is within 30 m (89.75 s) | ⭐ dump it; check the lone guard's round (CE-2373 / D-2m: `SquadDefendPlay.Round`, `CoveragePosts.NextRound`) — which posts the round visits and whether one sees the east approach |
| `pc-urban-ambush` | the clearing squad stalls ~40 m short of H5 | ⭐ UrbanMovement / ClearBuilding play territory — dump at `every=5` (1 s runs out of memory) |
| `pc-ambush-bypass` | the bypass works; a straggler idles 40–60 s after his CombatPosture order completes, so "the movement ends" never fires | ⭐ the UrbanMovement play's completion — runFor 300 did not help (reverted to 200) |
| `bt-window-duel` "bounds to Van 2" | the street rifleman never changes cover | ⭐ pre-existing since before CE-8308; look at why the attack approach never picks Van 2 |
| `FireAtPointExecutor` | a squad's REPORTED point is still aimed plain (no opening rule) | ⭐ route it through `AimPoint.BlindAt` too (one blind aim) — small |
| base reds, not ours | `StressSystemTests` CE3462/CE2272 under the parallel checks; `PersonHandsTests.CE3248` | ⛔ do not chase on this lane |

## 3. How to work here

| | |
|---|---|
| scenario run | `OUT=<dir> NO_BUILD=1 scripts/scenario-regression.sh <name>` — ONE name per call; two at a time (4 cores); rebuild `Hrot/Runner/Hrot.ClusterRunner` first |
| full dump | `scratchpad/dump.sh <scenario> [every]` — `HROT_VERIFY_REVIEW` → shots + per-second component snapshots (rebuild the script if the scratchpad was wiped: it sets `HROT_VERIFY_REVIEW={"At":100000,"Straight":true,"RunOut":"<json>","Every":N}` and `FDP_STAGING_ROOT`) |
| behaviour rails | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --no-build --filter "TacticsTreesTests\|PeekAndFireNodesTests\|SquadManeuverTests\|CombatPosture\|SuppressNodesTests\|FireAndMovement\|SquadTactics\|BasicInfantrySop"` — 541/0 at HEAD |
| weapon rails | `Fdp.Toolkits.Tests` filter `AimAndFire\|AimPoint\|FireProcessing\|HiddenTarget\|LineOfFire\|FireAtPoint` — 198/0 |
| ⚠ traps met | a red-proof mutation that does not COMPILE (CS0162 is an error here) runs the stale binary and "passes" — use a compiling mutation; after a merge, `dotnet restore` the test projects (Presentation.Tests failed on `VisualFamily` until restored) |
| bisect | a clean worktree at a commit (`git worktree add -f <dir> <sha>`, full build) answered "did D-15 cause it?" in one run |
