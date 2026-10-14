<!--STATUS
state: LIVE
updated: 2026-10-14
build-state: DESIGN
current-answer: §2 (the programme) — §1 is the measured ground it stands on, so nobody re-derives it.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Sensors_And_Doctrine.md — OWNS vision (the broadphase, the visual sensor, Optics); this programme changes WHICH direction its cone points and the shipped FOV value
  - docs/DESIGN_Weapon_Layers.md — W-14 (CE-3195, backend): a person's body follows a lasting aim (HumanGait.MaxAimTwist, AlignGain); the body-turn mechanism LD-1 extends from "turn to the aim" to "turn to a look direction"
  - docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md — OWNS the squad orders (Watch, overwatch, march slots) that LD-3 makes face a sector; §7.7.6 records "no facing control exists"
-->

# Look direction and a realistic field of view — a programme for later

🔒 User, `2026-10-14`: *"Backend added soldier body turning to the firing direction. It also found the FOV of humans is 360 which I don't
know how it is used"* · *"record tasks related to look direction control and FOV reduction to realistic values as a small programme for
later, include all what you found now so no rederiving necessary."*

**The decision (lean, not yet approved):** keep 360° until a soldier's eyes can be pointed. Then narrow humans to **~200°**. Order:
look-direction control (LD-1) → the vision cone follows it (LD-2) → behaviours point it (LD-3) → narrow the TKB value (LD-4) → demos (LD-5).
**Rejected:**
- *Narrow to 200° now:* a standing man would look wherever he last walked, blind to his assigned sector — worse than all-seeing.
- *Keep 360° forever:* nobody can ever be surprised from behind, and W-14's body turning buys sight nothing.
- *A separate head sensor:* more machinery than one look direction on top of the body cone, and no demo needs it yet.

## 1. What exists — measured `2026-10-14` (do not re-derive)

### 1.1 How vision uses the FOV

| fact | where |
|---|---|
| ONE cone test: `dot(forward, dirToTarget) >= fovCos`; forward = the BODY rotation (`SimTransform.Rotation`, X-forward), 2-D | `FDP/Toolkits/Fdp.Toolkits/Perception/Systems/VisionBroadphase.cs:59-91` |
| `fovCos = cos(FieldOfViewDegrees / 2)`, computed at spawn into `PerceptionReceptor.FieldOfViewCos` | `Perception/Translators/PerceptionTkbTranslator.cs:38-47` |
| ⇒ **360° ⇒ fovCos = −1 ⇒ the test always passes: the FOV is OFF** | — |
| the implicit visual sensor reads the receptor; an explicit sensor reads its `SensorCapability` visual record | `Perception/Sensors/VisualPerception.cs` `Optics` (~l.60-80) |
| thermal has its own FOV; radar's DTO has one | `Perception/Sensors/ThermalPerception.cs:47` · `Tkb/Domain/SensorEntryDto.cs:71,78,97` |
| the FOV can already be retuned at runtime over the wire (`SensorConfig.FovDegrees` → `FieldOfViewCos` on ingress) | `Hrot.Network.NED/SimDescriptors.cs:470` · `SimHost/PerceptionTranslators.cs:74,391` |
| the map draws the cone | `Hrot.Common/Diagnostics/Gizmos/VisibilityConeGizmo.cs:32` |
| hearing has NO cone (a range only; an implicit acoustic sensor, part 1001) | `PerceptionComponents.cs:18` · `PerceptionTkbTranslator.cs:92-95` |

### 1.2 Where 360° comes from

