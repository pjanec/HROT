<!--STATUS
state: LIVE
build-state: BUILT (2026-11-17, agg-behaviors — S1…S4 built; S5 DROPPED by the user, R-8365; §11 is the as-built, read it first)
updated: 2026-11-17 (as built: §11)
current-answer: §11 AS BUILT (deviations, rails, scenarios) · then §0 the decision · §3–§6 the UML (as amended by §11) · §7 the sub-questions · §9 cross-lane edits · §10 findings
stale-below: §8 row S5 and X2-F's "S5 measures it" — SUPERSEDED by R-8365 (§11). §2 claim 4 — SUPERSEDED (§11: the ingress applies a Simulation-phase publish TWO frames later).
known-rot: §3 places PlayFor on TacticalIntentResolutionSystem — as built it is OrderReissue.PlayFor (R-8367). §4's frame numbers assume a one-frame ingress lag (as built: two, R-8368).
known-conflict: DESIGN_Stochastic_Units_And_Resolution.md §13 X-1 / X-3 / D-P7 say a split goes ONE LEVEL at a time; the as-built split FLATTENS the composite to men (StochasticUnitFill.Leaves) — this design scopes X-2 to the squad echelon and files the conflict (§10 F-X2-1), it does not resolve it
related-designs:
  - DESIGN_Stochastic_Units_And_Resolution.md — OWNS X-2 (§13), the split process D-P1…D-P7 (§16a, D-P6 the handover), SV-10 (§17.1: restart from the current situation; an untyped order cannot switch), the manager and switch as built (§23.4) and our routing (§23.5–§23.5c, F-5); this file answers X-2 only
  - DESIGN_DP3_Planned_Posts.md — OWNS the planned posts (PlannedPosts 138) and DP3-D ②, which §7 X2-F here builds (the re-resolved Defend carries them as D-2f Posts, MaxPosts 8 → 13)
-->

# X-2 — a unit keeps its order when it switches resolution *(agg-behaviors, `2026-11-17`)*

