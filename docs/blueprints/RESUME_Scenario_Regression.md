<!--STATUS
state: LIVE
updated: 2026-10-22
current-answer: §1 (in flight) then §2 (what to do next)
known-rot: ⚠ §1 is STALE — the post-R-3039 re-record landed 2026-10-23 (8e84265f…39aab237); §2 rows 1–6 remain the open list. Coordinator replay work continues in RESUME_Coordinator_Replay.md
stale-below: nothing yet
related-designs:
  - ../DESIGN_Scenario_Regression.md — THE design (UML, grammar, the golden, measured, build steps, grammar gaps); this file is state only
  - ../TESTING_Harness_And_Goldens.md — §4: the lane making a change re-records the goldens it moves
-->

# RESUME — Scenario regression (CE-4042), coordinator lane

RELEARN

⭐ **What this is:** every scenario is judged by machine in the editor's stepped preview (`scenarios/<s>/checks.json`), and a feature
scenario is also compared with its recorded `golden.json`. Each scenario runs in a FRESH process. The engine owns it, with HTTP and
command-line front ends (R-4065, R-4066). **The design is [DESIGN_Scenario_Regression.md](../DESIGN_Scenario_Regression.md)**: read
§3 (grammar), §4 (golden), §7 (measured), §9 (gaps). This file holds only what is in flight. ⚠ Verify every sha below against git
before acting on it.

| | |
|---|---|
| branch | `coordinator` (HEAD at writing `641a386bd`; merged `backend@c57376c39`, `behaviors@48de25f88`, `ui@a3f940bcc`) |
| tracker | [CE-4042](Blueprint_Issues_Tracker.md) (open, slice 1 BUILT) · CE-4043 ✅ · CE-4044 ✅ (backend) · coordinator next free **CE-4045**, **R-4067** |
| rulings | R-4065 (conditions for all, a golden for feature scenarios, a fresh process each) · R-4066 (engine-owned; HTTP + CLI, not Python) |
| user's words | *"Verify button ok, per scenario"* · *"Nightly … startable from command line (imagine github running this)"* · *"You are the lane to write checks"* · *"Verify and re-record golden must be accessible from ai debug http as well as from command line"* · *"backend is solving determinism issues so hand those to them"* |

## 1. ⏳ In flight at writing

| what | state | when it lands |
|---|---|---|
| ✅ **DONE and committed** — 62/62 re-recorded (`ua-casualty-care-heavy` failed, §2 row 2); ⏭ the story diff (②) is NOT run yet. **re-record of 62 + 1 goldens** after merging backend's R-3039 S1 (system memory moved onto entities ⇒ new components such as the channels' `DispatchedAction`, `SquadMemberSense`; `MissionPlanQueue` hashes moved) | background, two streams, out `…/scratchpad/r-a`, `r-b`. The +1 is `ua-casualty-care-heavy`, which tests the non-finite fix | ① commit the goldens; ② run `scratchpad/tools/storydiff.py <recap.txt>` (compares old vs new golden on positions / health / times / shots / blasts, ignoring component hashes) to split **"components only"** from **"behaviour MOVED"**; ③ hand every MOVED one to its lane |
| ⚠ **scratchpad is session-only** | the `storydiff.py` logic is three lines: `git show HEAD:scenarios/<s>/golden.json` vs the new file; compare `steps/times/shots/blasts` and each snapshot's `position`/`health` (tolerance 1e-3) | rewrite it if the session is gone |

## 2. ⏭ Next — in order

