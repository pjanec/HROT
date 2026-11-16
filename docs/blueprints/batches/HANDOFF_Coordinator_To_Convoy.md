<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/vehicle-tactics/DRAFT_Road_Convoy.md — ⭐ THE direction agreed with the user (two modes, one column-follower, the rules); you turn it into the design
  - ../Architect_Question_93_Sumo_Traffic_Node.md — the SUMO traffic node: TrafficHandover, TrafficBodies, TrafficReplicas
  - ../../designs/navig-2/Navigation_Design_v2_0.md — §5.2a the route planner (mode 2)
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §11a the platoon's mounted convoy (PlatoonConvoyBehavior)
  - ../../reference/NED_DDS_Protocol.md — the wire, incl. the SUMO traffic section; keep it current (scripts/protocol-doc-check.py)
-->

# HANDOFF — coordinator → `convoy` (a NEW lane): the ROAD CONVOY in SUMO traffic — measure, design, build, prove (CE-4631)

**For:** a NEW lane on branch **`convoy`** (create it from `origin/coordinator`). **From:** coordinator. **Dispatched at `74c53dd69`**; ⛔ scope FROZEN
there — later documents are FYI; one that invalidates a step ⇒ STOP that step, do the rest (R-106).

🔒 User, `2026-11-16`, verbatim: *"Convoy never split, ignores traffic lights, and the sumo vehicles needs to react to it."* · *"I meant the leading sumo
vehicle to be the one with priority, with beacon or siren. So no traffic lights apply … because Sumo handles all of that already … The route can stay as a
second valid option because it works outside of Sumo, offroad, so it is worth supporting it."* · *"Write a handoff for a lane in a new branch to do the
measurement and then to implement the convoy feature fully with both sumo/route options and feature test scenarios."* — R-4282.

## ⭐⭐⭐ UNATTENDED
Never ask, never wait, never end a turn on a question. Every open choice: measure it, decide by the claim table (code + design), build it, record an
`R-9xxx` lane decision *"for the user's review"* with what would change it. ⛔ The four RULES in §2 are the user's — never traded away. A failed premise
stops ONE step, never the batch (R-106). A defect is fixed where it lives, with a rail in that feature's own suite (T-1). Commit, push and update
`STATUS_Convoy.md` after every step (a restart or a compaction resumes from it). Cross-lane edits (SumoSharp, `Hrot.Traffic`, `CarKinem`, navigation,
behaviours) are allowed and SAID in the commit and the status. ⛔ Never take an id outside your blocks.

## 0. Start
`git fetch origin coordinator`; create `convoy` from `origin/coordinator`; `dotnet restore` the test projects you run; push `chore: started convoy at
<sha>`. Read RULINGS.md, CLAUDE.md, then the draft END TO END, then the related designs (R-129). First command: `scripts/cbm.sh list_projects` in the
background — every claim about what exists goes through the graph AND grep.

## 1. Your id blocks *(fixed, measured free on all eight branches `2026-11-16`)*
tracker / rulings **CE-9000…CE-9999 · R-9000…R-9999** · component ids **108, 109, 157, 158** (reserved in `GlobalComponentIds.cs`'s header comment) ·
descriptor ordinals **198–203** (reserved in `AllDescriptors.cs`).

## 2. The rules (the user's — the acceptance of everything)
| rule | |
|---|---|
| the convoy NEVER splits | one route for the column; followers never overtake; it stops and starts together |
| it IGNORES traffic lights | the lead runs reds; followers follow the lead and never look at a light |
| SUMO's vehicles REACT to it | they queue behind it, stop for it at junctions, never cut into it |
| it ACTS when the scenario needs it | under attack or switched to a battle task, the convoy's own plays take over at once; no vehicle is re-created |

## 3. The steps, in order

**Step 1 — MEASURE** (the draft's §6, and anything else the design needs). Write each answer, with its `file:line`, into the design's INVENTORY:
① does our vehicles' avoidance brake for SUMO car replicas (`TrafficReplicas`)? ② can SumoSharp command a lane change / make a car yield? (`KraussModel.cs:434`
says no lane-change model in phase 1; other lane-change code exists) ③ the net's driving side ④ how far `FormationFollower` (CarKinem) gets toward a
path-following column ⑤ how `TrafficHandover` spawns the shadow car (its vehicle type, its route via `SetDestination`) and removes it ⑥ how a SUMO vehicle
type's `jmDriveAfterRedTime` / `JmIgnoreFoe*` are set at runtime for one vehicle ⑦ how `TrafficBodies` registers a body on lanes (for the one continuous
column obstacle) ⑧ what the convoy-hit plays and `PlatoonConvoyBehavior` need to take over mid-route.

