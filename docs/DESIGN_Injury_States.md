<!--STATUS
state: LIVE
updated: 2026-11-25 (§9 REVIVE — approved and BUILT, ui, CE-11061) · 2026-10-13
build-state: BUILT (I-a) — I1–I6 approved 2026-10-13 (R-2011); behaviors lane; CE-2155; I-b wounded SPEED built 2026-10-14 (ui, CE-1082: WoundedSpeedFactor 0.6, DESIGN_Map_3D_Mode §6m); wounded AIM still open
current-answer: ⭐ §9 REVIVE (built — §9.3 as built) · §8 I-a as built · §8a CE-2318 (a man down is no target, and lies) · §3 classes · §4 sequence · §5 modules · §6 decisions (leans) · §7 slices
stale-below: nothing
known-rot: ⛔ 2026-10-20 (live, user) — a man down is still moved and turned by collision avoidance — CE-2321 (backend). ⛔ SUPERSEDED: "§6 I4 'units stop shooting him' does NOT hold" — fixed 2026-10-20 by CE-2318, §8a.
known-conflict: none
related-designs:
  - UX/DESIGN_Edit_Framework.md — OWNS Edits.Submit and its kinds; §9 adds the Revive kind in the F3 shape
  - UX/DESIGN_Details_Forms.md — OWNS the Unit form; §9 adds the Strength row's trailing Revive action
  - DESIGN_Man_Downed_Event.md — OWNS the ONE event raised when a HIT moves a man out of the fight (CE-3364): the downing, with who fired, read by kills / assists / team kills
  - OVERVIEW_Rules_And_Demos.md — the INDEX of the rules and demos built across the behaviour designs (rule → what implements it → demo); this design OWNS the rows that cite it
  - designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — OWNS hit location: limb wounds derived from PartDamage like the states here from Health (HL-7), filling this file's open wounded AIM
  - docs/DESIGN_Map_3D_Mode.md §6m — DRAWS the wounded limp and the incapacitated figure, and builds slice I-b (WoundedSpeedFactor 0.6)
  - OVERVIEW_Behaviour_Building_Blocks.md — §8a OWNS the one-table summary of what a behaviour can rely on from these states
  - DESIGN_Operations_Behaviour_Catalogue.md — §10.1 D1 OWNS the RULING (R-263): four human states + bleed-out, the vehicle damage states; this file is the human half's build design
  - DESIGN_Entity_Interactions.md — §7.4.8 OWNS the person carrier (CE-2152), the first consumer: only an incapacitated soldier may be carried (R-2009 C3)
  - designs/packs-1/DESIGN.md — §2.B OWNS the opt-in MobilityKill and "death always stops" (CE-3092), which the incapacitated row extends
  - DESIGN_Decision_Layer.md — §3.3f OWNS CE-3090's "wounded" (a HealthFraction curve today), which this state can later replace
  - DESIGN_Visual_Effects.md — §7 OWNS how the dead LOOK (reads CombatLife.IsDestroyed); an incapacitated figure is a later presentation item
  - designs/group-maneuvers/DESIGN_Squad_Tactics.md — L4 OWNS casualty care (buddy aid, moving the casualty), which waits for these states
  - docs/DESIGN_Civilians_And_Crowd_Control.md — CV-6 adds a STORED EffectState (stunned, irritated, knocked down) — the full-health stun case rule I1 names.
-->

# Injury states — ok · wounded · incapacitated · dead *(R-263 D1; CE-2155)*

🔒 *User, 2026-10-11 (catalogue §10.1 D1): "humans: the four injury states + bleed-out, as leaned"* — the lean being *"wounded = slower,
worse aim; incapacitated = no move/fire, needs aid"*.

## 1. Why now

The person carrier (CE-2152, approved C3) may carry only an INCAPACITATED soldier, and that state does not exist. Squad tactics' casualty
care (L4) and the medic and panic entries (MED-1/2/3, IND-5, CIV-3) wait on it too.

## 2. INVENTORY *(graph CLI `search_graph` `.*Injur.*` (9, blast radius only) · `.*Incapacit.*` (0) · `.*Casualt.*` (0) · `.*Wound.*` (21, none a state) · `.*CombatLife.*` (1) · `.*HealthState.*` (IG's retired one), + grep for `Current <= 0`, 2026-10-13)*

