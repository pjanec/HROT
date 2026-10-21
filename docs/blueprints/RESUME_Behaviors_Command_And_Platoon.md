<!--STATUS
state: LIVE
updated: 2026-10-21 (AQ99 built, both clearing twins pass — N.1a) · 2026-10-20 (CE-2318 fixed, AQ96 approved + built, description handed to backend; rewritten top: §N "now" is the start — the urban-battle plan is done; next: the user's live findings + Q95's behaviors half)
current-answer: §N (start here, 2026-10-20) · then §2 for what the plan built · §5 measured traps
stale-below: §0 and §1 (as of 2026-10-19 — superseded by §N) · §2b · §6's "first move" (that merge was done) · §7 — history. Every "open / not built / in flight" line anywhere here is a STATE claim: verify it against git and the tracker before acting
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md — APPROVED 2026-10-20: signatures, hearing by distance, the lossless ring, the Band; behaviors owns E (CE-2315)
  - docs/DESIGN_Injury_States.md — §8a CE-2318 as built: a man down is no target, and he lies
  - docs/blueprints/Architect_Question_96_Movement_Under_Fire.md — ASKED 2026-10-20: movement under fire (CE-2319 / CE-2320)
  - docs/blueprints/Architect_Question_88_Soldier_Speed_And_Retreat_Under_Fire.md — the crouched run chosen at issue only (CE-2319 / CE-2320)
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — the command machinery (CC-A…CC-E), BUILT; §8a as-built, §8d the request chain
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — DA-A…DA-L, §8d PlatoonDefend, §8f.1 PlatoonAttack (as built)
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — LD-3 slice 1 as built (§3.3a, with its known-rot) and the demos (§3.4)
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — the attack / defend / clear programme on top of it (§11 slices, §13 waves)
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — the clearing slices (CE-2257…CE-2259) and the report concept
  - docs/designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md — the plan (§5, all steps done) and the demo-city ladder + flagship (§6)
-->

# RESUME — behaviors lane: the command chain and the urban battle are built; now the live findings *(top rewritten 2026-10-20)*

**RELEARN** — run the re-learn and open your first reply with the DESIGN BRIEF (`CLAUDE.md`).

## N. ⭐ Now — start here *(written 2026-10-20 at `c12f4e9ac`)*

