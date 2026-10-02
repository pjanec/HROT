<!--STATUS
state: LIVE
build-state: DESIGN
updated: 2026-09-08
current-answer: §3's four sub-questions, each with a recommended lean. §4 holds the relayed
  NotebookLM input once it arrives, and §5 the verification of every load-bearing claim in it.
design-basis: docs/designs/navig-2/Navigation_Design_v2_0.md §2.2 (the CrowdAgent tag is the
  structural writer-routing filter), §7 line 644 (MobilityProfile sourced from VehicleParametersDto;
  the Infantry arm registers the crowd agent with radius=Width/2, maxSpeed=MaxSpeedFwd), §8.2
  (NavLayerMask Infantry/Vehicle), §8.3 (the NavAgentProfile component).
known-rot: none yet.
known-conflict: none. ⚠ The notebook snapshot at ask time was Docs.All_277 — it predates CE-232/233/234,
  none of which touch the files this question concerns.
-->

# Q67 — **Infantry is routed as a vehicle. Is `VehicleParametersDto` the intended shared carrier, and what should discriminate a human agent from a wheeled one?**

> ⭐ **Scope.** Why infantry never walks and never animates on any host. ⛔ Not in scope: the Stride
> composition defects already fixed (`CE-233` perception tier, `CE-234` projectile integrator).
> 📄 Owning design: [`Navigation_Design_v2_0.md`](../designs/navig-2/Navigation_Design_v2_0.md).

## 1. INVENTORY

⭐ `search_graph` + grep + live host measurement, `2026-09-08`, branch `claude/reset-working-branch-qd1qpv`.
⚠ `check_index_coverage` is not reachable through the CLI, so no count below is a completeness proof.

| query | total | what it settled |
|---|---|---|
| `search_graph(".*Kinematic.*", label="Class")` | **11** | ⭐⭐ **NOT ONE is human/character.** `CarKinematicsSystem`, `LinearKinematicsSystem` (projectiles), `GroundKinematicsModule`, `VehicleKinematicsTkbTranslator`, registries/packs |
| `grep "NavAgentProfile"` writers | **0 production writers** | the component the design names as the routing seam **is never stamped on anything** |
| `grep "MobilityProfile"` in `Tkb/` + `CarKinem/` | **0** | ⛔ **the field the design routes on does not exist on the DTO** |
| live `GET /tkb/types/2002` | — | `InfantrySoldier` carries `StrideRenderModelDefDto{ShapeKind:"Capsule"}` **and** `VehicleParametersDto{Length 0.6, Width 0.4, MaxSpeedFwd 2}` |
| live `GET /entities/<spawned soldier>` | — | has `VehicleState` + `VehicleParams{Class:"PersonalCar"}`, **no `NavAgentProfile`**, **no `CrowdAgent`** |
| `VehicleClass` enum | 5 values | ⭐ **`Pedestrian = 4` — "Person on foot (very small, tight turns)" ALREADY EXISTS** |

## 2. THE PROBLEM IN ONE PARAGRAPH