| what exists | where | what it means here |
|---|---|---|
| `Health { Current, Max }`, replicated in the Brain group | `Combat/Components/Health.cs:18-21`; CE-523 | the one input; a state derived from it needs NO new wire |
| `CombatLife.IsAlive / IsDestroyed / ApplyHitCapabilities` — "the ONE rule" for death and hit effects | `Combat/CombatLife.cs:22-62` | the home for the state test and the incapacitated capability strip |
| both damage paths call `ApplyHitCapabilities` | `DamageSystem.cs:83`, `HealthApplicationSystem.cs:105` | one call site per path already exists |
| opt-in `MobilityKill { BelowFraction }` strips `CanMove` below a fraction | packs-1 §2.B, CE-3092 | the precedent for a per-type health threshold |
| "dead" read raw (`Current <= 0`) outside `CombatLife` | `ThreatDanger.IsDead:33`, `AliveFilterTest.cs:33`, `AreaEffectSystem.cs:105`, `Possession.cs:224` | four readers decide "still a target / threat" — each needs a call on incapacitated (I4) |
| "wounded" = a `HealthFraction` curve in the posture decision | `CombatPostureDecision.cs:42-65` (CE-3090) | works today; NOT replaced by this design |
| infantry top speed scaled per frame by stance | `CarKinematicsSystem.UpdateVehicle:146-150` | where a wounded speed factor would multiply (a later slice) |
| healing | ⛔ none (grep `Current +=` on `Health`: 0) | nothing restores health, so states move in one direction only |

## 3. Classes

```mermaid
classDiagram
  class InjuryDefDto {
    NEW TkbDescriptor Gen.Injury
    +WoundedBelow : float = 0.6
    +IncapacitatedBelow : float = 0.3
    +BleedOutSeconds : float = 120
  }
  class InjuryThresholds { NEW component, type data, stamped at spawn +WoundedBelow +IncapacitatedBelow +BleedRate }
  class InjuryTkbTranslator { NEW in TkbTranslatorSet.Base }
  class InjuryState { NEW enum Ok Wounded Incapacitated Dead }
  class Injury {
    NEW static rules
    +StateOf(view, e) InjuryState
    +IsIncapacitated(view, e) bool
    +IsInTheFight(view, e) bool
  }
  class BleedOutSystem { NEW: Health owner drains Health while incapacitated }
  class Health { existing Current Max }
  class CombatLife { existing: ApplyHitCapabilities strips CanMove and CanShoot when incapacitated }
  class ThreatDanger { existing IsDead reads Injury.IsInTheFight }
  InjuryTkbTranslator ..> InjuryDefDto : reads
  InjuryTkbTranslator ..> InjuryThresholds : stamps
  Injury ..> Health : reads
  Injury ..> InjuryThresholds : reads
  Injury --> InjuryState
  BleedOutSystem ..> Injury
  BleedOutSystem ..> Health : writes
  CombatLife ..> Injury
  ThreatDanger ..> Injury
```

*What the picture shows that prose hid: the state is a FUNCTION of replicated `Health` and type data — no stored state, no new topic — so
every node agrees on it by construction, and the only writer added is the bleed-out drain on the node that already owns `Health`.*

## 4. Sequence — a hit that incapacitates, then bleed-out

```mermaid
sequenceDiagram
  participant D as DamageSystem / HealthApplicationSystem (Health owner)
  participant C as CombatLife.ApplyHitCapabilities
  participant I as Injury.StateOf
  participant B as BleedOutSystem (Health owner)
  participant R as any node
  D->>D: Health.Current -= damage
  D->>C: apply
  C->>I: state?
  I-->>C: Incapacitated (Current < Max x IncapacitatedBelow)
  C->>C: strip CanMove and CanShoot
  loop each frame while Incapacitated
    B->>B: Health.Current -= BleedRate x dt
  end
  B->>C: at 0: Dead (strip already done)
  R->>I: StateOf from the replicated Health
```

## 5. Modules

```mermaid
graph TD
  TS[TkbTranslatorSet.Base - every node's spawn] --> IT[InjuryTkbTranslator NEW]
  HA[CoreInfrastructureCapabilities.HealthApplication - every live node] --> HAS[HealthApplicationSystem]
  HA --> BO[BleedOutSystem NEW]
  K[kernel each frame] --> HAS
  K --> BO
  HAS --> CL[CombatLife]
  BO --> CL
  CL --> INJ[Injury rules NEW]
```

