<!--STATUS
state: LIVE
updated: 2026-10-12
doc-type: HANDOFF — behaviors lane to the next behaviors session. Dispatched at 653e1f2a9 (branch `behaviors`).
current-answer: §1 where we stopped · §2 the work, in order · §3 traps already paid for · §4 gates and rails
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad build: S1 §3, S2 §4, the unit commander §7, embark measured §5, formations measured §6 + SquadMarch §6.1
  - docs/DESIGN_Entity_Interactions.md — OWNS embarkation (§7, §7.1 plan I-2a..d, §7.2 what "inside" means)
  - docs/DESIGN_Operations_Behaviour_Catalogue.md — OWNS the S1–S3 order (§11) and the convoy ambush slice (P1, D8)
  - docs/OVERVIEW_Behaviour_Building_Blocks.md — the map of the machinery; read before the INVENTORY, extend it when you measure
-->

# Handoff — squad tactics S3 (march + dismount) and embarkation `CE-3110`

⭐ **This file points at designs; it is not one.** Every class and sequence lives in the designs named in `related-designs`.
⛔ Do not draw UML here — extend the owning design.

## 1. Where we stopped *(`behaviors` @ `653e1f2a9`, 2026-10-12)*

| done | where it is recorded |
|---|---|
| S1 — generic `SquadManeuverRunner` + `ISquadPlay`, `SuppressAndManeuver`, `AdvanceTo` | Squad Tactics §3.6 · `CE-2123` |
| S2 — squad sensing edges (SquadContact / UnderFire / AllClear / LeaderDown), `ReactToContact` (ReturnFire → Assault / BreakContact / Hold → HoldOn), `BasicSquadSop`, `tt-react-contact` | Squad Tactics §4.7 · `CE-2123` |
| `CE-2125` + `CE-514` — the body-less unit commander (`UnitEntity`, hidden from sensing, at the members' centroid), leader succession, the 6 s command lull | Squad Tactics §7.3 · R-272 (behaviors) |
| backend merged (`653e1f2a9`): rulings 166/166, tracker + digest OK, toolkit 292/1 skip, SimHost 99/99, generators 404/404 | — |

Not run after the merge: the live cluster rails (`SquadTacticsScenarioTests`, `FireDistributionScenarioTests`). Backend's change was possession and the weapon HUD, not squad code. Run them once as T3 in the background.

## 2. The work, in order *(lean: this order; the user's "go to S3" is the go)*

| # | item | design basis | what is NOT measured yet — measure it first |
|---|---|---|---|
| **1** | **`SquadMarch`** — one more play on the S1 runner: the lead walks to the objective, the others go to template slots, fall back to the lead's breadcrumbs, and the lead waits for stragglers. Infantry only | Squad Tactics §6.1 (still marked *lean* — fold the as-built and drop the word when built) | the path-request cost of the re-orders on SimHost (estimate ≈ 4/s per squad) against the solver budget — measure on the live rail |
| **2** | **`CE-3110` I-2a** — one `Embarkation` rules class, `EmbarkationSystem` on the vehicle's owner, `EjectPassengersCommand`, executors raise commands, `EditorCargoSystem` retired, genesis calls the rules, an `ActionId` for embark, both executors in `CgfLogicPack` | Entity Interactions §7 table + §7.1 · R-224 | — |
| **3** | **I-2b** — `PassengerBuffer` replicated from the vehicle's owner; a derivation system on CGF **and** SimHost sets `IsEmbarkedTag` + the capability bits | §7.1 · R-224 (Q-1 approved) | ⛔ **every reader of `IsEmbarkedTag` / `ActorCapabilityState` and its phase** — §7 says this gets enumerated before I-2b. §7.1 lists 7 readers from grep; confirm with `search_graph` |
| **4** | **I-2c** — carried, hidden unless the vehicle is open (TKB `PassengerExposure`), dies with the vehicle; `IsEmbarkedTag` gains `Exposed` | §7.2 · R-271 (behaviors) | ⚠ the hide choke point: `PhysicalPresence.IsHidden` / `ExcludeHidden` (`FDP/Toolkits/Fdp.Toolkits/Perception/PhysicalPresence.cs`) today covers the grid builder and `EntitiesInAreaGenerator` only. Hits, near misses and `EntitiesInRadius` go through the **collider hash**. R-271 names five systems. ⇒ measure whether a carried passenger keeps a collider before choosing where `Exposed = 0` is enforced |
| **5** | **I-2d + S3 dismount** — library `Embark(vehicle)` / `Disembark`; a commander-level `Dismount` drill (a play on the runner) | §7.1 I-2d · Squad Tactics §5 verdict · catalogue §11 S3 | the convoy-ambush scenario (catalogue P1, D8) is the acceptance — design its layout on a walkable spot (trap ③) |

Fences (🔒 user):
- *"No handoffs to backend"* — the behaviors lane builds all of it. Name each cross-lane edit in its commit.
- *"Ignore stride host."*
- Proper formation movement is `CE-2124`, a separate programme that has not started. ⛔ Do not repair `JoinFormation` (Squad Tactics §6 verdict).

Ids:
- Tracker ids come from the behaviors block `CE-2000`–`CE-2999`. The last one used is `CE-2125`, so the next is `CE-2126` (verify).
- Component ids come from the squad block 256–299. Behaviors used 274 and 275; backend uses 356–364. Check `GlobalComponentIds` before taking one.

## 3. Traps already paid for *(each one cost a round)*

| ① | **Sim time is slow in the cluster harness**: ~150 frames per sim-second. Live rails need a budget of about 30000 frames and a 600 s timeout |
| ② | **A new `*.btree.json` recipe is an AdditionalFile, and an incremental build does not pick it up.** Touch it and rebuild, then list it in the csproj as Content (as `BasicSquadSop` is) |
| ③ | **A move to a point more than 2 m off the walkable area fails forever, and CGF has no navmesh.** Send members to EQS answers (solved on SimHost) or to breadcrumbs. `BoundingOverwatch`'s geometric bounds stranded a squad (Squad Tactics §4.7). The S1 layout is proven walkable — reuse it |
| ④ | **`CombatPosture` with an objective suppresses IN PLACE.** Every runner MOVE order uses `AdvanceTo`; HOLD orders use `CombatPosture` |
| ⑤ | **`UtilityScorer.ChooseOption` scores only `DecisionKind.PostureSelect`.** Any other kind returns winner 0 |
| ⑥ | **The event bus publishes for the NEXT frame.** The leader's alert lands one frame after the merge |
| ⑦ | **A resumed drill must re-take its own `ManeuverKind` / `PhaseId` / `Elements`** (the `Own*` snapshot in `SquadManeuverRunner`), or a reaction's play overwrites it. A resume after a tick gap re-issues orders |
| ⑧ | **Scenarios use the `Squad` composite** (TKB 302) with `Behavior` / `Sop` on the commander, and the leader as a member designated `SquadLeader`. Copy `tt-react-contact` (`Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Scenarios/`) |
| ⑨ | **Build the TEST project, never the production one, and never two builds at once.** A concurrent cluster build left the generators suite running a stale binary |
| ⑩ | ⛔ **Never commit a file a red-proof script mutated.** Restore it and check `git status` before the commit |
| ⑪ | **Open finding `CE-1055`** (filed by ui): the EQS module can fault with a dictionary data race in `VisionBroadphase.Select`. S1/S2/S3 all ask EQS — a flaky live squad rail may be this, not your change. Check its row before chasing one |

Known gaps (recorded in Squad Tactics §7.3):
- A successor's `Designation` stays CGF-local, because the replication path appends duplicate roster entries.
- `Infantry_Officer` (201) has no template.

## 4. Gates and rails

| gate | command |
|---|---|
| ledger | `python3 scripts/rulings-check.py` |
| tracker | `python3 scripts/tracker-counts.py --check` |
| designs | `python3 scripts/design-digest.py --check` · `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file>` |

| feature rails (T-1 — run these first) | project |
|---|---|
| `SquadManeuverTests` · `UnitCommanderTests` · `CgfLogicPackTests` (pack counts change when you register executors — update them deliberately) | `Hrot/Subsystems/Hrot.SimHost.Tests` |
| `SquadCoordinationSystemTests` · `EmbarkExecutorTests` | `FDP/Toolkits/Fdp.Toolkits.Tests` |
| `SquadTacticsScenarioTests` · `FireDistributionScenarioTests` (live, T3, run in the background) | `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests` |
| `BTreeGoldenCorpusTests` (the recipe count moves when you add a recipe) | `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests` |

For each item:
1. Extend the owning design's UML before you build (`build-state: READY-TO-BUILD`).
2. Red-prove each new rail.
3. Fold the as-built into the design.
4. Update the tracker row (`CE-2123` for S3, `CE-3110` for I-2a..d) and the map.
5. Commit and push to `behaviors`.
