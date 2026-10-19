<!--STATUS
state: LIVE
updated: 2026-10-19 (after the night run — written at fc2107aaa for the next session)
current-answer: §0 start here · §1 where it stands · §2 the next steps, in order · §5 measured traps · §6 open with the user
stale-below: §7 is the record of the 2026-10-19 night run (history, not instructions) — every "open / not built / in flight" line is a STATE claim: verify it against git and the tracker before acting
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — the command machinery (CC-A…CC-E), BUILT; §8a as-built, incl. CE-2288's slot route
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — wave 0 (DA-A §8a, DA-C §8b, DA-K §12c, DA-L §13c) and the next slices (DA-D §8, the sector levels §13a)
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — LD-3 slice 1 as built (§3.3a, with its known-rot) and the demos (§3.4)
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — the attack / defend / clear programme on top of it (§11 slices, §13 waves)
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — the clearing slices (CE-2257…CE-2259) and the report concept
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — the recorded urban ambush demo (not scheduled) and the infrastructure it needs (§4)
-->

# RESUME — behaviors lane: the command chain is built; now the platoon programme *(rewritten 2026-10-19)*

**RELEARN** — run the re-learn and open your first reply with the DESIGN BRIEF (`CLAUDE.md`).

## 0. Start here

| | |
|---|---|
| ⭐ lane | `behaviors` (stable branch `behaviors`). Push with `git push -q origin behaviors`. No PRs |
| ⭐ written at | `fc2107aaa` (behaviors) — after merging `backend` at `6b7e1ed81`; `ui` and `coordinator` had nothing new |
| ⭐ ids | tracker next free **`CE-2301`**, rulings next free **`R-2054`** (block tables: `Blueprint_Issues_Tracker.md` "ID blocks", `RULINGS.md` §0a). ⚠ Component ids: backend proposed per-lane blocks (behaviors **418–433**) and already uses its own (434, 435) — take a new component id from 418 up until the user rules |
| ⭐ first moves | `git fetch origin backend ui coordinator` and merge what moved · read §1 · then §2 step 1 |
| ⚠ environment | a fresh cloud VM: `bash scripts/cloud-bootstrap.sh` if `dotnet` is missing; the codebase-memory MCP may be down — use `codebase-memory-mcp cli <tool>` (no `check_index_coverage` there: say so in any exhaustive claim) |

## 1. Where it stands *(2026-10-19)*

| | |
|---|---|
| ⭐⭐ **the command chain — BUILT, runs live** | `CE-2289` ROE up the chain · `CE-2262` SL-0 · `CE-2291` CC-A · `CE-2292` CC-B · `CE-2293` CC-C · `CE-2295` CC-E · `CE-2294` CC-D · **`CE-2288`** one read route over every behaviour slot (`GET /entities/{id}/slots`). `cc-platoon-bound` passes live and as a twin. 📄 [Command Chain §8a](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| ⭐ **wave 0** | `CE-2273` proficiency ✅ (§8a) · **`CE-2274` proficiency in peek-and-fire ✅** (§8b: exposure, bursts, who switches by tier — PC-15, reaction delay on a new target) · `CE-2282` sectors ✅ (§13c; leans DA-26…DA-30 await the user) · `CE-2281` roles 🟡 part (§12c; fire-team split and role takeover BLOCKED, §6) — 📄 [Defend & Attack](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) |
| ⭐ **look direction** | **`CE-2297` LD-3 slice 1 ✅** — one `Look` node (`Hrot.AI.Behaviors/Brains/LookNodes.cs`) in `CombatPosture` and `PersonHold`; the eyes go to: the sound of the shot if he was just shot at → the top threat → a fresh heard sound → his sector (scanned in 2 s steps) → `watchTowards`. The operator's `POST /entities/{id}/sector` (forwards to the brain's node). **`CE-2298`** `ld-sentry-surprise` + `ld-sentry-surprise-360` ✅ pass live (no in-process twin yet). 📄 [Look Direction §3.3a, §3.4](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) |
| ⛔ **the known-rot you start from** | a squad's Hold / Watch member runs `CombatPosture` toward his point and it **ENDS on arrival** when nothing is to hunt (`PostureNodes.cs:269`) ⇒ his eyes (and anything reading his sector) stop there. §2 step 1 |
| civilians (`CE-2235`…`CE-2240`, `CE-2256`, `CE-2249`…`CE-2251`) | a different programme on this lane, still open. ⭐ backend unblocked `CE-2249` / `CE-2250` / `CE-2251` (speech, mood, the baton blow — `CE-3264`…`CE-3266`, Civilians §7.18). Not part of this resume |

## 2. The next steps, in order

⭐ **Read each slice's design section end to end before building it** (R-129); fold every deviation back (as-built rule); the
feature's own rails first, then new rails in the feature's suite; builds and tests in the background; red-proof each rail with a mutant.

