<!--STATUS
state: LIVE
updated: 2026-08-28
build-state: DESIGN · the TKB-only half is buildable as CE-113; the SCENARIO-OVERRIDE half is
  DELIBERATELY UNIMPLEMENTED — §4 is the record of what it would take, not a plan to do it.
current-answer: §1 is the governing rule and §3 the current intent; §3.1 is the CE-113 AS-BUILT
  (ruling 4 is now satisfied -- read it before quoting 4's old "the TKB cannot express a Tank").
  §4 is the unimplemented story.
  Read §1 first; it decides every case of this shape without further analysis.
stale-below: nothing — this document is new.
known-rot: nothing yet.
known-conflict: docs/designs/others/DESIGN-NetworkSpawning.md:112 says "Initial components (position,
  entity master, etc.) are set as an override on top of TKB template defaults." ⭐ TRUE, and it means
  the LIFECYCLE ESSENTIALS only (things that are also published descriptors). ⛔ It must NOT be read as
  licence for arbitrary scenario-authored overrides — see §1.
-->
# ⭐⭐⭐ `DESIGN` — **ENTITY STATE SOURCING: where an entity's state legitimately comes from**

> 📄 **Decision record: [`Architect_Question_64`](blueprints/Architect_Question_64_Scenario_Component_Overrides_Across_The_Wire.md)**
> — four rejected transport designs and why each failed. ⛔ **This document is the INTENT; `Q64` is the
> archaeology.** Do not read `Q64` to learn how the system works.

---

## 1. 🔒🔒🔒 THE RULE — **two sources, and only two**

> ⭐⭐⭐ **Entity state must be reconstructible from either**
> **(a) the TKB, or (b) published `TransientLocal` descriptors.**
> ⛔⛔ **Anything in NEITHER is unreconstructible by a late joiner and therefore MUST NOT EXIST as durable
> state.**

⭐⭐ **Why there are exactly two.** 📌 **NED's contract is that a node joining late reconstructs every
entity purely by listening to DDS.** That works because:

| source | how a late joiner gets it |
|---|---|
| ⭐ **the TKB** | it already has it — a **static per-node asset**, staged as a ZIP **offline**, identical on every node *(never on DDS: 📄 `docs/designs/tkb-1/tkb-design-ideas.md` §2 — "DDS is not used for TKB transport. TKB is static asset data.")* |
| ⭐ **published descriptors** | **`Reliable` + `TransientLocal`** history replays them on subscribe |

⇒ ⭐⭐⭐ **Together they are COMPLETE BY CONSTRUCTION.** ⛔ **A third source is not "less elegant" — it is
UNRECONSTRUCTIBLE**, and it makes two nodes' view of one entity diverge permanently with no way to detect
it.

### 1.1 ⭐⭐ THE COROLLARY THAT DOES THE WORK — **`STATE` is `TransientLocal`, `COMMANDS` are `Volatile`**

📐 Measured `2026-08-28`, and the split is deliberate:

| | QoS |
|---|---|
| entity-state descriptors — `GenericDescriptors.cs:77/134/168`, all six in `MapDescriptors.cs` | ⭐ **`Reliable` + `TransientLocal`** |
| request/command messages — `CreateEntityRequest`, `UpdateEntityDescriptorRequest`, the Acks | ⭐ **`Volatile`** |

⇒ ⭐ **A command is not state, and is correctly not replayed to a late joiner.** ⛔ **So a command can
never be the carrier of anything durable.**

---

## 2. ⭐⭐ THE CLASSIFICATION TEST — **one question, asked per component**

> 🔒 **"Does this state need to survive a late join?"**
> ⭐ **YES ⇒ it must be a published `TransientLocal` descriptor.**
> ⭐ **NO ⇒ it must come from the TKB.**
> ⛔ **There is no third answer.**

⚠ **Two filters that look plausible and are NOT the test** — 📌 both were tried and discarded:

| ⛔ | why it fails |
|---|---|
| *"is it registered on the consuming node?"* | 📐 measured: **22 of the 23** components a scenario stores are registered on SimHost ⇒ excludes **one**. Useless as a scope |
| *"is it written by a TKB translator?"* | ⭐ better *(14 of 22)*, ⛔ but indirect — it describes today's wiring, not the requirement |

---

## 3. ✅ CURRENT INTENT — **every entity is created from TKB defaults, ALWAYS**

🔒 **User ruling, `2026-08-28`:** *"The vehicle parameter values need to be stored just in the TKB and
loaded equally by all nodes needing them. Their saving to the scenario is an error at this stage."*

| # | |
|---|---|
| ① | ⭐⭐⭐ **Per-type values live ONLY in the TKB** — every node derives them identically, offline |
| ② | ⛔ **The scenario storing translator-derived components is an ERROR**, not an override. They are stale duplicates of TKB material and are to be removed |
| ③ | ⛔⛔ **A receiving node MUST NOT read the scenario file.** The loading node stays authoritative and sends only *published-descriptor* state |
| ④ | ⭐ **Consequence: the TKB must be SUFFICIENT.** ✅ **BUILT `2026-08-28` — `CE-113`; §3.1 is the as-built** *(prior state: "🔴 It is not today — `VehicleParametersDto` drops `Height`/`TurnRate`/`Mobility`, so the TKB cannot express a Tank" — **SUPERSEDED**)* |

⭐⭐ **Ruling ③'s real purpose is not tidiness:** it is what allows **any non-scenario entity to be created
at runtime**. A design that reads the scenario on the receiver works for scenario load and fails for every
other spawn.

### 3.1 ✅ AS-BUILT — **ruling ④ satisfied: the TKB can now express a Tank** *(`CE-113`, `2026-08-28`)*

⚠⚠ **The build deviated from the plan in four ways, each measured. This section is the truth; the plan's
wording is superseded where they disagree** *(obligation ⑤)*.