| fact | where |
|---|---|
| the DTO default is 360 ("360 = omnidirectional") | `Tkb/Domain/SensorCapabilitiesDto.cs:25-30`; the sensor-entry DTOs too |
| every shipped soldier and civilian sets 360 | `Hrot.Core/Tkb/UrbanCombatTkbCatalog.cs:129` (civilian), `:170` (soldier) · `MapDefinitions/Tkb/BdcTkbBuilder.cs:270` · examples `DemoTkbSetup.cs:67,174,223` |
| ⚠ **seven scenarios already author `PerceptionReceptor.FieldOfViewCos: 0` = a 180° FOV** — the TKB translator does not overwrite a receptor the scenario already has (`!HasComponent`) | `hill-attack` (6), `hill-attack-close` (6), `tt-nav-los` (4), `hsm-channel-e2e` (3), `mission-demo-bp` (2), `test-fire` (2), `test-move` (1) — likely a saved default, not a choice. Not measured whether intended |
| design basis | ⛔ searched `docs/` + `.dev/`: no design states 360 models "he can turn freely / scans". `.dev/_DONE/tkb-2/design-talk.md` sets 360 as a default with no rationale; `DESIGN_Sensors_And_Doctrine.md:1677` uses 360 only as a benchmark setting |

### 1.3 What turns a person's body today

| writer | where |
|---|---|
| travel: `HumanGait.Integrate` turns only while moving | `CarKinem/Systems/CarKinematicsSystem.cs:458` (`FinishMove`) |
| ⭐ a lasting aim: W-14 — one twist limit `HumanGait.MaxAimTwist` 75°, `BodyAim.HoldsFire`, `AlignGain`, `HumanGait.FacingTurn` (Stride uses it too) | backend `5871b2ac0` (CE-3195), `DESIGN_Weapon_Layers.md` W-14 |
| the operator (`ManualMotion.Person`), the Stride motor (from velocity), a passenger takes the vehicle's (`EmbarkedStateSystem.cs:120`) | — |
| ⛔ **no behaviour writes a heading** (nothing in `Hrot.AI.Behaviors`); the squad `Watch` order has no facing (Squad Tactics §7.7.6: "no facing control exists") | — |
| an animation `LookAt*` channel exists — drawing only, unplayed (CE-3010) | backend W-14 inventory |

### 1.4 Human numbers (for LD-4)

Horizontal visual field ≈ **200–220°** with both eyes; the head turns ≈ **±70–80°**, so scanning covers 360° over a few seconds.

## 2. The programme

| id | owner | what | lean |
|---|---|---|---|
| **LD-1** | backend | a **look direction** for a person: an intent (a yaw or a point) a behaviour can set; a standing man turns his body to it with W-14's `FacingTurn` (no new turn code). Later, optionally, a head yaw (±70°) on top that does not move the body | one component/intent, last writer = the active behaviour; aim (W-14) wins while firing |
| **LD-2** | backend | the vision cone points along the look direction (the head yaw when it exists, else the body) instead of the raw body rotation | change `VisionBroadphase`'s forward only; the cone test stays |
| **LD-3** | behaviors | who points it: `Watch` faces its sector (crossing security, overwatch watchers); `CombatPosture` hold faces the threat, else its sector; march flank men face outward; an idle soldier / Sentry scans slowly (±60°) | one helper per order kind; the look comes from the order, not from type data |
| **LD-4** | backend (data) | narrow humans to **~200°** in `UrbanCombatTkbCatalog` / `BdcTkbBuilder`; decide the seven scenarios' authored `FieldOfViewCos: 0` (re-save without the receptor, or keep 180° on purpose) | only after LD-1..LD-3 |
| **LD-5** | behaviors | demos + checks: an ambush from behind is not seen until it fires or is heard; a security man sees his sector, not the road behind him | new `ua-*` scenarios in `scripts/utility-demo-check.py` |
| LD-6 *(open, later)* | — | peripheral vision slower than central (detection time grows toward the cone edge); vehicle crew FOV per seat | not needed for LD-1..LD-5 |

**Interim option (only if a demo needs rear surprise before LD-1):** narrow the cone for MOVING units only, keep 360° while standing — a
small rule in the cone test, to be retired by LD-2.

⚠ Before building, per CLAUDE.md: an INVENTORY (`search_graph` for facing / heading / look / aim writers), UML for LD-1/LD-2, and the
user's approval of the lean above.