| step | id | what | design | notes |
|---|---|---|---|---|
| **1** | `CE-2297` (slice 2a) | ⭐ **a Hold / Watch member stays**: the runner's `SquadManeuverRunner.Hold` asks `CombatPosture` with a `hold` flag so arriving does not end it — his eyes and his fight stay until the squad's next order | [Look Direction §3.3a](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) known-rot + lean | ⚠ **measure first**: which drills advance a phase on a member's task ENDING (a `BehaviorFinished` / `ProgressKind.Set` from the staff — CC-C sends PROGRESS on task end!) rather than on his position. ⛔ CC-C's staff composes PROGRESS when a member's task ends — a member that no longer ends may stop the squad's SET report. Grep `SquadSensing`, `StaffComposerSystem`, the plays' `PhaseFor`. This is a lean, not a ruling: if it reaches the staff, ask the user |
| **2** | (new row) | ⭐ **`MemberArc`** — an arc in the member order (`MemberOrder` in `SquadManeuverRunner.cs:69`); the runner publishes `SetEngagementAreaEvent` (origin Superior) for each man with an arc; withdraw what the play set when it ends (`ISquadPlay.Release`). First user: the danger crossing's security men (left / right along the road — `SquadDangerCrossingBehavior.cs:169-228`, `MemberOrderKind.Watch`) | [Defend & Attack §13a](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) (DA-24, R-2038 — approved) | ⭐ allocate a CE row (`CE-2301`). Design detail with UML before code (it is new ground: `SectorPlanner.ForMembers` / `Moving`) |
| **3** | `CE-2297` slice 2 + `CE-2298` | the march's file-place glances, the crossing security sides, the `Sentry` JSON tree's eyes, `ld-crossing-security`; an in-process twin for `ld-sentry-surprise` (`SquadTacticsScenarioTests` is the twin precedent) | [Look Direction §3.3, §3.4](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) | `ld-watch-last-window` (PC-16) and `ld-room-sectors` wait on the clearing |
| **4** | `CE-2275` | **SquadDefend** (DA-D): `DefencePlan`, `FindDefensivePosts` / `FindBuildingPosts` / `FindRoofPosts`, set-back window posts (DA-6), `OccupyPost` / `Displace`, HoldFire and its lift (DA-7); `DefendArea` maps to it | [Defend & Attack §3, §4.2, DA-4…DA-7](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) | big: write §8c (INVENTORY + UML) first. Needs steps 1–2. WorldSurvey (BR-12) is built for the building posts |
| 5 | small | **DA-28 self-defence by the shooter**: `NearMissEvent` now carries the round's origin (`CE-3262`) — "a contact within 50 m" can become "the one who shot at me"; `RecentSenses` keeps only kind + time, so the origin needs a home | Defend & Attack §13c DA-28 | optional; a lean of mine, so small |
| 6 | wave 2 → 4 | clearing `CE-2257`…`CE-2259`, sensors `CE-2283` → `PlatoonDefend` `CE-2276`, `PlatoonAttack` `CE-2277`, PL-1 `CE-2264`, smoke `CE-2278`, sniper `CE-2279`, urban `CE-2284` → repertoire `CE-2280`; demo harness `CE-2296` | [Platoon §13](../designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md) | after 1–4 |
| later | urban battle | [§4](../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md) items ①–⑨ | — | ⛔ not scheduled (R-2051) |

## 3. What binds you

| ruling | in one line |
|---|---|
| R-2042 / R-2043 | a commander's knowledge lives by SCOPE: the unit's in three components, each play's in its own slots |
| R-2044 / R-2045 | requirements by a `SetReportingEvent` (the ROE twin); the staff sends everything; reports watchable and fakeable over HTTP |
| R-2046 / R-2047 | the platoon programme PC-1…PC-16 — ⭐ PC-15 (who switches windows by tier) is now built in CE-2274 |
| R-2048 / R-2049 | look direction: the existing look-at channel; the head only glances, a lasting look turns the body (R-3003 for aiming) |
| R-2035 / R-2036 / R-2038 | Defend & Attack DA-1…DA-25 — ⭐ DA-24: squad sector = an area entity, a man's = an ARC in his order |
| R-2050 | a commander's memory reads like any component; one route over every behaviour slot (built) |
| R-2051 / R-2052 / R-2053 | the urban battle is recorded, infrastructure first; demo-city quarters (ui) |

## 4. Other lanes *(check `git log origin/<lane>` before you wait on anything)*

| lane | state | where |
|---|---|---|
| backend — **delivered** | BR-3 reports on the wire · BR-12 WorldSurvey · BR-13 ROE / reporting across nodes · LD-1 / LD-2 look · BR-6 spread + proficiency on the wire · BR-7 acquisition by exposure · BR-8 MG roles · BR-9 sniper / DMR / scope · BR-2 stun · BR-4 custody · BR-5 lean · ⭐ the sectors note ① ② ③ ⑤ (`CE-3261`…`CE-3263`: sector across nodes — tactical-intent kind 4; `EngagementArea` replicated; near-miss origin; succession on the wire) · `CE-3246` damage across nodes · `CE-3248` a throw is not a gunshot | [their note to behaviors](batches/NOTE_Backend_To_Behaviors_Platoon_Programme_Built.md) |
| backend — **asked, open** | ⭐ [sight beyond 100 m](batches/NOTE_Behaviors_To_Backend_Sight_Beyond_100m.md) (an exposed walker at ~113 m never acquired) · the sectors note ④ the fire-team byte (waits on the user) · `CE-2287` brain-side geometry (Q94, waits on the user) | — |
| ui | **UI-1…UI-7** buildings, `house-3f`, the CQB house, `cqb-village`, the command-picture map, the Reports window · **UI-8…UI-12** demo-city quarters | [platoon demos](batches/HANDOFF_Behaviors_To_Ui_Platoon_Demos.md) · [quarters](batches/HANDOFF_Behaviors_To_Ui_Demo_City_Quarters.md) |
| coordinator | ⚠ an editor rail red since CE-4031 (`ThereIsOneNetworkIdResolverTests` → `TrafficReplicas.cs`) — reported, not ours | [note](batches/NOTE_Behaviors_To_Coordinator_NetworkId_Rail_Red.md) |