| ⭐ what shipped | ⚠ deviation from the plan, and why |
|---|---|
| **`VehicleParametersDto` gained `TurnRate` (float) + `VehicleClass?` (nullable)** | ⛔ **`Height` was NOT added, and the plan said to add it.** 📐 Nothing on the kinematics path consumes it — `VehicleParams` has no height field and `PhysicsCollider` carries only `Radius`. ⇒ adding it would be a field nothing reads. ⭐ **Its real home is `StrideRenderModelDefDto.ShapeHeight`** *(`CE-118`'s lane)*. Same for `FuelCapacity`/`FuelConsumption` |
| **`Mobility` became `VehicleClass?`, not `TerrainMobility`** | 🔒 **LAYERING: `Fdp.Toolkits` (which owns the DTO and the translator) cannot reference `Hrot.Core`**, where `TerrainMobility` lives. ⇒ the DTO carries the FDP-level `VehicleClass` and the enum mapping stays upstream. ⚠ The mapping is lossy *(`Air`/`Naval` → `PersonalCar`)* — **that loss predates this work and is unchanged** |
| **`BuildVehicleParams` was SPLIT, not moved** | ⚠ the plan said *"route it into the translator"*. 📐 It cannot move whole: its first half switches on `TerrainMobility` *(Hrot.Core)* and its second half reads `VehiclePresets` *(Fdp.Toolkits)*. ⇒ **the mapping half became `NedTkbBuilder.MapMobility`; the preset+override half became `VehicleKinematicsTkbTranslator.BuildVehicleParams`.** 🔒 Semantics preserved exactly, including every `> 0f` override guard |
| **`VehicleClass` is NULLABLE** | ⭐⭐ **not in the plan at all, and it is the load-bearing decision.** `PersonalCar` is `0`, so a non-nullable enum cannot distinguish *"authored as PersonalCar"* from *"absent"* — and **absent is the normal case for any TKB json predating the field** |

#### ⭐⭐⭐ Why nullability matters — **the TWO-PRODUCER hazard, and the reason this fix is not self-securing**

📐 **Measured:** `VehicleParametersDto` is filled by **two independent producers**:

| producer | source of the three fields |
|---|---|
| `NedTkbBuilder.WithPhysics` *(code)* | `SimVehicleDef` — ✅ **fixed permanently by this batch** |
| the generated thunk ← `TkbDeserializer` | 🔴 **whatever a staged TKB zip's json says** — ⛔ **untouched by this batch** |

⇒ ⚠⚠ **When a real TKB zip is staged, `TkbLoadClusterStateHandler:96` calls `_tkbDb.Clear()` and REPLACES
the code catalog**, so the DTO is filled from json alone. Json authored against the old six-field schema
simply lacks the new properties, and `System.Text.Json` defaults them **silently**
*(`UnmappedMemberHandling` is unset ⇒ `Skip`)*.

🔴 **So the defect would return, wearing a different face:** `Mobility`-as-`0` would have meant
`TerrainMobility.Tracked`, and `TurnRate = 0` ⇒ `MaxSteerRate = 0` ⇒ **a vehicle that accelerates but can
never change its steer angle drives straight forever.** ⭐⭐ **Two things prevent it:** the class is
**nullable** so absence is visible rather than guessed, and **every float override is guarded by `> 0f`** so
an absent value keeps the preset's. ⇒ ⭐ **the worst case is now "the wrong class", never "a zeroed
envelope".**
⚠ **One authoring constraint follows:** `FdpJsonOptionsRegistry.DefaultRelaxed` registers
`StrictStringEnumConverter`, so **`"VehicleClass": "Tank"` is required and `"VehicleClass": 3` throws.**
📄 Ruled a **feature** — a loud failure beats a silent integer-as-enum guess — and pinned by a rail.
🔴 **`CE-119`** *(user, `2026-08-28`)* adds load-time validation so plainly-wrong TKB values are reported in
HROT's log rather than inferred.

#### ✅ VERIFIED LIVE — **`--mode all`, `2026-08-28`**

📐 **Entity 1001 (`tkbType 100`) on the SimHost/MUSCLE perspective:** `Class Tank` · `AccelGain 1.8` ·
`MaxSteerAngle 0.8` · `MaxSteerRate 0.2617994` · `WheelBase 4.758`.
⭐⭐ **Motion, as a position delta over a `simTime` delta:** `simTime` 0 → **5.937**, position
`(446.32, 420.90)` → `(489.73, 409.71)` ⇒ **~44.8 m** at **14.48 m/s**.
⭐⭐⭐ **The `Scenario`/CGF (BRAIN) perspective reports the identical values** — ⇒ **the brain/muscle
divergence that was `CE-103` is closed.**

⚠ **What this run does NOT show:** the before-state was measured in an earlier session, so the
before/after rests on the **inverse-edit red-proofs**, not on this boot. ⚠ **`B3` has no live proof** —
the debug API exposes no scenario-save route, so its guarantee is the `GetSaveableMask` rail. 🔒 Driving
the editor's hand-tested save path was deliberately avoided.

#### ⚠ Known incompleteness — **stated, not hidden**

| | |
|---|---|
| ⭐ **`Hrot.SimHost.Integration.Tests`' harness FIXED** | 📐 `SimHostInstance.cs:818` already **took** a `VehicleClass`, used it to pick a preset, then flattened it into the six-field DTO — **losing the caller's class.** ⇒ now carries `VehicleClass = vehicleClass`. ⭐ Direct evidence the missing field was FELT |
| ⚠ **~15 `FDP/Examples` producers still author no class** | ⇒ they resolve to `PersonalCar`. ⛔ **NOT fixed: out of `CE-113`'s scope** *(the reported symptom is the HROT cluster)*. ⭐ **They are strictly better than before** — a real preset baseline instead of zeros — just not the right class. 📌 Several are visibly tank-shaped *(`Length 7.0`, `Width 3.5`, `MaxSpeedFwd 12`)* |
| ⚠ **`VehiclePresets.GetPreset` does not stamp its own `Class`** | 📐 Found while writing the rails: `GetPreset(Tank).Class == PersonalCar`. ⇒ **every caller must assign `Class` itself** — the silent-default shape. The translator does; a rail pins the trap descriptively |

---

## 4. ⛔ UNIMPLEMENTED — **how a scenario-overridden entity WOULD work**

⚠⚠ **Nothing here is built or scheduled.** ⭐ It is recorded so the next person does not re-derive it —
📌 it took four rejected designs to reach.

### 4.1 ⭐ The only legal shape

⭐⭐ **An override is not a transport problem. It is a CLASSIFICATION change.** By §1, a value that must
differ from the TKB **durably** has to become **published state**:

| # | step | note |
|---|---|---|
| ① | **decide the value is published state**, not internal | 🔒 the §2 test. ⛔ This is the actual decision; everything below is mechanics |
| ② | give it a **descriptor arm** in `EDescriptorType` / `EntityDescriptorUnion` | ⭐ arms are cheap: the union is **payload-only** *(no `[DdsTopic]`)*, so an arm alone adds nothing to published state |
| ③ | give it its **own DDS topic** with **`Reliable` + `TransientLocal`** | ⭐⭐⭐ **this is the step that makes it late-joinable, and it is the whole point.** One topic per descriptor type is the existing convention |
| ④ | write an **egress** translator *(creator side)* and an **ingress** translator *(owner side)* | ⭐ both already gate on `HasAuthority(entity, packedKey)` — *"owned components only"* is the established pattern, not new work |
| ⑤ | add a **`DescriptorGrant`** if the owner is not the creator | 📐 `BrainMuscleOwnershipStrategy` today grants `dtWorldPos` + `dtNavigationStatus` to the Muscle and keeps the intents on the Brain |
| ⑥ | if the value must be present **before** the entity simulates, add a **`MandatoryComponent`** to the template | ⭐ `GhostPromotionSystem` blocks on `Hard` until it arrives. ⚠ Template-scoped, so only for *always-present* state — see §4.3 |

⇒ ⭐⭐ **`SimTransform` is the worked example, already in the tree**: TKB default from
`SpatialCoreTkbTranslator`, per-entity truth published as `dtWorldPos` *(TransientLocal)*, owned by the
Muscle, and `Hard`-mandatory on every template. ⭐ **Copy that, not anything in §4.2.**

### 4.2 ⛔⛔ WHAT WAS REJECTED — **four designs; do not revive them**

📄 Full reasoning in `Q64` §10–§15. ⭐ One line each, so nobody re-proposes one:

| ⛔ design | fatal flaw |
|---|---|
| carry overrides in `SpawnEntityCommand.InitialComponents` over the wire | 📐 that path is **inbound-to-the-creator only** *(`CreateEntityRequest`: one reader, CGF)*. It is not how a creator tells other nodes anything |
| use `InitialAttributesJson` | same channel, same direction. ⭐ It is the **interactive/external request** path *(ExCon, MCP spawn, editor placement)* |
| **per-entity readiness mask** *(component ids, or a `uint64` of descriptor ids)* | ⛔ component ids are **FDP-internal** and must not reach the wire *(📄 `Architect_Question_59` §7)*; ⛔ a `uint64` of descriptor ids has a **ceiling** the descriptor count will pass; ⛔ a wide mask is unacceptable payload on **lightweight, numerous** entities |
| **nest the overrides in the entity-creating sample** | ⛔ **there is no creating sample.** 📐 `GhostCreationSystem.CreateGhost` is called from **≥10 ingress translators** ⇒ the ghost is made by whichever descriptor arrives first. **First-touch creation** |
| 🔴 **a wait-FLAG in `EntityMaster` + a one-shot bundle** | ⛔⛔ **WORSE THAN NOTHING.** A late joiner reads `EntityMaster` from **TransientLocal** history and *sees the flag*, but the bundle was a `Volatile` command, long gone ⇒ **the ghost is stuck FOREVER.** 📌 **Every "flag + side-channel" scheme has this**: the flag is durable, the channel is not |

### 4.3 ⚠ THE OPEN SUB-PROBLEM, if overrides are ever built

⭐ `MandatoryComponents` is **per TEMPLATE**; an override is **per ENTITY**. 📐 In `hill-attack`, `Health` is
stored for **6 of 8** entities and `UnitSubordinate` for **4 of 8**.
⛔ Marking such a component `Hard` **deadlocks** every entity of that type that does not override it;
`Soft` makes them all pay a timeout and lets a late value land after systems ran on the default.

⇒ ⭐⭐ **Under §1 this problem largely dissolves**: once the value is a `TransientLocal` descriptor, a late
joiner gets it from history and *"has it arrived yet?"* stops being a distributed question. ⚠ **What
remains is the FIRST-JOIN ordering case**, and the honest answer there is that a per-entity requirement
cannot be expressed in a per-template list — ⛔ **so do not try to; publish the state and let TransientLocal
do the work.**

---

## 5. ⚠ THE BOUNDARY — **runtime parameter commands are NOT the override mechanism**

🔒 **Ruling ④ of §3's source:** a parameter that must change at runtime goes as a **command after
creation** *(`UpdateEntityDescriptorRequest`-shaped)*, explicitly **not** as published state.

⛔⛔ **That command's effect is ALSO unreconstructible by a late joiner** — a node joining after it holds
TKB defaults while the others hold the changed value. **Silent divergence, by §1.**

| ⭐ | |
|---|---|
| ✅ **safe today** | no overrides exist ⇒ every node derives the same values from the same TKB ⇒ **no divergence is possible** |
| ⚠ **safe only as** | a **transient / authoring-time** action whose divergence is **knowingly accepted** |
| ⛔ **never** | the general override mechanism. 🔒 **Durable difference from the TKB ⇒ §4.1's reclassification, no exceptions** |

---

## 6. 🔴 INVENTORY — **the measurements this design rests on** *(`2026-08-28`)*

| claim | how measured |
|---|---|
| TKB is never on DDS | 📄 `tkb-design-ideas.md` §2 verbatim + zero DDS topics mention TKB; a node loads `{staging}/TKB/{name}.zip` via `TkbUnifiedLoader` |
| state is `TransientLocal`, commands are `Volatile` | `[DdsQos]` on `GenericDescriptors.cs:77/134/168`, `MapDescriptors.cs` ×6, vs `GenericMessages.cs` |
| one topic per descriptor type; the union is payload-only | `DdsWriter<EntityMaster>(…,"EntityMaster")`, `DdsWriter<NavigationStatus>`, …; **`EntityDescriptorUnion` has no `[DdsTopic]`** |
| ghost creation is **first-touch** | `GhostCreationSystem.CreateGhost` called from **≥10** ingress translators |
| `EntityMaster` is a promotion prerequisite | it carries `TkbType`, and `EntityMasterIngressTranslator:140` is the **only** place `TkbIdentity` is stamped |
| owned-only application is the existing pattern | `HasAuthority(entity, packedKey)` in `EntityMasterEgress:73`, `NavigationStatusEgress:83`, `EntityDamageEgress:86`, `EntityInfoIngress:178`, `GeoSpatialIngress:85`, `EntityMissionIngress:98` |
| the ownership split | `BrainMuscleOwnershipStrategy`: `dtWorldPos` + `dtNavigationStatus` → Muscle; `dtEntityMission` + `dtNavigationIntent` → Brain |
| 22 of 23 scenario-stored components are registered on SimHost | live `GET /components` *(103 registered)* vs `hill-attack`'s 23 |
| the TKB cannot express a Tank | `VehicleParametersDto` has 6 fields; `WithPhysics` drops `Height`/`TurnRate`/`Mobility` with a *"Phase 6"* comment that never happened ⇒ `CE-113` |

⚠ **Graph coverage:** the translator enumeration behind §4.2 is `search_graph`-derived *(10 TKB
translators)*; the rest is grep plus live measurement on a `--mode all` boot. ⛔ No exhaustive claim here
rests on grep alone.
