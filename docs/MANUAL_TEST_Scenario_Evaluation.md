<!--STATUS
state: LIVE — the manual test guide for objectives, triggers, scores, macros and their screens: one row per feature, how to try it by hand, what you should see
updated: 2026-11-03 (behaviors: A1, A2, A6 data half, B1 written and run live; skeleton by coordinator; each owning lane writes and LIVE-VERIFIES its rows' steps — HANDOFF_Coordinator_To_*_Rules_Runnable.md)
current-answer: §2 the feature rows
stale-below: nothing
known-rot: rows marked "steps: owner to write" carry no verified steps yet
related-designs:
  - docs/EXPLAINER_Scenario_Evaluation_And_Direction.md — HOW each feature works (this file is only HOW TO TRY IT)
  - docs/SHOWCASE_Scenarios.md — the two scenarios almost every row runs on (Bridgehead = training, Crossfire = game)
  - docs/DESIGN_Scenario_Rules.md · docs/DESIGN_Rules_View_UI.md · docs/DESIGN_Player_HUD_And_Debrief.md · docs/DESIGN_Scenario_Macros.md · docs/DESIGN_Macro_Authoring_Editor.md · docs/DESIGN_Node_Features.md — the owning designs
-->

# MANUAL TEST — **objectives, triggers, scores, macros and their screens**

⭐ For a person at a desktop: **start this, open that, do this, you should see that.** Each row's steps are written and **run live** by the lane that
owns the feature; a row with no verified steps says so. ⛔ No screenshots in git — a row may name the rail that renders one.

## 1. Starting *(backend; verify with §1.4)*

### 1.1 Build once *(a fresh checkout)*
```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj
```

### 1.2 Start the whole cluster with a scenario LIVE — ONE command
```bash
./run_All.sh --scenario showcase-crossfire --play       # the game   (Windows: run_All.bat --scenario showcase-crossfire --play)
./run_All.sh --scenario showcase-bridgehead --play      # the training
./run_All.sh                                            # no scenario: an empty cluster, load one from the editor
```
⭐ One process runs every node (orchestrator, SimHost, the player / IG, ExCon, CGF, traffic). `--play` = load LIVE and run; `--scenario <name> --load
edit` would open it for editing instead. Options after the domain pass straight to the runner (`./run_All.sh 3 --scenario …` uses DDS domain 3).
⚠ `run_All.bat`'s option forwarding is written but not yet run on Windows.

| window | what it is |
|---|---|
| the **editor / instructor** window | the map, *Window ▸ Rules*, *Window ▸ Mission summary* / *Game summary*, the cluster toolbar |
| the **player** window (IG) | the 3-D view and the player HUD |

### 1.3 Take a player seat
On the map, **right-click a soldier ▸ Possess** — the player window drives him (first person, the HUD shows his side's data).
Over HTTP: `POST /entities/{networkId}/possess`.

### 1.4 Check it is running *(the debug API: start with `HROT_DEBUG_API_PORT=8111` in front of the command)*
```bash
curl -s localhost:8111/status          # ⚠ localhost, never 127.0.0.1 (RUNBOOK §2.1): clusterState "OperatingLive", simTime rising
curl -s localhost:8111/objectives      # every objective / trigger / score row
curl -s localhost:8111/features        # per node: which HUD elements are on, and which layer set them ("scenario" = the scenario's preset)
```

### 1.5 Where the recording lands
`$FDP_STAGING_ROOT/nodes/node-<id>/exercises/<exerciseId>/node_<id>.fdp` — one file per node; `FDP_STAGING_ROOT` defaults to
`/tmp/FDP_Temp` (Linux) or `C:\FDP_Temp` (Windows). Open it with `./run_ReplayBrowser.sh`.

| also | how |
|---|---|
| the editor alone | `./run_Editor.sh` |
| a player station on its own | `./run_Player.sh` |
| a recording afterwards | `./run_ReplayBrowser.sh` |
| the two scenarios | `scenarios/showcase-bridgehead` (training: objectives, standards, evaluation) · `scenarios/showcase-crossfire` (game: capture points, tickets, score) |

## 2. The feature rows

| # | feature | owner | steps → what you should see | verified |
|---|---|---|---|---|
| **A1** | objectives tick and report (`GET /objectives`, the Rules window) | behaviors + ui | Start: `HROT_DEBUG_API_PORT=8111 ./run_All.sh` · `curl -s -X POST localhost:8111/scenario/load/live -d '{"name":"<scenario>","waitForReady":true}'` · `curl -s -X POST localhost:8111/sim/play -d '{}'` *(⚠ `localhost`, not `127.0.0.1`; a fresh cluster refuses the load until `GET /status` says `canOperate`)*. On `showcase-bridgehead`: `curl -s localhost:8111/objectives` → 37 rules, every objective `state` Active; read it again 10 s later → `score` moves (*Bridgehead* 0.45 → 0.58, *Members* 0.5 → 0.92 measured), `progress` cur/need per objective (*Bridgehead* 0/3: Mission · Conduct · Members). | ✅ `behaviors@d549b2b1f`, 2026-11-03 (data half; the screen half is ui's) |
| **A2** | a trigger fires its Do list, step by step (*step 2 / 3*) | behaviors + ui | Start: `HROT_DEBUG_API_PORT=8111 ./run_All.sh` · `curl -s -X POST localhost:8111/scenario/load/live -d '{"name":"<scenario>","waitForReady":true}'` · `curl -s -X POST localhost:8111/sim/play -d '{}'` *(⚠ `localhost`, not `127.0.0.1`; a fresh cluster refuses the load until `GET /status` says `canOperate`)*. On `showcase-bridgehead`: find *Phase 2 starts* in `GET /objectives` (its `networkId`) → `curl -s -X POST localhost:8111/objectives/<networkId>/control -d '{"request":"Fire"}'` → poll `GET /objectives`: `triggerState` **Acting** with `stepCur` 2 / `stepCount` 3 while its Order waits for the platoon's answer (~0.4 s), then **Fired** (`fireCount` 1), then **Done**; `GET /entities/<networkId>/journal` shows *"Fire by DebugApi"*. ⚠ A Do list of instant steps (Crossfire's *Sudden death*: Message + 6 × SetObjectiveActive) completes in one tick — no step is ever shown; that is right, not a bug. | ✅ `behaviors@d549b2b1f`, 2026-11-03 (data half; the screen half is ui's) |
| **A3** | fire / disarm / re-arm / reset a rule by hand | ui | *owner to write* | ⏳ |
| **A4** | author a trigger in the editor (When / Do / Rules tabs, + Add, drag to connect), save, reload | ui | *owner to write* | ⏳ |
| **A5** | change a rule LIVE (cooldown while armed) — the fire count stays; a bad edit is refused with its full reason | ui + behaviors | *owner to write* | ⏳ |
| **A6** | the *Coming up* strip and the round clock count down to timed triggers | behaviors + ui | Start: `HROT_DEBUG_API_PORT=8111 ./run_All.sh` · `curl -s -X POST localhost:8111/scenario/load/live -d '{"name":"<scenario>","waitForReady":true}'` · `curl -s -X POST localhost:8111/sim/play -d '{}'` *(⚠ `localhost`, not `127.0.0.1`; a fresh cluster refuses the load until `GET /status` says `canOperate`)*. On `showcase-crossfire`: `GET /objectives` → each armed timed trigger's `timing.expectedAt` is its time: *Kick-off* 3, *Crate drop 1* 180, *Sudden death* 720, and the round's end (*Blue wins* / *Red wins* / *Draw*) **900**; after 3 s *Kick-off* is Done with `expectedAt` 0. The round-ending trigger is the one whose `do` has `EndExercise` (CE-4238). | ✅ `behaviors@d549b2b1f`, 2026-11-03 (data half; the screen half is ui's) |
| **A7** | the instructor's **End** button ends the exercise through the scenario's own end | ui | *owner to write* | ⏳ |
| **A8** | messages: a briefing reaches the right players; the instructor sees **what went to whom** | ui | *owner to write* | ⏳ |
| **B1** | per-member standards (Bridgehead): every man's evaluation, role / standard means | behaviors | Start: `HROT_DEBUG_API_PORT=8111 ./run_All.sh` · `curl -s -X POST localhost:8111/scenario/load/live -d '{"name":"<scenario>","waitForReady":true}'` · `curl -s -X POST localhost:8111/sim/play -d '{}'` *(⚠ `localhost`, not `127.0.0.1`; a fresh cluster refuses the load until `GET /status` says `canOperate`)*. On `showcase-bridgehead`, after ~6 s: `GET /objectives` lists a `MemberEvaluation` *Members* (8 children, score ~0.9), one `WeightedMean` *Member standards* per member (4 children each: hit ratio, no harm, no fratricide, report latency, `subject` = the man), and the `SelectionMean`s: by role (*All Wingman*) and by standard (*Hit ratio (mean)* value 0.27 vs target 0.3, *Report latency (mean)* 3.0 s vs 60, *No harm (mean)*, *No fratricide (mean)*). *Members* is one of *Bridgehead*'s three children. | ✅ `behaviors@d549b2b1f`, 2026-11-03 |
| **B2** | the mission summary — score per side with trend and pass mark, scores by chain / objective / role, *why* | ui | *owner to write* | ⏳ |
| **B3** | the trainee HUD (my objectives, my standards) and the trainee debrief (GO / NO-GO, went well / work on), live and on a recording | ui | *owner to write* | ⏳ |
| **B4** | the coaching hint (*"you are in the open"*), off by default | ui | *owner to write* | ⏳ |
| **C1** | Crossfire: capture points, tickets, respawn waves, the end naming the winner | backend | Start Crossfire (§1.2). ⓐ The game bar (player window) and *Window ▸ Rules* show **Point A / B / C**: a side that outnumbers the other inside a point for **10 s** takes it (`GET /objectives` → the HoldArea row's `owner`, `challenger`, `progress` = seconds of advantage of 10). ⓑ While a point is held the OTHER side's **tickets** fall 1 per 10 s (*Blue tickets* / *Red tickets* `value`, start 150), and every man down costs his side 1. ⓒ A side with fewer than 8 men up gets a **bot every 15 s** at its base (*"Blue bot"* / *"Red bot"* appear and run to the middle). ⓓ The round ends when a side's tickets reach 0, a side holds all three points for 60 s, or at **15:00** — and the end **names the winner**: one of *Blue wins* / *Red wins* / *Draw* turns Achieved and the log says *"exercise ended … Friend Win / Hostile Lose"* (or the reverse / Draw). ⚠ known: point A is rarely visited (CE-3362). | ⏳ live run pending |
| **C2** | the per-player score, +100 pop-ups, hold-K scoreboard, kill feed | backend + ui | *(data half — the screens are ui's)* Start Crossfire. Every man of both sides — respawned bots too — gets a **`Score`** row (`GET /objectives`, kind `Points`), its children *Kills* ×100 · *Assists* ×50 · *Captures* ×150 · *Team kills* ×−200 · *Bounty kills* ×300 (*Deaths*, *Streak*, *Best streak*, *Hit ratio* shown, unscored); the score = the sum. *Blue score* / *Red score* = the team totals. Shoot a friend: his *Team kills* rises and his score drops by 200, his *Kills* does not move. ⚠ known: ~1 in 10 downings may be uncredited (behaviors' note `NOTE_Backend_To_Behaviors_Uncredited_Downings.md`). | ⏳ live run pending |
| **C3** | streaks, the bounty (announced, marked on the map, claimed for 300), awards | backend + ui | *(data half)* Start Crossfire. A man's **Streak** = kills since he was last downed. At a streak of **5** his **Bounty** row turns Active and everybody gets *"Bounty on <name>!"*; whoever downs him gets **Bounty kills** +1 (+300) and the Bounty row reads *"claimed by <killer>"*. At the end (15:00, or press the instructor's End — A7) the **awards** settle: *Top scorer*, *Most kills*, *Point keeper*, *Sharpshooter*, *Untouchable*, *Bounty hunter*, *Longest streak* — each Achieved with its winner as `subject`, or Failed *"nobody qualified"*. ⭐ To SEE a bounty quickly: AI rarely reaches 5 — lower `minStreak` to 2 in `macros/game/score/Default/scenario.json` (the `Bounty` entity) for the run, and restore it after. | ⏳ live run pending |
| **C4** | the round summary (winner, scoreboard, awards, best moment), live and on a recording | ui | *owner to write* | ⏳ |
| **C5** | a supply crate: inserted on time, held for tickets | backend | Start Crossfire. At **3:00, 6:00, 9:00, 12:00** *"Supply drop in 30 s"*, then *"Supply crate down"* — a crate appears at one of the four spots (`GET /entities` → *Supply crate*). Its **Crate N** row turns Active: a side that outnumbers the other at the spot for **20 s** takes it ⇒ *"Blue secured a crate: +15 tickets"* (their *tickets* +15), and each of their men there is credited a *Capture* (+150). | ⏳ live run pending |
| **D1** | place a macro in the editor; its marker, member lines, dimmed transient members, the Scenario view | backend + ui | *(placement half — the marker, lines and dimming are ui's)* Start Bridgehead. The **Red outpost** (a macro instance, `forces/red-outpost`) stands at one of three places — its two men are placed around the instance's anchor and face its heading; another run variant (`--param '$variant=2'`) moves them together. | ⏳ live run pending |
| **D2** | insert a macro at runtime (*Add ▸ Macros*, and an `InsertMacro` step), with a **preview** first (MA-10) | ui | *owner to write* | ⏳ |
| **D3** | the director: a counter-attack macro triggered by the trainees' progress; the skill amplifier | backend | Start Bridgehead. When Blue holds the crossroads (*Hold the crossroads* Active), **120 s later** the **Red QRF** platoon is inserted at the map edge (*"Enemy moving towards you"*) and attacks the crossroads (its squads run `PlatoonAttack` / `SquadDefend`); 180 s after Hold a civilian car drives to the checkpoint. The director adapts: score < 0.35 after 20:00 ⇒ *"Red eased"* (Red's skill −1 tier, at most twice); score > 0.80 before 25:00 ⇒ *"Red pushed harder"*. `GET /objectives` → *Counter-attack*, *Checkpoint inject*, *Ease off*, *Push harder* fire counts. | ⏳ live run pending |
| **E1** | game-master palette: run any shared action by hand; a per-side skill slider | ui | *owner to write* | ⏳ |
| **E2** | features per node: training vs game HUD by the scenario's preset; the operator's feature grid toggles one live | ui + backend | *(preset half)* Start Crossfire, then `GET /features`: on the player node (IG) `hud.objectives` and `hud.own-evaluation` are **off, source "scenario"** (the training HUD is hidden in the game). Start Bridgehead: `game.tickets`, `game.capture-points`, `game.round-timer`, `game.score-popups`, `game.bounty-marker`, `game.scoreboard`, `hud.kill-feed` off, source "scenario". ⚠ SimHost still reads `role default` (it holds no copy of the scenario; it draws no player HUD). | ⏳ live run pending |
| **E3** | the score / evaluation entities never show on the map or in the ORBAT; they do show in the Scenario view and the rules view | ui | *owner to write* | ⏳ |

⭐ **A row is VERIFIED** when its owner ran exactly those steps on the current branch and saw the result; the cell names the branch@sha and date.
⚠ A known bug that does not stop the row is noted in the row (*"score may miss ~1 in 10 downings — CE-…"*), not a reason to leave it unverified.
