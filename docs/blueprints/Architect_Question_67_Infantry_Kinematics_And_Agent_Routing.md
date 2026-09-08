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

## 4. RELAYED INPUT — NotebookLM architect

⏳ *Pending — recorded verbatim on arrival, as INPUT and not a ruling.*

## 5. VERIFICATION — **every load-bearing claim, against source**

⏳ *Pending. `Q66` scored 6 confirmed · 2 misleading · 2 fabricated; both falsehoods were retired
artefacts quoted as current. Every named symbol below will be opened before it is acted on.*

## 6. DECISION

⏳ **The user's call, per sub-question.**