| # | item | owner | basis |
|---|---|---|---|
| 1 | after §1: report to **backend**, whose merge moved the goldens, which scenarios' goldens changed in components only vs in behaviour. ⭐ Already known as MOVED from the verify-only run: `ua-tank-ammo` (Rifleman 1's 0.23 s opening shot gone; the soft round at the technical at 51.07 s, outside its window ⇒ **red**), `df-lone-soldier-reflex`, `ua-ifv-standoff`, `ua-javelin-lock`, `ua-tank-corner`. Also red after the merge: `uc-convoy-hit-ifv` ("the Leader stays aboard" fails at 44 s). ⚠ The merge range holds BOTH backend (R-3039 S1/S4, R-3048, CE-3299) and behaviors (CE-3306 enemy-in-sight on the perception clock, CE-2352 exposure vs sight range) — attribute by bisecting the two merges, not by guess | coordinator measures, lanes fix | DESIGN §7; TESTING_Harness §4 |
| 2 | ⛔ **STILL FAILS after `641a386bd`** — the re-record run (with the non-finite-by-name fix built in) failed the same way at step 256 ⇒ the throw is NOT in `ScenarioGolden`'s hash/write path. Next suspect: the dump itself (`DebugApiService.DumpEntity` serialising a component), or `ConditionBook`/the report write — get the stack trace (the runner's catch drops it: log `e.ToString()`). ⚠ **which component held +∞** (`ua-casualty-care-heavy`, step 256): run a scratch copy with `"golden": {"every": 1, "full": true}` and search for `"Infinity"`. Then tell its owner, and check whether `GET /entities/{id}` (the debug API's own JSON write) throws on it too | coordinator → owner | — |
| 3 | `hill-attack` (not `-close`): fires only at 196 s, so "back at the baseline (x < 540)" cannot hold by 200 s ⇒ raise `runFor`, or tell behaviors the full variant is slow. `-close` PASSES since CE-2352 | coordinator (check) / behaviors | behaviors' reply §3 |
| 4 | update DESIGN §7 + the CE-4042 row with the post-R-3039 numbers | coordinator | — |
| 5 | §9 grammar gaps — ⏳ **awaiting the user's nod** (lean: a route-call `do`; a `decision` cue on `UtilityDecisionLog`; cues by side/kind; `any`) | user decides | DESIGN §9 |
| 6 | switching on the CI template (`docs/ci/scenario-regression.workflow.yml`) — ⛔ **the user's call** (runner minutes) | user | DESIGN §8 ⑪ |

## 3. Open with other lanes

| note (all in `batches/`) | to | waiting on |
|---|---|---|
| `NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings` + `_2` | behaviors | the editor reds, filed by them as **CE-2352** (hill-attack ✅ fixed; rpg-window = a race, see next row; posture = designed) |
| `NOTE_Coordinator_To_Behaviors_Scenario_Check_Findings_Reply_2` | behaviors | ⭐ `ua-rpg-window`: I kept "an IFV fires back" and asked for **sturdier IFVs** (content); `ua-posture`'s "fights again" returns if they give it a sturdier Hostile |
| `NOTE_Coordinator_To_Behaviors_Scenario_Regression_After_Merge` | behaviors | `uc-reinforce-dry`: reserve sent at 40.5 s before the merge, never after (lean: R-2071 E-1, unmeasured) |
| `NOTE_Coordinator_To_Backend_Determinism_Reply` §3–§4 | backend | `GhostProtocolTests` TIME-budget test relayed; R-3039's traffic lean **accepted** (a restore restarts traffic) |
| behaviors' `NOTE_Behaviors_To_Coordinator_Scenario_Check_Findings_Reply` | me | answered (Reply_2) |

## 4. How to run it (do not re-derive)

```bash
R=Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll
dotnet restore Hrot/Runner/Hrot.ClusterRunner && dotnet build Hrot/Runner/Hrot.ClusterRunner --no-restore   # ⚠ restore after any merge
dotnet $R --mode editor --headless --verify <a,b|all> --verify-out <dir> [--verify-capture] [--verify-timeout 60]
scripts/scenario-regression.sh [a,b|all]                       # the nightly: summary.json + junit.xml + a log and a report per scenario
POST /director/verify {"scenario": "x", "capture": true, "fresh": true}   ;  GET /director/verify/jobs/{job}
quick-check.sh Hrot/Subsystems/Hrot.Editor.Tests/Hrot.Editor.Tests.csproj "FullyQualifiedName~Hrot.Editor.Tests.Director"   # 15 rails
```

| ⛔ traps, all measured this session | |
|---|---|
| a report's `passed` is FALSE when only the golden differs | select re-records on `golden == "different"` **and all verdicts ok**. 📌 Filtering on `passed` produced an EMPTY list, and `--verify ""` booted a plain editor that never exits |
| the golden diff report lists the first SNAPSHOT first, then times / shots / blasts | "the first differing snapshot is at t=0 in a channel component" does NOT mean the story is unchanged — read the `times:`/`shots:` lines, or run storydiff |
| a capture of a FAILING run is refused | its older golden stays. When the behaviour is fixed, that golden differs on everything since; the fixing commit re-records it |
| `uc-urban-battle` steps ~9× slower than real time (~30 min) | give it its own stream; `--verify-timeout` defaults to 60 |
| parallel parents | ✅ safe now: each child has a private `FDP_STAGING_ROOT`. ⛔ Never `pkill -f Hrot.ClusterRunner` from Bash: the pattern matches the shell's own command line and kills it (exit 144). Kill by PID from `pgrep -f "dotnet Hrot/Runner"` |
| a container restart kills background runs | only the reports already written survive; re-run the scenarios that have no `<s>.json` |

## 5. Done this session (for the record — the commits say more)

- **Built:** the verifier, the Director panel's Verify, the nightly (JUnit), fresh-process verify over HTTP, checks for all 103 scenarios.
- **Golden behaviour:** handles are hashed by meaning (`"[i, vG]"` → `"@name"`); a non-finite number is written by name.
- **Process isolation:** each child gets a private staging root, and `--verify-timeout` limits its wall clock.
- **Full runs:** 70/103 at first → 86/102 → **88/103** on the merged tree.
- **Determinism:** 61 goldens came out byte-identical across a merge. The first regression caught across a merge was `uc-reinforce-dry`.
