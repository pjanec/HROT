<!--STATUS
state: LIVE
updated: 2026-11-18
build-state: READY-TO-BUILD — ✅ APPROVED by the user `2026-11-18` ("approved, fold both into the design", R-4296): VA-1…VA-12, LK-1…LK-7, with §3a (partial damage, how expressed) and LK-7 (effect categories + the DIS munition type); ids fixed at dispatch — `2026-11-18`: components 290 `VehicleOpenings` · 291 `VehicleLights`; descriptors 243 · 244 (`HANDOFF_Coordinator_To_Backend_Vehicle_Appearance.md`)
current-answer: §1 inventory · §2 the proven solution · §3 decisions · §3a partial damage, how expressed · §4 components and TKB · §5 UML · §6 slices · §6a hatches, doors, ramps as built (VA-S2) · §6b lights as built (VA-S3) · §6c partial damage and the kill kind as built (VA-S1) · §7 the shared look library (CE-4671) · §7a effect categories · §7b LK-S1 as built (Hrot.Look, EffectRules)
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../thermal/DESIGN_Thermal_Drivers.md — OWNS `HeatSource` (engine Off / Idle / Running / Dead + Since) and `FireState` (fire spots, ignition, burn damage, the wreck's flames) — this design READS both for rotors, engine sound and burning parts, and adds nothing beside them
  - ../hit-location/DESIGN_Body_Parts_And_Hit_Location.md — OWNS `PartDamage` (per-part levels for vehicles and aircraft, HL-6), the part ids, the zones (HL-12 class templates) and the DERIVED mobility / firepower kill (S2-8, S2b-6) — this design only exposes them as a visible state
  - ../../DESIGN_Operations_Behaviour_Catalogue.md — §10.1 D1 OWNS R-263 (mobility kill · firepower kill · destroyed)
  - ../../DESIGN_Visual_Effects.md — OWNS W3 (`WreckSpawnSystem`, a per-display burn) that the thermal design re-bases on `FireState`; our 3D map draws THIS design's state
  - ../../DESIGN_Entity_Interactions.md — §7.4 OWNS embarkation, the seats and the boarding arbiter (the `PassengerBuffer` owner) that the openings follow
  - ../IG/DRAFT_Unreal_Image_Generator.md — an external renderer is the first outside consumer (U-2 "the minimal entity snapshot")
  - ../../reference/NED_DDS_Protocol.md — the wire as outsiders see it; "appearance bits" listed as NOT carried — VA-11 fills that for the gateway
  - ../weather/DESIGN_Weather_And_Time.md — the light curve (R-3017) the light rule reads
-->

# DESIGN — RENDERER-FACING VEHICLE STATE: lights, hatches and doors, rotors, partial damage (CE-4670)

🔒 Request (`2026-11-18`, another lane for the user): an external renderer reading HROT over DDS through a gateway must show vehicles convincingly —
lights, hatches / doors / ramps, rotors and engines, partial damage, smoke launchers, wreck flames. *Simulation state, not presentation;
recorded; replicated; generic and renderer-agnostic; per-type data in the TKB with ids a renderer maps to its model parts; HROT's own 3D map
consumes the same state.* Lights, hatches and partial damage matter most.

## 1. INVENTORY *(measured `2026-11-18`; grep over `FDP/` and `Hrot/` for `struct .*(Light|Hatch|Door|Ramp|Rotor|Engine|PartDamage|VehicleState|FlightState|Seat|Smoke|Wreck|Effect|TurretPose|WeaponSystem)`, `Headlight|Blackout|Searchlight`, `Rpm`, `Mobility|Firepower`; the design corpus for hatch / ramp / rotor / kill kinds. ⚠ graph search returned document sections only for these names, so the list rests on grep; index coverage cannot be checked through the CLI)*

| piece | where | what it gives a renderer | verdict |
|---|---|---|---|
| `PartDamage` | `Physics/BodyParts/BodyPartComponents.cs:17` | a level 0–255 per part, written by the Health owner, replicated (`dtPartDamage`) — ⭐ vehicles AND aircraft | ✅ partial damage EXISTS |
| vehicle part ids | `BodyParts.cs` — Hull 0 · Turret 1 · Gun 2 · RunningGearL 3 · RunningGearR 4 · Engine 5; aircraft Fuselage · Cockpit · Wing · Engine · Tail · Turret · Gun · Stores | fixed ids, the same on every node and the wire | ✅ the ids a renderer maps |
| mobility / firepower kill | `WoundEffect.Mobility` / `Firepower` (`BodyZonesDto.cs:17-20`) DERIVED from `PartDamage` (`Wounds`); applied in `CombatLife.ApplyHitCapabilities` (S2-8); `MobilityKill` (a type's health-fraction rule) | ✅ derived, never stored — every node agrees |
| `HitMarks` | `BodyPartComponents.cs:70` | where rounds struck, per part, part-local | ✅ |
| `TurretPose`, `WeaponSystemState` (incl. `Destroyed`) | existing, replicated | turret and weapon pose / status | ✅ |
| `HeatSource` (thermal, approved, CE-4657/4658) | `DESIGN_Thermal_Drivers.md` §3e | engine Off · Idle · Running · Dead + `Since` | ✅ the engine half of "rotors and engines" |
| `FireState` (thermal, approved, CE-4660) | §3c | fire SPOTS in the vehicle's frame; the wreck burn becomes simulation state | ✅ "burning" per part and the wreck's flames |
| `FlightState` | `Motion/Flight.cs:62` | airborne, vertical speed | ✅ |
| `AreaEffectCloud` (`CloudKind.Smoke`) | `Combat/AreaEffectClouds.cs:35` | a smoke cloud: radius, height, transmittance, lifetime, replicated (`EntityAreaEffectCloud`); IR-blocking flag (CE-4662) | ✅ the smoke a launcher would make |
| `CarrierSeats` (`ExposedMask` = seats that ride exposed — a TYPE fact) · `PassengerBuffer` | `InteractionComponents.cs:147`, `Embarkation.cs` | who sits where; the boarding arbiter is the `PassengerBuffer` owner (`Embarkation.cs:53-55`) | ⚠ no RUNTIME hatch / ramp state |
| `DoorState` / `EntityDoorState` | buildings | a building's doors | ⛔ buildings only |
| `WreckSpawnSystem` | `Hrot.Presentation/Effects/EffectSystems.cs` (W3) | flames + smoke decided per display | ⛔ presentation — re-based on `FireState` by CE-4660 |
| lights | — | grep `Headlight|Blackout|Searchlight|NavLight|LightDiscipline` ⇒ **nothing** | ⛔ missing |
| hatches, doors, ramps (vehicle) | — | nothing beside the seat list | ⛔ missing |
| rotor rpm / engine start-stop | — | grep `Rpm` ⇒ nothing; `HeatSource` gives the state and its time | ⚠ derivable (VA-5) |
| kill KIND (catastrophic vs knocked out vs burnt out) | — | `Health` 0 + `PartDamage` + `FireState`; a turret blown off is not stated | ⛔ one byte missing (VA-7) |
| smoke LAUNCHERS (dischargers) | — | grep `SmokeLauncher|Discharger` ⇒ nothing | ⛔ missing (VA-9) |

⇒ ⭐ **Half of the request is already state.** Partial damage, kills, the engine and fire are built or approved; what is missing is **lights,
openings, the kill kind, smoke launchers — and ONE reader every renderer shares** so nobody re-derives "damaged" or "rotor speed" differently.

## 2. The proven solution, and what we take

| practice | what it is | ⭐ we |
|---|---|---|
| **DIS Entity State appearance / RPR-FOM `PhysicalEntity`** | bit fields for lights (head, tail, brake, blackout, interior, landing, nav, anti-collision, searchlight), hatch (closed / popped / open), ramp, smoke / flaming, damage (none / slight / moderate / destroyed), power plant | ⭐ ADOPT the vocabulary — the gateway maps 1:1 (VA-11); ⛔ not the encoding (our parts and lights are per-type lists with ids, richer than DIS bits) |
| **game-engine vehicle rigs** | a model names its light sockets, doors and damage sets; the simulation drives them by id | ⭐ ADOPT: TKB ids per type, the renderer maps id → its socket |
| **our thermal drivers rule (TD-1)** | a state + the time it began, written on change; every reader computes "now" (a ramp half-open, a rotor spinning up) | ⭐ ADOPT — so an animation's progress is computed, never streamed |

## 3. Decisions — leans for the user

| # | question | ⭐ lean | why |
|---|---|---|---|
| **VA-1** | the shape | ⭐ semantic STATES + `Since`, written on change, recorded and replicated; animation progress is DERIVED (`Since` + the TKB's open / spin-up seconds) | the thermal rule; a late joiner sees a ramp mid-opening right |
| **VA-2** | one reader | ⭐ ONE static reader `VehicleLook` (Fdp.Toolkits): per-part visible state, kill kind, rotor speed, effective lights (brake from deceleration), opening progress — our 3D map, the gateway and any renderer call it | one truth; ⛔ no renderer re-derives "damaged" its own way |
| **VA-3** | lights | ⭐ NEW `VehicleLights`: an on-mask over the type's light GROUPS + a `Discipline` (Normal · Convoy · Blackout · Off) + `Since` + a searchlight azimuth / elevation. Written by the vehicle's BRAIN (the crew decides): the rule = discipline (SOP / ROE / an order) × darkness (the weather light curve, R-3017) × engine state (`HeatSource`) × airborne (nav, anti-collision, landing near the ground); a possessed vehicle by the station's switches (a command to the owner). ⛔ brake lights are NOT stored — derived from deceleration | lights are a crew decision; light discipline is the military point (a blacked-out convoy) |
| **VA-4** | hatches, doors, ramps | ⭐ NEW `VehicleOpenings`: per opening of the type (≤ 8) a state (Closed · Opening · Open · Closing; a hatch also `CrewExposed`) + `Since`. Written by the BOARDING ARBITER (the `PassengerBuffer` owner, `Embarkation.cs:53`) — embark / dismount OPENS the ramp or doors and closes them after; a crew member's "head out" (observing, an order, the SOP) is a request to that owner; doors-off helicopters are TYPE data (no opening) | one writer that already owns who boards; the opening follows what the simulation does |
| **VA-5** | rotors, propellers, engines | ⭐ NOTHING NEW: `HeatSource` (engine state + `Since`) + `FlightState` + the TKB's spin-up / spin-down seconds ⇒ `VehicleLook.RotorSpeed(now)` 0..1 (ground idle vs flight from airborne) and starting / stopping from `Since` | ruling 9 — the engine state is already approved; a speed field would be per-frame noise |
| **VA-6** | partial damage | ⭐ NOTHING NEW for the parts: `PartDamage` (exists, per part, replicated) → `VehicleLook.PartState` = Intact · Damaged · Destroyed by the zone's thresholds (HL-12 class templates), Burning when a `FireState` spot covers the part; the kills stay DERIVED (`Wounds` — mobility / firepower, R-263) | the request's own words: *"if the damage model already computes this internally, expose it"* — it does |
| **VA-7** | the kill KIND | ⭐ append ONE byte to `Health` / `EntityDamage`: `Destruction` = None · KnockedOut (crew dead, hull intact) · BurntOut · Catastrophic (an ammunition / fuel explosion — the turret thrown); decided ONCE by the Health owner at destruction from the killing hit's part and the type's TKB (ammo / fuel stowage) | the one fact a renderer cannot derive and that changes the model completely; beside the thermal `Health.StateSince` |
| **VA-8** | the TKB | ⭐ `Gen.Lights` { groups: id, kind (Head · Tail · Brake · Interior · Convoy · Searchlight · Nav · AntiCollision · Landing), a socket name } and `Gen.Openings` { id, kind (Hatch · Door · Ramp · CargoDoor), the seat(s) it serves, open seconds, a socket name }; parts already have ids (body plans, zones); ⭐ defined ONCE PER CLASS (an MBT, an APC, a truck, a car, a helicopter, a jet) and overridden per type only where needed — HL-12's rule | a renderer maps `socket` / id → its model; classes keep the TKB small |
| **VA-9** | smoke launchers | ⭐ a weapon-mount kind `SmokeDischarger` (a salvo pattern in the TKB) whose round makes the EXISTING `AreaEffectCloud` (Smoke, IR-blocking by type); the "pop smoke when threatened" decision is a behaviour (behaviors) | the cloud, its replication and its IR flag already exist |
| **VA-10** | wreck flames and smoke | ⭐ NOTHING NEW: the thermal `FireState` (CE-4660) makes the wreck's fire simulation state; smoke after the flames = the spot's end + the type's smoulder seconds (`VehicleLook`) | already approved and dispatched |
| **VA-11** | the gateway | the DIS appearance mapping joins the thermal TD-11 table in the protocol reference: lights → the light bits, openings → hatch / ramp, `PartState` + `Destruction` → the damage state, `FireState` → flaming / smoking, `HeatSource` → power plant | an outside product needs it written down |
| **VA-12** | the wire | ⭐ two descriptors (`VehicleLights`, `VehicleOpenings`) with the wire's OWN types and enums (R-4295); their translators in the vehicle subsystem's bridge, handed to the node by the host; `EntityDamage` gains `Destruction` (APPENDED, R-169) | the network layer stays separate from HROT internals |

⭐ **Component ids — there is room** *(re-measured `2026-11-18`, user: "the components ID space cannot be full … just look for it")*: production
requires an explicit `[ComponentId]` (`FdpConfig.cs:106-118`), so the used set is exactly the constants of the classes `[ComponentId(…)]`
references (`GlobalComponentIds`, `HrotComponentIds`, the `*ComponentIds` classes, `NavFakeIds`) plus literal ids, on every lane branch:
**≈ 112 of 512 are free** (e.g. 86–108, 171–199, 228–254, 290–299, 319–329, 500–511). ⛔ The earlier "space 0–511 is FULL" note
(`GlobalComponentIds.cs` R-10000) counted the constants of EVERY `*Ids` class — behaviour, panel, request and tool ids too — an overcount.
VA-3 / VA-4 take two at dispatch.

## 3a. Partial damage — how it is expressed *(VA-6 / VA-7 in full; approved `2026-11-18`)*

| layer | what | where |
|---|---|---|
| **stored** (replicated, recorded) | `PartDamage { Plan, Levels[16] }` — one byte per part, 0 untouched … 255 destroyed; vehicle ids Hull 0 · Turret 1 · Gun 2 · RunningGearL 3 · RunningGearR 4 · Engine 5; aircraft Fuselage · Cockpit · Wing · Engine · Tail · Turret · Gun · Stores; written by the Health owner when a hit lands on a part (the hit model finds the part); `Health` stays the overall pool | existing (HL-6) |
| **stored, new** | `Health.Destruction` — None · KnockedOut · BurntOut · Catastrophic, decided ONCE at destruction | VA-7 |
| **derived, every node** | each part belongs to a ZONE whose threshold is TKB data (per class, overridden per type — HL-12); running gear / engine over it ⇒ MOBILITY kill (`CanMove` off), turret / gun ⇒ FIREPOWER kill (`CanShoot` off) | existing (S2-8, R-263) |
| **derived for renderers** (`VehicleLook.PartState`) | **Intact** (0) · **Damaged** (> 0, under the zone's destroy threshold) · **Destroyed** (at / over it) · **Burning** (a `FireState` spot covers the part) | new, pure |
| **DIS** (gateway, VA-11) | overall damage (none / slight / moderate / destroyed) from `Health` + `Destruction`; the mobility-kill and firepower-kill flags from the derived kills; smoking / flaming from `FireState` | per-part detail goes beyond DIS: a direct reader of our wire maps part ids to its model parts |

## 4. Components and TKB

| component | on | fields (unmanaged; times `SimInstant`) | size | written by | replicated / recorded / saved |
|---|---|---|---|---|---|
| **`VehicleLights`** *(new)* | a vehicle or aircraft whose type has `Gen.Lights` | `OnMask` (bit per group id) · `Discipline` · `Since` · `SearchAz` · `SearchEl` | ≈ 20 B | the vehicle's Brain (the light rule; a station's switches by command) | a descriptor · recorded · the authored `Discipline` saved |
| **`VehicleOpenings`** *(new)* | a vehicle whose type has `Gen.Openings` | `Count` + per opening (≤ 8): `State` · `Since` | ≈ 72 B | the `PassengerBuffer` owner (embark / dismount; a crew head-out request) | a descriptor · recorded · not saved (a scenario starts closed unless authored) |
| `Health` / `EntityDamage` *(existing, gains a field)* | anything with Health | `Destruction` (None · KnockedOut · BurntOut · Catastrophic) | + 1 B | the Health owner, once, at destruction | the existing descriptor (APPENDED) |
| `PartDamage`, `HeatSource`, `FireState`, `FlightState`, `TurretPose`, `AreaEffectCloud` | *(existing / approved)* | — | — | — | read by `VehicleLook` |

| TKB (type data, never sent at runtime) | |
|---|---|
| `Gen.Lights` (new) | groups: `Id` · `Kind` · `Socket` — per class, overridden per type |
| `Gen.Openings` (new) | openings: `Id` · `Kind` · `Seats` · `OpenSeconds` · `Socket` — per class, overridden per type |
| `Gen.PowerPlant` (thermal's `HasPowerPlant` / `PowerClass`) gains | `SpinUpSeconds` · `SpinDownSeconds` (rotors, engines) |
| combat platform gains | `AmmoStowage` / `FuelStowage` parts (what makes a kill Catastrophic / BurntOut) · `SmoulderSeconds` |
| weapon mount gains | `SmokeDischarger` kind + its salvo pattern |

## 5. UML

```mermaid
classDiagram
  class VehicleLights {
    <<new, Brain-owned, replicated>>
    OnMask Discipline Since SearchAz SearchEl
  }
  class VehicleOpenings {
    <<new, boarding-arbiter-owned, replicated>>
    Count State Since
  }
  class Health {
    <<existing, gains Destruction>>
  }
  class PartDamage {
    <<existing, per part, replicated>>
  }
  class HeatSource {
    <<thermal, approved>>
  }
  class FireState {
    <<thermal, approved, spots>>
  }
  class FlightState {
    <<existing>>
  }
  class AreaEffectCloud {
    <<existing, smoke>>
  }
  class VehicleLook {
    <<new, the ONE reader, static, 0 B>>
    PartState(part) Destruction() RotorSpeed(now)
    Lights(now) OpeningProgress(id, now) Smoking(now)
  }
  class LightRule {
    <<new, on the Brain>>
  }
  class Embarkation {
    <<existing, the boarding arbiter>>
  }
  class Map3D {
    <<existing, our 3D map>>
  }
  class Gateway {
    <<external, DIS / renderer>>
  }
  LightRule --> VehicleLights : discipline x darkness x engine
  Embarkation --> VehicleOpenings : embark, dismount, head out
  VehicleLook ..> VehicleLights
  VehicleLook ..> VehicleOpenings
  VehicleLook ..> PartDamage
  VehicleLook ..> Health
  VehicleLook ..> HeatSource
  VehicleLook ..> FireState
  VehicleLook ..> FlightState
  Map3D ..> VehicleLook
  Gateway ..> VehicleLook
  Gateway ..> AreaEffectCloud
```
*What it shows that prose hid: only TWO components are new; everything a renderer needs about damage, engines, rotors and fire comes from state
that already exists or is approved, through ONE reader — our 3D map and an outside renderer cannot disagree.*

```mermaid
sequenceDiagram
  participant B as Brain (crew)
  participant O as boarding arbiter
  participant N as every node
  participant R as a renderer
  B->>N: VehicleLights Blackout since 1200 s (night, convoy SOP)
  O->>N: VehicleOpenings ramp Opening since 1300 s (dismount ordered)
  Note over N,R: ramp progress = (now - 1300) / OpenSeconds - computed by VehicleLook
  O->>N: ramp Closed since 1320 s (last man out)
  N->>N: a hit - PartDamage RunningGearL 255 (Health owner)
  Note over N,R: VehicleLook - left track Destroyed, mobility kill (derived)
  N->>N: destroyed - Health 0, Destruction Catastrophic, FireState spot at the hull
  Note over N,R: turret thrown, burning until the spot ends, then smouldering
```
*What it shows: a renderer receives a handful of samples per event and computes every animation's progress itself.*

```mermaid
graph TD
  BR[Brain - CGF] --> LR[LightRule]
  LR --> VL[VehicleLights]
  OWN[PassengerBuffer owner] --> EMB[Embarkation]
  EMB --> VO[VehicleOpenings]
  HO[Health owner] --> PD[PartDamage + Health.Destruction]
  MUS[node that moves it] --> HS[HeatSource - thermal]
  HO --> FS[FireState - thermal]
  VL --> PACK[descriptors - every node]
  VO --> PACK
  PD --> PACK
  HS --> PACK
  FS --> PACK
  PACK --> LOOK[VehicleLook - the one reader]
  LOOK --> M3D[our 3D map]
  LOOK --> GW[gateway - external renderer]
  W3[WreckSpawnSystem - per display] -.retires via CE-4660.-> M3D
  linkStyle 15 stroke:#c33,stroke-dasharray: 5 5
```
*What it shows: who writes each piece and where; the red edge is the per-display wreck burn the thermal work retires.*

## 6. Slices — what to build first *(lean; ids and lanes at dispatch)*

| order | slice | builds | done when (R-4277) |
|---|---|---|---|
| 1 | **VA-S1 partial damage exposed** | `VehicleLook.PartState` / `Destruction`, `Health.Destruction` decided at destruction, the TKB stowage parts; our 3D map draws a damaged / destroyed part and a thrown turret from it | a scenario: an MBT hit on the left track shows that part Destroyed + mobility kill on every node; an ammo hit kills Catastrophic |
| 2 | **VA-S2 hatches, doors, ramps** | `Gen.Openings` per class, `VehicleOpenings` written by the boarding arbiter, the head-out request; our 3D map draws them | a scenario: an IFV dismount opens the ramp, the squad leaves, it closes; a late joiner sees the right state |
| 3 | **VA-S3 lights** | `Gen.Lights` per class, `VehicleLights` + the light rule (discipline, darkness, engine, airborne), the station switches | a scenario: a convoy at night drives blacked out under its SOP, with headlights under Normal; a helicopter's anti-collision light on while running |
| 4 | **VA-S4 rotors and engines** | `VehicleLook.RotorSpeed` from `HeatSource` + `FlightState` + spin-up seconds (after thermal CE-4658) | a helicopter on the ground spins up before take-off, down after shutdown |
| 5 | **VA-S5 smoke launchers** | the `SmokeDischarger` mount + its salvo; the "pop smoke" behaviour (behaviors) | a tank under ATGM threat fires its smoke; the clouds block sight and IR |
| 6 | **VA-S6 the gateway mapping** | VA-11's rows in the protocol reference | the table |

## 6a. As built — hatches, doors and ramps *(VA-S2, CE-4670, backend, `2026-11-18`)*

```mermaid
sequenceDiagram
  participant E as Embarkation (any path: embark, dismount, eject, re-carry)
  participant W as VehicleOpeningsSystem (the passenger list's owner)
  participant C as VehicleOpeningsCatalog (class template by DIS + Gen.Openings)
  participant N as dtVehicleOpenings 243 (every node, a late joiner too)
  participant R as a renderer
  E->>E: PassengerBuffer seat 3 changes hands
  W->>W: seat changed? (a per-vehicle snapshot, first sight: none)
  W->>C: openings serving that seat's ROLE
  W-->>N: ramp Opening since now (once)
  Note over W: idle for open seconds + 2 s hold
  W-->>N: ramp Closing since now
  N-->>R: VehicleOpenings.Progress(state, since, openSeconds, now)
```
*What the picture shows that prose hid:* nothing calls the opening writer — it WATCHES the passenger list, so a new boarding path needs no wiring; and a
renderer computes the animation from two samples per dismount.

| piece | as built |
|---|---|
| **`VehicleOpenings`** (component 290, NoScenario) | `Count` + ≤ 8 × (`OpeningState` Closed · Opening · Open · Closing, `Since`); `Progress(state, since, openSeconds, now)` — the ONE progress rule every reader calls (ui's `VehicleLook.OpeningProgress` delegates to it when LK-S1 lands) |
| **the class templates** | ⛔ deviation from "`Gen.Openings` per class": the class template rides on the EXISTING body profiles of `bodies.json` (matched by DIS class — the one class match, ruling 9): MBT / SP artillery 3 hatches · AFV (APC / IFV) a rear ramp + 3 hatches · wheeled utility 2 doors + a cargo door · car 3 doors · helicopter cockpit + cabin door · jets / attack helicopter a canopy · cargo plane a ramp + crew door. `Gen.Openings` (`VehicleOpeningsDto`) is the type's SPARSE override by id (replace · `Remove` · add) — HL-12's rule. `VehicleOpeningsCatalog.Of(template)`, cached per type, in id order = slot order |
| an opening serves seat ROLES | ⛔ deviation from "seats": a class template cannot know a type's seat INDICES, so an opening names `SeatRole`s; a seat's role comes from `CarrierSeats.Roles` |
| **`VehicleOpeningsSystem`** (`Interactions` capability, last) | on the passenger list's owner (`Embarkation.OwnsVehicle`): a seat whose occupant changed opens every opening serving its role (Opening since now, once — a second man out keeps it); idle `OpenSeconds` + `HoldSeconds` (2 s) ⇒ Closing. First sight / a reload / a replay: nothing. The component is ADDED on first sight, all closed (D-31). 0 B per steady frame |
| the wire | `EntityVehicleOpenings` + `DdsOpening` (the wire's own byte for the state, R-4295), `dtVehicleOpenings` 243, Brain group (with `PassengerBuffer`); translators beside the thermal ones in `Hrot.Network.NED` — ⛔ deviation from VA-12's "the vehicle subsystem's bridge": the component lives in `Fdp.Toolkits`, which NED already references (no subsystem internals crossed) |
| rails | `VehicleOpeningsTests` (the class template + an override; a dismount opens the ramp once and closes it; only the arbiter writes; 0 B; the progress rule) · `EntityVehicleOpeningsTranslatorTests` (ids; byte for byte to a late joiner; the owner keeps its own) |
| scenario | `uc-convoy-hit` + 3 rows: the carrier's ramp opens as the squad dismounts, is closing by the end, the driver's hatch never opens |
| ⏳ not built | a crewman's "head out" (a request to the arbiter) · opening BEFORE a man arrives (it opens on the boarding itself) · our 3D map drawing them (ui, with LK-S1) · reopening while Closing restarts from 0 |

## 6b. As built — lights *(VA-S3, CE-4670, backend, `2026-11-18`)*

```mermaid
graph TD
  W[weather RelativeLight - CE-3229 curve] --> LR[LightRuleSystem - the lights owner, Brain group]
  H[HeatSource Idle or Running] --> LR
  D[VehicleLights.Discipline - authored, saved] --> LR
  C[VehicleLightsCatalog - class groups by DIS + Gen.Lights] --> LR
  LR -->|OnMask + Since, on change| VL[VehicleLights 291]
  VL -->|dtVehicleLights 244| N[every node, a late joiner too]
  SOP[SOP / ROE / order] -.->|not wired yet| D
```
*What the picture shows that prose hid:* the discipline is an INPUT the rule never writes — today it is authored; the dashed edge (an SOP or an
order setting it) is the open half.

| piece | as built |
|---|---|
| **`VehicleLights`** (component 291, DataPolicy Default — saved) | `OnMask` (bit i = the type's light group i) · `Discipline` (Normal · Convoy · Blackout · Off) · `Since` (the mask's last change) · `SearchAz` / `SearchEl` |
| the class templates | `lights` on the same body profiles as the openings: road vehicles head · tail · brake · convoy; a car interior instead of convoy; helicopter nav · anti-collision · landing · searchlight; jets / cargo nav · anti-collision · landing. `Gen.Lights` the sparse override; `VehicleLightsCatalog` merges with the SAME `MergeById` as the openings (one copy) |
| **`LightRuleSystem`** (`Interactions` capability, on the `dtVehicleLights` owner) | every 0.5 s: `MaskOf(lights, discipline, dark, engine)` — head + tail when dark (relative light < 0.3) under Normal, the convoy marker under Convoy, an aircraft's nav + anti-collision whenever its engine runs (day too); nothing blacked out / off / engine off; writes only on a change (`Since` = then); the component ADDED on first sight (D-31). 0 B steady |
| the wire | `EntityVehicleLights` (blittable, compared whole — the `EntityHeatSource` shape), the wire's own byte for the discipline (R-4295), `dtVehicleLights` 244, Brain group; `LightRuleSystem.DescriptorOrdinal` pinned to it by a rail |
| rails | `VehicleLightsTests` (the rule over discipline × darkness × engine; owner-only, on change, an authored Blackout holds, 0 B) · `EntityVehicleLightsTranslatorTests`-in-`EntityVehicleOpeningsTranslatorTests` (byte for byte to a late joiner) |
| scenario | `scenarios/demos/platoons-and-vehicles/va-lights-night` — two crewed T-72s idling at 00:30: the Normal one lit (head + tail, `OnMask` 3), the authored Blackout one never |
| ⏳ not built | the discipline from an SOP / ROE / an order (behaviors) · a possessed vehicle's switches (a command to the owner) · the searchlight's aim · interior lights · landing lights near the ground · brake lights (a reader derives them from deceleration — ui's `VehicleLook`) |

## 6c. As built — partial damage and the kill kind *(VA-S1, CE-4670, backend, `2026-11-18`)*

```mermaid
classDiagram
  class HealthApplicationSystem {
    <<existing, the Health owner>>
    on the hit that kills: Destruction = DestructionKinds.Decide
  }
  class DestructionKinds {
    <<new, the ONE rule, pure>>
    Decide(body, hasPart, part)
  }
  class ResolvedBody {
    <<existing, gains>>
    AmmoStowageMask FuelStowageMask SmoulderSeconds
  }
  class Health {
    <<existing, gains Destruction byte>>
  }
  class HealthStateSinceSystem {
    <<existing>>
    clears Destruction when it lives again
  }
  class VehicleLook {
    <<new, Hrot.Look, 0 B>>
    PartState(part) PartState(part, centre, now)
    Destruction() TurretThrown() Burning(now) Smoking(now)
  }
  class VehicleDamageDraw {
    <<new, our 3D map - ui file, cross-lane>>
    DestroyedMask BitOf ThrownTransform
  }
  class EntityDamage {
    <<wire, gains Destruction byte>>
  }
  HealthApplicationSystem --> DestructionKinds
  DestructionKinds ..> ResolvedBody : bodies.json class + platform override
  HealthApplicationSystem --> Health
  HealthStateSinceSystem --> Health
  Health --> EntityDamage : egress, ingress
  VehicleLook ..> Health
  VehicleLook ..> ResolvedBody : zone thresholds, smoulder
  VehicleDamageDraw ..> VehicleLook
```
*What it shows that prose hid: ONE decision site (the Health owner, on the killing hit) and ONE reader; the map and a gateway call the same reader.*

| as built | why / deviation |
|---|---|
| ⭐ the kill kind: killing hit's part in the type's **ammunition** stowage ⇒ Catastrophic · **fuel** ⇒ BurntOut · any other part, or a blast with no part ⇒ KnockedOut · a person ⇒ None; decided once (a hit on the wreck never changes it); cleared by `HealthStateSinceSystem` when it lives again | ⭐ the stowage rides on the `bodies.json` class profiles (`ammoStowage`, `fuelStowage`, `smoulderSeconds` — Tank: ammunition turret, fuel engine, 1800 s; AFV: turret / hull + engine; wheeled: engine; aircraft: stores / wing), overridden per type on `Combat.PlatformDef` (`AmmoStowage`, `FuelStowage`, `SmoulderSeconds`; the NED catalogue's `SimCombatDefBuilder` passes them through) — HL-12's rule, the same profile match as the zones. 📌 First override: the **T-72** keeps its ammunition in the hull carousel under the turret (hull + turret) — 📐 measured in `va-tank-kill-kinds`: the M1's round lands on the T-72's HULL, which under the class rule was only a KnockedOut |
| ⭐ `PartState` = Intact · Damaged · Destroyed by the part's ZONE threshold (the same number that makes the mobility / firepower kill); the hull (no effect) is Destroyed only at 255 | — |
| ⚠ **deviation — Burning per part needs the renderer's part centre** (`PartState(part, centre, now)`, the vehicle's frame): the simulation keeps no part geometry it could send. `Burning(now)` answers for the whole vehicle | §3a said "a FireState spot covers the part" — true only where the part's position is known, which is the renderer |
| ⭐ `Smoking(now)`: dead, no burning spot, within the type's smoulder seconds after its last spot went out (never lit: after its time of death) | VA-10 |
| ⭐ the wire: `EntityDamage.Destruction` APPENDED (R-169), the wire's own byte (R-4295), copied by egress / ingress; an unknown value decodes to None | VA-12 |
| ⚠ our map (`EntityBodyLayer3D`, ui's file — cross-lane, NOTE to ui): a kit part maps to a simulation part by ROLE (turret, gun), a running-gear part (track, wheels — Hull role) by being low on one side; a destroyed part draws wreck-dark; a Catastrophic wreck draws its turret and gun thrown beside the hull | kit parts carry no simulation part id; the thrown pose is a starter for ui to tune |

Rails: `BodyPartDamageTests.CE4670_*` (2), `HealthStateSinceSystemTests.CE4670_*`, `VehicleLookTests` (4, 0 B), `EntityDamageEgressTranslatorTests.CE4670_*` (late joiner),
`EntityDamageTranslatorTests.CE4670_*`, `VehicleDamageDrawTests` (3). Scenario: `scenarios/demos/platoons-and-vehicles/va-tank-kill-kinds`.

## 7. The shared LOOK library — every renderer derives the same picture *(CE-4671, `2026-11-18`)*

🔒 Request (`2026-11-18`, the same lane): *"Move these rules (kinds, anchors, timings, which munition gives which effect) into a shared library with no
presentation dependency. HROT's map should keep using it, and external renderers' gateways should use it too … The same library is the home for the
person-appearance resolver … Pure functions over components and events; deterministic; no Stride, no ImGui."*

⭐ **The overlap with §3:** `VehicleLook` (VA-2) IS such a derivation — so the library is ONE home for three readers: vehicles (this design),
effects, and people (`DESIGN_Person_Outfits.md`). ⛔ Not three libraries.

| measured `2026-11-18` | where | ⛔ what ties it to presentation today |
|---|---|---|
| the effect RULES — muzzle flash per fire event (none for silent weapons, bombs), explosion + ground mark per detonation, impact puffs and marks per surface, dust for inert thrown items and broken barriers; kinds and lifetimes from `EffectTkbCatalog` (the round's `Effect.Set`, else its calibre class, VE-D) | `Hrot.Presentation/Effects/EffectSystems.cs` (`EffectSpawnSystem`, 525 lines) | the rule and the SPAWNING (effect entities on each map host) and two HUD feeds (player messages, the kill feed) share one system |
| the wreck's flames ~60 s and smoke ~180 s | `WreckSpawnSystem` (same file, :449) | a per-display decision ⇒ the thermal `FireState` (CE-4660) replaces the decision; the library keeps only "smoke after the fire" |
| the person-appearance resolver | `Map3D/Outfits.cs` (624 lines), `Wardrobe.cs`, `KitFromTkb.cs` | the resolved look carries `Raylib.Color` |

| # | question | ⭐ lean | why |
|---|---|---|---|
| **LK-1** | where | ⭐ ONE new assembly **`Hrot.Look`** (refs `Fdp.Toolkits` + `Hrot.Core`; ⛔ no Raylib, Stride, ImGui or `Hrot.UI`) — `VehicleLook`, `EffectRules`, `PersonLook` | the person resolver needs `Hrot.Core.Tkb`, so it cannot sit in `Fdp.Toolkits`; one home for all three keeps renderers agreeing (ruling 9) |
| **LK-2** | the effect rules | ⭐ split RULE from SPAWN: `EffectRules.For(event, view, tkb) → EffectSpec[]` (kind, anchor, lifetime, size — pure, deterministic, 0 B on the steady path); `EffectSpawnSystem` keeps spawning the map's effect entities FROM the specs; the HUD feeds move out of it (they are not effects) | a gateway calls the same function on its node's events and gets the same flashes and marks |
| **LK-3** | the wreck | `WreckSpawnSystem` retires into the thermal `FireState` (CE-4660, dispatched); `EffectRules`/`VehicleLook` answer "smoke after the fire" from the spot's end + the type's smoulder seconds | one truth: fire is simulation state |
| **LK-4** | people | `PersonLook.Of(view, entity, tkb) → PersonAppearance` — the outfit, colours as a plain RGBA struct (⛔ no `Raylib.Color`), the face, the extras (Person Outfits design); the map converts to its colour type when drawing | the resolver is already pure apart from the colour type |
| **LK-5** | determinism | every function a pure function of components, events, the TKB and an entity-seeded random (the outfits' seeded variety) — never the wall clock, never a frame count | the same picture on every renderer, in replay too |
| **LK-7** | ⭐ *(approved `2026-11-18`)* effect CATEGORIES vs the DIS entity type | ⭐ BOTH, for different jobs: every `EffectSpec` carries ① the munition's DIS entity type (identity — every TKB type has one, `TkbMaster.DisType`) and ② OUR category = kind × size class × surface (§7a), built to map ONE-TO-ONE onto DIS's detonation-result vocabulary | the DIS type says WHAT exploded, not how it looks (a renderer without that munition's asset cannot size it); the category is the look every renderer and our map share; the gateway fills both DIS fields by table |
| **LK-6** | the order | ⭐ FIRST move what exists (the effect rules, the person resolver) with their rails unchanged — a pure refactor; THEN `VehicleLook` grows there with VA-S1 | moving first is cheap and testable (the map's rails must stay green); new code then lands in its final home |

| slice | builds | done when |
|---|---|---|
| **LK-S1** | `Hrot.Look`; `EffectRules` extracted, `EffectSpawnSystem` spawns from it, the HUD feeds moved out | the map's effect rails (`EffectTests`, the 3-D frame rails under `xvfb-run -a`) green UNCHANGED; a rail calls `EffectRules` with no presentation assembly loaded |
| **LK-S2** | `PersonLook` extracted (RGBA, no Raylib) | the outfit / wardrobe rails green unchanged; the same rail with no presentation assembly |
| **LK-S3** | `VehicleLook` lands here (VA-S1 onward) | as §6 |

### 7a. Effect categories *(LK-7, approved `2026-11-18`)*

| today (measured) | where |
|---|---|
| an effect TYPE = kind (MuzzleFlash · Explosion · Decal · Tracer · Fire · Smoke · Flare · Chaff) + duration, size, colour, fade | `EffectVisualDto`, `EffectDtos.cs:7-52` |
| an AMMO type names its effect set (which flash, explosion, mark) — HEAT and APFSDS of one gun differ | VE-D, `Effect.Set` |
| no set ⇒ a default by SIZE CLASS Small · Medium · Large · Huge, from damage per hit (< 50 small, ≥ 500 large) or explosive mass (0.01 / 1 / 5 kg) — a hit and a miss look alike | `EffectTkbCatalog.cs:46-60` (CE-1231) |
| impacts: a puff + a mark per surface a round enters or leaves, by the same class | CE-1129 |

| ⭐ the `EffectSpec` every renderer receives from `EffectRules` | |
|---|---|
| `Kind` · `SizeClass` (Small · Medium · Large · Huge) · `Surface` (Ground · Water · Building · Vehicle · Air) | our category — the look |
| `MunitionDis` | the munition's DIS entity type — the identity, for a renderer with its own asset and for interop |
| `EffectType` (TKB id) | the exact per-ammo override, when one is set |
| anchor, lifetime, size | as today |

⭐ The category set is chosen so that kind × size × surface maps 1:1 onto DIS's detonation-result codes (HE hit small / medium / large, dirt blast
small / medium / large, water blast, building hit, air burst, smoke …). ⚠ The exact code list is pinned by the gateway slice (VA-S6) against the
SISO reference — not verified here.


### 7b. LK-S1 — the effect rules in `Hrot.Look` *(ui, `2026-11-18`; UML drawn before the build, folded to the as-built after)*

```mermaid
graph TD
  FT[Fdp.Toolkits] --> LK[Hrot.Look]
  HC[Hrot.Core: EffectTkbCatalog, EffectAnchor, EntityClassTable] --> LK
  LK --> HP[Hrot.Presentation: the map]
  LK --> GW[a gateway on a node - future]
  HP -->|MapInteraction.InteractionSystems, node side| K[each map host's kernel, PostSimulation]
  HP -->|ReplayBrowserSubsystem runs them itself| RB[the Replay Browser]
```
*What the picture shows that prose hid:* `Hrot.Look` sits BELOW the presentation (no Raylib / ImGui / `Hrot.UI` reference), so a gateway can call the
same rules; the map still reaches its kernel only through `MapInteraction`, and the Replay Browser runs the same two systems by hand — both callers
get the HUD-feed system next to the spawner, or the feeds go dark there.

```mermaid
classDiagram
  class EffectRules {
    <<static, Hrot.Look>>
    +ForShot(fire, world, tkb, now, into) int
    +ForDetonation(hit, world, tkb, now, into) int
    +ForImpact(impact, world, tkb, now, into) int
    +ForContactBreach(breach, tkb, now, into) int
    +ForFrame(world, tkb, now, into) int
    +IsInertHandItem(tkb, ammo) bool
    +ImpactDamage(world, tkb, impact) float
    +SurfaceOf(impact) Vector3
    +MarkRotation(normal) Quaternion
  }
  class EffectSpec {
    <<struct>>
    +EffectKind Kind
    +CalibreClass SizeClass
    +EffectSurface Surface
    +DISEntityType MunitionDis
    +long EffectType
    +Vector3 At
    +Quaternion Rotation
    +EffectAnchor? Anchor
    +float Lifetime
    +float Size
  }
  class EffectSurface { <<enum>> Ground Water Building Vehicle Person Air }
  class FiringBlasts { <<static, Hrot.Look>> +BlastOf() FiringBlast +BackblastPoint() Vector3 +TypeOf() +KindOf() }
  class VisualFamilies { <<moved to Hrot.Look, namespace kept>> +Of(template) VisualFamily }
  class EffectSpawnSystem { <<Hrot.Presentation>> -Spawn(spec) ; keeps decoys, sounds, recent shots, hit memory, housekeeping }
  class HudFeedSystem { <<Hrot.Presentation, NEW>> player messages ; kill feed }
  class WeaponKits { <<Hrot.Presentation>> BlastOf / KindOf / TypeOf forward to FiringBlasts }
  EffectRules --> EffectSpec : appends
  EffectSpec --> EffectSurface
  EffectRules --> FiringBlasts
  FiringBlasts --> VisualFamilies
  EffectSpawnSystem --> EffectRules : one rule per event
  WeaponKits ..> FiringBlasts : forwards
```
*What the picture shows that prose hid:* the RULE (which effects, where, how long) and the SPAWN (an entity per spec) are two classes now; the blast
rule pulls the type classifier (`VisualFamilies`, `WeaponKind`) down with it — moved with its namespace so no presentation caller changes; the
HUD feeds leave the effect system for their own.

```mermaid
sequenceDiagram
  participant K as kernel PostSimulation
  participant H as HudFeedSystem
  participant S as EffectSpawnSystem
  participant R as EffectRules
  K->>H: Execute (also in a replay)
  H->>H: messages, kill feed (feature-gated)
  K->>S: Execute
  S->>S: strikes and hit memory, then stop if a replay
  S->>R: ForContactBreach(each breach)
  R-->>S: dust spec
  S->>R: ForShot(each fire) after recent shots and the sound
  R-->>S: flash spec, blast spec
  S->>R: ForImpact / ForDetonation
  R-->>S: puff + mark / explosion + ground mark
  S->>S: Spawn(spec) = entity, SimTransform, TkbIdentity, Lifespan, EffectAnchor?, ScenarioIgnoreTag
```
*What the picture shows that prose hid:* the specs come back in the order the old inline code spawned them, so the effect entities are created in the
same order — the goldens and frames cannot move.

**As built (LK-S1, `2026-11-18`) — where it differs from LK-2 / §7a, and why**

| | as built | why |
|---|---|---|
| the signature | per event: `ForShot` · `ForDetonation` · `ForImpact` · `ForContactBreach`, and `ForFrame` (all four, in the map's order) — each APPENDS `EffectSpec`s to a caller's list and returns the count | ⛔ not `→ EffectSpec[]`: an array per event allocates; a reused list keeps the steady path at 0 B (rail `LKS1_AFrameWithNoEvents_AllocatesNothing`) |
| `EffectSurface` | Ground · Water · Building · Vehicle · **Person** · Air | a body that is not a vehicle needed its own (§7a had none); ⚠ Water is not told apart yet (reads Ground); Building = an impact more than 0.25 m off the ground — the category only, the map draws by `EffectType` |
| the classifier | `VisualFamily` / `VisualFamilies`, `WeaponKind`, `FiringBlast`, `FiringBlasts`, `AirMissiles` live in `Hrot.Look` UNDER THEIR OLD NAMESPACE `Hrot.UI.Common.Map3D` | the blast rule needs them; keeping the namespace made it a move with no caller edited (`WeaponKits` / `JetStores` forward). ⚠ known-rot: a `Hrot.UI.*` namespace in a non-UI assembly — rename when a renderer-facing name is chosen |
| the HUD feeds | `HudFeedSystem` (presentation), scheduled right before the spawner on every map host and in the Replay Browser | the two HUD rails changed ONE line each (the system they build), their assertions unchanged |
| unchanged | every effect type, position, anchor, lifetime and the creation ORDER | the map's effect rails pass unchanged (`EffectTests` 66 / 66 with the self-check) |

### 7c. LK-S2 — `PersonLook` in `Hrot.Look` *(ui, `2026-11-18`; the plan, drawn before the build)*

**INVENTORY** *(grep + the files read; `search_graph` for `PersonLook|Outfit|Wardrobe` found no look library — `2026-11-18`)*: the outfit RULES are
`Outfits.cs` (624 lines: `Outfit`, `FaceLook`, `PartDetail`, `Outfits.For / FaceOf / Details / ColourOfPart / CarColour`), `OutfitPalettes.cs` (53) and
`Wardrobe.cs` (107) — Raylib only through `Color`; but the INPUTS a gateway needs live in the map's `EntityBodyLayer3D` (2 300 lines): `SeedOf`
(network id, else index), `SideOf`, `OwnColourOf` / `ColourOf` / `HasEntityColour` / `ParseHex`, and the PERSON half of `TypeLook Resolve(template)`
(soldier, child — which reads the kit catalogue's default person height —, kit, elderly, sex, wardrobe, the type's colour). `KitFromTkb` (56) is a
vehicle's turret geometry over `ShapeKit`'s type head (`ShapeKit.cs` lines 1–308, Raylib-free) — moved with LK-S2 as the handoff lists it, but it is
`VehicleLook`'s material, not a person's.

```mermaid
classDiagram
  class Rgba { <<struct, Hrot.Look>> +byte R G B A ; +FromHex(hex) Rgba? }
  class PersonLook {
    <<static, Hrot.Look.People>>
    +Of(world, entity, tkb) PersonAppearance
    +FactsOf(template) PersonFacts
    +SeedOf(world, entity) long
    +OwnColourOf(world, entity, facts) Rgba?
  }
  class PersonFacts { <<struct>> Soldier Child Kit Elderly Sex Wardrobe TypeColour? }
  class PersonAppearance { <<struct>> Outfit Outfit ; long Seed ; ForceId Side }
  class Outfits { <<moved, namespace kept>> For / FaceOf / Details / ColourOfPart }
  class Outfit { <<record struct, colours Rgba>> }
  class EntityBodyLayer3D { <<Hrot.Presentation>> Resolve takes the person half from PersonLook.FactsOf ; draws with Rgba.ToColor() }
  PersonLook --> PersonFacts
  PersonLook --> PersonAppearance
  PersonLook --> Outfits
  Outfits --> Outfit
  Outfit --> Rgba
  EntityBodyLayer3D --> PersonLook
```
*What the picture shows that prose hid:* the outfit rules alone are not enough for a gateway — the FACTS (who the type is) and the seed / side / own-colour
reads move too, or a gateway would dress a man differently; the map's `Resolve` then TAKES its person half from `PersonLook.FactsOf` (one owner).

| ⭐ step | | the check |
|---|---|---|
| 1 | `Rgba` in `Hrot.Look`; the map's `ToColor()` / `ToRgba()` conversions in the presentation | — |
| 2 | move `Outfits`, `OutfitPalettes`, `Wardrobe` (namespace kept), their `Color` → `Rgba`; the person part indices `BlockFigure` names → `PersonParts` (forwarded) | the outfit / wardrobe rails, changed only where a test builds a `Raylib.Color` literal to compare (named in the report) |
| 3 | `PersonLook.FactsOf` = the person half of `Resolve`; `Of` = `SeedOf` + `SideOf` + `OwnColourOf` + `Outfits.For` | a `Hrot.Look.Tests` rail: `PersonLook.Of` on a world with no presentation equals what the map draws for the same entity (the same `Outfit`) |
| 4 | `KitFromTkb` + `ShapeKit`'s type head to `Hrot.Look/Kits` (namespace kept) | the kit rails unchanged |