*What the picture shows that prose hid: the drain rides the cross-role `HealthApplication` capability built in CE-2144 (R-2008), so every
node that applies damage also bleeds — and only the `Health` owner writes, by the same authority gate.*

## 6. Decisions *(✅ I1–I6 approved 2026-10-13, R-2011)*

🔒 *User, 2026-10-13: "I1-I6 approved, C5-C10 approved, go ahead with CE-2155"*

| # | question | ⭐ lean | blast radius | what would change it |
|---|---|---|---|---|
| **I1** | where the state lives | ⭐ DERIVED from `Health` + per-type thresholds (no stored state, no wire) | one rules class; all nodes agree for free | a state that cannot be read off health (e.g. stunned at full health) |
| **I2** | thresholds | ⭐ per type via a NEW `Gen.Injury` descriptor; a type without it has no wounded/incapacitated band (today's behaviour: ok until dead) | soldier templates opt in; vehicles never declare it | wanting it on every human without data — then default it in `BehaviorTkbTranslator` |
| **I3** | bleed-out | ⭐ the `Health` owner drains linearly from `IncapacitatedBelow` to 0 over `BleedOutSeconds`; reaching 0 is ordinary death | `BleedOutSystem` in `HealthApplication` | stabilisation by a medic (MED-2) — a later "stabilised" flag stops the drain |
| **I4** | is an incapacitated soldier a threat / target | ⭐ NOT a threat (`ThreatDanger.IsDead` → `!Injury.IsInTheFight`), and the alive-filter skips him, so units stop shooting him; area effects and possession still see him alive | 2 readers changed, 2 kept | a doctrine of "make sure" fire on the downed |
| **I5** | wounded effects (slower, worse aim) | ⭐ NOT in the first slice — CE-3090's `HealthFraction` curve already shapes behaviour; a speed/aim factor is slice I-b | none now | a scenario that needs wounded soldiers visibly slower |
| **I6** | incapacitated capabilities | ⭐ strip `CanMove` and `CanShoot` in `ApplyHitCapabilities` (the one rule), regardless of `MobilityKill` | one branch in `CombatLife` | — |

Rejected (one line each): **a stored, replicated `InjuryState` component** — a second producer of what `Health` already says, which could
disagree with it (R-132) · **per-body-part wounds** — ruled out in D1 (cost, little behaviour gain) · **thresholds as global constants** —
a civilian and a soldier would bleed alike; D1 lists civilians as casualties too.

## 7. Slices

| slice | what | rails |
|---|---|---|
| **I-a** (`CE-2155`, ✅ BUILT 2026-10-13 — §8) | `Gen.Injury` + translator + `InjuryThresholds`; `Injury` rules; `ApplyHitCapabilities` strips at incapacitated; `BleedOutSystem`; I4's two readers; soldier templates declare it | a hit below the band strips move and fire but leaves him alive · bleed-out reaches 0 in `BleedOutSeconds` on the owner only · a replica reads the same state · an incapacitated contact has danger 0 · no `Gen.Injury` ⇒ unchanged behaviour |
| **I-b** | wounded speed / aim factors (I5) | ⭐ SPEED built `2026-10-14` (ui, CE-1082): `Gen.Injury.WoundedSpeedFactor` (0.6, user-approved) → `InjuryThresholds` → `Injury.SpeedFactor`, multiplied in `CarKinematicsSystem` beside stance and carry (it scales the TOP speed). AIM still open. 📄 DESIGN_Map_3D_Mode.md §6m |

## 8. I-a as built *(`2026-10-13`, behaviors, `CE-2155`)*

As §3–§5. `InjuryDefDto` (`Gen.Injury`) → `InjuryTkbTranslator` (in `TkbTranslatorSet.Base` and the two FDP example lists) →
`InjuryThresholds` (component 370, always rewritten from the type; a band that cannot be read throws at spawn) · `Injury.StateOf /
IsIncapacitated / IsInTheFight` (`Combat/Injury.cs`) · `CombatLife.ApplyHitCapabilities` strips move and fire at incapacitated (I6) ·
`BleedOutSystem` in the `HealthApplication` capability, behind `HealthApplicationSystem`, same authority gate (I3) · declared on BDC
`Infantry_Rifleman` (`NedTkbBuilder.WithInjury`) and the four UrbanCombat soldier types.

| deviation / finding | why |
|---|---|
| ① `ThreatDanger.IsDead` **renamed** `IsOutOfTheFight` (Roslyn, 5 sites) and given I4's meaning | its three callers — `HaveLiveTarget`, `ThreatInSight` and the squad's own-strength sum — all meant "out of the fight"; with `ThreatDanger.Of` and the EQS `AliveFilterTest` that makes **4 readers changed**. Kept: `AreaEffectSystem` (a blast still hits a man down) and `Possession` (an operator still holds a downed unit). The design said "2 changed, 2 kept"; the count was of files, not readers |
| ② bleed applied in **1-second steps**, not per frame | `Health` is published on change (`EntityDamageEgressTranslator`), so a per-frame drain would send one sample per frame per casualty |
| ③ ⭐ **found and fixed: `Embarkation.Leave` gave move and fire back to any ALIVE passenger** | an incapacitated casualty set down would have stood up armed. It now restores only to someone in the fight. Not in the §2 inventory — found by listing every writer that GRANTS capabilities (one: this) |
| ④ `InjuryThresholds.BleedPerSecond` (a health fraction a second) instead of `BleedOutSeconds` | the drain reads it every step; the translator does the division once |
| ⑤ the FDP UrbanCombat example's own catalogue (`DemoTkbSetup`) does **not** declare `Gen.Injury` | a self-contained FDP demo with its own validator; absence = ok until dead, so it is unchanged |
| ⑥ ⭐ **the band is 0.3, not the lean's 0.25** *(user, 2026-10-13: "go with 0.3")* | at 0.25 a 100-HP soldier under 25-damage rifle fire went 75 → 50 → 25 → 0 and was never down; at 0.3 the third hit puts him down. Measured blast radius: 14 scenarios hold banded soldiers (all `bt-*` / `ua-*`, 100 HP); hill-attack and the convoy do not; 50-HP and 25-HP soldiers are unchanged. ⛔ SUPERSEDED: "left for the user" |
| ⑦ 📐 **measured live, `CE-3094` `ua-universal-soldier` (2026-10-13): two hostiles land three rounds inside a second, 100 → 25 HP** | on CGF (the Health owner, which runs his brain) `StateOf` = Incapacitated, `CanMove`/`CanShoot` stripped, the cover node's moves fail and he stays where he fell (24.75 HP after one bleed step) until a later round kills him. ⭐ **The strip is on the Health owner only, exactly like death**: SimHost's copy keeps `CanMove`/`CanShoot` and stops because the owner's `LocomotionDispatcherSystem` sends the stop (`NavigationIntent` None, `CE-3091`); a replica reads the state through `StateOf`. The scenario rail's CE-3092 check ("a wounded man keeps moving") now measures the WOUNDED window only (asserted when he stays wounded ≥ 3 s). ⚠ A live "does not move while down" check was tried and dropped: with the strip removed it stayed green, so it proved nothing — the strip's proof is the unit rail `InjuryTests` |

Rails — `InjuryTests` (8):
- the state read off health and the band
- a hit below the band strips move and fire but leaves him alive
- no `Gen.Injury` ⇒ ok until dead
- bleed-out on the owner only, in steps, wounded do not bleed
- a replica reads the same state
- an incapacitated contact has danger 0 and is filtered as a target
- a casualty set down stays down
- an unreadable band is refused at spawn

Plus `LiveSimulationPlanRails` pins `BleedOutSystem` second on every live node, and the two editor sequence rails gained it.

## 8a. CE-2318 — a man down is no target, and he lies *(`2026-10-20`, behaviors)*

🔒 *User, live, 2026-10-20: "AI soldiers keep firing at an enemy who is already laying wounded on the ground … and they keep firing above
it (like if aiming at still standing figure)."* I4 said units stop shooting him; I-a changed the threat SCORE but not the TARGET tests.

| what | as built |
|---|---|
| ① the target test | every reader that asks *"is this enemy still a target / a threat"* now asks `Injury.IsInTheFight`, not `CombatLife.IsAlive` (Health > 0) — **21 sites**: `EqsTacticsNodes.Engageable` (the kept current and the ranking's top — the live case: nothing scored above 0, so the downed current was KEPT), `AimAndFireExecutor` / `TrackTargetExecutor` end, `PostureNodes` hunting + `UntilTargetDown`, `SquadAssignment`, the squad runner's live pool and `SuppressAndManeuver`'s outcome, `NothingToFight`, `DangerAreaSensorSystem`, `TacticalGait.Threatened`, the air / IED / spotter / hold-the-line / eject / vehicle-cover pickers, the hill-attack target, the escalation ladder's warning |
| kept on health (they ask about HEALTH, the unit's own men or friends) | `CasualtyCare` (he died), `EmbarkedStateSystem` (passengers die with the vehicle), `HiddenTargetThrow.FriendlyInBurst` (⭐ a downed FRIEND in the burst still stops the throw — the tracker row had listed it as a target test), own-subordinate rosters (`SupportCommand`, the hill-attack tanks, `UnitCentroidSystem`), self (`PushThrough.CanMove`), `CrowdSystem`'s security presence, the presentation (ui), `BlueprintWorldLibrary.IsAliveInCombat` (a named node: alive is what it says) |
| ② the stance | `LogicalStance.Of` answers **Prone for a man down** (incapacitated or dead), whatever his brain last ordered — so the aim point (`AimPoint.Middle`), sight's body points, the round's body band and the hit model follow the lying body his hit boxes already take (`PersonBody.PoseOf` → `Downed`). One rule: Building Interiors §3f *"one profile for being seen, being shot and being hit"* |
| rails | `InjuryTests.CE2318_AManDown_LiesProne_SoTheAimPointIsTheLyingBody` · `AimAndFireExecutorTests.CE2318_AimAndFire_ReportsSuccess_WhenTheTargetIsDown` · `TacticsTreesTests.CE2318_AnIncapacitatedEnemy_IsNoLongerTheThreat_NorAThreatToMoveFrom` — each red-proofed by putting the old test back |
| what would change it | a doctrine of *"make sure"* fire on the downed (I4's own condition) — then a play asks for it explicitly; an operator's own shot is not gated (`AimAndFire`'s operator path returns before the test) |


## 9. REVIVE — one action that puts a man back in the fight *(`2026-11-25`, ui, `CE-11061`; RV-1…RV-5 approved by the user `2026-11-25`; BUILT — §9.3)*

🔒 User, `2026-11-25`: *"I do not want any hacks like that health edit from low to high automatically changes other fields; what would help is the
'revive' icon or something that tells the entity to do whatever needed to make it operational again."* ⇒ **R-11021**: typing a number in Strength sets
ONLY `Health.Current` (`FormAttributeInstaller.cs:19`, unchanged); putting a man back in the fight is its OWN action, one body, run by his owner.
📐 Why a number alone is not enough (measured): death strips `CanMove` / `CanShoot` (`CombatLife.ApplyHitCapabilities`) and nothing gives them back on a
Health raise — only a LOWERED part damage does (`FormAttributes.cs:241`); a value under the incapacitation line bleeds back to 0 (`BleedOutSystem`); his
task was dropped at death (`BrainTickSystem.RetireTheDead`, `:488`).

**INVENTORY** *(graph CLI `search_graph`, `2026-11-25`)*: `.*Reviv.*` (3 — doc sections only, none a feature) · `.*Resurrect.*` (1, a doc heading) · `.*Restore.*Capabilit.*` (1 — `CombatLife.RestoreHitCapabilities`, reused) · `.*Heal.*` (docs only); + grep `reviv|alive again|RestoreHitCapabilities` — the one give-back is the part-damage path (`FormAttributes.cs:241`); `HealthStateSinceSystem.cs:39` already clears the kill kind when Health rises. ⇒ nothing to extend but `CombatLife`; coverage could not be checked (CLI).

**Mockup:** [`UX/revive-visual.svg`](UX/revive-visual.svg) (`scripts/gen-revive-mockup.py`) — scope: the Strength row's trailing button only.

```mermaid
classDiagram
    class CombatLife {
        <<static, EXISTS Fdp.Toolkits/Combat>>
        +ApplyHitCapabilities(repo, e, health)
        +RestoreHitCapabilities(repo, e, health)
        +Revive(repo, e) string? NEW
    }
    class Edits {
        <<static, EXISTS>>
        +Submit(repo, op) EditTicket
    }
    class EditOp {
        <<record, EXISTS>>
        +Revive(target, label)$ EditOp NEW
    }
    class ComponentEdits {
        <<static, EXISTS — the F3 shape>>
        +PatchKey "$component"
    }
    class ReviveEdits {
        <<static, NEW Fdp.Toolkits/Replication/Edits>>
        +PatchKey "$revive"
        +Patch() string
    }
    class UpdateEntityAttributeRequestSystem {
        <<EXISTS Hrot.Network.NED — the owner's intercept, :255>>
    }
    class FormField {
        <<EXISTS Fdp.Presentation/Forms>>
        +Action FieldAction? NEW trailing button
    }
    class HrotForms_Unit {
        <<EXISTS — the Strength row>>
    }
    Edits ..> CombatLife : owner here ⇒ Revive
    Edits ..> ReviveEdits : owner elsewhere ⇒ ask with the patch
    UpdateEntityAttributeRequestSystem ..> ReviveEdits : intercepts PatchKey
    UpdateEntityAttributeRequestSystem ..> CombatLife : Revive on the owner
    HrotForms_Unit --> FormField : Strength row carries the Revive action
    FormField ..> Edits : Submit(EditOp.Revive)
    ComponentEdits <.. ReviveEdits : same shape (precedent)
```
*What the picture shows that prose hid:* ONE body (`CombatLife.Revive`) and TWO ways to reach it — here, or at the owner through the request system's
intercept, exactly the F3 component add / remove shape (`Edits.cs` `ComponentEdit`, `UpdateEntityAttributeRequestSystem.cs:255`). ⛔ No new wire type.

```mermaid
sequenceDiagram
    actor U as user (Details, Unit form)
    participant A as UI world A (threaded copy)
    participant Q as sim job queue (CE-11060)
    participant S as sim world S (this node)
    participant O as the man's owner node (when not S)
    U->>A: click Revive (Strength row)
    A->>A: Edits.Submit(EditOp.Revive) — gate (Review refuses), journal Pending
    A->>Q: post the route (threaded node)
    Q->>S: route on the sim thread, same request id
    alt S owns his Health (the editor's one node)
        S->>S: CombatLife.Revive — Health = Max, wounds / stun / bleed / own fire cleared, CanMove / CanShoot per his type
        S-->>A: outcome Applied (job reply)
    else owned elsewhere (a CGF brain)
        S->>O: UpdateEntityAttributeCommand {"$revive":true} + request id
        O->>O: intercept ⇒ CombatLife.Revive
        O-->>S: EditOutcome Applied (wire) ⇒ S's bus ⇒ A's log (MirrorFromBus)
    end
    Note over S,O: next frames — HealthStateSince stamps "alive since", the brain is idle (his task was dropped at death) and takes orders again
```

```mermaid
graph TD
    UF["Unit form (HrotForms.Unit) — Editor, CGF, SimHost stations"] -->|click| SUB["Edits.Submit"]
    SUB -->|threaded| JQ["SimJobQueue — drained by StagedPresentation.Step every wall frame, paused or not"]
    SUB -->|serial / headless| RT["route on this world"]
    JQ --> RT
    RT -->|owner here| REV["CombatLife.Revive"]
    RT -->|owner elsewhere| REQ["UpdateEntityAttributeRequestSystem (NED pack) on the owner"]
    REQ --> REV
    REV -.->|next frame| HSS["HealthStateSinceSystem (HealthApplication)"]
    REV -.->|next frame| BTS["BrainTickSystem — task already dropped; idle"]
```
*Caption:* who runs what each frame — the request intercept lives in the NED pack, so a networkless world (the editor's one node, a test) revives on the
first branch and never needs it.

### 9.1 Decisions — leans for approval

| # | decision | ⭐ lean | rejected (one line each) |
|---|---|---|---|
| RV-1 | what Revive restores | **Health = Max; his own wounds (`PartDamage`), stun / knock-down (`EffectState`), bleeding (`BleedOut`) and his own fire (`FireState` on HIM) cleared; `CanMove` / `CanShoot` given back by `RestoreHitCapabilities` (his type's grants, seat, carry rules kept)** | keep the current number and only un-strip — a dead man at 0 cannot be "operational"; a chosen health — the Strength field already sets any number after |
| RV-2 | when it is offered | **while he is not Ok: wounded, incapacitated or dead** (one button, tooltip names what it does) — and on every entity with `Health` + `ActorCapabilityState` (men; a vehicle has none ⇒ not offered here) | only when dead — an incapacitated man bleeding out is the commonest case |
| RV-3 | the brain | **nothing extra: he stands up idle** (his task was dropped at death, `RetireTheDead`) and takes the next order / his doctrine's choice. ⚠ To measure in the build: whether his `UnitIntent` (R-3119) re-issues by itself; if not, a follow-up row | re-issue his last task — it may be long stale; reset the whole brain — loses his memory and knowledge for no gain |
| RV-4 | how it reaches the owner | **an edit kind `Revive` through `Edits.Submit`** — owner here ⇒ `CombatLife.Revive`; else the `$revive` patch the request system intercepts (the F3 `$component` precedent) — outcome back under the request id | a new DDS command topic — a wire type for one verb; an action ATTRIBUTE (`Health.Revive`) — a verb in the value vocabulary |
| RV-5 | the record | **one edit like any other** (journal, undo step in Edit mode, refused in Review); the `ManDowned` / kill already counted stays counted | un-count the kill — rewriting history the debrief already reported |

### 9.2 Acceptance (rails, in the feature suites)
- `InjuryTests` — a dead man revived: Health = Max, `CanMove | CanShoot` back, no `BleedOut` / `EffectState` / `PartDamage`; an incapacitated one too; a
  man aboard keeps what his seat strips.
- `TheAppliersBelongToTheWorldTests` (threaded, CE-11060) — `EditOp.Revive` on A revives on S, outcome Applied on A.
- `UpdateEntityAttributeRequestSystem` suite — the `$revive` patch on the owner revives and answers Applied; on a non-owner it is not applied.
- `TheUnitFormDrawsTests` — the button is drawn for a dead / wounded man and not for a healthy one; a click submits one `Revive` op; fidelity frame vs the mockup.
- Scenario (R-4277): a squad member killed, revived from the form while paused, unpaused ⇒ he stands and moves under his squad's next order.

### 9.3 As built *(`2026-11-25`, ui, CE-11061)* — matches §9's diagrams, plus two additions

| piece | where | note |
|---|---|---|
| the ONE body | `CombatLife.Revive` (`Fdp.Toolkits/Combat/CombatLife.cs`) | RV-1 as leaned; every value SET with its final value |
| the kind | `EditKind.Revive`, `EditOp.Revive`, `Edits.Revive` route; `ReviveEdits.Patch` = `{"$revive":true}` | RV-4; on a threaded node it runs on the sim thread (CE-11060) |
| the owner's intercept | `UpdateEntityAttributeRequestSystem`, beside `$component` | gated on `HealthOwnership.AppliesHere` (the Brain group's claim), not the entity's primary owner |
| the button | `FormField.Action` + `FieldAction` (`Fdp.Presentation/Forms`), drawn by `FormView` after the control; the Strength row's `HrotForms.Revive`, offered by `HrotForms.ReviveOffered` (RV-2) | a generic row action — any form row can carry one; 0 B per steady frame |
| ➕ the route | `POST /entities/{networkId}/revive` (`DebugApiService.ReviveEntity`, route doc `revive_entity`) | ➕ not in the design: the scenario's action and an agent need it; the same `Edits.Submit` |
| ➕ the scenario action | `checks.json` `do`: `"revive": "<who>"` (`ScenarioChecks`, `IVerifyWorld.Revive` — editor service, cluster route) | ➕ not in the design: R-4277's scenario needs a revive the way a user does it |

Rails: `InjuryTests.CE11061_*` (3) · `EditOutcomeOwnerTests.CE11061_ReviveOnANonOwner…` · `TheAppliersBelongToTheWorldTests.CE11061_ReviveOnAThreadedNode…` ·
`TheUnitFormDrawsTests.CE11061_TheStrengthRow…` (frame, `unit-form-revive.png` matches the mockup). Scenario: `ua-revive`.