| | |
|---|---|
| ⭐ lane | `behaviors` (stable branch). `git push -q origin behaviors`. No PRs. Commit trailers per the system reminder; no model ids in the repo |
| ⭐ ids | tracker next free **`CE-2347`** · rulings next free **`R-2067`** · component ids **420–433** (418 = `UnderFireMove`, 419 = `MapGraphic`, R-2056) · next architect question **100** (AQ99 asked 2026-10-20: person spacing + what a cleared room may cost, `CE-2336`) (AQ97 approved R-2062 and BUILT 2026-10-20: default reflexes, `CE-2322`) |
| ⭐ unmerged | `backend` is 1 commit ahead — `8d6e2ed05` CE-3278 (a weapon whose round bursts states its burst's carry) + its note [Launch_Hearing_Measured](batches/NOTE_Backend_To_Behaviors_Launch_Hearing_Measured.md) (already read, cited by Q95). `ui` is 1 commit ahead. ⇒ first move: `git fetch origin backend ui coordinator`, merge both, gate the touched suites (SimHost, Toolkits) in the background |
| ⚠ environment | fresh cloud VM: `bash scripts/cloud-bootstrap.sh` if `dotnet` is missing; codebase-memory MCP may be down ⇒ `codebase-memory-mcp cli <tool>` (no `check_index_coverage` there — say so) |

### N.1 Where it stands

| | |
|---|---|
| ✅ **the urban-battle plan (R-2060) — all 8 steps done** | see §2; the flagship `uc-urban-battle` passes live 6/6, nothing faked |
| ✅ **Q95 approved (A–E + B3)** | [Q95](Architect_Question_95_Sound_Signatures_And_Lossless_Hearing.md) §5 — a sound signature per weapon (the emitter's TKB type; the ear's proficiency decides how much is named); hearing bounded by DISTANCE, never by the high-resolution terrain (guns stand 2–20 km off it); a lossless emission ring (per-sensor serial cursor, overflow counted); demo-city bounds over its road net for perf tests; a TKB `Band` (Low/Mid/High) scaling WQ-E's future obstacle loss. ⭐ Dispatched to backend: [handoff](batches/HANDOFF_Behaviors_To_Backend_Sound_Signatures_And_Lossless_Hearing.md) (S1 ring · S2 CE-3279 timeouts · S3 signature · S4 carries · S5 the rule · S6 Band) |
| 🟡 **behaviors' half of Q95 — `CE-2315`** | E1 after backend's S3: `StaffSystems.IsLaunch` reads the signature, CE-2309's ≥ 1 km rule goes. **E2 (not blocked): measure why backend's run delivered 9 heard contacts to the brain and no SPOT came.** E3: L7 `uc-request-chain` with the guns 2–3 km OFF the terrain |
| ⭐ **the user's live findings, filed 2026-10-20** | see N.2 — measured, leaned, not built |

### N.1a ⭐ The clearing demos *(2026-10-20/21 — start here for building clearing)*

| | |
|---|---|
| ✅ built + rails | CE-2334 (the drill on the real house) · CB-64 (the stack inside its room) — [Building Clearing §7.1b](../designs/group-maneuvers/DESIGN_Building_Clearing.md) |
| ✅ [`AQ99`](Architect_Question_99_Clearing_Demos_Person_Spacing_And_Losses.md) approved (R-2065) + BUILT (`CE-2336`) | A: TKB `AvoidanceRadius` 0.3 for soldiers / fighters / security, civilians keep 0.8 · B: ③ = no friend hit by a friend, casualties reported · C: a man down stepped over only where no way round (Motion Domains §5h.1) |
| ✅ fixed on the way | `CE-2337` an occupant cowers and stays · `CE-2338` a friend lying down holds no shot · `CE-2339` a civilian keeps no room uncleared · `CE-2340` a man lying dead hides nobody (sight, fragments) · `CE-2335` one defect (a door ghost no longer publishes) · `CE-2326` behaviors half (blind burst names the body) |
| ✅ twins PASS, un-skipped | `bc-house-clear` 3/3 on the final build (BuildingClear ~200 s, 0–3 lost, civilians unhurt) · `bc-room-clear` 3/4 (RoomClear 38–50 s, 2–3 lost) — the fourth lost all four entry men ⇒ in the flaky lane, `CE-2341` (closes with BC-4/BC-5) |
| ⏳ open | `CE-2335` the door split itself — backend (ddsmonitor + a repair re-send, lean in the row) |
| ⏭ next | PL-1 (`CE-2264`, `bc-platoon-house`); BC-2 (civilians controlled, Comply) now has an occupant to control |

### N.2 The user's live findings *(tracker rows, all with file:line and a lean)*

| row | lane | what | lean |
|---|---|---|---|
| ~~`CE-2318`~~ ✅ fixed 2026-10-20 | behaviors (+ backend aim) | soldiers keep shooting a DOWNED man, and over him | ① targeting uses `Injury.IsInTheFight`, not `CombatLife.IsAlive` (Health > 0) — `TopThreat`'s `Engageable` (`EqsTacticsNodes.cs:359`), `AimAndFireExecutor.cs:77`, `PostureNodes.cs:287,400`, `SquadManeuverRunner.cs:804`, `SquadAssignment.cs:24`, `HiddenTargetThrow.cs:87-119`; ⛔ classify each caller with **Roslyn** find-references (some want HEALTH: carry, medic). ② `LogicalStance.Of` answers Prone for a man down, so aim (0.85 m today) matches his `Downed` hit boxes. No architect question needed — Injury States §6 I4 already says it |
| **`CE-2319`** | behaviors | an attack advances UPRIGHT AT A WALK under heavy fire | ⚠ part is mine: `pc-defend-house` authors `PlatoonAttack Speed 2`, overriding the rush 4. Gait chosen once at issue (`LocomotionMoveTo.cs:93-94`); no go-to-ground. ⇒ **write an architect question first** (touches every play): rush speed under fire · re-pick the gait when `Threatened` changes · one soldier-level reflex (near miss in the open ⇒ down 2–4 s, up, rush). Relates CE-2158, CE-2270/2272 (stress), AQ88 |
| **`CE-2320`** | behaviors | taking cover across OPEN GROUND upright and slow | same mechanism (`PeekAndFireNodes.cs:407,457-476`, `TakeCover.btree.json:47`); a cover move is always a crouched rush; a light weave only in the open under fire (zig-zag: no design record) — in CE-2319's question |
| `CE-2321` | backend | a downed/dead man is pushed and turned by avoidance | `CarKinematicsSystem` query `:80-92` has no out-of-the-fight filter; lean: not integrated until carried/dragged |
| `CE-2316` | ui | the map key in first person flashes and returns to 3-D; M should toggle 2-D↔3-D everywhere | the press read twice (`PossessionStation.cs:102` on `ui`) |
| `CE-2317` | ui + backend | a possessed ZU-23 technical aims, never fires | not located; CE-1206's fix (`51c65bec3`) is not on `ui`; show `CanFire`'s `WeaponHold` on the HUD first |
| `CE-1172` | ui | no baton in the policeman's hand | existing row, live note added |

✅ *2026-10-20, the user: "Lets solve CE-2318 … CE-2319 and CE-2320 would get one architect question."* ⇒ **`CE-2318` FIXED** (Injury
States §8a: 21 target tests on `Injury.IsInTheFight`, a man down lies Prone for aim / sight / the round) · **[AQ96](Architect_Question_96_Movement_Under_Fire.md) APPROVED (R-2061) and BUILT** (§6: `UnderFireMove` 418 +
`UnderFireMoveSystem` — rush under fire, crouch mid-move, the drop with its outcomes, cover moves crouched; `CE-2319` / `CE-2320` closed;
live `uf-alone-under-fire` PASS; `uf-squad-under-fire` — see AQ96 §6) · **scenario `header.description`** handed to backend
([handoff](batches/HANDOFF_Behaviors_To_Backend_Scenario_Description.md); behaviors fills its own scenarios once the round-trip lands).
✅ *2026-10-20, the user: "Approved, go"* ⇒ **[AQ97](Architect_Question_97_Default_Reflexes_Soldiers_And_Civilians.md) (R-2062) BUILT, `CE-2322` closed**: every lone soldier reacts by default (`BasicInfantrySop` on the soldier types, gated off under a live commander or aboard), `InertSop` = the explicit "no response", a civilian's fear is `CivilianFearSystem`'s and `CivilianSop` reacts off his routine; live `df-lone-soldier-reflex` + `df-civilian-ordered` PASS; TakeCover now drops a man fired on with no cover point (Eqs §6 A8).
⭐ next: Q95's behaviors half (`CE-2315` E2), N.3's ladder. The ui/backend rows wait for those lanes unless the user hands them over.

### N.3 Still open from the plan

L1 street panic · L3 tank vs AT teams · L5 FPV on the convoy (ports of passing demos) · L7 full (now: E3, guns off the terrain, after
Q95's ring lands) · the flagship's air beats (no REQUEST came) · `CE-2304` (men stopping ~6 m short in `sq-defend-house`, unmeasured).

### N.4 Traps learned 2026-10-20 *(add to §5 when touched)*

| trap | |
|---|---|
| ⛔ an enum in a scenario's params JSON parses BY NAME — a number drops the whole order silently | `PlatoonAttack Preset` (CE-2277) |
| ⛔ a BasicPlatoonSop hold-row guard needs the generator goldens + a `BTreeCallShapeTests` row; ⛔ keep the btree JSON's indent (`indent=1` if it starts `{\n "`) or the whole file rewrites | CE-2314 |
| ⚠ `utility-demo-check.py` WARNS about entities off the terrain's grids — off the grids a unit cannot be seen or collide; loud sounds still reach (Q95 B) | the L7 retraction |
| ⚠ kill a stray cluster with `ps -eo pid,args \| grep "[H]rot.ClusterRunner/bin" \| awk '{print $1}' \| xargs -r kill` — ⛔ `pkill -f` matches your own shell (exit 144) | live runs |
| ⚠ demo-city: soldier vision 150 m; staff merges elements within 50 m (`FoldElement`); `CombatPosture "{}"` walks to the origin | the ladder |

---

*⛔ Below: as of 2026-10-19 — §0/§1 superseded by §N; §2 is the plan's record.*

## 0. Start here

| | |
|---|---|
| ⭐ lane | `behaviors` (stable branch `behaviors`). Push with `git push -q origin behaviors`. No PRs |
| ⭐ written at | `fc2107aaa` (behaviors) — after merging `backend` at `6b7e1ed81`; `ui` and `coordinator` had nothing new |
| ⭐ ids | tracker next free **`CE-2303`**, rulings next free **`R-2059`** *(2026-10-19, after `CE-2302`)* (block tables: `Blueprint_Issues_Tracker.md` "ID blocks", `RULINGS.md` §0a). ⭐ Component ids: per-lane blocks ✅ approved (R-2056) — behaviors takes **418–433** |
| ⭐ first moves | `git fetch origin backend ui coordinator` and merge what moved (⭐ §6: an ungated merge was aborted — redo it and gate it) · read §1 · then §2 step 1 |
| ⚠ environment | a fresh cloud VM: `bash scripts/cloud-bootstrap.sh` if `dotnet` is missing; the codebase-memory MCP may be down — use `codebase-memory-mcp cli <tool>` (no `check_index_coverage` there: say so in any exhaustive claim) |

## 1. Where it stands *(2026-10-19)*

| | |
|---|---|
| ⭐⭐ **the command chain — BUILT, runs live** | `CE-2289` ROE up the chain · `CE-2262` SL-0 · `CE-2291` CC-A · `CE-2292` CC-B · `CE-2293` CC-C · `CE-2295` CC-E · `CE-2294` CC-D · **`CE-2288`** one read route over every behaviour slot (`GET /entities/{id}/slots`). `cc-platoon-bound` passes live and as a twin. 📄 [Command Chain §8a](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md) |
| ⭐ **wave 0** | `CE-2273` proficiency ✅ (§8a) · **`CE-2274` proficiency in peek-and-fire ✅** (§8b: exposure, bursts, who switches by tier — PC-15, reaction delay on a new target) · `CE-2282` sectors ✅ (§13c; leans DA-26…DA-30 await the user) · `CE-2281` roles 🟡 part (§12c; fire-team split and role takeover BLOCKED, §6) — 📄 [Defend & Attack](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) |
| ⭐ **look direction** | **`CE-2297` LD-3 slice 1 ✅** — one `Look` node (`Hrot.AI.Behaviors/Brains/LookNodes.cs`) in `CombatPosture` and `PersonHold`; the eyes go to: the sound of the shot if he was just shot at → the top threat → a fresh heard sound → his sector (scanned in 2 s steps) → `watchTowards`. The operator's `POST /entities/{id}/sector` (forwards to the brain's node). **`CE-2298`** `ld-sentry-surprise` + `ld-sentry-surprise-360` ✅ pass live (no in-process twin yet). 📄 [Look Direction §3.3a, §3.4](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) |
| ✅ **the known-rot of 2026-10-19 is fixed** | a squad's Hold / Watch member no longer ends his `CombatPosture` on arrival — `stay` (`CE-2297` slice 2a, 2026-10-19, merged backend at `f2b249e27` first) |
| civilians (`CE-2235`…`CE-2240`, `CE-2256`, `CE-2249`…`CE-2251`) | a different programme on this lane, still open. ⭐ backend unblocked `CE-2249` / `CE-2250` / `CE-2251` (speech, mood, the baton blow — `CE-3264`…`CE-3266`, Civilians §7.18). Not part of this resume |

## 2. The plan *(✅ approved 2026-10-19 — R-2060; ONE copy of it: [Urban Battle §5](../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md))*

⭐ The goal: attack + defend (platoon against platoon, over an AREA — Platoon §5a) and the urban battle at the end. The plan's eight
steps live in Urban Battle §5 — ⛔ do not copy them here. Where it stands:

| | |
|---|---|
| ✅ done before the plan | `CE-2276` E-1, E-2, E-3a (FallBack), E-3b (block the stair) — §8d, rails red-proved; `pc-defend-dry` live (region 0) |
| ✅ step 1 — done 2026-10-19 | the frame lean, [Defend & Attack §8c D-2l](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) (DA-77…DA-84, R-2059) — built, rails red-proved, as-built folded; ⚠ not run live yet (no demo exercises a Regular squad at windows with a real survey — `sq-defend-house` is the one to watch) |
| ✅ step 2 — done 2026-10-19 | battle positions over an area, Defend & Attack §8d **E-4** (DA-85…DA-92; the obstacle rule per the user) — built, rails red-proved, as-built folded; ⚠ not run live |
| 🟡 step 3 — BC-1 done 2026-10-19 | one room, one squad (`CE-2257`, [Building Clearing §7.1a](../designs/group-maneuvers/DESIGN_Building_Clearing.md)) — built, rails red-proved (11/11 mutants killed); ⚠ not live (no CQB house yet) |
| ✅ step 3 — BC-3 done 2026-10-19 | `ClearBuilding` `CE-2259` — [Building Clearing §7.2a](../designs/group-maneuvers/DESIGN_Building_Clearing.md): room by room, one drill (`RoomDrill`), stairs, the teams turning, a blocked room skipped; ⚠ not live |
| ✅ step 4 — done 2026-10-19 | the request round trip, Casevac first (`CE-2305`, [Command Chain §8c-a](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md)): the staff asks once per man down, `PlatoonDefend` `DAnswer` sends the free reserve to `SquadRecover` him; ⚠ not live |
| ✅ step 5 — done 2026-10-19 | the scene's reactions (Urban Battle §5): ✅ ④ trigger man + ambush — `CE-2306`, [Defend & Attack §8e.1](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md), not live · ✅ ⑤ convoy halt — `CE-2307`, [Squad Tactics §5b.1](../designs/group-maneuvers/DESIGN_Squad_Tactics.md), not live · ✅ ⑥ insurgent spotter — `CE-2308`, [Indirect Fire §3.11a](../designs/indirect-fire/DESIGN_Indirect_Fire.md), not live |
| ✅ step 6 — done 2026-10-19 | ② finding the mortar — `CE-2309` (a heard launch is a SPOT) + `CE-2310` (the platoon asks FireSupport / Reinforce), [Command Chain §8d](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md) · ③ armed search — `CE-2311` · ① the headquarters play `SupportCommand` — `CE-2312`, [Command Chain §8d-a](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md); ⚠ none live yet; ⚠ the ZU-23 shares the mortar's sound class ([note](batches/NOTE_Behaviors_To_Backend_Launch_Sound_Class.md)) |
| ✅ step 7 — done 2026-10-20 | `PlatoonAttack` `CE-2277` ([Defend & Attack §8f.1](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md)) · ✅ `pc-attack-dry` live 12/12 · ✅ `pc-defend-house` live — it found AT-13 (the platoon SOP held the attack) and DA-98 (a line hit before it is set never opened fire) |
| ✅ step 8 — done 2026-10-20 | the ladder + the flagship on demo-city ([Urban Battle §6](../designs/group-maneuvers/DESIGN_Urban_Battle_Demo.md)): ✅ L2 `uc-convoy-hit` (halt + dismount) and `uc-convoy-hit-ifv` (push-through) · 🟡 L7 `uc-request-chain` (9/9 once; a coin-flip on demo-city's size) + ✅ `-dry` 9/9 · ✅ L4 `uc-a10-zu23-dry` · ✅ L6 `uc-reinforce-dry` · ✅ **the flagship `uc-urban-battle` 6/6, nothing faked**. Found live and fixed: `CE-2313` (a brain node read every moving vehicle as standing; a squad got out of a pushing carrier), `CE-2314` (the platoon SOP held the HQ), DA-98, AT-13 |
| still open | L1 street panic · L3 tank vs AT teams · L5 FPV on the convoy (ports of passing demos, not built) · L7 full on a bigger terrain · the flagship's air beats (no REQUEST came this run: the HQ's assets are wired, the fight did not call them) · a burst is stamped on its SHOOTER whose `DetonationAudibleRange` may be 0 ([note](batches/NOTE_Behaviors_To_Backend_Launch_Not_Heard_Live.md), backend's) |
| backend, in parallel | BR-14 / BR-15 / BR-16 — [handoff](batches/HANDOFF_Behaviors_To_Backend_Urban_Battle_Data.md) (filed 2026-10-19) |
| still open, not in the plan | `CE-2304`: the fifth `sq-defend-house` run's men stopping ~6 m short — cause unmeasured |

## 2b. ⛔ HISTORY — the next steps as of the night run *(superseded by §2)*

⭐ **Read each slice's design section end to end before building it** (R-129); fold every deviation back (as-built rule); the
feature's own rails first, then new rails in the feature's suite; builds and tests in the background; red-proof each rail with a mutant.

| step | id | what | design | notes |
|---|---|---|---|---|
| ~~1~~ ✅ | `CE-2297` (slice 2a) | ✅ **BUILT 2026-10-19** (a `stay` flag on the whole `CombatPosture`, Look Direction §3.3a as built) — ⭐ **a Hold / Watch member stays** (✅ approved, R-2055): the runner's `SquadManeuverRunner.Hold` asks `CombatPosture` with a `hold` flag so arriving does not end it — his eyes and his fight stay until the squad's next order | [Look Direction §3.3a](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) known-rot + lean | ⚠ **measure first**: which drills advance a phase on a member's task ENDING (a `BehaviorFinished` / `ProgressKind.Set` from the staff — CC-C sends PROGRESS on task end!) rather than on his position. ⛔ CC-C's staff composes PROGRESS when a member's task ends — a member that no longer ends may stop the squad's SET report. Grep `SquadSensing`, `StaffComposerSystem`, the plays' `PhaseFor`. Approved (R-2055); if it reaches the staff's reports, say so in the report |
| ~~2~~ ✅ | `CE-2302` | ✅ **BUILT 2026-10-19** — `MemberArc` on the member order; the runner publishes `SetEngagementAreaEvent` (origin Superior) on issue, an order without an arc withdraws, and `SubordinateOrders.Release` (the run's one release) withdraws every man's arc when the commander's run ends. First user: the crossing's security men cover their side of the road | [Defend & Attack §13a-1](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) (DA-31…DA-33) | ⚠ the withdraw lives in the release, not in the play's End — a task clear never reaches End (measured; DA-32) |
| ~~2b~~ ✅ | `CE-2301` ① (`4c5ebbc97`) | the report inbox on every node: `StaffInboxSystem` logs before its `CommandPicture` guard, and a cross-role capability `CoreInfrastructureCapabilities.ReportInbox` declared ONLY in the brainless plans (SimHost, IG) — one inbox per node | [Command Chain §8b](../designs/group-maneuvers/DESIGN_Command_Chain_Example.md) (R-2058) | part ② (picture replication) is backend's — [note](batches/NOTE_Behaviors_To_Backend_CommandPicture_Replication.md); ui's "not on this node yet" — [note](batches/NOTE_Behaviors_To_Ui_Command_Display_D4_Answer.md) |
| **3** | `CE-2297` slice 2 + `CE-2298` | the march's file-place glances, the `Sentry` JSON tree's eyes (the crossing security sides ✅ came with `CE-2302`), `ld-crossing-security`; an in-process twin for `ld-sentry-surprise` (`SquadTacticsScenarioTests` is the twin precedent) | [Look Direction §3.3, §3.4](../designs/look-direction/DESIGN_Look_Direction_And_FOV.md) | `ld-watch-last-window` (PC-16) and `ld-room-sectors` wait on the clearing |
| **4** | `CE-2275` | **SquadDefend** (DA-D): `DefencePlan`, `FindDefensivePosts` / `FindBuildingPosts` / `FindRoofPosts`, set-back window posts (DA-6), `OccupyPost` / `Displace`, HoldFire and its lift (DA-7); `DefendArea` maps to it | [Defend & Attack §3, §4.2, DA-4…DA-7](../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) | ⭐ §8c WRITTEN, **D-1 BUILT 2026-10-19**; D-2 next (§8c Slices). ✅ DA-35's alone lift built (user-accepted). Steps 1–2 are built. WorldSurvey (BR-12) is built for the building posts |
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
| backend — **asked, open** | ⭐ [sight beyond 100 m](batches/NOTE_Behaviors_To_Backend_Sight_Beyond_100m.md) (an exposed walker at ~113 m never acquired) · (the sectors note ④ the fire-team byte: ✅ built since, R-3019) · `CE-2287` brain-side geometry (Q94, waits on the user) | — |
| ui | ✅ UI-4…UI-6 built on `ui` (`cqb-village`, the command picture on the map, the Reports window — not yet merged here; UI-7 offer open) · **UI-1…UI-7** buildings, `house-3f`, the CQB house, `cqb-village`, the command-picture map, the Reports window · **UI-8…UI-12** demo-city quarters | [platoon demos](batches/HANDOFF_Behaviors_To_Ui_Platoon_Demos.md) · [quarters](batches/HANDOFF_Behaviors_To_Ui_Demo_City_Quarters.md) |
| coordinator | ⚠ an editor rail red since CE-4031 (`ThereIsOneNetworkIdResolverTests` → `TrafficReplicas.cs`) — reported, not ours | [note](batches/NOTE_Behaviors_To_Coordinator_NetworkId_Rail_Red.md) |

## 5. Measured traps *(2026-10-18 / 19 — re-measure before relying on one)*

| trap | where it bit |
|---|---|
| ✅ fixed 2026-10-19: `CombatPosture` ended on arrival with nothing to hunt — now only without `stay` (an operator's posture, an advance) | a squad's Hold / Watch member sends `stay` |
| ⛔ adding a shipped `*.btree.json` asset needs the AI generator goldens: `AI_REGENERATE_SNAPSHOTS=1 dotnet test Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests --filter Golden` + bump the corpus count in `BTreeGoldenCorpusTests` | CE-2294 shipped `BasicPlatoonSop` without them; fixed at CE-2297 |
| ⛔ `SquadManeuverTests` has `Ticks(Squad, int n)` = n FRAMES; an integer literal there is frames, not seconds (BC-1's seconds helper is `TickFor`) | BC-1's hostile-in-the-room window was 4 frames ⇒ its mutant survived (2026-10-19) |
| ⛔ `HoldOrder` has THREE fulfillers (`PersonHold`, `InfantrySquadHold`, `WeaponSystemHold`) and `SopParamsRoundTripTests.CE2079` requires each to read every field it is sent | a `Towards` field on it was reverted |
| ⛔ the CycloneDDS IDL generator emits a file's enums into one scope — member names must be unique across enums in one file | `OccurrenceSlotCatalog` (`OwnerUnknown` / `RoleUnknown`) |
| ⛔ `GET /entities/{id}/memory` returns `modalities` as a BITMASK (Visual = 1), not text | the sentry check's first runs |
| ⚠ bt-range is 200 m across; House A / B sit at y = 100, the Solid Block at x 100–120 / y 150–170, the Low Wall at x 100–120 / y 40 — the **clear strip is x ≥ 170** | `ld-sentry-surprise` |
| ⚠ an authored `PerceptionReceptor.VisionRange` cannot widen the implicit visual sensor's search radius (fixed at spawn from the TKB — `VisualPerception.ImplicitEntry`); and ~113 m was never acquired on bt-range (asked of backend) | the sentry demo crosses at ~70 m |
| ⚠ `utility-demo-check.py --launch` refuses a runner older than its sources — merge another lane only AFTER a live run, or rebuild `Hrot/Runner/Hrot.ClusterRunner` | the night run |
| ⚠ a container restart kills background tasks — re-run them; the repo and `bin/` survive | the night run |
| ⚠ flaky: ST-030 `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` (green alone) | every SimHost run |
| ⛔ (2026-10-18) mission plans: ≤ 8 phases, one unit, operator only · an insurgent with no order only hides · mortars have no firing sound · `GET /orders` is the catalogue, not issued orders · demo-city's houses are one storey | the platoon / urban demos |

## 6. The user's rulings on the night's leans *(2026-10-19)*

🔒 User, `2026-10-19`: *"Pls also record there i am approving your leans. Backend might have already actend on those."*

| lean | ruling | state |
|---|---|---|
| a squad's Hold / Watch man STAYS (`stay` flag on the whole `CombatPosture`) | ✅ **R-2055** | ✅ built 2026-10-19 (`CE-2297` slice 2a) |
| the sector leans DA-26…DA-30 | ✅ **R-2054** | ✅ already built as leaned (`CE-2282`) — nothing to change |
| the fire-team carrier (`CE-2281`) | ✅ **R-3019** (backend recorded it) | ✅ backend BUILT the wire half — a `FireTeam` byte on `EntityInfo` + a scenario key (`2c58000ea` on `backend`) — ✅ merged into `behaviors` at `f2b249e27` (2026-10-19) — the plays' split by team is buildable |
| per-lane component-id blocks | ✅ **R-2056** | behaviors takes new component ids from **418–433**; ⏳ the table still belongs in `GlobalComponentIds.cs`'s header (backend's file — check whether backend wrote it) |
| TC-7 — a held throwable is lethal only within its throw (`CE-2256`) | ✅ **R-2057** | ⏳ not built |
| role takeover (`CE-2281`) | — | still needs a weapon pick-up design first (Defend & Attack §12c) |

⭐ **First move next session:** merge `backend` (17 commits at `d1e524cd6`: R-3017…R-3019, the fire-team byte, Q94's "the muscle fires or
refuses") and `ui` (UI-4 `cqb-village`, UI-5 the command picture on the map, UI-6 the Reports window, CE-1193 the lean drawn) — a
merge was started on 2026-10-19 and aborted UNGATED when the session was stopped; it auto-merged with no conflicts. Gate it (SimHost,
Toolkits, NED, Editor) before committing.

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