🔒 User, `2026-11-17`: *"Yes, I agree, including the X too"* — X-2 is agg-behaviors'. The ask (§13 X-2, SV-10, RV-17): when a stochastic unit
splits into men, or men merge back, the unit goes on doing what it was ordered to do — under the play that fits its new resolution.
Links: [main design](https://github.com/pjanec/HROT/blob/exp/agg-behaviors/docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md) ·
[DP3](https://github.com/pjanec/HROT/blob/exp/agg-behaviors/docs/designs/aggregate-units/DESIGN_DP3_Planned_Posts.md).

## 0. The decision *(under a page)*

**Decision.** A RECONCILER, not an event handler: our F-5 system (`UnitSopResolutionSystem`, renamed `UnitResolutionFitSystem`) gains an
ORDER arm. Every frame, for each owned `AggregateUnit`, it compares the play the unit RUNS with the play its CURRENT order resolves to at its
CURRENT resolution (the one routing table, `ResolutionRoutingTable`); equal ⇒ nothing (one hash compare, 0 B). Different ⇒ it re-issues the SAME
order — kind from `CurrentOrders.Of` (RV-17), params from `BehaviorStartRecord` / `PausedTask`, the SAME origin and `RequestId` — as one
`AssignBehaviorEvent { IsResume = true }`. A physical squad is re-issued only once its men are in its `UnitRoster`. The ROSTER is formed by
agg-backend's `Handover` (one `CmdAssignSubordinate` per released man — the one hierarchy path, `UnitHierarchySystem`) and dropped by its `Merge`
(the unit's `UnitRoster` removed). The order restarts from the current situation (SV-10); a SquadDefend restarts with the planned posts as D-2f
`Posts` (DP3-D ②) so the men are Set at once. Commanders stop re-sending across a switch (their checks match by `RequestId`, either form).

**Rejected** *(one line each)*
- a hook in agg-backend's `Handover` that re-issues the order — §16b: the manager never knows tactics; and the roster it needs is formed one frame LATER (the hierarchy is an event), so a same-tick hook would start the play on an empty roster.
- a "switched" event from the manager to subscribe to — a second record of a fact `AggregateUnit.Resolution` already holds; an event is lost on a seek, a reconciler re-derives (F-5's R-8327 argument).
- leaving it to the COMMANDER's re-send — only commanded units have one; an operator's or an authored order has none; and the re-send carries no planned posts and a NEW id (restarting the defenders, CE-2304).
- writing `UnitRoster` directly in `Handover` — a second implementation of `UnitHierarchySystem`'s assign (wire marking, squad state, capacity) beside the one that exists.
- re-issuing through `AssignTacticalIntentEvent` (`Order.<K>`) — the table would resolve it the same, but the station's request would be answered twice (`ReplyTo`, `BehaviorIngressSystem.cs:399`); `IsResume` exists for exactly "a restarted order keeps its id; answered already" (`:559`).
- phase mapping of a play's progress across the switch — SV-10 ruled it out unless a play proves it needs it; none does yet.
- re-resolving platoons and companies now — the split flattens them to men (30 > `UnitRoster.Capacity` 16, and `PlatoonDefend` orders squads, not men); needs the one-level split first (§10 F-X2-1).

**Design docs checked**
- `DESIGN_Stochastic_Units_And_Resolution.md` §13 X-2, §16a D-P6, §17.1 SV-10, §19 (RV-17), §23.4, §23.5–§23.5c — applies: owns the requirement; ⚠ its X-5 / D-P7 "one level at a time" is not what the split does (§10).
- `DESIGN_DP3_Planned_Posts.md` §6 DP3-D, §9 — applies: X2-F builds DP3-D ②.
- `docs/DESIGN_Orders_Params_Entity_Classes.md` §15 (R-2088 derived `CurrentOrder`, R-2089 a play's kind, R-2090 one `RequestId`) — applies and DECIDES what is re-issued and how it is recognised.
- `docs/DESIGN_Decision_Layer.md` §4.1 / §4.9a (the gate, reactions pause a task) — applies: decides X2-E (a re-issued order ends the old resolution's reaction).
- `.dev/_DONE/commander-subordinates/DESIGN.md` §5.2 (`UnitHierarchySystem`, the one roster writer) — applies: decides X2-B (the roster through `CmdAssignSubordinate`).
- `docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md` §7a CC-D / CE-2304 (a re-send restarts a posted defender) — applies: decides X2-H.
- `DESIGN_Aggregate_Squad.md` (FusionSquad) — does not apply: deferred (R-4253); no fusion row is touched.

## 1. INVENTORY *(graph AND grep; `scripts/cbm.sh`, project `home-user-HROT`)*

⚠ **The graph is STALE for this area** — it predates the aggregate code: `search_graph name_pattern=".*(ResolutionManager|BusResolutionSwitch|UnitSopResolution|UnitPostPlanning|CurrentOrders|UnitHierarchy).*"`
returned only `.dev` sections and `UnitHierarchySystemTests`; a re-index (`index_repository`) **aborted** twice (*"files changed while the run was in flight"* — the
builder is working). ⇒ every row below is grep-confirmed, and every absence claim is grep-only. ⚠ Coverage could not be checked (`check_index_coverage` is not in the CLI).

| query | total | what it found that matters |
|---|---|---|
| `search_graph ".*Roster.*" Class` | 8 | `UnitRoster` (Fdp.Core, the commander's list, cap 16, NoScenario, "derived from the bottom-up `UnitSubordinate` records"), `SquadRoster` (Map3D view), `NodeRoster` (orchestrator) — ⭐ no aggregate roster |
| `search_graph ".*Handover.*" Class` | 4 | `BrainHandOverSystem` (brain OWNER handover — unrelated), `TrafficHandover` — the split's `Handover` is a method (grep: `UnitResolution.cs:408`) |
| `search_graph ".*BehaviorStart.*"` · `".*SubordinateOrder.*"` Class | 1 · 2 | `BehaviorStartRecord` (name, json, instance, `RequestId`); `SubordinateOrders` (the commander's record, brain-local) |
| `search_graph ".*Resolution.*" Class` | 17 | `TacticalIntentResolutionSystem` … ⛔ no `ResolutionManagerSystem` (stale) |
| `search_graph ".*(ReResol\|Reissue\|Reresol).*" Class` | 1 | `RoslynClrSignatureResolver` — ⭐ nothing re-resolves an order |
| `search_code "new CmdAssignSubordinate"` | 42 grep matches | publishers: `OneManCommand`, `EntityInfoIngressTranslator` ×3, `UpdateEntityAttributeRequestSystem`, `Edits`, `VehicleCommandSystem` — ⭐ the one roster writer is `UnitHierarchySystem` (grep `UnitRoster.Add(`: no production caller) |
| `search_code "IsResume = true"` | 1 | `BehaviorIngressSystem` (`:559`, a paused order restarted keeps its id) |
| grep `AggregateChild` (production) | 6 files | `UnitResolution.cs`, `PlannedPosts.cs`, `HeldChildren.cs`, `SplitProgress.cs`, `HeldManGizmo.cs`, `HrotSharedComponentRegistry` — ⭐ no behaviour code reads it; the children join no roster |
| grep `KeyFor(\|StillRoutedRunning(\|GaveRouted(` | 6 sites | `CompanyBehaviors.cs:445, :932, :939` · `PlatoonAttackBehavior.cs:586` · `UrbanMovementBehavior.cs:183` · `PlatoonConvoyBehavior.cs:318` — the commander checks that see the member's resolution |
| grep `beh.Register("InfantrySquad…` (generated registrar) | 3 | `InfantrySquadAttack` · `InfantrySquadHold` · `InfantrySquadMove` — ⭐ NO `InfantrySquadDefend` / `…BreakContact` |
| grep `Fulfils = ` | 23 plays | Defend: `SquadDefend` · `PlatoonDefend` · `CompanyDefend` · `StochasticUnitDefend`; BreakContact: `BreakContact` · `StochasticUnitBreakContact`; … |

## 2. The claim table

| # | the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|---|
| 1 | nothing re-issues an order on a switch; `Handover` maps only health / stress / ammo | ✅ `UnitResolution.cs:408-429` | ✅ X-2 / SV-10 / RV-17 say it should (§13, §17.1, §19) |
| 2 | a unit's order is DERIVED: kind from the running (or paused) play, id from the start record (or paused task) | ✅ `CurrentOrders.cs:41-53`, `BehaviorStartRecord.cs:26-40`, `PausedTask.cs` (JsonParams, Origin, RequestId) | ✅ R-2088, RV-17 |
| 3 | the switch writes `Resolution` in the manager, serially, 1 Hz; the F-5 system runs after it in the same frame | ✅ `UnitResolution.cs:279-284, :301-305`; `CgfLogicPack.cs:293-305` | ✅ §16b, R-8327 |
| 4 | the ingress runs in `Input`, before every `Simulation` system ⇒ an assign published in frame N is applied before the reconciler runs in N+1 (no double start) | ✅ `BehaviorIngressSystem.cs:39` | ⛔ searched, no design states the frame order — rail S2-c pins "one start per switch" |
| 5 | the roster is written ONLY by `UnitHierarchySystem` from `CmdAssignSubordinate`; it runs at the tail of `Simulation` | ✅ `UnitHierarchySystem.cs:146-210`, `CgfLogicPack.cs:73-77` (CE-221: declared last) | ✅ commander-subordinates §5.2; `UnitRoster` doc ("only `UnitHierarchySystem` maintains the roster") |
| 6 | a destroyed subordinate leaves the roster through the `DestructionOrder` cascade — which only the LIFECYCLE path publishes | ✅ `UnitHierarchySystem.cs:53-89`; `EntityLifecycleModule.cs:327-345`; `BusResolutionSwitch.Remove` destroys in place without `Lifecycle` (`UnitResolution.cs:361-367`) | ⛔ searched, none |
| 7 | a stochastic unit that still HAS a `UnitRoster` (even Count 0) is skipped by the staff composer's stochastic arm ⇒ no SITREP / Set / TaskFailed | ✅ `StaffSystems.cs:126-129` | ✅ §23.5 D-2 |
| 8 | the routing table at Physical is the `<Class><Order>` name rule: a squad resolves Move / Attack / Hold, but **Defend and BreakContact are REFUSED** | ✅ `ResolutionRouting.cs:13`, `OrderResolver.cs`; registrar has no `InfantrySquadDefend` | ✅ R-8302 (APPROVED) proposed physical rows "built from `Fulfils` + the echelon class" — never built |
| 9 | the stochastic plays read the neutral DTO; every physical play of kind K reads K's neutral DTO strictly | ✅ R-8300 rail `BehaviorParamsValidationTests.R8300_…` (7 kinds, 26 plays) | ✅ R-8300 — ⚠ the REVERSE is not guaranteed: a physical play's JSON (`SquadDefend` with `Posts`, `MinRange`) is refused by `DefendOrder`'s strict read ⇒ the merge must PROJECT |
| 10 | a commander's routed check compares the RECORD's key with the key for the member's CURRENT resolution ⇒ after a switch it differs and the commander re-sends with a NEW id | ✅ `CommandNodes.cs:160-186` (`GaveRouted`, `StillRoutedRunning`), `IdFor :189` (time-keyed) | ✅ CE-2304 (a re-send restarts a posted defender — the defect to avoid) |
| 11 | a re-issued order with `IsResume` keeps its id and is not answered again; an order outranks a running reaction and drops the paused task | ✅ `BehaviorIngressSystem.cs:389-400, :331-337`; `PausedTask` doc ("dropped when an order replaces … the reaction") | ✅ Decision Layer §4.1 |
| 12 | the split FLATTENS a nested composite to men — a platoon splits into 30 men, not 3 squads | ✅ `StochasticUnitFill.cs:31-50` (`Leaves`), `BusResolutionSwitch.Materialise :369-398`; `agg-split-on-contact` splits a 30-man platoon | ⛔ CONFLICTS with X-5 / D-P7 "one level at a time" (§13, §16a) — F-X2-1 |
| 13 | the squad's blackboard (`SquadDefendParams.Authored`, `MaxPosts` 8) is RECORDED: it lives in `BlueprintBlackboard{256…16384}`, `[DataPolicy(NoScenario)]` = recorded; the component LAYOUT does not change when the block grows | ✅ `BlueprintBlackboard16384.cs:15-17`, `SquadDefendBehavior.cs:28-57, :285-298`; `RootParamsAccess.cs:392` refuses a slot smaller than the block | ✅ SV-12 (a later change is a deliberate, versioned break) — ⚠ the replay of an OLD recording mid-`SquadDefend` is NOT measured (S5 measures it) |
| 14 | `TkbChildSlot.Designation` is per composite slot; `StochasticUnitFill.Leaves` keeps it per leaf | ✅ `TkbCompositionDef.cs:5-20`, `StochasticUnitFill.cs:28-50` | ✅ X-3 / SV-3 (the same composite, the same children) |
| 15 | **the proven solution** for this problem class (multi-resolution hand-off): the disaggregated entities join the unit's ORBAT and the entity-level C2 RE-PLANS the unit's CURRENT TASK from the CURRENT state; the hand-off carries mission + state, never the plan's internals. The implementation pattern for "state X changed ⇒ behaviour must fit" is a RECONCILE loop (desired vs actual, idempotent) | adopt: the roster (X2-B) + re-issue the order (X2-C) + restart (X2-E) · adopt the reconciler (F-5 already is one) · reject: internals hand-off (SV-10) | ✅ §13 "the proven solution" (state mapping, identity), SV-10 |

## 3. Classes

```mermaid
classDiagram
  direction LR
  class UnitResolutionFitSystem {
    <<EXISTING UnitSopResolutionSystem renamed, Hrot.CGF/Systems>>
    SOP arm - EXISTING F-5, 1 Hz
    NEW order arm - every frame, owned AggregateUnit only
    NEW reaction arm - a reaction of the other resolution cleared
  }
  class TacticalIntentResolutionSystem {
    <<EXISTING Hrot.CGF>>
    SopFor(repo, unit, isBehaviour) EXISTING
    NEW PlayFor(repo, unit, resolution, kind, isBehaviour)
    ReportRefusal EXISTING
  }
  class ResolutionRoutingTable {
    <<EXISTING agg-backend, Hrot.Core/Tkb>>
    Resolve(classes, dis, resolution, order)
    NEW physical FALLBACK rows after the name rule
    NEW IsStochasticPlay(name)
  }
  class OrderReissue {
    <<NEW static, Hrot.AI.Behaviors/Orders>>
    ParamsFor(kind, json, toStochastic, posts) string
    ProjectToNeutral(kind, json) string
    WithPlannedPosts(json, PlannedPosts) string
    BlockReason(repo, unit, target) string
  }
  class CurrentOrders { <<EXISTING CurrentOrders.cs>> Of(world, unit) }
  class BehaviorStartRecord { <<EXISTING recorded>> JsonParams RequestId }
  class PausedTask { <<EXISTING recorded>> JsonParams Origin RequestId }
  class AssignBehaviorEvent { <<EXISTING>> Origin RequestId IsResume }
  class ResolutionManagerSystem {
    <<EXISTING agg-backend UnitResolution.cs>>
    NEW SwitchBlocked seam - automatic switches only
  }
  class BusResolutionSwitch {
    <<EXISTING agg-backend>>
    Handover NEW publishes CmdAssignSubordinate per released man, squad echelon
    Merge NEW removes the unit UnitRoster
    NEW ChildDesignation host Func
  }
  class UnitHierarchySystem { <<EXISTING the one roster writer>> }
  class UnitRoster { <<EXISTING Fdp.Core, cap 16, NoScenario>> }
  class PlannedPosts { <<EXISTING 138, DP3>> TryGet(k) }
  class SquadDefendPostJsonDto { <<EXISTING Hrot.Core>> MaxPosts 8 to 13 }
  class CommandOrders {
    <<EXISTING CommandNodes.cs>>
    StillRoutedRunning GaveRouted - CHANGED resolution-blind by RequestId
  }
  UnitResolutionFitSystem ..> CurrentOrders : kind, origin, id
  UnitResolutionFitSystem ..> BehaviorStartRecord : json
  UnitResolutionFitSystem ..> PausedTask : json if paused
  UnitResolutionFitSystem ..> TacticalIntentResolutionSystem : PlayFor
  TacticalIntentResolutionSystem ..> ResolutionRoutingTable
  UnitResolutionFitSystem ..> OrderReissue
  OrderReissue ..> PlannedPosts : DP3-D 2
  OrderReissue ..> SquadDefendPostJsonDto
  UnitResolutionFitSystem ..> UnitRoster : waits for the men
  UnitResolutionFitSystem --> AssignBehaviorEvent : publishes
  ResolutionManagerSystem ..> OrderReissue : SwitchBlocked = BlockReason
  ResolutionManagerSystem --> BusResolutionSwitch
  BusResolutionSwitch --> UnitHierarchySystem : CmdAssignSubordinate
  UnitHierarchySystem --> UnitRoster : writes
  BusResolutionSwitch --> UnitRoster : Merge removes
```
*What it shows that prose hid: ONE new type (`OrderReissue`, pure functions) — everything else is an arm on an existing owner. The play is chosen by
the SAME table that routes a commander's `Order.<K>`; the roster by the SAME system that writes every roster; the order by the SAME derivation
`CurrentOrders` gives everyone. agg-backend's boxes change in three small places, none of which learns about tactics.*

## 4. Sequence — the split (Stochastic → Physical), a squad holding a house

```mermaid
sequenceDiagram
  participant MGR as ResolutionManager - serial, 1 Hz
  participant SW as BusResolutionSwitch
  participant H as UnitHierarchySystem - Simulation tail
  participant FIT as UnitResolutionFitSystem - every frame
  participant ING as BehaviorIngressSystem - Input
  participant U as squad unit
  Note over MGR,U: frame N - the handover tick, D-P6
  MGR->>SW: Handover(unit)
  SW->>SW: map slot state, Held = 0 per man
  SW-->>H: CmdAssignSubordinate per released man, designation from the composite leaf
  MGR->>U: Resolution = Physical
  FIT->>U: runs StochasticUnitDefend, wants SquadDefend - but roster not complete, wait
  Note over H,U: frame N+1
  H->>U: UnitRoster = the men, UnitSubordinate on each
  Note over FIT,U: frame N+2
  FIT->>U: CurrentOrders.Of = Defend, Superior, id R - start record json
  FIT->>FIT: PlayFor Physical Defend = SquadDefend, json + Posts from PlannedPosts
  FIT-->>ING: AssignBehaviorEvent SquadDefend, Superior, RequestId R, IsResume
  Note over ING,U: frame N+3
  ING->>U: SquadDefend starts, start record keeps id R
  U->>U: D-2f - every man already on his post, Set at once
  Note over FIT,U: later frames - running == wanted, one hash compare, 0 B
```
*What it shows: the re-issue waits two frames for the roster — the hierarchy is an EVENT, so no same-tick hook could have started the play with
its men; the order keeps its id, so the commander (CommandOrders, X2-H) sees the same order and does not re-send.*

## 5. Sequence — the merge (Physical → Stochastic)

```mermaid
sequenceDiagram
  participant MGR as ResolutionManager - serial, 1 Hz
  participant FIT as UnitResolutionFitSystem
  participant SW as BusResolutionSwitch
  participant ING as BehaviorIngressSystem
  participant U as squad unit
  MGR->>FIT: SwitchBlocked(unit, Stochastic) via OrderReissue.BlockReason
  FIT-->>MGR: null - order Defend has a Stochastic row
  MGR->>SW: Merge(unit)
  SW->>SW: sum the men into the slot table, remove the men
  SW->>U: RemoveComponent UnitRoster
  MGR->>U: Resolution = Stochastic
  FIT->>U: runs SquadDefend, wants StochasticUnitDefend - no roster wait
  FIT->>FIT: ProjectToNeutral Defend - drop Posts, MinRange
  FIT-->>ING: AssignBehaviorEvent StochasticUnitDefend, same origin and id, IsResume
  ING->>U: StochasticUnitDefend - occupies from where the unit stands, SV-10
```
*What it shows: the merge needs no wait (the stochastic play has no men) but needs a PROJECTION — R-8300 guarantees physical ⊇ neutral, not the
reverse, so the physical JSON fed back raw would be refused by the strict read. The roster goes in the same tick, or the staff composer stops
serving the unit (claim 7).*

## 6. Modules — who runs it each frame

```mermaid
graph TD
  CGF["CgfLogicPack - every CGF / brain node, AI node only"] --> PL["UnitPostPlanningSystem - EXISTING DP3"]
  CGF --> MGR["ResolutionManagerSystem - EXISTING, 1 Hz"]
  CGF --> FIT["UnitResolutionFitSystem - EXISTING F-5, CHANGED: + order arm every frame"]
  MGR --> SW["BusResolutionSwitch - Handover / Merge CHANGED"]
  SW -.event.-> HIER["UnitHierarchySystem - EXISTING, infrastructure capability, Simulation tail"]
  FIT -.AssignBehaviorEvent.-> ING["BehaviorIngressSystem - EXISTING, Input phase, next frame"]
  ING --> BRAIN["the squad brain phase - per-entity parallel, SquadDefend"]
  EDITOR["editor host"] --> CGF
  PLT["a split PLATOON - echelon 1 or more"]:::dead -.no roster, no re-issue, F-X2-1.-> FIT
  classDef dead stroke:#c33,color:#c33,stroke-dasharray: 5 5
```
*What it shows: the order arm rides the registration site that already carries the manager and F-5 (`CgfLogicPack.cs:293-305`), so every host that
splits also re-resolves — by construction (the editor builds the same pack). The hierarchy is a separate capability declared LAST, which is why the
roster lands a frame after the handover. ⚠ The dashed edge: a platoon split is NOT reached by this design — its men are not its squads (§10).*

## 7. The sub-questions — each with a lean *(lane decisions R-8353 … R-8363, for the user's review)*

| # | question | ⭐ lean | blast radius | would change it |
|---|---|---|---|---|
| **X2-A** · **R-8353** | who owns the re-resolution | **ours, a reconciler**: F-5's system (renamed `UnitResolutionFitSystem` — a Roslyn rename) gains the order arm, every frame over owned `AggregateUnit` units; wanted = `TacticalIntentResolutionSystem.PlayFor(unit, Effective resolution, kind)` (beside `SopFor`, the same table); running ≠ wanted ⇒ re-issue. A PHYSICAL unit is re-issued only when every live released child (`AggregateChild.Unit == unit`, `Held == 0`) is in its `UnitRoster`. Steady state: one hash compare per unit, 0 B (R-2084) | `Hrot.CGF/Systems` (rename + one arm), `TacticalIntentResolutionSystem` (`PlayFor`), `CgfLogicPack` (the registration line) | the user preferring an explicit switch event from the manager (then F-5 moves too) |
| **X2-B** · **R-8354** | the roster at handover / merge | **agg-backend's switch, through the one hierarchy path**: `Handover` publishes `CmdAssignSubordinate { man, unit, designation }` per released child — squad echelon only (`AggregateUnit.Echelon == 0`, ≤ 13 ≤ `UnitRoster.Capacity`); the designation from a host Func `ChildDesignation` (= `StochasticUnitFill.Leaves[slot − 1].Designation`, wired in `CgfLogicPack` beside `ChildType`), `FireTeam.None` (no behaviour reads it — grep). `Merge` removes the unit's `UnitRoster` after removing the men (the men's side goes with them; claim 7 is why the component itself must go) | agg-backend: `UnitResolution.cs` `Handover` (+5 lines), `Merge` (+1), one init property; `CgfLogicPack` one line | the user ruling the hierarchy a TACTICS concept the manager must not touch ⇒ then a behaviors-side system observing `Held == 0` children publishes the same event (stateless, same timing) |
| **X2-C** · **R-8355** | what is re-issued | **the same ORDER**: kind = `CurrentOrders.Of(unit).Kind`; origin and `RequestId` kept (R-2090: one id); params = `BehaviorStartRecord.JsonParams` (or `PausedTask.JsonParams` when paused). Split: the json as it is (the stochastic play read the neutral DTO; R-8300 says the physical one reads it). Merge: `OrderReissue.ProjectToNeutral` — relaxed read into the kind's `[Order]` DTO, re-serialise (drops `Posts`, `MinRange` …). Published as `AssignBehaviorEvent { BehaviorName = PlayFor(…), Origin, RequestId, IsResume = true }` — the ingress gate, no second answer to a station | `Hrot.AI.Behaviors/Orders/OrderReissue.cs` (new, pure); the arm | a kind whose neutral DTO a physical play cannot read honestly (R-8300's "would change") |
| **X2-C2** · **R-8356** | the physical play for a squad's Defend / BreakContact (claim 8: the table REFUSES them at Physical) | **R-8302, as approved**: agg-backend's table accepts PHYSICAL rows as a FALLBACK consulted only when the name rule finds nothing (so nothing that resolves today changes): `(InfantrySquad, Physical, Defend) → SquadDefend` · `(InfantrySquad, Physical, BreakContact) → BreakContact`. A rail derives the expected rows from `Fulfils` + the class and refuses a row the name rule already resolves | agg-backend: `ResolutionRouting.cs` (the physical-row throw relaxed, a fallback branch); side effect: an operator's `Order.Defend` to a physical squad starts `SquadDefend` instead of being refused (a fix) | the user wanting physical rows to WIN over the name rule (then `InfantrySquadHold` vs `SquadHold` must be decided too) |
| **X2-D** · **R-8357** | the merge direction, and SV-10's "untyped order cannot switch" | the stochastic play gets the order by X2-C. ⭐ The gate: a seam `SwitchBlocked: Func<repo, unit, target, string?>` on the manager (init, default none — like `Policy`), = `OrderReissue.BlockReason`: an order with **no kind**, or whose kind has **no row at the target resolution** (Recover, Embark, Strike …) ⇒ an AUTOMATIC switch waits (logged once per reason change at Debug). ⭐ The instructor's pin wins (SV-14): the switch goes, the order is cleared and its commander hears TaskFailed (`ReportRefusal`) | agg-backend: `Decide` (2 guarded returns) + the property; ours: `BlockReason`, the wiring | the user wanting SV-10 to bind the pin too |
| **X2-E** · **R-8358** | an order mid-way, a reaction running | **restart from the current situation** (SV-10): the re-issued play starts fresh with the order's params — Move / Attack plan from where the unit stands, Defend takes the planned posts (X2-F). A PAUSED order is re-issued too: the order outranks the reaction (claim 11), which ends — the new resolution's SOP re-judges. A REACTION with no order whose play belongs to the OTHER resolution (`ResolutionRoutingTable.IsStochasticPlay(name)` ≠ the unit's) is CLEARED at origin Reaction; `StochasticUnitHold` joins `ResolutionRoutingTable.Plays` so the catalogue is complete | the arm; `ResolutionRouting.cs` `Plays.Hold` + `IsStochasticPlay` (agg-backend file) | a play that proves it needs phase mapping (SV-10 allows it then) |
| **X2-F** · **R-8359** | DP3-D ② — the posts, and `MaxPosts` 8 → 13 | when the wanted play is `SquadDefend` and the unit's `PlannedPosts.TryGet` gives posts: the json gains `Posts` (each posted entry: point, watch — zero ⇒ none, `Window`) — D-2f hands them to the nearest men, who stand on them ⇒ Set at once, no ask. `MaxPosts` 8 → 13 (= `PlannedPosts.Capacity`). ⭐ **Measured (claim 13): the blackboard IS recorded, as bytes inside `BlueprintBlackboard*`; the component layout does not change ⇒ `SchemaValidator` does NOT refuse old recordings.** ⚠ An OLD recording sought mid-`SquadDefend` holds a slot sized for 8 posts: `RootParamsAccess.TryGetBlockFor` refuses a too-small slot (`:392`) — whether every read goes through that guard is NOT measured ⇒ S5 measures it with a rail; either way it is declared a versioned change under SV-12 | `Hrot.Core` `SquadDefendParamsJsonDto.cs:66`; `SquadDefendParams` grows 125 B (5 × (6 floats + 1 byte)) ⇒ the squad's occurrence store may move up a tier (`RootParamsCost`) — the tier rails decide | the S5 rail showing a mis-read on seek ⇒ keep 8 and let men 9–13 survey (one ask, no break) |
| **X2-G** · **R-8360** | parallel-phase safety | **everything is SERIAL**: the reconciler and the manager are serial `Simulation` systems (not phases); their only writes are EVENTS (`AssignBehaviorEvent`, `ClearBehaviorEvent`, `CmdAssignSubordinate`) and the manager's `RemoveComponent<UnitRoster>` on the unit it owns (D-P6: creation, mapping and roster as serial follow-ups). The brain phase only READS the roster (already declared). Machine checks: the rails under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`; `stepped-hash.py --threads 1,4,8` on `agg-split-keeps-order` | none beyond the rails | a phase that writes the roster (none today) |
| **X2-H** · **R-8361** | a commander re-sends across the switch (claim 10) | `CommandOrders.StillRoutedRunning` / `GaveRouted` become **resolution-blind**: the entry matches when its key + json equal EITHER routed form (`Order.<K>` + neutral, or the physical play + its json) — and, for `StillRoutedRunning`, the member's current `RequestId` equals the entry's (kept by X2-C). The two `KeyFor` sites (`CompanyBehaviors.cs:939`, `PlatoonConvoyBehavior.cs:318`) move to the routed helpers | `CommandNodes.cs` (2 helpers), 2 sites; the CE-2304 rail extended | — |
| **X2-I** · **R-8362** | the echelon scope | **squad echelon only.** A split platoon / company keeps today's behaviour (no roster, the stochastic play left running) until the split is one level at a time (F-X2-1, filed to agg-backend); then the same reconciler re-resolves it with `(InfantryPlatoon, Physical, Defend) → PlatoonDefend` rows and new `InfantryPlatoon` / `InfantryCompany` classes (DIS echelon 4 / 5, `UnitDis.cs:19-29`) | none now | the user wanting platoons in this slice (then F-X2-1 first — agg-backend's work) |
| **X2-J** · **R-8363** | the kind comes back from the PLAY | the many-to-one stochastic rows lose the original kind: March / Dash → Move, Hold → Defend, ClearBuilding / Bound / FireAndMovement / SupportByFire → Attack (the play's `Fulfils`, R-2089). Accepted and documented: the re-resolved play is the row's representative kind | none | the user wanting the exact kind back ⇒ record the order's intent on `BehaviorStartRecord` (a recorded managed record ⇒ an SV-12 change) |

⭐ **Physical behaviour unchanged** where no switch happens: the order arm acts only when a unit's play does not fit its resolution; the routing
fallback rows act only where the name rule finds nothing; the commander helpers accept MORE matches, never fewer.

## 8. Slices and acceptance *(R-4277: the scenarios are the verdict)*

| # | lane | builds | ⭐ passes when |
|---|---|---|---|
| **S1** | agg-backend *(or agg-behaviors, named cross-lane)* | X2-B: `Handover` → `CmdAssignSubordinate`, `ChildDesignation`; `Merge` → `UnitRoster` removed. X2-D: `SwitchBlocked`. X2-C2 / X2-E: the physical fallback rows, `Plays.Hold`, `IsStochasticPlay` | `UnitResolutionRails` (Fdp.Toolkits.Tests): a squad's handover publishes one assign per live man with his leaf designation; a platoon's publishes none; a merge leaves no `UnitRoster`; a blocked automatic merge waits, a pinned one goes. `ResolutionRoutingTableTests`: a physical row is a fallback (the name rule still wins), Defend / BreakContact resolve for `InfantrySquad`; a row the name rule resolves is a data error. Each red-proved by one compiling mutation |
| **S2** | agg-behaviors | X2-A / X2-C / X2-E: the rename (Roslyn), the order and reaction arms, `PlayFor`, `OrderReissue` (minus posts) | `SquadManeuverTests.ResolutionSop.cs` (F-5's suite, T-1) green unchanged + new: **a** split squad under an operator's Defend runs `SquadDefend` with the same `RequestId`; **b** it waits for the roster (the mutation "no wait" ⇒ the play starts with 0 men); **c** ONE start per switch (count `BehaviorStartRecord.InstanceId` changes); **d** a merged squad runs `StochasticUnitDefend` from a projected json (the mutation "no projection" ⇒ refused); **e** a paused order is re-issued, the old reaction ends; **f** a stochastic reaction alone is cleared on a physical unit; **g** a steady frame allocates 0 B (200 frames) |
| **S3** | agg-behaviors | X2-H: the resolution-blind commander helpers, the two sites | the CE-2304 re-send rail + new: a `PlatoonDefend` over a stochastic squad that splits gives ONE order (no re-send after the switch); the Company / Platoon / Convoy / Urban suites green |
| **S4** | agg-behaviors | X2-F: `Posts` from `PlannedPosts`, `MaxPosts` 13 | `SquadManeuverTests.PlannedPosts.cs` + new: the re-resolved SquadDefend carries one post per posted entry; with every man on a post it reports Set with no window ask (the mutation "no Posts" ⇒ an ask is issued); the physical defence suites (`CE2275_*` … DP3 §7 list) 318 / 0 unchanged |
| **S5** | agg-behaviors | the recording measurement of X2-F | a rail records a running `SquadDefend` with an 8-post block, then seeks it with the 13-post build: the read is REFUSED (guarded), never mis-read — else X2-F falls back to 8 (its "would change") |

**Feature-test scenarios** (`scripts/gen-agg-scenarios.py`, `scenarios/demos/aggregates/<name>/`; each `description` says *What* / *Judge*, its
`checks.json` the same as `expect` rows + a golden):

| scenario | what | ⭐ the expect rows |
|---|---|---|
| **agg-split-keeps-order** *(new)* | a stochastic squad of 10 under an authored Defend in the open; a physical patrol closes to the split band | task `SquadDefend` on the squad within 3 s of the handover · never `StochasticUnitDefend` after handover + 3 s · `UnitRoster.Count` = 10 · every man `UnitSubordinate.Commander` = the squad · PROGRESS Set reported |
| **agg-merge-keeps-order** *(new; or `agg-merge-back` re-judged — the builder picks the cheaper, says which)* | the split squad's enemy leaves; it merges | task `StochasticUnitDefend` after the merge · no `UnitRoster` on the squad · a SITREP after the merge (the stochastic staff arm, claim 7) |
| **agg-squad-holds-house** *(re-judged)* | as built (DP3 §9a) — now the men are Set at once | + task `SquadDefend` after the handover · PROGRESS Set within 3 s of the handover · the men still on the planned posts at 20 s (the existing rows) |
| **agg-commanded-split** *(new)* | a physical platoon (`PlatoonDefend`) over a stochastic squad that splits | the squad runs `SquadDefend` · the platoon's journal shows ONE order to it (no re-send after the switch) |
| `agg-split-on-contact` · `agg-forced-handover` · `agg-merge-back` *(platoons)* | unchanged (X2-I) | golden SAME |

Parallel rule (X2-G): the S2–S4 rails under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`; `stepped-hash.py --threads 1,4,8 --scenario agg-split-keeps-order` — STATE EQUAL.

## 9. Cross-lane edits *(named, per the lane protocol — the builder says them in each commit)*

| file | lane that owns it | the edit |
|---|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Aggregate/UnitResolution.cs` | agg-backend | `BusResolutionSwitch.Handover` publishes `CmdAssignSubordinate` (squad echelon); `ChildDesignation` init property; `Merge` removes `UnitRoster`; `ResolutionManagerSystem.SwitchBlocked` init property + 2 guarded returns in `Decide` (automatic only) |
| `Hrot/Engine/Hrot.Core/Tkb/ResolutionRouting.cs` | agg-backend | physical FALLBACK rows (the throw relaxed for Physical, consulted after `OrderResolver`); `Plays.Hold`; `IsStochasticPlay` |
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | shared (agg-backend's constructor site) | `ChildDesignation = …`, `SwitchBlocked = OrderReissue.BlockReason`, the renamed fit system |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/SquadDefendParamsJsonDto.cs` | behaviors (the defence) | `MaxPosts` 8 → 13 |

## 11. AS BUILT *(agg-behaviors, `2026-11-17` — read this first)*

| slice | built | commit | rails *(each red-proved by one compiling mutation)* |
|---|---|---|---|
| **S1** (cross-lane, agg-backend files) | ✅ `Handover` → one `CmdAssignSubordinate` per released man at squad echelon, designation from `ChildDesignation` (`StochasticUnitFill.ChildDesignation`, the same leaf as `ChildType`); `Merge` removes `UnitRoster`; `ResolutionManagerSystem.SwitchBlocked` (automatic split AND merge wait; the pin goes); the physical FALLBACK rows (`Resolve` consults them only when `OrderResolver` returns NoBehaviour); `Plays.Hold`, `IsStochasticPlay`, `StochasticPlays` | `5a56af0f2` | `UnitResolutionRails.X2B_*` ×2, `X2D_*` · `ResolutionRoutingTableTests.X2C2_*`, `X2E_*` · `BehaviorParamsValidationTests.X2C2_*` (every physical row fulfils its order and the name rule finds nothing) |
| **S2** | ✅ the Roslyn rename (stdio — 3 files, grep agrees); `UnitResolutionFitSystem`'s ORDER + REACTION arms; `OrderReissue` (`PlayFor`, `BlockReason`, `ParamsFor`/`Project`, `WithPlannedPosts`); `ReportRefusal(repo, entity, origin)`; `CgfLogicPack` wires `ChildDesignation` + `SwitchBlocked` (the fit system built before the manager, so both ask one registry) | `b0c32fe58` | `SquadManeuverTests.X2_*` ×6 (split: same id, ONE start, rostered first · merge: projected · paused · stale reaction · no play ⇒ cleared + TaskFailed · 0 B over 200 frames) |
| **S3** | ✅ `GaveRouted` / `StillRoutedRunning` match EITHER routed form (the running check by `RequestId`); `CompanyBehaviors` / `PlatoonConvoyBehavior` off `KeyFor` (left in place, unreferenced — public API) | `b0c32fe58` | `X2H_ACommandersRoutedCheck_SurvivesTheMembersSplit_ByRequestId` |
| **S4** | ✅ the re-issued `SquadDefend` carries `PlannedPosts` as D-2f `Posts`; `MaxPosts` 8 → 13 | `b5b157e6c` | `X2F_ASplitSquadsSquadDefend_StartsFromItsPlannedPosts_NoAsk_AndReportsSet` |
| **S5** | ⛔ **DROPPED** — 🔒 user, `2026-11-17`: *"Breaking replay is no issue."* (R-8365): MaxPosts goes to 13 regardless; an old recording replayed mid-`SquadDefend` may mis-read its blackboard block — accepted. ⛔ SUPERSEDED: X2-F's "S5 measures it … else keep 8" | — | — |

**Deviations from §2–§7, each with its reason**

| design said | as built | why |
|---|---|---|
| wanted = `PlayFor(…)`, re-issue when running ≠ wanted | the FIT test is the play's FAMILY vs the unit's resolution (`IsStochasticPlay` by hash) — only a misfit calls `PlayFor` (R-8366) | ⭐ "running ≠ wanted" would re-resolve a physical squad that never switched (`SquadHold` ≠ `InfantrySquadHold`), breaking "physical behaviour unchanged"; and `Resolve` builds strings — the family test is one hash compare, 0 B |
| `PlayFor` on `TacticalIntentResolutionSystem` | `OrderReissue.PlayFor` (R-8367) | `BlockReason` (Hrot.AI.Behaviors) needs it and cannot see Hrot.CGF — ONE implementation where both reach it |
| claim 4: an assign published in frame N is applied before the reconciler runs in N+1 | ⛔ measured false: the kernel runs Input → swap → Simulation (`ModuleHostKernel.cs:654, :715`), so a Simulation-phase publish is read by the ingress TWO frames later. ⇒ the arm waits while a request for the unit is ON THE BUS (`ReadManaged<AssignBehaviorEvent>` / `Read<ClearBehaviorEvent>`) — stateless, seek-safe (R-8368) | without it the S2-c rail measured two starts per switch |
| `BlockReason`: an order with no kind / no row ⇒ wait | the same, **squad echelon only** (R-8369) | a platoon's Defend has no physical row (no `InfantryPlatoon` class) — unscoped, every automatic platoon split under a Defend would have waited forever (X2-I) |
| the merge projects into "the kind's `[Order]` DTO" | into the TARGET play's own params DTO (`ParamsDtoRegistry.TryGetBehavior`) (R-8370) | kinds with no `[Order]` DTO (Bound, ClearBuilding, …) route to a play that has one; the target's DTO is what its strict read accepts |
| X2-E: a stale reaction is "CLEARED at origin Reaction" | cleared at origin **Self**, and the same for a stale task at origin Sop / Self (R-8364) | a Reaction-origin clear is gated like a reaction — refused against a MORE urgent running one; a Self clear ends the reaction as if it ended itself (a paused task resumes) |
| X2-I: "squad echelon only (`AggregateUnit.Echelon == 0`)" | ONE scope rule, `UnitSplits.RostersItsMen` = echelon 0 AND a slot table ≤ `UnitRoster.Capacity` — used by the switch's assigns, `BlockReason` and the reconciler (R-8372; cross-lane, `UnitResolution.cs`) | 📌 measured in the end-of-run sweep: `agg-merge-back`'s platoon has an AUTHORED 30-slot table, so the fill never set its echelon (0) — `BlockReason` held its split (no physical Defend for a platoon) and the scenario failed 62/64; with the rule it is out of scope again. Rails `X2I_*` ×2 |
| `agg-merge-keeps-order` *or* re-judge `agg-merge-back` | a NEW scenario (R-8371) | `agg-merge-back` is a PLATOON — X-2 is squad-only (X2-I) |

**Scenarios** (R-4277, editor tier, each in a fresh process, golden captured then SAME in a second; main design §23.6i): `agg-split-keeps-order` 6/6 ·
`agg-merge-keeps-order` 6/6 (new; `agg-merge-back` is a platoon) · `agg-commanded-split` 5/5 · `agg-squad-holds-house` re-judged (+ SquadDefend within 3 s, + every man
still inside 3 s after the handover; the 20 s row moved to "within 5 m of the north wall" — F-X2-6). ⚠ "PROGRESS Set" (§8) is not expressible: `ReportOutbox` is not in the
entity dump — judged by the men not leaving their posts instead. ⚠ A merge's stray TaskFailed (F-X2-5) is not exercised: no scenario merges a COMMANDED squad.

**Gates:** S1 `UnitResolutionRails` 11/0, `ResolutionRoutingTableTests` 8/0 · S2–S4 `SquadManeuverTests` + `CgfLogicPackTests` + `BehaviorParamsValidationTests` +
`TacticalIntentResolutionSystemTests` (+ `ScenarioRulesTests`, `BasicInfantrySopTests`) 545/0 plain; under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`
541/4 — the 4 are `BasicInfantrySopTests`' soldier SOP (an undeclared `EntityInfo` read in `EqsTacticsNodes.Engageable`, no X-2 code on the stack; not proved at the base).

**End-of-run sweep** (every `agg-*` + the four defend scenarios, after merging `coordinator`): first pass 12 of 13 reached green, `agg-merge-back` 62/64 ⇒ R-8372;
after it, the remaining 18 (agg-merge-back included) 18/18, every golden SAME (the defend ones have none). **Parallel:** `HROT_PARALLEL_SIM=1 stepped-hash.py --threads 1,4,8 --chunk 60`
(6 s, past the 2.25 s handover and the re-issue) on `agg-squad-holds-house` and `agg-split-keeps-order` — STATE EQUAL.

**Findings while building:** F-X2-5, F-X2-6 (§10).

**Lane decisions:** R-8364 … R-8372 (above) · R-8365 is the user's ruling.

## 10. Findings *(measured while designing)*

| # | finding | where it goes |
|---|---|---|
| **F-X2-1** | ⛔ **the split FLATTENS the composite**: a stochastic platoon splits straight into its 30 men (`StochasticUnitFill.Leaves`), not into 3 squads — contrary to X-5 / D-P7 (*"one level at a time"*). Its men exceed `UnitRoster.Capacity` (16) and `PlatoonDefend` orders squads, so no roster or order can be handed to a split platoon | to agg-backend (a one-level split: a platoon's children are stochastic SQUADS, each splitting by its own trigger) — X2-I waits on it |
| **F-X2-2** | ⛔ **at Physical the routing table refuses Defend and BreakContact for a squad** (no `InfantrySquadDefend`): today it never matters because commanders `Assign` physical plays by name (R-8305), but any `Order.Defend` to a physical squad — an operator's included — is refused | X2-C2 (R-8302's rows) |
| **F-X2-3** | ⚠ a commander's routed check re-sends after its member switches (a new id ⇒ the defenders restart, CE-2304) | X2-H |
| **F-X2-5** *(as built)* | ⚠ on a MERGE the physical play ticks once more after its men left (the reconciler's re-issue reaches the ingress two frames later): `SquadManeuverRunner.Tick` returns Failure on an empty roster, so the play ENDS before the stochastic one starts — the re-issued assign then lands in an empty slot and the order goes on (same id); a commander may hear a stray PROGRESS TaskFailed | ours, open — measured in `agg-merge-keeps-order` (§11) |
| **F-X2-6** *(as built)* | ⚠ `agg-squad-holds-house`: once the men RUN SquadDefend (X-2), Red's fire from 480 m (first round 8.55 s) displaces 4 of them 1–4 m out of the house's north wall by 10–14 s (D-2 displacement picks a point outside). Before X-2 the men ran no play and stood still | the physical defence (behaviors) — whether a displaced defender may leave the building |
| **F-X2-4** | ⚠ once a split forms a roster, a merge that left the unit's `UnitRoster` behind (even at Count 0 — the `DestructionOrder` cascade empties it, never removes it) would silently drop the unit from the staff composer's stochastic arm (no SITREP / Set / TaskFailed) | X2-B |