`Navigation_Design_v2_0.md` §7 routes a `MoveTo` on `MobilityProfile`, **sourced from
`VehicleParametersDto`**, and for the Infantry arm adds a `CrowdAgent` and registers a dtCrowd agent
with `radius = Width/2, maxSpeed = MaxSpeedFwd` — read from that same DTO. §2.2 states the `CrowdAgent`
tag is the structural filter routing an entity to its `SimVelocity` writer (dtCrowd for crowd agents,
`CarKinematicsSystem` for vehicles). **None of that discriminator was built.** `MobilityProfile` does
not exist on the DTO; `NavAgentProfile` exists but nothing writes it. So
`NavigationIntentBridgeSystem.cs:236/244` invented a proxy — *"crowd registration for infantry
(entities **without** `VehicleState`)"* — which is **structurally impossible for infantry**, because
`VehicleKinematicsTkbTranslator.cs:36` injects `VehicleState` unconditionally from the very DTO the
design mandates infantry carry. Measured live: the soldier is driven by `VehicleNavigationIntentSystem`
(`cmd spd=0.60 steer=0.700`), while `KinematicVehicleMotor.cs:161` **skips any capsule body** — so it is
commanded by a system that cannot move it and ignored by the one that could. It never moves, so the
Bullet reverse-sync (which derives a character's `SimVelocity` from frame-to-frame pose delta) writes
zero, so the animation blend — driven from `SimVelocity` — sits at Idle forever. **One defect: infantry
never walks and never animates, on every host.** The bridge logs its own suspicion 24× per run.

## 3. SUB-QUESTIONS, EACH WITH A LEAN

### A — is one shared parameter descriptor the intent, or should humans get their own?

⭐ **Lean: ONE shared descriptor, as the design already says.** §7:644 sources infantry's crowd radius and
max speed *from `VehicleParametersDto`*, and `VehicleClass.Pedestrian` already exists. Inventing
`HumanKinematicParametersDto` would be a second producer for one slot (`R-132`) and would strip the
`NavigationIntent`/`NavigationStatus`/`FrustrationTicks`/`FormationController` that the same translator
stamps and infantry needs. ⚠ **What would change the lean:** if the record intends *kinematic* parameters
(steering, wheelbase) to be genuinely vehicle-only, with human locomotion parameterised elsewhere.
⚠ **Blast radius:** none for A itself — it is a ratification.

### B — where does `MobilityProfile` come from?

⭐ **Lean: an explicit field on the shared descriptor**, per §7:644's own words. ⛔ Deriving it from
`VehicleClass == Pedestrian` conflates *chassis identity* with *locomotion mode*; deriving it from
`ShapeKind == Capsule` couples navigation to a **render** descriptor (`StrideRenderModelDefDto`), which
would not exist on a headless node. ⚠ **Blast radius: a TKB schema addition** — a new DTO field, plus
re-authoring the ~2 infantry templates. Existing vehicle templates default to Wheeled.

### C — who stamps `NavAgentProfile`, and is it the right seam?

⭐ **Lean: the TKB translator, at injection time**, filling `MobilityProfile`, `AgentRadius = Width/2`,
`AgentHeight`, `PreferredLayerMask` from the descriptor — because §8.3 already defines the component for
exactly this and nothing writes it today. ⚠ **What would change the lean:** if the profile is meant to be
derived per-query by the nav solver rather than stamped per-entity.

### D — what happens to `VehicleState` on infantry?

⭐ **Lean: leave it, and stop reading it as a discriminator.** Replace
`NavigationIntentBridgeSystem`'s `HasComponent<VehicleState>` test with `MobilityProfile == Infantry`.
⛔ **Do not suppress `VehicleState` injection for capsules** — the bridge's own warning proposes exactly
that, but `VehicleState` is a harmless zeroed struct whereas the same translator call stamps the four
navigation components infantry requires. ⚠ **Blast radius: shared `Fdp.Toolkits` code** — this lands on
SimHost, CGF and Stride alike. ⭐ Note the fix is two lines from the field it needs:
`NavigationIntentBridgeSystem.cs:220` **already reads** `agentProfile.MobilityProfile` for another purpose.

### E — naming *(minor, separable)*

⚠ `VehicleParametersDto` / `VehicleParams` / `VehicleState` carrying a pedestrian is confusing, and the
live soldier reads `Class: "PersonalCar"`. ⭐ **Lean: rename later, separately from the behavioural fix**,
and re-author infantry as `Pedestrian`. ⛔ Not worth coupling a rename's blast radius to a defect fix.

## 4. RELAYED INPUT — NotebookLM architect *(`2026-09-08`, notebook `HROT - 279`, snapshot `*_279`)*

⚠ **INPUT, not a ruling.** Job `20260908T191737Z-bac0a2bc`. No `refresh` was run: the notebook was
already at `279` *(newer than the client's last-used `277`)* and carries `Stride.All_279.txt`; per the
`Q66` user ruling a refresh is only for significant source change and would worsen the echo.
⚠ The client crashed **printing** the answer *(cp1252 vs `U+1F4A1`)* — the ask itself succeeded and the
text was read from `results/<id>.json`.

| # | claim |
|---|---|
| **A** | ONE shared descriptor was deliberate. `TkbCivilianPedestrian` and `TkbInfantrySoldier` both carry `VehicleParametersDto`; steering fields are **simply ignored** for humans; `KinematicVehicleMotor` has a character-body guard skipping `Capsule` / `CrowdMotorIntent` |
| **B** | The class is **already derived** at TKB build time — `BdcTkbBuilder.MapMobility(TerrainMobility)` maps `Infantry → VehicleClass.Pedestrian`. The DTO carries `VehicleClass? VehicleClass`, **not** a `MobilityProfile` byte. The record is **silent** on field-vs-derive |
| **C** | `NavAgentProfile` was meant to be stamped at TKB injection time; **nothing writes it** in production — only tests/harnesses do. The record is **silent** on which system was to own it |
| **D** | ⭐⭐⭐ **The codebase already committed to the STRIP strategy** — a dedicated `InfantryVehicleStateStripTkbTranslator` runs after `VehicleKinematicsTkbTranslator` and removes `VehicleState`/`VehicleParams` from capsule entities |
| **E** | Consumers that would move if the discriminator changed: `LinearKinematicsSystem` (`.Without<VehicleState>()`), `CarKinematicsSystem` (`With<VehicleState>()`), `KinematicVehicleMotor`, the bridge, harness suites; DDS/replication is insulated *(uses `SimTransform`)* |

## 5. VERIFICATION — **every load-bearing claim, against source**

| # | claim | verdict |
|---|---|---|
| A1 | steering fields ignored; motor guards on capsule | ✅ `KinematicVehicleMotor.cs:161` skips `CollisionShapeKind.Capsule` |
| B1 | `BdcTkbBuilder.MapMobility`, `Infantry → Pedestrian` | ✅ **REAL** — `BdcTkbBuilder.cs:330`, used at `:133` `VehicleClass = MapMobility(physicsDef.Mobility)` |
| B2 | the DTO has `VehicleClass?`, not `MobilityProfile` | ✅ `VehicleParametersDto.cs:82` |
| C1 | `NavAgentProfile` has zero production writers | ✅ confirmed independently before asking |
| D1 | ⭐⭐⭐ `InfantryVehicleStateStripTkbTranslator` **EXISTS** | ✅✅ **REAL and NEW to this session** — `Stride/Hrot.Stride.Core/InfantryVehicleStateStripTkbTranslator.cs:32`. Its guard is **verbatim** as quoted: `VehicleParametersDto` present **and** `StrideRenderModelDefDto.ShapeKind == Capsule` ⇒ remove `VehicleState` + `VehicleParams` |
| D2 | it runs after `VehicleKinematicsTkbTranslator` | ✅ `EditorStrideSubsystem.cs:660-664` uses `TranslatorPlacement.After<VehicleKinematicsTkbTranslator>(...)`. ⚠ `TkbTranslatorSet.cs`'s "contract violated today" note is **STALE** — it describes the `BasePlus` append that `CE-146` retired |
| E1 | `LinearKinematicsSystem` filters `.Without<VehicleState>()` | ✅ verified this session *(`CE-234`)* |

📐 **Tally: 7 checked, 7 confirmed, 0 fabricated.** ⭐ A markedly better result than `Q66` (6/2/2), and
**`D1` is the answer's decisive contribution — a real translator this session had not found.**

### ⭐⭐⭐ WHAT THE VERIFICATION THEN FOUND ON ITS OWN — **the strip never runs in HOSTED mode**

⛔ The strip exists, is correct, and is correctly placed — **and our live soldier still had
`VehicleState` + `VehicleParams{Class:"PersonalCar"}`.** 📐 Cause, measured:

| | |
|---|---|
| `EditorStrideSubsystem.cs:652` builds an `EntityCreationPack` **with** the placement | ⭐ the **standalone** Stride arm |
| `Hrot.Editor/EditorSubsystem.cs:1353` builds one with **no `ExtraTranslators` and no `TranslatorPlacements`** | its own comment: *"⛔ ExtraTranslators is empty: this host's list was plain `Base()`"* |
| `Hrot.Editor.csproj` has **no Stride reference** | ⇒ it **structurally cannot** name the strip translator |
| we run `STRIDE_HOST_REAL_EDITOR=1`, which boots the real `EditorSubsystem` | ⇒ creation goes through `:1353` ⇒ **no strip** |
| the only hand-over seam is `MuscleCapabilitiesFactory` (`EditorSubsystem.cs:803`) | ⛔ there is **no translator equivalent** |

⇒ ⭐⭐⭐ **This is exactly the `CE-233` shape:** the Stride arm declares something the hosted arm never
receives, because the hand-over seam covers capabilities and not translators.

## 6. DECISION

⏳ **The user's call.** ⭐ But the verification collapses the question: **A is ratified, and B/C are no
longer needed for the defect** — the record's answer to D already exists in code and simply never runs
in mode 1. ⇒ ⭐⭐ **the fix is a Stride-lane composition change** *(hand the translator placement to the
hosted arm, mirroring `MuscleCapabilitiesFactory`)*, **not the shared-`Fdp.Toolkits` discriminator change
§3D proposed.** ⛔ My §3D lean *("leave `VehicleState`, change the discriminator")* is **withdrawn** —
the codebase committed to stripping, and the strip is right.
⚠ **Still open and worth doing separately:** `NavAgentProfile` has no production writer (§3C), and
infantry reads `Class: "PersonalCar"` rather than `Pedestrian` (§3E) — neither blocks the fix.


## 7. ⛔ THE FIX WAS ATTEMPTED AND REVERTED — **`CE-237` needs `CE-234`'s edge closed first** *(`2026-09-08`)*

⭐ §6's recommendation *(hand the translator placement to the hosted arm)* was **built, and it worked as
far as it went**: a new `EditorSubsystem.TranslatorPlacements` seam, set by `EditorStrideSubsystem`'s
hosted arm. 📐 Verified live — a spawned `InfantrySoldier` came out with **`VehicleState` absent,
`VehicleParams` absent**, `NavigationIntent` intact, and the mannequin bound to the animation backend.

⛔⛔ **Then it flew off at a constant 111 m/s.** With `VehicleState` stripped and no `CrowdAgent` tag yet,
the soldier matches `LinearKinematicsSystem`'s `.Without<VehicleState>().Without<CrowdAgent>()` query —
the system `CE-234` added to the Stride muscle — so it is ECS-integrated **while Bullet also owns it as a
`CharacterComponent` capsule**. The reverse-sync then derives the character's `SimVelocity` from the
inflated pose delta and feeds the integrator again: a self-sustaining loop.

⇒ ⭐⭐⭐ **`CE-234`'s documented "residual edge" was the blocker all along, and stripping is what creates
the entity it warned about.** ⭐ **Reverted** rather than left in the tree.

| ⭐ the ordering this establishes | |
|---|---|
| **1st** | exclude **Bullet-owned** entities from the ECS integrator. ⚠ The exact marker is `PhysicsBodyReference` (`Hrot.Stride.Core`), which shared `Fdp.Toolkits` cannot reference ⇒ it needs a **seam**, not a query edit |
| **2nd** | then the hosted-arm hand-over is safe, and infantry walks and animates |
| **3rd** | then §6's follow-up — stamp `NavAgentProfile`, route on `MobilityProfile`, retire the strip |

⚠ **This also weakens the strip strategy the architect reported as settled:** it only works where nothing
else integrates the entity. On a Bullet host that is not free.
