<!--STATUS
state: LIVE — ANSWERED 2026-10-19 (R-3018): A–F approved as leaned, A refined by the user (§0a).
updated: 2026-10-19
build-state: BUILDING — E BUILT (`CE-3258`); A-1…A-4 BUILT 2026-10-19 (§4.1 as-built); A-5 indirect, A-6 bomb, A-7 battery plan, B, C, D not yet.
current-answer: §0a the ruling · §4 A as ruled + §4.1 A as built · §5 C and D carriers · §2 the sub-questions with leans.
stale-below: nothing.
known-rot: none.
known-conflict: R-2037's premise ("only the muscle knows smoke, doors") is NOT literally true in the cluster today — doors and clouds are CGF-owned (§1 rows 1–2). The ruling is not contested; F asks what follows.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — §13b OWNS R-2037 and the inventory this question settles for backend's sites (CE-2287)
  - docs/DESIGN_World_Survey_Sensor.md — the muscle-side answer channel (standing, ~1 s): fits B; does NOT fit a per-shot gate (A)
  - docs/DESIGN_World_Query_Seam.md — IWorldQuery: R-2037 decides WHO may call it; its known-rot already records the brain-side callers
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — owns the fire chain (WeaponTask, FireProcessingSystem): A would move a gate into it
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — CB-19 / CB-22 consume door approach and the fire gate
-->

# Architect Question 94 — backend's brain-side geometry reads (`CE-2287`, R-2037)