## 5. Measured traps *(2026-10-18 / 19 — re-measure before relying on one)*

| trap | where it bit |
|---|---|
| ⛔ `CombatPosture` ENDS on arrival with nothing to hunt (`PostureNodes.cs:269`) | a squad's Hold / Watch member — §2 step 1 |
| ⛔ adding a shipped `*.btree.json` asset needs the AI generator goldens: `AI_REGENERATE_SNAPSHOTS=1 dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests --filter Golden` + bump the corpus count in `BTreeGoldenCorpusTests` | CE-2294 shipped `BasicPlatoonSop` without them; fixed at CE-2297 |
| ⛔ `HoldOrder` has THREE fulfillers (`PersonHold`, `InfantrySquadHold`, `WeaponSystemHold`) and `SopParamsRoundTripTests.CE2079` requires each to read every field it is sent | a `Towards` field on it was reverted |
| ⛔ the CycloneDDS IDL generator emits a file's enums into one scope — member names must be unique across enums in one file | `OccurrenceSlotCatalog` (`OwnerUnknown` / `RoleUnknown`) |
| ⛔ `GET /entities/{id}/memory` returns `modalities` as a BITMASK (Visual = 1), not text | the sentry check's first runs |
| ⚠ bt-range is 200 m across; House A / B sit at y = 100, the Solid Block at x 100–120 / y 150–170, the Low Wall at x 100–120 / y 40 — the **clear strip is x ≥ 170** | `ld-sentry-surprise` |
| ⚠ an authored `PerceptionReceptor.VisionRange` cannot widen the implicit visual sensor's search radius (fixed at spawn from the TKB — `VisualPerception.ImplicitEntry`); and ~113 m was never acquired on bt-range (asked of backend) | the sentry demo crosses at ~70 m |
| ⚠ `utility-demo-check.py --launch` refuses a runner older than its sources — merge another lane only AFTER a live run, or rebuild `Hrot/Runner/Hrot.ClusterRunner` | the night run |
| ⚠ a container restart kills background tasks — re-run them; the repo and `bin/` survive | the night run |
| ⚠ flaky: ST-030 `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` (green alone) | every SimHost run |
| ⛔ (2026-10-18) mission plans: ≤ 8 phases, one unit, operator only · an insurgent with no order only hides · mortars have no firing sound · `GET /orders` is the catalogue, not issued orders · demo-city's houses are one storey | the platoon / urban demos |

## 6. Open with the user *(each with my lean)*

- ⭐ **`hold: true`** for a squad's Hold / Watch member (§2 step 1) — lean yes; measure the staff's PROGRESS first.
- ⭐ the sector leans **DA-26…DA-30** (Defend & Attack §13c) — built as leaned; ask for a yes or the one to change.
- **CE-2281's fire-team carrier** — lean: a `FireTeam` byte on `EntityInfo` (backend: the wire can take it). **Role takeover** needs a weapon pick-up design first (§12c).
- **component-id blocks per lane** — lean yes, as backend proposed ([note](batches/NOTE_Backend_To_All_Lanes_Component_Id_Space.md)); three collisions in one day.
- **TC-7** (how lethal a man holding a throwable counts, `CE-2256`) — backend and behaviors agree per held item; not ruled.

## 7. The 2026-10-19 night run — the record

| commit | what |
|---|---|
| `e323f8d74` | `CE-2288` the slot route (Command Chain §8a) |
| `a43b4bd3c` | merge backend; component ids converged (Lean 416, EngagementArea 417) |
| `6458a33fd` | `CE-2297` LD-3 slice 1 + the `CE-2294` golden fix |
| `e93cab7a6` | `CE-2298` `ld-sentry-surprise` passes live; "a man shot at turns to the shot"; note to backend (sight beyond 100 m) |
| `8d4c5d3f8` · `8af8091c2` | merge backend (sectors across nodes …); the sector route forwards to the brain's node |
| `f2cc53779` | `CE-2274` DA-C proficiency in peek-and-fire (Defend & Attack §8b) |
| `00680a1b8` · `fc2107aaa` | LD-3 known-rot folded; merge backend (`CE-3248`) |

Last gates on `fc2107aaa`: SimHost 1648/0, Toolkits 3903/0 (Editor 518/1 = the CE-4031 red; NED 149/0 at the earlier merge).