**Step 2 — DESIGN.** Turn the draft into `docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md` (the draft → SUPERSEDED, linking it): STATUS block with
`related-designs` both ways, the INVENTORY with the graph queries you ran, the proven solution for the problem class (convoy / platoon control in traffic
simulation) and why each piece fits, decisions with leans (you decide them — lane decisions), and the UML — a `classDiagram`, a `sequenceDiagram` (a
convoy crossing a junction on red while SUMO cars stop; an ambush switching it) and the MODULE diagram (who registers each system, on which node, who calls
it each frame). Validate every diagram (`MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>`). Then build against it.

**Step 3 — BUILD, mode 1 (on the network): the SUMO-driven priority lead.** Our escort vehicle (beacon / siren) handed to SUMO with a PRIORITY vehicle
type (red ignored, junctions forced); SUMO routes it to the destination, keeps its lane and waits behind blockers; it stays our entity (shot, possessed,
ordered).

**Step 4 — BUILD, the column-follower (shared by both modes).** Followers drive the LEAD'S ACTUAL PATH (its recorded trail) at a gap measured ALONG the path;
never overtake; stop together and close up to a minimum gap; the lead slows when the column stretches. Extend what step 1 ④ found rather than add a second
follower (ruling 9).

**Step 5 — BUILD, SUMO's reaction.** The column reported to SUMO as ONE continuous obstacle along its lane, first vehicle to last (no cutting in). If step 1
② allows it: cars queued AHEAD of a priority vehicle at a red clear into the junction (a small rule in our SumoSharp); otherwise the escort waits behind them
and the status says so.

**Step 6 — BUILD, mode 2 (off the network): our own route.** The route planner's route (off-road start → the network → off-road target) with a LANE
OFFSET when the route's points are emitted (the outer lane on the net's driving side; lane counts and widths are in the road graph); the same
column-follower. Joining and leaving the network from / to mode 1.

**Step 7 — BUILD, switching to action.** Attacked or ordered into a battle task ⇒ the lead's shadow is removed and our driving takes over; the convoy-hit /
`PlatoonConvoyBehavior` plays run. An order to authors: a convoy task / order (the existing order vocabulary) with its mode, lead, destination, gap,
priority flag.

**Step 8 — the FEATURE SCENARIOS** (R-4277 — each a regression test: a What / Judge `description`, `checks.json` expect rows, a golden; DONE =
`scripts/scenario-regression.sh <name>` passes in the editor tier, and in the cluster tier `--mode all` once it applies) in `scenarios/demos/convoy/`:
| scenario | the Judge lines |
|---|---|
| `cv-red-lights` | a priority-led convoy crosses a town through red lights; no vehicle ever stops for a light; the column never splits (max gap along the path ≤ the limit) |
| `cv-no-cut-in` | heavy SUMO traffic: cars queue behind the convoy and stop for it at junctions; no SUMO car is ever between two convoy vehicles |
| `cv-blocked-road` | a SUMO queue / a breakdown ahead: the convoy waits as one, then moves on — still unsplit |
| `cv-ambush` | an ambush mid-route: the shadow is removed, the convoy-hit play takes over within N s; no vehicle re-created |
| `cv-offroad-join-leave` | mode 2: an off-road start joins the network, drives in its lane, leaves it to an off-road target, unsplit |
| `cv-mode-1-to-2` | a convoy leaves the network mid-route (mode 1 → 2) and continues off-road |
Add each to `docs/CAPABILITIES.md` (§9) with its ✅ / ⚠ mark.

## 4. Gates — every step
T-1 first (each touched feature's own suites: traffic / handover / bodies / replicas, CarKinem formation, navigation, the convoy plays, SumoSharp's own
tests and goldens — ⚠ a SumoSharp change must keep its parity goldens byte-identical unless the change is meant to move them, and say so); every behaviour
step with `HROT_PARALLEL_SIM=1` + `HROT_READ_CHECK=1` + `HROT_WORLD_GUARD=1`, and `stepped-hash.py --threads 1,4,8` on a convoy scenario (the parallel
rule's own gate); each new rail red-proved by a compiling mutation; 0 B per steady frame (R-2084, no LINQ on the hot path); invariant output (R-4221);
recorded components unmanaged (CLAUDE.md "NEW COMPONENTS"). New wire types ⇒ `docs/reference/NED_DDS_Protocol.md` updated and
`python3 scripts/protocol-doc-check.py` OK. The gate-report contract (rule 8).

## 5. The report — `STATUS_Convoy.md`
per step built / stopped and the design folded (as-built, obligation ⑤) · the measurements · every lane decision with what would change it · the
scenarios' verdicts · the gate-report contract · ids allocated from your blocks.