[`CE-2287`](Blueprint_Issues_Tracker.md) (backend's code that reads world geometry on the brain) is a sweep of ten sites. R-2037
(🔒 *"the line of sight must always be a sensor or raycast query which goes to the muscle and returns back as a result … the final
decisions, of course, can stay on brain"*) is ruled. The question is the HOME of each site — and three of them sit in the FIRE CHAIN,
which is why this is an architect question and not a batch.

## 0a. ✅ ANSWERED `2026-10-19` (R-3018)

🔒 User: *"Approved. Move fire check to muscle. Anyway brain should be sending just fire requeat and can update ammo based on fire notification (and we would need to add  refused flag to the notification)."*

⇒ A–F approved as leaned, with A REFINED: the brain sends only the fire REQUEST; the muscle runs every check and either fires or REFUSES; the fire notification gains a REFUSED flag (with the reason — masked, too close, out of range); the brain's ammo count follows the notifications (a fired round spends, a refused one does not) — so §2 A's "a refused shot must give the round back" falls away: the brain never spends before the muscle has fired.

## 0. The decision, in under a page

**Decision (recommended):** move each read to where the world already is, by site kind — ① the **fire gates** (muzzle masked,
indirect-fire arc, bomb release) move INTO the muscle's fire pipeline, which already flies the round through the live terrain and
answers back through a status; ② the **danger-area contact sight** rides the danger-area sensor that ALREADY solves on the muscle;
③ **heights** take a replicated height (the entity's own Z) or are resolved once on the muscle; ④ the dormant raycast node stays.
The decisions (relocate, hold, cross, which charge to order) stay on the brain.

**Rejected:**
- *one WorldSurvey answer for everything* — it is a standing ~1 s answer; a per-shot gate cannot wait for it or use a stale one (§1 row 6).
- *keep the brain gates and call them "advisory"* — R-2037 forbids it outright, and destruction is the case the brain cannot see at all.
- *a "brain node" flag that skips the checks* — none exists (§1 row 5), and the editor fuses brain and muscle in one world, so a flag
  would silently switch the gate off there.

**Design docs checked:**
- **Defend and Attack §13b** — applies: the ruling and the inventory. It does not settle the homes; it lists them as "each moves onto an answer from the muscle".
- **World Survey** — applies to B only: it is a standing sensor, and has no point-to-point or arc shape.
- **World Query Seam** — applies: it already marks "the brain never touches it" as false. It is silent on the homes.
- **Weapon Systems** — applies to A: `WeaponTask` is the existing brain-to-muscle fire channel with a status back. It has no masked status today.

## 1. Claim table — what the leans rest on

| # | claim | code — how it IS | design — how it was MEANT |
|---|---|---|---|
| 1 | doors are CGF-owned (created at scenario load, brain only) | ✅ `NedOwnershipGroupBinding.cs:98-100` (no group ⇒ creator); `ScenarioLoadStep.cs:180-187` | ⛔ searched `docs/` — R-2037 assumes the muscle owns them; no design says so |
| 2 | clouds are CGF-owned (owner 0 ⇒ the default processor) | ✅ `AreaEffectClouds.cs:169`; `CgfSubsystem.cs:892` | ✅ `DESIGN_Civilians_And_Crowd_Control.md:913` "a CGF-owned area entity" |
| 3 | the fire gate `MuzzleMasked` runs on the brain only | ✅ `CgfLogicPack.cs:167` (AimAndFire in the Brain executor set); no muscle pack registers a weapon executor (`CombatModule.cs:37-45`) | ✅ §13b's inventory row |
| 4 | the muscle flies every round through the live terrain but REFUSES nothing | ✅ `FireProcessingSystem` honours any intent; `BallisticsSystem.cs:184`, `HitResolutionSystem.cs:125-128` carry it | ⛔ searched Weapon Systems — no "the muscle may refuse" rule |
| 5 | there is no "this node is the brain" flag at run time | ✅ `HrotNodeContext.cs:42-169` has no role; the role is known only at composition (`HrotNodeBuilder.cs:74-83`) | ⛔ searched, none found |
| 6 | WorldSurvey is standing, ~1 s refresh, 3 s staleness; no point-to-point or arc shape | ✅ `WorldSurveySolve.cs:~68-90`; World Survey §5 | ✅ World Survey §5 (DA-25 as amended, R-2038) |
| 7 | the danger-area sensor ALREADY solves on the muscle (finds the areas), only the contact-sight rating is on the brain | ✅ `DangerAlongRouteSolve.cs:40` via `EqsSolverSystem.cs:392`; `DangerAreaSensorSystem.cs:106,160,184` | ✅ `DESIGN_Utility_AI_Demo_Scenarios.md:939,954` (the hold releases the moment the rifleman sees the hostile reach cover) |

⚠ Built from one read-only sweep (every hit traced to its pack registration) plus my own spot-checks of rows 2–3. codebase-memory's
`check_index_coverage` is not available through the CLI, so the inventory is a sweep, not proof of completeness.

## INVENTORY — the queries run *(2026-10-19)*

| query | total | |
|---|---|---|
| grep `WorldQuery.Of\|TerrainWorld\|SightBlocked\|SegmentBlocked\|GroundHeightAt\|ResolveLevel\|WeatherField.(At\|WindAt)` over `Fdp.Toolkits`, then each hit traced to the pack that registers its system | 10 sites (a–j below) | the CE-2287 list, re-measured; ⭐ one more found: `GasFootprints.cs:58` (muscle-side, but it predicts the drift the cloud system makes — routed with E) |
| codebase-memory `search_graph name_pattern=.*(Brain\|NodeRole\|IsBrain).*` | — | no run-time role flag (claim 5) |

## 2. Sub-questions — each with a recommended answer

### A. The fire gates: muzzle masked (b), indirect-fire arc / charge (d), bomb release (c)

**⭐ Lean: the CHECK moves into the muscle's `FireProcessingSystem`, the DECISION stays on the brain.** The brain sends the intent as
today. The muscle, which already has the eye, the barrel and the live terrain for the round, refuses a masked shot instead of flying it.
It chooses the lowest clearing charge and arc for an indirect-fire point, and releases a bomb when the cue says so. It reports back
through a status (`Masked`, `TooClose`, `OutOfRange`, `Released`) on the existing fire-notification road. The brain keeps the
consequence: `PeekAndFire` relocates on `Masked` exactly as now. Its reason source changes, not its behaviour.
- **Blast radius:** the fire chain. Ammo is spent on the brain at fire time (`AimAndFireExecutor.cs:299-301`), so a refused shot must
  give it back, or spending moves to the muscle with the round. The executors' `Masked` hold becomes one frame late (the status
  round trip). Every rail that asserts a brain-side `Masked` moves to the muscle.
- **What would change it:** if you accept a one-shot delay before a masked man relocates (he fires once into the wall first), the gate
  could stay and simply become a status. That is cheaper, but it reproduces the `ua-rpg-window` self-kill once.
- ⛔ **Not WorldSurvey:** a per-shot gate cannot use a ~1 s standing answer (claim 6).

### B. The danger-area contact sight (a)

**⭐ Lean: the remembered contacts' positions go INTO the danger-along-route sensor's config, and its muscle solve returns, per area,
"seen from a contact" (a bitmask). The brain multiplies the area's danger by it; that stays its own decision.** The sensor already solves
on the muscle (claim 7); this adds a column to its answer.
- **Blast radius:** the CE-3138 hold rail (`EqsDistributedTests ~583`) and the utility demo's timing. The release now waits up to one
  sensor refresh (~0.1–1 s), not the same tick. A re-pointed contact list bumps the epoch.
- **What would change it:** if the same-tick release is load-bearing for a demo, the brain keeps a last-answer cache and releases on
  "contact no longer in a seen area".

### C. Which side of a door to walk to (e, `DoorNodes.ApproachPoint`)

**⭐ Lean: a door-keyed navigation destination resolved on the muscle** (`NavigationDestination.Of`, which already turns an `OnSurface`
intent into a 3-D point there). The brain says "the near side of door N from where I stand", and the muscle picks the point.
- **Blast radius:** `MoveToDoor` and `DoorLocksmithBehavior` only.
- **What would change it:** none foreseen. The door geometry is authored and static, so this site is R-2037 by principle, not by
  stale data.

### D. A spawned entity's ground level (f, `NetworkSpawningSystem` `ResolveLevel`)

**⭐ Lean: the level stays in the spawn command and is resolved ONCE on the node that owns the position (the muscle), as
`VehicleCommandSystem.cs:61` already does for vehicles.** CGF stops resolving it.
- **Blast radius:** every owner-0 spawn with a non-absolute height. A person is born at Z = level-unresolved for one frame on the
  brain's replica.
- **What would change it:** if a brain-side reader needs the true Z in the birth frame, which no site measured so far does.

### E. Ground height for cloud drift (g) and the fog judge (h) — ✅ BUILT as leaned (`CE-3258`)

**Taken: the entity's own Z is the ground** (`WeatherField.WindAt` / `At` take an explicit ground; the cloud lies where it burst, and
a judged man stands on his surface). There are no terrain reads on the brain for these two, and `GasFootprints` uses the same rule, so
its predicted drift is the drift.
- ⭐ **Found by its rail, and fixed in the same item:** under a scenario wind (a level above the ground, a log profile to zero AT the
  ground), a cloud's BASE is height 0. So a cloud lying on the ground NEVER drifted; only the tests' single-level steady wind moved it.
  It now drifts on the wind at its MIDDLE (`AreaEffectClouds.DriftWind`). Civilians §7.3b's drift row carries the as-built.
- **Blast radius:** a man on a roof or upstairs is judged with the visibility at his own floor, not at the roof height above the
  terrain. ⚠ Fog thickens near the ground, so an upstairs man is judged in thicker fog than before.
- **What would change it:** if you want height above the TERRAIN there, it becomes a PointSurvey answer.

### F. The premise: doors and clouds are CGF-owned (§1 rows 1–2)

**⭐ Lean: no ownership move now.** R-2037 still holds as architecture: destruction exists nowhere but the muscle, and a Stride or
production muscle will own world state the FDP brain never mirrors. But the reason the brain's answer is wrong TODAY is narrower than
the ruling says: it is destruction, not doors or smoke.
- **What would change it:** if you want the premise literally true, moving clouds to the muscle (an ownership-group grant at
  `NedOwnershipGroupBinding.cs:122-123`) is a small, separate item, and it would make the cloud's drift a muscle job.

### Site j — `Action_QueryRaycast` (dormant)

**⭐ Lean: leave it** (unreferenced is not unintentional). Re-point it at WorldSurvey when a behaviour wants it. No deletion.

## 3. Sequence — A as leaned

```mermaid
sequenceDiagram
  participant B as AimAndFireExecutor (brain)
  participant F as FireProcessingSystem (muscle)
  participant T as live terrain (muscle)
  B->>F: fire intent (target, mount, aim)
  F->>T: ShotOrigin.From + TerrainPenetration.Carry (the muzzle line)
  alt masked
    F-->>B: status Masked (no round, ammo not spent)
    B->>B: hold reason Masked ⇒ PeekAndFire relocates
  else clear
    F->>F: the round flies (as today)
    F-->>B: fire notification
  end
```

```mermaid
classDiagram
  direction LR
  class AimAndFireExecutor { <<EXISTING brain>> intent · consumes Masked status }
  class FireProcessingSystem { <<EXISTING muscle, grows>> refuses a masked shot · picks the arc · releases the bomb }
  class LineOfFire { <<EXISTING>> MuzzleMasked — called by the muscle only }
  class FireSolution { <<EXISTING>> For(world) — called by the muscle only }
  class DangerAlongRouteSolve { <<EXISTING muscle, grows>> seen-from-contact mask per area }
  class DangerAreaSensorSystem { <<EXISTING brain>> rates by the mask, no terrain }
  class NavigationDestination { <<EXISTING muscle, grows>> door-keyed near side }
  AimAndFireExecutor ..> FireProcessingSystem : intent / status
  FireProcessingSystem ..> LineOfFire
  FireProcessingSystem ..> FireSolution
  DangerAreaSensorSystem ..> DangerAlongRouteSolve : the answer
```
*What the pictures show that prose hid:* every geometry call (`LineOfFire`, `FireSolution`, the contact sight) gets exactly one caller,
on the muscle. The brain's side shrinks to reading statuses and answers, which is R-2037's shape.

## 4. A — the detailed design as RULED (R-3018) *(backend, `2026-10-19`; build-state: READY-TO-BUILD)*

INVENTORY *(codebase-memory CLI `search_graph .*Fire(Notification|Intent|Request|Refus).*` + grep, one sweep; ⚠ `check_index_coverage`
is not available through the CLI)*:

| what | where | ⭐ |
|---|---|---|
| the gates on the brain | muzzle masked `LineOfFire.MuzzleMasked` (`Combat/LineOfFire.cs:65-84`) called at `AimAndFireExecutor.cs:270-276` (aimed) and `:441` (blind); min range `HoldOf` `:616-619` (a distance, NO geometry — stays); indirect `FireSolution.For(WorldQuery…)` at `FireAtPointExecutor.cs:121`; bomb `ReleaseCue` (`AimAndFireExecutor.cs:632-665`, `GroundHeightAt`) and `FireAtPointExecutor.cs:162-170` | the three geometry gates move; min range stays (it reads no world) |
| the request | `WeaponFireIntent` (`Combat/Events/WeaponFireEvents.cs:25-69`) → egress `WeaponFireIntentEgressTranslator` → DDS `WeaponFireRequest` (`FireInteractionMessages.cs:30-67`) → `WeaponFireRequestIngressTranslator` ⇒ `FireProcessingSystem` (`Combat/Systems/FireProcessingSystem.cs:54-400`) | the muscle checks NO ammo and NO geometry today; a failed check is a silent `continue` |
| the notification | `WeaponFireNotification` (`WeaponFireEvents.cs:85-120`, 76 B pinned by `CombatComponentTests.cs:117`), one per round, DDS `WeaponFire`, crosses to every node | no field can say "refused" (`Quantity` 0 would still be drawn and heard; `Round` = Null means a decoy) ⇒ APPEND |
| ammo | spent on the brain at request time (`AimAndFireExecutor.cs:299-300, 455-456, 381`, `FireAtPointExecutor.cs:179, 286`, `HiddenTargetThrow.cs:58`, `Countermeasures.cs:117-118`); ⭐ the TURRET path already spends from the muscle's `RoundsFired` (`:501-510`, W-4 `DESIGN_Weapon_Layers.md:184`) | ⭐ R-3018 makes the turret's rule THE rule |
| a brain plan reading geometry Q94 did not name | `IndirectFireBehaviors.SolutionFor` (`Brains/IndirectFireBehaviors.cs:82-90`; plan `:261`, sheaf `:307-309`) | ⇒ A-7 |

```mermaid
classDiagram
  direction LR
  class WeaponFireIntent { <<EXISTING, + RequestId appended>> Shooter Target WeaponIndex AimPoint Charge RaisedAt }
  class WeaponFireRequest { <<EXISTING DDS, + RequestId appended>> }
  class WeaponFireNotification { <<EXISTING, + RequestId + Refusal appended>> Quantity FiredAt Round }
  class FireRefusal { <<EXISTING enum, grows>> None Masked TooClose OutOfRange Expired }
  class FireProcessingSystem { <<EXISTING muscle, GROWS>> the gates: MuzzleMasked, FireSolution, the bomb cue · fires or REFUSES }
  class LineOfFire { <<EXISTING>> MuzzleMasked }
  class FireSolution { <<EXISTING>> For(IWorldQuery) }
  class BombBallistics { <<EXISTING>> ReleaseNow }
  class ShotLedger { <<NEW, brain, per mount>> Pending(RequestId, rounds, sentAt) · Spend on fired · Clear on refused or timeout }
  class AimAndFireExecutor { <<EXISTING brain, SHRINKS>> sends the request, reads the refusal as its hold reason }
  class FireAtPointExecutor { <<EXISTING brain, SHRINKS>> sends point + "solve there" }
  class WeaponState { <<EXISTING brain>> Ammunition (the one counter) }
  AimAndFireExecutor --> WeaponFireIntent
  FireAtPointExecutor --> WeaponFireIntent
  WeaponFireIntent ..> WeaponFireRequest : egress across nodes
  WeaponFireRequest ..> FireProcessingSystem
  FireProcessingSystem ..> LineOfFire
  FireProcessingSystem ..> FireSolution
  FireProcessingSystem ..> BombBallistics
  FireProcessingSystem --> WeaponFireNotification : fired, or refused with a FireRefusal
  WeaponFireNotification ..> ShotLedger : matched by RequestId
  ShotLedger --> WeaponState : spends only what flew
  ShotLedger ..> AimAndFireExecutor : the refusal is the hold reason
```
*What the picture shows that prose hid:* the brain's ammo counter gets ONE writer for rounds, the ledger, fed only by notifications;
and every refusal reaches the executor through the same notification the round would have — no second status channel.

```mermaid
sequenceDiagram
  participant X as AimAndFireExecutor (brain)
  participant L as ShotLedger (brain)
  participant F as FireProcessingSystem (muscle)
  X->>L: Reserve(mount, rounds) — refused if ammo minus pending is 0
  X->>F: WeaponFireIntent { RequestId }
  F->>F: MuzzleMasked / FireSolution / bomb cue (live terrain)
  alt refused
    F-->>L: WeaponFireNotification { RequestId, Refusal = Masked, Quantity 0 }
    L->>L: clear the pending reservation, spend nothing
    L-->>X: hold reason Masked (MaskedSince) ⇒ PeekAndFire relocates
  else fired
    F-->>L: WeaponFireNotification { RequestId, Quantity n }
    L->>L: spend n from WeaponState, clear the reservation
  end
  L->>L: a reservation older than the timeout clears (a lost request)
```
*What the picture shows that prose hid:* ammo is reserved at the request (so a burst cannot over-ask between request and report) and
SPENT only at the report — the brain still never asks for more than it has.

| # | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **A-1** | the carrier | APPEND `RequestId` (uint, per shooter, wraps) to `WeaponFireIntent` / `WeaponFireRequest`, and `RequestId` + `Refusal` (`FireRefusal`, byte) to `WeaponFireNotification` / DDS `WeaponFire` (F2: appended, an old sample reads `None`); the size rail moves 76 → 81 B | `Quantity` 0 as "refused" — every reader would draw and hear it · a separate refusal topic — a second channel for one answer |
| **A-2** | readers of a refused notification | every current reader (sound, heat, effects, traffic panic) SKIPS `Refusal != None` — one predicate `WeaponFireNotification.Fired` | leaving them — a refused shot would be heard |
| **A-3** | ammo | the brain RESERVES at request (ammo − pending > 0 to ask) and SPENDS on the fired notification; a refusal or a timeout (2 s) clears the reservation | spend at request and refund on refusal — a lost notification leaks rounds forever · moving `WeaponState` to the muscle — rejected by W-4 |
| **A-4** | masked | `FireProcessingSystem` runs `LineOfFire.MuzzleMasked` on the live terrain before the round; refused ⇒ `Masked`; the executor's hold reason comes from the refusal (`MaskedSince` as today) | keep the brain check "advisory" — R-2037 |
| **A-5** | indirect | the brain sends the point and asks the muscle to SOLVE (`Charge` = auto); `FireSolution.For` runs on the muscle; refused ⇒ `TooClose` / `Masked` / `OutOfRange` and the action fails with that reason one frame later | the brain solving with the terrain — R-2037 |
| **A-6** | the bomb | the brain sends the release REQUEST when its own ballistic cue (no terrain: the replicated height) says "about now"; the muscle releases at its true cue (`BombBallistics.ReleaseNow` with the live ground) or refuses `Expired` after 3 s | the brain timing with `GroundHeightAt` — R-2037 |
| **A-7** | the battery plan (`IndirectFireBehaviors.SolutionFor`) | plans with the terrain-free reach (`FireSolution.For(null…)`: range and arc only); a masked gun is learned from its refusal per shot | the plan reading the terrain — R-2037; it was not named in Q94 but is the same site kind |

**Blast radius (measured):** the request/notification structs and two DDS topics (appended) · `FireProcessingSystem` · the two executors,
`HiddenTargetThrow`, `Countermeasures` (spend sites) · `IndirectFireBehaviors` · tests that move: `AimAndFireExecutorTests` CE2218 ×2,
M4, A6, CE2173 ×2, CE2180, IF1 ×2; `ActionStatusTests.Masked_IsAHold`; spend-at-fire tests `WeaponStateTests:39`,
`CountermeasureSystemTests:135`, `StarterPackIntegrationTests:694`, `AimAndFireExecutorTests:855`. ⚠ `BS-1-DESIGN.md` §5.4c ("one
counter, the brain's, as for every round") stays true — the counter is the brain's — but its spend MOMENT changes; fold on build.

### 4.1 A — AS BUILT *(backend, `2026-10-19`; A-1…A-4 BUILT; A-5 indirect, A-6 bomb, A-7 battery plan NOT YET)*

```mermaid
classDiagram
  direction LR
  class WeaponState { <<EXISTING brain, + 5 ledger fields>> Ammo MagazineRounds CooldownSecondsRemaining LastRequestId LastAnsweredId PendingSeconds RefusalHoldSeconds Refusal }
  class ShotLedger { <<NEW static>> Ask() Pending() Held() Tick() Answer() Settle() MountOf() }
  class Magazine { <<EXISTING>> Ready counts the pending asks · Spend }
  class WeaponDispatcherSystem { <<EXISTING brain>> Settle once a frame · Tick beside the cooldown drain }
  class AimAndFireExecutor { <<EXISTING brain>> asks · holds on Held · HoldReasonOf }
  class FireAtPointExecutor { <<EXISTING brain>> ShootAtPoint asks · holds on Held }
  class HiddenTargetThrow { <<EXISTING brain>> asks }
  class FireProcessingSystem { <<EXISTING muscle>> MuzzleMasked for entity and point · Refuse }
  class WeaponFireNotification { <<EXISTING>> RequestId Refusal Fired }
  AimAndFireExecutor ..> ShotLedger : Ask, Held
  FireAtPointExecutor ..> ShotLedger : Ask, Held
  HiddenTargetThrow ..> ShotLedger : Ask
  ShotLedger --> WeaponState : the ledger lives here
  Magazine ..> ShotLedger : Pending
  WeaponDispatcherSystem ..> ShotLedger : Settle, Tick
  FireProcessingSystem --> WeaponFireNotification : fired or refused
  WeaponFireNotification ..> ShotLedger : Settle matches RequestId
  ShotLedger ..> Magazine : Answer fired spends
```
*What the picture shows that prose hid:* there is no new component and no new system — the ledger is a rule over fields of the counter it
guards, driven by the dispatcher that already drains that counter's cooldown and reload; and the three brain paths that make a per-shot
round all go through the same two calls (`Ask`, `Held`).


| step | what landed | rails *(all red-proofed by mutants)* |
|---|---|---|
| **A-1** carrier | `WeaponFireIntent.RequestId` (uint, 70 B) · `WeaponFireNotification.RequestId` + `Refusal` (81 B) + `Fired` · DDS `WeaponFireRequest.RequestId`, `WeaponFire.RequestId` + `Refusal` (appended) · `FireRefusal` grows `Expired = 4`, `CannotFire = 5` | size rails · `M8_TheLoadedRound_RoundTripsTheWire` · `Q94_TheAnswer_CarriesTheRequestIdAndTheRefusal` · the IG ingress rail |
| **A-2** readers | sound, heat, effects, event-to-effect, traffic panic skip `!Fired` | `Q94_ARefusedShot_IsNotHeard` |
| **A-2** muscle answers every request | `FireProcessingSystem`: no `WeaponState` / a laid-down mount ⇒ `CannotFire`; the fired notification echoes `RequestId`; one `Refuse` helper | `FireProcessingSystemTests.Q94_AShotTheMuscleCannotMake_IsAnsweredCannotFire` |
| **A-4** masked | `FireProcessingSystem` runs `LineOfFire.MuzzleMasked` on the live terrain for an entity target AND a direct round at a point ⇒ `Masked`; ⛔ the brain's two `MuzzleMasked` calls (aimed, blind) are GONE | `Q94_AMaskedShot_IsRefusedByTheMuscle_AnOpenOne_IsFired_EachAnswerNamesItsRequest` (bt-range, the live `ua-rpg-window` geometry) · `Q94_AMaskedRoundAtAPoint_IsRefusedByTheMuscle_AnOpenOne_Flies` |
| **A-3** ledger | `ShotLedger` (static, `Combat/ShotLedger.cs`) over five APPENDED `WeaponState` fields (`LastRequestId`, `LastAnsweredId`, `PendingSeconds`, `RefusalHoldSeconds`, `Refusal`; 32 → 52 B). `Ask` at the request; `Magazine.Ready` reserves the pending rounds; `WeaponDispatcherSystem` calls `ShotLedger.Settle` (answers ⇒ `Magazine.Spend` + `AmmoChoice.SpendLoaded`) and `ShotLedger.Tick` (2 s timeout, 1 s refusal hold) | `Q94_TheAsk_ReservesTheRound_TheFiredAnswerSpendsIt_Once` · `Q94_AMaskedRefusal_SpendsNothing_HoldsMasked_ThenAsksAgain` · `Q94_AnUnansweredAsk_TimesOut_NothingSpent_ALateAnswerIgnored` |

⭐ **Tests that moved (the spend moment changed, not what they assert):** a brain-only test world has no muscle, so `Fdp.Toolkits.Tests`
gained `TestMuscle.FireAll` (every ask answered "fired") — called from `AimAndFireExecutorTests.Tick` and before the ammo asserts of
`G3_R1`, `P2_ASelfPropelledGun`, `IF1_AGunnerFiresTheMortar`, `CE2173_ABombAtAPoint`, `PersonHandsTests.CE3230`, `StarterPack CE3089`;
`AirAttackNodesTests` (SimHost) answers the same way after each executor frame — without it the ledger's reservation cap stopped the
rocket ripples and door-gun bursts (7 reds, all of that one harness). `WeaponStateTests` size rail 32 → 52.

**Deviations from §4, each argued:**

| §4 said | built | why |
|---|---|---|
| `ShotLedger` a NEW class, per mount, `Pending(RequestId, rounds, sentAt)` | a static helper over five fields APPENDED to `WeaponState` (the ids are per mount and rise by one, so pending = `LastRequestId − LastAnsweredId`; one age, not one per ask) | the counter it guards already lives there (W-4: the brain's one counter) — no new component to register on every brain world, and no second entity lookup per shot. ⚠ An answer that skips ids drops the skipped asks unspent (the DDS writer is ordered, so it is only a lost sample) |
| a refusal clears the reservation | it also CANCELS the cooldown the ask set and holds the weapon `RefusalHoldSeconds` (1 s) with the reason | without it a long-cooldown weapon (an RPG) reported `Cooldown` for its whole interval before `Masked` reached the behaviour — `PeekAndFire` would burn the window late. The hold is the "re-check hold" §4 left implicit: the brain does not re-ask every frame for a shot the muscle just refused |
| (silent) | the muscle judges a DIRECT round at a POINT too, not only one at an entity | the blind path's brain check is gone, so the muscle must own it; an operator's direct round into the wall at his muzzle is now refused as well (it would have stopped there anyway) |
| (silent) | the action's round count still counts ASKS; a refused round is not given back to the burst | a refusal holds the weapon and the behaviour moves (the `Masked` contract); refunding into a running action's slot would need the executor to read the ledger per frame for a case the behaviour already leaves |
| — | ⛔ **not on the ledger:** the turret path (spent from the muscle's `RoundsFired`, W-4 — unchanged) and a DISPENSER release (`Countermeasures.Release`: the muscle never answers a dispenser, `FireProcessingSystem` skips it) | both already have their own honest counter |

⚠ `FireRefusal` has no `OutOfRange` (§4's class box says so): the indirect "out of reach" is the existing `TooFar`, read as the hold
`OutOfRange` (`AimAndFireExecutor.HoldReasonOf`). §4's table and diagrams above stay as the RULED design; this subsection is the as-built.

## 5. C and D — the carriers, measured *(backend, `2026-10-19`; build-state: READY-TO-BUILD as leaned below)*

📐 Measured with the codebase-memory CLI + grep (one sweep; ⚠ `check_index_coverage` is not available through the CLI).

### 5.1 C — the door approach

| fact | where |
|---|---|
| ⛔ no free carrier in the request path: `MoveToParams` is at its 32-byte limit (`NavigationActions.cs`), `NavigationIntent.Flags` bits 0–7 are all taken, `RoadUse` needs 3 bits (6 values, `NavigationComponents.cs:176-192`); a door key (`TerrainObjectRef`, a 64-byte `FixedString64`) cannot ride | `BehaviorConstants.cs:10`, `NavigationConstants.cs:86-125` |
| ⭐ `NavigationIntent.RouteEntityId` (long) is ON THE WIRE (`SimDescriptors.cs:745`) and read only under FollowRoute (`NavigationIntentBridgeSystem.cs:103`, `AirMotionSystem.cs:48/61`) | — |
| door entities replicate (SimTransform on `dtWorldPos`, the key on the door-state descriptor) ⇒ the muscle can find a door by network id; its `TerrainWorld` has `Doors`, `Panels`, `DoorIndexOf` | `TerrainObjectRequests.cs:49-62`, `EntityDoorStateIngressTranslator.cs:79`, `TerrainWorld.cs:111-145` |
| one production caller: `DoorLocksmithBehavior` via `MoveToDoor` | `DoorLocksmithBehavior.cs:32/48`, `DoorNodes.cs:100` |

| # | ⭐ lean | rejected — one line each |
|---|---|---|
| **C-1** | a new `NavigationMode.ApproachDoor` (enum, appended) with the door's NETWORK id in `RouteEntityId` and the door's centre as `FinalDestination`; `MoveToDoor` raises it through its own small executor (a new action id, its params: door entity, speed, arrival) | a flag bit — none free · reusing `TargetNodeId` / `TrajectoryId` — not on the wire |
| **C-2** | the muscle resolves it in `NavigationDestination.Of` — the ONE place a destination becomes a point — with `DoorNodes.ApproachPoint` moved there (from = the mover's own position); a door it cannot find ⇒ the centre, as today's fallback | a second resolver beside `NavigationDestination.Of` |

### 5.2 D — the spawn's ground level

| fact | where |
|---|---|
| ⛔ **the lean's premise does not hold for a person**: an owner-0 spawn is serviced by the broadcast arbiter — CGF (or the editor) — and step 8b runs ONLY there; the muscle never runs it on its replica, and no system there reads `SpawnHeight` (it is on no replicated descriptor) | `EntityCreationRouting.cs:55-67`, `CreateEntityRequestSystem.cs:187, 334-338`, `CgfSubsystem.cs:892` |
| vehicles differ: `VehicleCommandSystem` resolves on the muscle because the vehicle command RUNS there | `VehicleCommandSystem.cs:61` |
| CGF has terrain resident (R-182, terrain universal) — `WorldQuery.Of` is non-null on every ECS host | `WorldQuery.cs:17-21`, `CgfSubsystem.cs:1088` |
| the creator's first `dtWorldPos` carries its 8b Z; the yield of the Muscle group happens the same tick, after 8b; the muscle's ghost takes the creator's value | `NedReplicationModule.cs:702-750`, `GeoSpatialEgressTranslator.cs:136` |
| publisher: Add Entity's placement gizmo (`EntityPlacementGizmo.cs:271`); ⚠ IG's remote placement DROPS the height (`IgEntityCreationRequests.cs:62-73`) | — |

| # | ⭐ lean | rejected — one line each |
|---|---|---|
| **D-1** | a one-shot `SpawnLevelRequest { Mode, Level }` component written by 8b instead of resolving on a node that will GRANT the position away, in the Muscle ownership group (so it travels with the position grant, one small descriptor); the muscle's `SpawnLevelSystem` resolves it once with its live terrain when it owns the position, writes `SimTransform.Z`, removes it. A node that keeps the position (the editor, a muscle-creator) resolves at 8b as today | append the level to `dtWorldPos` — the hottest descriptor pays 3 bytes forever for a one-time fact · keep CGF resolving — R-2037 by principle, and a floor destroyed before the spawn is unseen there |
| **D-2** | the IG's remote placement carries the height (`IgEntityCreationRequests`) — a found defect, fixed with D-1 | — |
| ⚠ consequence | the person is at the SENT height on the brain's replica until the muscle's first position sample (Q94 §2 D foresaw "one frame") | — |

```mermaid
classDiagram
  direction LR
  class MoveToDoor { <<EXISTING brain node>> raises ApproachDoor instead of computing the side }
  class ApproachDoorExecutor { <<NEW brain>> writes NavigationIntent Mode ApproachDoor, RouteEntityId = door net id }
  class NavigationIntent { <<EXISTING wire>> Mode + ApproachDoor value appended · RouteEntityId · FinalDestination }
  class NavigationDestination { <<EXISTING muscle>> Of(view, mover, intent) · + ApproachDoor }
  class DoorApproach { <<MOVED from DoorNodes>> ApproachPoint(world, door, from) }
  class SpawnLevelRequest { <<NEW component, Muscle group>> Mode Level }
  class NetworkSpawningSystem { <<EXISTING>> 8b: resolve if the position stays here, else write SpawnLevelRequest }
  class SpawnLevelSystem { <<NEW muscle>> resolves once when it owns the position }
  MoveToDoor --> ApproachDoorExecutor
  ApproachDoorExecutor --> NavigationIntent
  NavigationIntent ..> NavigationDestination : on the muscle
  NavigationDestination ..> DoorApproach
  NetworkSpawningSystem --> SpawnLevelRequest
  SpawnLevelRequest ..> SpawnLevelSystem : travels with the position grant
```
*What the picture shows that prose hid:* both sites become a REQUEST the brain writes and the muscle resolves where the geometry
lives — C through the one destination resolver, D through the position's own ownership group, so neither adds a second channel.

```mermaid
sequenceDiagram
  participant C as CGF (creator)
  participant M as SimHost (muscle)
  C->>C: 8b — the position is granted away: SpawnLevelRequest, no terrain read
  C->>M: dtWorldPos (sent Z) + SpawnLevelRequest, then the Muscle group grant
  M->>M: SpawnLevelSystem — owns the position: ResolveLevel with the live terrain, set Z, remove the request
  M->>C: dtWorldPos (resolved Z)
```
*What the picture shows that prose hid:* the brain's replica holds the sent height for exactly the trip to the muscle and back.
