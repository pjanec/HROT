<!--STATUS
state: LIVE
updated: 2026-08-28
current-answer: ⭐⭐⭐ §0.0e — "--mode all VISUAL-CHECK CORRECTIVES + the cgf==editor TKB ruling".
  START AT §0.0e.3c — THE BUILD PLAN (CE-113). It is the ONLY work left and it carries every file
  anchor, the four things still to measure, and the exact verification probe. The rest of §0.0e is
  context: §0.0e.1 the rulings, §0.0e.3b why the design question is CLOSED, §0.0e.5 the boot recipe,
  §0.0e.6 which suites lie.
  ⚠ UPDATED 2026-08-28 (FOURTH pass): CE-103 is ROOT-CAUSED as a WIRE-HOP loss between the Brain
  (CGF, correct) and the Muscle (SimHost, degraded) — NOT a load-handler defect and NOT a TKB
  difference. The USER HAS NOW RULED the direction (Q64 §6): TKB is the only source; a receiving
  node must never read the scenario; the loading node sends everything but TKB material over DDS.
  My own "receiving node reads the scenario" lean is REJECTED — a runtime-created entity has no file.
  ⭐ THE BASELINE IS CE-113 (widen the TKB DTO + route the already-written BuildVehicleParams into
  the translator) and it is BLOCKED on B4, a two-writer duplicate. CE-114 is the investigation.
  CE-109 is re-scoped: a real ruling-9 duplicate, but it fixes nothing the user reported.
  CE-110/CE-111/CE-112 are BUILT and gated. Nothing is mid-build.
  ⚠ §0.0d is now HISTORY: phase 2 (slices 1-3, J1, J2, J3) is DONE. Do not restart it.
  Read §0's header block only for the branch/ids/dispatch-sha facts.
stale-below: ⛔ EVERYTHING except §0's header and §0.0e is HISTORY, newest first — §0.0c (the CE-070/071
  way-forward), §0.0b (phase 1's seam), §0.0a/§0.0 (phase 0), §0-prev and below. They are kept as the
  record of WHY, not as instructions. ⚠ §0.0c and the §0 header both used to say "Start here"; §0.0d
  supersedes both (corrected 2026-08-27).
known-conflict: ⛔ HANDOFF_Cgf_Bootstrap_Unification.md (the dispatched frame) is STALE on two points —
  its stage-1 god-facade prerequisite and its phase-0 rail wording. AQ63 §10.4 and §12 supersede both,
  deliberately. The handoff is NOT edited (rule 1: never amend a dispatched handoff).
-->
# ⭐⭐⭐ RESUME — **the UI / variable implementation lane**

> 🔒🔒 **Branch: `claude/reset-working-branch-qd1qpv`** *(re-pointed by the USER, `2026-08-23`)*. ⛔ Push
> nowhere else. ⭐ **CURRENT quest ids: `CE-` (next free `CE-110`)**; ⚠ `BP-` are this lane's HISTORICAL
> variable-model ids, tracker areas **`A`–`G`**.
> ⚠⚠ **This lane MOVED from `claude/hrot-implementation-j1jvin`** — ⛔ any document still naming `j1jvin`
> as this lane is stale; `.claude/CLAUDE.md`'s lane table *(`6b14d13fe`)* is authoritative.
> ⚠ **A third lane now exists:** `claude/blueprint-macro-feature-sdmspn` is the **BACKEND** lane
> *(ids `ST-`, tracker area `I` only)* — ⛔ the name is a historical reuse, not this lane.
> ⭐ **The coordinator is pushing to `claude/blueprint-authoring-status-6sr5ld`** — ⚠ CLAUDE.md's table
> still says `…-gm0akp`; **rule 7 syncs from wherever the live handoff is**, confirmed by ancestry.
> ⭐ **RELEARN** before acting on this file if the session is fresh or just compacted.

---

# ⭐⭐⭐ §0 — THE CURRENT QUEST: **subsystem-composition unification (`AQ63`)**

> 📄 **READ FIRST, IN THIS ORDER:**
> **⓿** ⭐⭐⭐ [`../DESIGN_Subsystem_Composition_Unification.md`](../DESIGN_Subsystem_Composition_Unification.md)
> — **THE STANDING DESIGN: the approach, the constraints, the phase plan.** ⚠ Read it AFTER §0.0d — §0.0d says which of its sections are live *(§5c is phase 2)*.
> **①** [`Architect_Question_63_Unify_Subsystem_Composition.md`](Architect_Question_63_Unify_Subsystem_Composition.md)
> — ⭐⭐ **§9 and §10 are USER RULINGS (canon)**; ⭐⭐ **§12 is the phase-0 venue**; ⛔⛔ **§11 is SUPERSEDED — do not quote it.**
> **②** [`batches/HANDOFF_Cgf_Bootstrap_Unification.md`](batches/HANDOFF_Cgf_Bootstrap_Unification.md) — the dispatched FRAME. ⚠ **stale on two points**, see the STATUS block.
> **③** [`Architect_Question_62_Unify_The_Composition_Root.md`](Architect_Question_62_Unify_The_Composition_Root.md) — the predecessor; ⚠ AQ63 §3 supersedes its SHAPE and STAGING.
>
> 🔒 **Branch `claude/reset-working-branch-qd1qpv`** · dispatch sha **`fd8da0967`** · rule-1b started-marker pushed (`1c4325ac5`; phase 0's own at `830fd32c7`). ⭐ ids **`CE-`**, next free **`CE-110`**.
> ⭐ **RELEARN** before acting on this file.

## ✅✅✅ 0.0 — **PHASE 0 IS DONE** *(`2026-08-27`, head `9bff523c7`)*
📄 **[`batches/REPORT_Composition_Phase0.md`](batches/REPORT_Composition_Phase0.md)** · as-built folded into
the design's **§5.6 / §5.7 / §5.8**.

| ⭐ what a next session must know, and must NOT re-derive | |
|---|---|
| ⭐⭐⭐ **The rail found a REAL CRASH on its first real run** — `CE-065`. The `E3` slice routed *"center on entity"* onto a shared system but left its **event registration** in `EditorSubsystem`, and `ClusterRunner/Program.cs:52` turns strict mode on **process-wide** ⇒ the publish threw out of CGF's ImGui context menu and killed the process. ⭐ Fixed by putting the two events on `PresentationComponentRegistry`'s ONE list *(where `SelectEntityCommand` already was — which is exactly why the sibling menu item worked)* | §5.7 |
| ⛔⛔ **`--mode all` IS THE ONLY MODE WE RUN** 🔒 *(user, `2026-08-27`: "we never use '--mode cgf'")* — and **`--mode cgf` alone CANNOT BOOT anyway.** `DdsIdAllocator` waits 30 s for `Hrot.Orchestrator` then throws; **exit 134** before `/status`. ⇒ **exercise CGF via `--mode all` + the `Scenario` perspective.** ⚠ *"the `--mode cgf` symptoms"* is shorthand for *"CGF's symptoms"* | §5.8 |
| ⭐⭐ **`BP-487` is HALF done.** The map FEED is reachable *(`GizmoBuffer` on `ISubsystemDebugProvider`, resolved per ACTIVE perspective)*; ⛔ `PanelSnapshot.ClearCaptured()` still has one production caller ⇒ that half is `MX-011`, **MCP lane** | §5.6 |
| ⛔ **`/missions` is STILL unclassified in `CapabilityManifest.CapabilityFor`** — **third report**, MCP lane. It makes `The_manifest_describes_this_host_truthfully` **red before its matrix loop**, so nothing new can be asserted there. ⭐ The `panels.gizmo` claim was moved to `TheMapsAgreeOnBothHostsRails`; move it back **and delete the copy** when `/missions` lands | §5.8 |
| ⚪ **The "map shows no entities" symptom does NOT reproduce** on `hill-attack` in `--mode all` — 📐 the cluster submits **739** primitives incl. **16 `SpatialAnchor`s naming ids 1000–1007**. ⛔ **NOT fixed, NOT closed** *(the user said "on some scenarios")*; the rail stands to catch it | §5.8 |
| ⭐ **Item ① needed no code** — all 8 drift instances were already railed by the preceding batch. ⛔ Do not rebuild them as a T3 comparison | §5.8 |
| ⚠⚠ **This batch TOUCHED MCP-LANE FILES** *(`DebugApiService.cs`, `DebugApiService.Panels.cs`, `CapabilityManifest.cs`)* — unavoidable for `BP-487`, declared in report §7 ②, **flagged for the coordinator** | report §7 |

## ✅✅✅ 0.0b — **PHASE 1's SEAM IS BUILT** *(`2026-08-27`, head `f7df23904`)*
📄 design **§5b** *(inventory + UML)* and **§5b.4** *(as-built, THREE argued deviations)*.

| ⭐ what a next session must know | |
|---|---|
| ⭐⭐⭐ **The seam existed already.** There were TWO interfaces named `IWindowRegistrar`: host-level *(`RegisterWindows`, 8 subsystems)* and **feature-level in `Hrot.Blueprints.Editor`, in-degree 24** — the bundle contract, unnamed. ⭐ `BlueprintWindowRegistrar` implements BOTH and is the working precedent. ⇒ phase 1 NAMED the shape | §5b.1 |
| ⭐⭐ **`IShellCommandRegistrar`** is the feature seam's new name *(`CE-068`)*; the ENGINE one keeps `IWindowRegistrar` | `CE-068` |
| ⭐⭐ **`IUiBundle`/`UiBundleContext`/`UiBundleHost`** in `Fdp.Presentation`; **`ShellCommandCoreBundle`** is adopter #1 and **both hosts compose it** | `CE-069` |
| ⛔⛔ **`SharedAiWindowRegistrar` was WITHDRAWN as first adopter** — 📐 of its 7 windows CGF constructs **0**, the editor **3**. Adopting it is *newly constructing seven windows on CGF* ⇒ **a question about CGF's ROLE**, not composition. ⭐ Answer that before touching it | §5b.4 |
| ⛔ **`DeclaredSystems()`/`ReportUnserviceable()` were NOT built** — no adopter needed them, and an unadopted member looks adopted. ⭐ They arrive with the first bundle that has something to declare | §5b.4 |
| ⭐⭐⭐ **The constraint is STRUCTURAL now:** `A_bundle_cannot_reach_the_run_set` asserts by reflection that `UiBundleContext` exposes only windows/menu/toolbar. ⚠ **If it fails, that is a DESIGN question, not a test to update** | §3.2 |
| 🔴 **`CE-067`: `Hrot.Blueprints.Tests` (3 983 tests) had NOT COMPILED**, and `--no-build` printed PASSED over the stale binary — the exact hazard CLAUDE.md's tier section names. ⭐ Now **3 965/0** and back in the gate set | `CE-067` |
| 📐 **Dead guard:** `WindowManager.MainToolbar` is NEVER null ⇒ every `MainToolbar != null` check was always true and its "toolbar-less host" comments described an impossible state | §5b.4 |

## ⭐⭐⭐ 0.0e — **`--mode all` VISUAL-CHECK CORRECTIVES + the `cgf==editor` TKB ruling.** ⛔⛔ **START HERE.** *(`2026-08-28`, head `7fbcf54e4`)*

> ⚠ **This supersedes §0.0d as the start-here section.** §0.0d's phase-2 plan is **DONE** *(slices ①②③, `J1`,
> `J2`, `J3` all closed — see §0.0d for its own record)*. ⛔ Do not restart phase 2 from it.

### 0.0e.1 🔒🔒 THE USER RULINGS THAT NOW BIND THIS WORK — **verbatim, newest first**

| # | ruling |
|---|---|
| 🔒🔒🔒 **`CE-109`** | *"shouldn't the TKb templates and scenario loading handlers be shared? the editor one's is very likely newer and better and the one to follow. there should be nothinkg like cluster tKB and editor TKB; we need cgf==editor"* ⇒ ⭐⭐ **where the hosts differ, the EDITOR is canonical and the cluster adopts it** — ⛔ never the reverse, ⛔ never a CGF-private variant |
| 🔒🔒 **the safety fence** | *"the scenario loading path was tested manually pretty well in the editor so pls be carefull with any 'fixes'"* ⇒ ⛔⛔ **do NOT touch the editor's scenario path.** ⭐ The cluster moves toward it |
| 🔒 **cross-lane** | *"feel free to make changes to other lane's files. No other lane is running. no collision risks."* ⇒ ⭐ TIME-lane / backend-lane files are editable; ⚠ still say which lane a change lands in |
| 🔒 **`CE-090`** | *"we are unifying the UI, so obviously the stuff should look same and they CAN'T look different by design if they are rendered by single shared code where host-type gates are undesired; no special boolean needed"* |
| 🔒 **`CE-086`** | *"Unify the internal window ids to snake, breaking layout is not an issue."* |
| 🔒 **`CE-093`** | *"system not deployed yet… We can and should use better stuff (resolveBase)."* |

### 0.0e.2 ✅ WHAT IS DONE — **do not redo any of this**

⭐ Phase 2 closed: slices ①②③ · `J2` *(`CE-091`)* · `J3` *(concluded not-worth-building, §5c.11)* · `J1` +
`J1-a` *(`CE-093`…`CE-100`, §5c.12–§5c.15)*.

⭐ Then the user ran `--mode all` **visually on Windows** and reported three defects. All were reproduced over
the debug API on a real boot in-container, and **four of the six filed items are fixed and gated**:

| id | state |
|---|---|
| ✅ **`CE-101`** | `--mode all` boots **PAUSED**. Root cause: `MasterSyncController`'s ctor published its t=0 baseline anchor with `TargetMode = Continuous`, and `ClusterTimeObservation.Apply` derives `PauseRequested` from that mode ⇒ **an anchor sent for a side effect was also a command**. Opt-in `startPaused` flag; anchor still broadcast. §5c.16 |
| ✅ **`CE-102`** *(= `HN-039`)* | CGF now registers the **shared** `HrotEditLoadHandler`. The blocker was one required arg — it threw on a null `IZoneManagerService`, which CGF composes none of; now optional **and reported**. entityCount 0→8. §5c.17 |
| ✅ **`CE-104`** | `/sim/pause`'s ack now means **applied** *(`AwaitPausedAsync`)*, not accepted |
| ✅ **`CE-105`** | `/sim/step {count:N}` honours `N` — the loop moved out of the single main-thread job to the HTTP handler, one gated step per frame. `count:60` → simTime exactly 1.0000000 |
| ⛔ **`CE-106`** | **REFUTED — my operator error.** `/logs` always had `level`/`max`; I passed `limit=400` |
| ✅ **`CE-107`** | the envelope's **success branch dropped `Hint` entirely** ⇒ the API could not say *"ok, but…"*. Fixed + `/logs` now names ignored filters |
| ⚠ **`CE-108`** | edit path never remaps behaviour-param entity ids — **on ANY host, editor included**. Filed, deliberately NOT fixed |
| 🔒 **`CE-103`** | **RULED §0.0e.3. ⭐ Baseline = `CE-113` (TKB-only), investigation = `CE-114`** |
| 🔒 **`CE-109`** | the ruling above. ⚠ **RE-SCOPED §0.0e.4** — a real duplicate, but NOT `CE-103`'s fix; priority dropped |

⭐ **The MCP SKILL sources carry this session's lessons** *(`CE-108` commit)* — §5b *"ok:true is not evidence"*,
§5c *"prove your instrument once"*, §5d the three localising reads, plus per-route notes on `/logs`,
`/sim/step`, `/sim/pause`, `/scenario/load/edit`, `get_entity`, `get_gizmo_frame`. ⛔ **`SKILL.md` is
GENERATED** — edit `DebugApiRouteDocs.cs` / `tools/ai-debug-mcp/skill-parts/`, then regenerate, and
⚠ **build the RUNNER first** *(`gen-catalog.mjs` shells out to `--mode dump-api`; otherwise it is a silent no-op)*.

### 0.0e.3 🔒🔒🔒 `CE-103` — **ROOT-CAUSED + the user has RULED the fix direction. Baseline = `CE-113`.**

📄 **[`Q64`](Architect_Question_64_Scenario_Component_Overrides_Across_The_Wire.md) — read §6 (the ruling)
FIRST, then §7 (the baseline), then §8 (the investigation).** ⛔ **§4's leans are SUPERSEDED.**

🔒 **The ruling, `2026-08-28`:** vehicle parameters live **only in the TKB**, loaded equally by every node.
Saving them to the scenario is **an error at this stage**. Overrides may come later, sent **from the loading
node over DDS** the way `SimTransform` already travels. ⛔⛔ **A receiving node must NOT read the scenario
file** — the loading node stays authoritative and sends everything but TKB material, *so that any
non-scenario entity can be created at runtime later.*

⛔⛔ **MY OWN LEAN WAS REJECTED, and the reason is worth carrying:** I recommended *"the receiving node reads
the scenario it already stages"* because it needed no wire change. ⭐⭐⭐ **A runtime-created entity has no
scenario file to read** ⇒ it would work for scenario load and fail for every other spawn. 📌 **I optimised
the COST axis and never checked the CAPABILITY axis.** ⚠ A cheap fix that forecloses a planned capability
is not cheap.

⭐⭐⭐ **THE BASELINE IS SMALL AND HALF-WRITTEN — `CE-113`.** `NedTkbBuilder.WithPhysics` receives a
`SimVehicleDef` carrying **`Height`, `TurnRate`, `Mobility`** and **drops all three**, commenting *"mapped
to VehicleParams by translator in Phase 6."* ⛔ **Phase 6 never happened** ⇒ the DTO has 6 fields and **the
TKB physically cannot express a Tank**, which is why every node derives `PersonalCar` / `AccelGain 0`.
⭐⭐ **`NedTkbBuilder.BuildVehicleParams` IS that missing mapping** and has **zero callers** ⇒ 🔒 **ROUTE it
into the translator; do not rewrite, do not delete.** ⚠ It is the function I matched and retracted twice —
never live, always intended; the scenario's stored block is a **fossil of its last run**.

⚠⚠ **`B4` BLOCKS the build:** **two** translators write `VehicleParams` *(`VehicleKinematicsTkbTranslator`
and `InfantryVehicleStateStripTkbTranslator`)*, both `!HasComponent`-guarded ⇒ first-writer-wins by
registration order. **Decide the owner first.**

### 0.0e.3b ✅✅✅ **CLOSED `2026-08-28` — TKB DEFAULTS ALWAYS. `CE-113` is the whole of the work.**

📄📄 **THE INTENT IS NOW A DESIGN: [`DESIGN_Entity_State_Sourcing.md`](../DESIGN_Entity_State_Sourcing.md)**
*(canon row **`R-136`**)* — read that to learn how entity state is sourced. ⛔ **`Q64` is the ARCHAEOLOGY**
*(four rejected designs); do not use it as the reference.* 🔒 **The user found the blocker in his own design
and it closes the question:**
*"NED concept requires each entity to be late-joinable just by listening to DDS and for the entity
descriptors… so each entity will be created from TKB defaults ALWAYS which is the original idea."*

⛔⛔⛔ **ALL FOUR transport designs are DEAD.** ⭐ Do not revive any of them:

| ⛔ dead design | why |
|---|---|
| component-id bitmask *(§12.4)* | leaked an FDP component id onto the wire ⇒ breaks `Q59` §7 |
| `uint64` descriptor mask *(§13)* | has a **ceiling**; the descriptor count will grow past 64 |
| nest overrides in the creating sample | ⛔ **impossible** — `CreateGhost` is called from ≥10 ingress translators ⇒ **first-touch** creation, no privileged sample |
| ⭐ wait-flag + aggregate bundle *(§14)* | 🔴🔴 **WORSE THAN NOTHING** — a late joiner reads `EntityMaster` from **TransientLocal** history and sees the wait bit, but the bundle was a one-shot **`Volatile`** command, long gone ⇒ **the ghost is stuck FOREVER.** 📌 **Any "flag + side-channel" scheme has this**: the flag is durable state, the channel is not |

✅ **Verified while closing:** the entity-state descriptors ARE **`Reliable` + `TransientLocal`**
*(`GenericDescriptors.cs:77/134/168` + all six in `MapDescriptors.cs`)*; the **command** messages are
`Volatile`. ⇒ ⭐⭐⭐ **STATE is TransientLocal, COMMANDS are Volatile — that split IS the architecture.**
⚠ My earlier *"Volatile defeats late joiners"* note was measured on the **commands** and wrongly
generalised to descriptors. ⭐ And `EntityDescriptorUnion` has **no `[DdsTopic]`** *(one topic per descriptor
type; the union is payload-only)*, so a type can exist as an `UpdateEntityDescriptorRequest` payload
**without** becoming published state.

🔒🔒🔒 **THE PRINCIPLE THAT NOW DECIDES EVERY CASE OF THIS SHAPE — the TKB is itself the late-join
mechanism for internal state:**

> ⭐⭐⭐ **Entity state must be reconstructible from (a) the TKB, or (b) published `TransientLocal`
> descriptors. Anything in NEITHER is unreconstructible by a late joiner and MUST NOT EXIST as durable
> state.**

⇒ ⛔ a side-channel override is exactly *"neither"* ⇒ **forbidden, not merely inelegant.** ⭐ That is why all
four designs failed: each tried to create a third source.

⭐ **`CE-114`'s filter is now sharp** — ⛔ not *"what does SimHost register"* *(that removed 1 of 23)* but
🔒 **"does this state need to survive a late join?"** ⇒ **yes ⇒ published descriptor · no ⇒ TKB. No third
answer.** ⚠ **Nothing is in scope today**: `VehicleParams` is ruled internal state ⇒ TKB-only.

⚠⚠ **THE ONE BOUNDARY:** a runtime parameter command has the **same hole, moved** — a node joining after it
holds TKB defaults while others hold the changed value. ⭐ Safe **only** as a transient/authoring action
with divergence knowingly accepted; ⛔ **never the general override mechanism.** 🔒 A parameter that must
differ from the TKB **durably** must be **reclassified** into a real published descriptor.



⭐⭐⭐ **Sequencing — the design question is CLOSED, so there is only one item: `CE-113`.**
⛔ `CE-116` **WITHDRAWN** · ⭐ `CE-114` re-scoped to *"promote to a published descriptor, or fix the TKB"*
with **nothing in scope today** · ⭐ `CE-115` *(per-translator mandatory declaration)* and the
`IDescriptorTranslator` naming reconciliation remain as small independent cleanups.
⭐⭐ **`CE-113` is the whole of the work and depends on none of it.**

⚠⚠ **PROCESS NOTE WORTH KEEPING:** I ran this as a code investigation and swept the design corpus only
after being told to. ⭐⭐ **The sweep changed the answer** — the design confirmed the user verbatim on two
points and revealed `CE-115`. 🔒 **`R-129`: read the owning design FIRST. This is its second occurrence.**

### 0.0e.3c ⭐⭐⭐ **THE BUILD PLAN — `CE-113`. THE ONLY WORK LEFT. ⭐⭐ BUILD THIS FIRST.**

⭐ **The bug:** on `--mode all` the tanks draw a path and do not move, because **SimHost** *(the muscle, which
runs `CarKinematicsSystem`)* builds its entity **from the TKB via ghost promotion** — and **the TKB cannot
express a Tank**, so it derives `PersonalCar` / `AccelGain 0` / `MaxSteerAngle 0` ⇒ zero acceleration, NaN
steer. 🔒 **TKB is ruled the source** *(§0.0e.3b · `R-136`)* ⇒ **make the TKB sufficient. Nothing else.**

#### ✅ `B4` — RESOLVED BY MEASUREMENT `2026-08-28`. **Not a blocker. Do not re-investigate.**

📐 I had filed *"two translators write `VehicleParams`, first-writer-wins, pick an owner"* as blocking.
**It is not:**

| | |
|---|---|
| `SimHostNodeBootstrapper.cs:146-155` — the cluster's translator list | `SpatialCore` · **`VehicleKinematics`** · `Behavior` · `Combat` · `Perception` · `AiDiagnostics` ⇒ ⭐ **`VehicleKinematicsTkbTranslator` is the ONLY `VehicleParams` writer on the cluster path** |
| `InfantryVehicleStateStripTkbTranslator` | 📐 registered **only** at `Stride/HrotStrideApp.Game/EditorStrideSubsystem.cs:1622` *(the Stride editor app, NOT the SimHost node)*, and its own comment says it **STRIPS** `VehicleState`/`VehicleParams` from capsule (infantry) entities ⇒ ⛔ **a remover on another host, not a competing writer** |

⇒ ⭐ **`VehicleKinematicsTkbTranslator` is the unambiguous owner. `B1`/`B2` are unblocked.**

#### ⭐⭐ THE THREE ITEMS, with every anchor needed

⚠⚠ **The file is `BdcTkbBuilder.cs` and the class inside it is `NedTkbBuilder`.** 📌 That mismatch cost me
three grep misses — ⛔ **search the METHOD name, never the file name.**

| # | item | anchors |
|---|---|---|
| **`B1`** | **Widen `VehicleParametersDto` by `Height`, `TurnRate`, `Mobility`** ⭐ **UNBLOCKED — format-safety measured, see below.** ⚠ **and the drop is FIVE fields, not three** *(+`FuelCapacity`/`FuelConsumption`, latent)* | `FDP/Toolkits/Fdp.Toolkits/Tkb/Domain/VehicleParametersDto.cs` — a `record` with `[TkbDescriptor("Gen.VehicleParameters")]`, **6 fields** *(Mass·Length·Width·MaxSpeedFwd·MaxSpeedRev·MaxAccel)*. ⭐ The source already HAS the three: `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/SimVehicleDef.cs` carries `Height`, `TurnRate`, `Mobility` *(+FuelCapacity/FuelConsumption)*, and `NedTkbBuilder.WithPhysics` *(`BdcTkbBuilder.cs:78`)* **drops them** under the comment *"Height, TurnRate, Mobility mapped to VehicleParams by translator in Phase 6."* ⛔ **Phase 6 never happened** |
| **`B2`** | **Route the already-written mapping into the translator** | ⭐⭐ `NedTkbBuilder.BuildVehicleParams(SimVehicleDef)` — `BdcTkbBuilder.cs:271`, **`private static`, ZERO callers**: maps `Mobility→VehicleClass` *(Tracked→Tank · Wheeled→Truck · Infantry→Pedestrian)*, bases on `VehiclePresets.GetPreset` *(`FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/VehicleClass.cs:75-91` is the Tank preset)*, overrides Length/`WheelBase=Length×0.6`/Width/MaxSpeedFwd/MaxSpeedRev/MaxAccel, and computes `MaxSteerRate = TurnRate × π/180`. 🔒 **ROUTE it, do NOT rewrite or delete** *(`CLAUDE.md`: unreferenced is not unintentional)*. Target: `FDP/Toolkits/Fdp.Toolkits/CarKinem/Tkb/VehicleKinematicsTkbTranslator.cs:33-41`, which today writes only 5 fields |
| **`B3`** | **Stop the scenario saving translator-derived components** *(start with `VehicleParams`)* ⭐⭐ **UNBLOCKED — and it is a ONE-ATTRIBUTE change** | 🔒 ruling ②: they are **stale TKB duplicates, not overrides**. 📐 `scenarios/hill-attack/scenario.json` stores a full 15-field `VehicleParams` on **6 of 8** entities. ✅ **MEASURED: add `[DataPolicy(DataPolicy.NoSave)]` to `FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/VehicleParams.cs`** — the save set is `repo.GetSaveableMask()`, so a component opts OUT by declaration. 🔒 **This does NOT touch the hand-tested scenario save path at all** *(the user's warning)*. Precedent: `UnitRoster.cs:26` |

#### ✅ WHAT I NEEDED TO KNOW — **ALL FOUR MEASURED `2026-08-28`. BOTH BLOCKERS CLEARED.**

| measured | verdict |
|---|---|
| ✅ **Does widening a `[TkbDescriptor]` `record` break the ZIP-loaded path?** | ⭐⭐ **NO — it is format-safe in BOTH directions, and `B1` is unblocked.** 📐 The generated thunk is `JsonSerializer.Deserialize<TDto>(jsonElement, FdpJsonOptionsRegistry.DefaultRelaxed)` — emitted by `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/TkbDescriptorGenerator.cs:137`, which **re-emits on every build**, so a widened record needs no hand edit. `UnmappedMemberHandling` is unset ⇒ default `Skip` ⇒ an OLD binary reading NEW json ignores the extra members; a NEW binary reading OLD json defaults them. ⚠⚠ **The real hazard is not a break, it is a SILENT ZERO:** a `Gen.VehicleParameters` block with no `Mobility` yields `Mobility = 0` = `TerrainMobility.Tracked`… which is *accidentally* right for tanks and wrong for everything else. ⛔ **`B1` must make absence recoverable, not silently `Tracked`.** ⚠ And `DefaultRelaxed` registers `StrictStringEnumConverter` ⇒ **an enum authored as an INTEGER in TKB json THROWS** — the widened `Mobility` must be authored as a string |
| ✅ **Where does the scenario SAVE path write `VehicleParams`?** | ⭐⭐⭐ **NOWHERE EXPLICITLY — and this makes `B3` a ONE-ATTRIBUTE change that does NOT touch the hand-tested path.** 📐 `ScenarioSerializer.SerializeEntity` *(`FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs:218`)* walks a **caller-supplied `BitMask512`** — `repo.GetSaveableMask()` *(`FDP/Engine/Fdp.Core/EntityRepository.Sync.cs:213`)* — and `FdpAutoSerializer` handles every remaining bit generically. ⇒ `VehicleParams` is saved **because it is registered and carries NO `[DataPolicy]`** *(`FDP/Toolkits/Fdp.Toolkits/CarKinem/Core/VehicleParams.cs:11-13` — only `[StructLayout]` + `[ComponentId]`)*. ⇒ ⭐ **`B3` = add `[DataPolicy(DataPolicy.NoSave)]`.** 🔒 **EXACT PRECEDENT ALREADY IN-TREE:** `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs:11,26` — *"not saved (`DataPolicy.NoSave`) because it is entirely derived"* — **the same argument, already accepted** |
| ✅ **Does `Mobility` reach `WithPhysics` for tkbType 100?** | ⭐⭐⭐ **YES — all three dropped fields ARE authored.** 📐 `BdcTkbCatalog.cs:26-37`, inside `WithPhysics(TkbEntityTypes.Tank_M1Abrams, …)`: `p.Height = 2.44f` · `p.TurnRate = 15.0f` · `p.Mobility = TerrainMobility.Tracked`. And `TkbEntityTypes.cs:6` ⇒ `Tank_M1Abrams = 100`. ⇒ **the data exists at the source and `BdcTkbBuilder.cs:87-96` discards it one line later.** ⭐ `B1`+`B2` are a real fix, not a speculative one |
| ✅ **Which TKB source did the live run actually use?** *(NOT on the original list — it turned out to decide whether `B1`'s builder half fixes anything)* | ⭐⭐ **the code-built catalog.** 📐 **TWO sources exist:** ① `HrotEnvironment.CreateTkb()` → `NedTkbCatalog.RegisterAll` → `NedTkbBuilder` *(`HrotNodeBuilder.cs:197`, `HrotNodeBuilderReplicationExtensions.cs:115,178`)*; ② `TkbUnifiedLoader` — **exactly ONE production caller**, `Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs:96`, which **`_tkbDb.Clear()`s and REPLACES the code catalog** when the staged scenario names a `TkbName`. 🔴 **But `find` shows NO TKB `.zip` and NO TKB `.json` anywhere in the repo** ⇒ ② cannot have run ⇒ ① is live. ⭐ **So fixing `WithPhysics` fixes the running system** — ⚠ **and when a real TKB zip IS staged one day, the authored json must carry the three fields or the bug returns via `Mobility = 0`** |
| ⚠ **Are the other translator-derived components ALSO degraded?** | ⭐⭐ **MEASURED — 33 components across the 6 cluster translators, and the answer is bigger than `VehicleParams`: the "Phase 6" migration is UNFINISHED IN FIVE PLACES IN ONE FILE.** ⛔⛔ **DO NOT fold these into `CE-113`** — see the table below and `CE-117`/`CE-118` |

##### ⛔⛔ The wider finding — **`WithPhysics` is the only one of five that is even PARTLY wired**

📐 Measured on `BdcTkbBuilder.cs`; the giveaway is a *"will be applied by translator in Phase 6"* comment in
each. ⚠ **Every one of them takes a `configure` lambda the catalog fills in, and four never store the result.**

| builder method | authored input | reaches a DTO | verdict |
|---|---|---|---|
| **`WithPhysics`** `:78` | **11** `SimVehicleDef` fields | **6** | 🔴 **drops FIVE, not three** — `Height` · `TurnRate` · `Mobility` **+ `FuelCapacity` · `FuelConsumption`** *(the last two are latent: nothing consumes them yet)*. ⇒ **`CE-113`** |
| **`WithCombat`** `:103` | `SimCombatDef` | **4 DTOs** ✅ | ⭐ **the one that IS finished** — the model to copy |
| **`WithVisual`** `:65` | **5** `IgVisualDef` fields | 🔴 **ZERO** | ⛔⛔ **`configure` is NEVER INVOKED** — the whole catalog lambda *(`SymbolCode`, `ModelPath`, `ColorHex`, `Scale`, `ShowLabel`)* is dead code, and **`VisualDefinitionDto` has ZERO producers repo-wide.** ⇒ **`CE-118`** |
| **`WithFaction`** `:170` | `factionId` | 🔴 **ZERO** | ⛔ ignores its argument entirely, **and `WithBehavior` `:204` never sets `BehaviorProfileDto.Faction`** ⇒ `BehaviorTkbTranslator.cs:35` stamps `EntityInfo { ForceId = dto.Faction }` = **0 for every TKB entity**. ⇒ **`CE-117`** |
| **`WithHeavyMemory`** `:222` | — | 🔴 **ZERO** | `Blackboard1024` never added despite the doc-comment promising it |

⭐⭐ **The design sweep that must precede touching the visual half** *(`R-129`, and it changed the verdict)*:
📄 **[`docs/UX/UX_Feature_Entity_Symbology.md`](../UX/UX_Feature_Entity_Symbology.md)** §0 — *"HROT has two
symbology pipelines, fully built, that are not connected to each other"* — the upstream one is
`StyleResolutionSystem`, a **3-layer merge whose FIRST layer is the TKB default**. ⇒ ⛔⛔ **`WithVisual`'s
drop belongs to that LIVE design's lane (`UXI-10`, "ready to break into `UXT` tasks"), NOT to `CE-113`** —
fixing it here is exactly the *"fixing a surface the design already plans"* error.
⭐ **The faction half has no such owner:** 📄 `docs/projects/Hrot/Engine/Hrot.Core.md:743` documents the
intended chain **including `WithFaction(id, n)`** ⇒ its no-op is a **genuine defect, not a vestige**.
⚠ **`CE-117` still owes ONE measurement before it is called a live bug:** does
`EntityDataAttributeInstaller.cs:46` *(which sets `ForceId` from an attribute record)* **overwrite** the
zero on the cluster path? If it does, the drop is masked in practice.

#### ⭐ HOW TO VERIFY — **the exact probe that diagnosed it**

```
POST /scenario/load/live {"name":"hill-attack","waitForReady":true}
POST /perspective        {"name":"SimHost"}        # ⛔ NEVER ?perspective= — it is IGNORED (CE-112)
GET  /entities/1001                                # Components.VehicleParams
```
⭐ **Expect on SimHost:** `Class Tank` · `AccelGain 1.8` · `MaxSteerAngle 0.8` · `MaxSteerRate 0.2617994` ·
`WheelBase 4.758`. 📐 **Before the fix it is** `PersonalCar` / `0` / `0`.
⭐⭐ **Then prove MOTION:** a **position delta over a `simTime` delta** — ⛔ never wall-clock, and ⚠ **the
cluster boots PAUSED**, so `/sim/play` or step first. 📄 Boot recipe: **§0.0e.5**.
⭐ **Worth adding:** the brain-vs-muscle conformance rail — this defect class is **invisible to every unit
rail by construction**, because a unit rail builds one world.

#### ⛔ WHAT WE ARE **NOT** DOING — **all of this is settled; do not reopen**

⛔ no wire change · no new descriptor · no readiness gate · no scenario read on any receiver.
`CE-116` **WITHDRAWN** · `CE-114` **nothing in scope** · `CE-109` **deprioritised** *(a real ruling-9
duplicate, but it fixes nothing reported)* · `CE-115` a **small independent** cleanup *(per-translator
`MandatoryComponents` declaration)* · the `IDescriptorTranslator` naming reconciliation, also independent.

### 0.0e.4 ⭐⭐ `CE-109` — **RE-SCOPED: no longer `CE-103`'s fix, and its priority DROPS**

📐 Measured: both live handlers funnel into the **same** `_extractor.Extract(...)`; the only differences are
**zones** *(only the SimHost/editor handler loads them)* and a **`behaviorRemapper`** *(only CGF passes one)*.
⇒ ⭐ still a genuine ruling-9 duplicate worth collapsing, ⛔ **but it fixes nothing the user reported.**
🔒 The editor's scenario path is still not to be touched.

### 0.0e.4b ✅ DONE THIS SESSION — `CE-110` / `CE-111` *(the instrument, and CGF's missing singleton)*

⭐ **`CE-110`** — the cluster `/tkb/*` served a private empty `TkbDatabase`; **third instance of one defect at
`Program.cs:429`** after `BP-487` and `CE-066`. ⭐ Fixed on the **provider seam** *(`ISubsystemDebugProvider.TkbDb`
· `PerspectiveScopedDispatcher.TkbDb` · `DebugApiService._tkbDb` which now **throws** rather than substituting an
empty catalog · `SubsystemDebugProvider.TkbFrom(world)` · `DebugCapabilities.TkbRead`)*, because the TKB is
genuinely per-node.
⭐ **`CE-111`** — CGF never published `ITkbDatabase` as a world singleton *(SimHost and IG both do)*, so
`DisEntityTypeTranslator` and `EntityPresentationGizmoShared` degraded **silently**.
⭐⭐ **7 new facts, both inverse-edit red-proved.** Live: 10 templates *(was 0)*, `tkb.read` 3-of-4.

⛔⛔ **THE LESSON TO CARRY:** the rule *"a caller that HAS a dependency must PASS it"* did **not** stop instances
2 and 3. ⇒ ⭐⭐⭐ **a per-node dependency has no business being a service field** — put it on the provider seam and
the composition root **cannot** forget it. ⭐⭐ **And `?? new X()` for a per-node dependency is not a convenience —
it is a FABRICATED ANSWER**, which is exactly what made this instance expensive where the other two were cheap.

### 0.0e.5 ⭐ HOW TO BOOT AND DRIVE BOTH HOSTS — **worked out this session; do not re-derive**

```bash
# build the runner FIRST (also required before regenerating the MCP catalog)
dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj --no-restore -v q --nologo
cd Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0
export HROT_DEBUG_API_PORT=8099 FDP_STAGING_ROOT=/tmp/.../staging   # per-boot dir
nohup xvfb-run -a dotnet Hrot.ClusterRunner.dll --mode all > /tmp/cluster.log 2>&1 &
# poll until it answers; ~4-13 s
curl -s --noproxy '*' -m 2 http://localhost:8099/status
```
⚠ **`--mode all` must run WINDOWED under Xvfb**, never headless. ⭐ Use a **second port** *(8098)* for a
simultaneous `--mode editor` so the A/B is one command apart. ⭐ `--mode all`'s perspectives:
`Blueprint, BTree, ExCon, HSM, IG, Scenario, SimHost` — **`Scenario` is CGF's**.

⛔⛔ **THREE SELF-INFLICTED TRAPS, all hit this session:**
1. ⛔ **`pkill -f Xvfb` KILLS YOUR OWN SHELL** — its command line contains the pattern. ⭐ Use
   `ps -eo pid,cmd | grep ClusterRunner | grep -v grep | awk '{print $1}' | xargs -r kill -9`.
2. ⛔ **`git commit -m "…"` with embedded quotes shreds into pathspec errors.** ⭐ Always `git commit -F -` + heredoc.
3. ⛔ **A grep pattern is a HYPOTHESIS** — three misses today, the worst being that `BdcTkbBuilder.cs` **contains
   class `NedTkbBuilder`**, so searching the filename "proved" it had no callers.

### 0.0e.6 ⚠⚠ INSTRUMENT RELIABILITY — **what a green does NOT mean here**

⭐ `CE-084`/`CE-088`'s family now confirmed in **four** assemblies. 📐 This session: `Hrot.Presentation.Tests`
red once then **3/3 green**, with the failing identity **ROTATING** *(`ScenarioFileServiceTests.SaveLoad_RoundTrip`,
then `EntityDragGizmoTests` / *"Component type ID 51 is not registered"*)*; `Hrot.SimHost.Tests` **2-red-identical-to-base**
*(⚠ CORRECTED `2026-08-28`: an earlier note here said "1 red"; re-measured over 3 base runs it is **2**, and the
SECOND IDENTITY ROTATES — `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState` ⇄
`EcsRecordReplayControllerTests.PrepareRecordingAsync_InstallsRecordingModule`, while
`FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe` is red in every run. ⇒ this makes
`Hrot.SimHost.Tests` a **FIFTH** member of the rotating-flake family)*. ⛔ Both over **process-global registries**. ⇒ ⭐⭐ **always prove a red at base by stashing the
change and re-running**, and ⭐ **re-run a suspicious suite 3× before believing either colour**.
⚠ **`AssetRootsTestCollection`** *(`CE-099`)* now serialises everything touching `AssetRoots.ConfiguredRoot` —
🔒 **join it if you add such a test**; a filtered green is not evidence.

### 0.0e.7 ⭐ OPEN, carried
`CE-103` *(in flight)* · `CE-109` *(the live-path slice)* · `CE-108` *(edit-path remapping, low)* ·
`CE-087` *(profiler not in the default layout — needs a WINDOWED session: place it, File > Layout > Save current
as default; user: no issue while unshipped)* · `CE-073` *(tracker gate matches only `BP-` rows — it reported OK
for every `CE-` row this session)* · `CE-084`/`CE-088` *(above)* · older: `CE-055`, `CE-062`, `CE-063`,
`CE-047`, `CE-048`, `CE-050`, `MX-011`, `CE-074`, `CE-077`.
⚠ **No windowed/eyes verification** of any of this session's work — every gate was API-level or a suite.


## ⛔ 0.0d — **HISTORY: phase 2's plan and safety net** *(`2026-08-27`)* — ⚠⚠ **DONE (slices ①②③, `J1`, `J2`, `J3`). SUPERSEDED by §0.0e; do NOT start here.**

> 🔒🔒 **USER RULINGS, `2026-08-27` — canon for phase 2:**
> ① *"in the end there should be **one UI logic (no drifts, no duplications)**, instantiated by calling
> shared code from different subsystems."*
> ② *"we never use `--mode cgf`, it was **`--mode all`**."*
> ③ *"i want to be **compaction safe**"* ⇒ ⭐⭐ **this section is written to be self-sufficient: it repeats the
> numbers rather than pointing at them, so a fresh session needs no archaeology.**

### ⭐ WHAT PHASE 2 IS
📐 The two composition roots. **`EditorSubsystem.cs` 5 375 lines / `CgfSubsystem.cs` 2 693** — ⚠⚠ but
**~37 % of both is COMMENT** *(1 836 + 1 126)*, so real code is **4 290**, and the composition itself is
**~1 650**: `EditorSubsystem.RegisterWindows` *(2 110 lines / **1 156 code**)* and CGF's
`BuildAiShell`+`WireAssetCreation`+`RegisterWindows` *(**~500 code**)*.
⛔ **A line-count target is the WRONG goal** — that comment mass is how a post-compaction session learns why
a line exists. ⭐ The goal is **one implementation per concept**, not fewer lines.

### 🔴 THE SCOPE — **5 hosts, not 2. I had this WRONG until the user asked.**
| surface | who has it |
|---|---|
| menus · toolbar · perspectives | ⭐ **ONLY the editor and CGF.** 📐 IG *(~62 code lines)* · SimHost *(~127)* · ExCon *(~124)* · Orchestrator *(~100)* · EyesAndMuscle *(~1, empty)* register **windows only** ⇒ they are **panel hosts INSIDE the shell, not shells.** ⛔ Nothing to unify with them there |
| ⭐⭐⭐ **the diagnostics window group** | 🔴 **22 instantiation sites across 7 host files, ~112 lines of copy-paste** — `FdpEntityInspectorWindow` **(5 hosts)** · `FdpEventBrowserWindow` **(5)** · `ArchitectureDiagnosticsWindow` **(4)** · `SystemProfilerWindow` **(4)** · `FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu` **(4)** |

⭐⭐ **The 22 sites differ by exactly FIVE host values** — `idPrefix` · `titlePrefix` · `perspective` ·
kernel/repo accessor · `titleBarColor`:
```
"ig_system_profiler",      "IG System Profiler",      "IG",       () => _app.Kernel?…,      IgWindowColor.TitleBar
"simhost_system_profiler", "SimHost System Profiler", "SimHost",  () => _app?.Kernel?…,     SimHostWindowColor.TitleBar
"cgf_system_profiler",     "CGF System Profiler",     "Scenario", () => _context?.Kernel?…, TitleBarColor
"editor_system_profiler",  "Editor System Profiler",  "Scenario", () => _kernel?…,          EditorWindowColor.TitleBar
```
⇒ ⭐⭐⭐ **that is a `HostServices` record** *(the `CgfEditorShellToolbar.HostServices` pattern)*, so **22 sites
collapse to ONE implementation + 5 one-line compositions** — the user's sentence, currently achieved by
copy-paste 22 times. ⚠ 112 lines is small; ⛔ **22 sites is 22 chances to drift.**

### ✅ `D1`–`D4`, RESOLVED *(design §5c.4d)*
| | |
|---|---|
| **`D1`** | ✅ **services arrive as CTOR ARGS.** ⛔ `UiBundleContext` is NOT widened — a service locator there is `AQ62`'s superseded `ComposeEditorExperience(deps)` bag and would breach `A_bundle_cannot_reach_the_run_set` |
| **`D2`** | ✅ **done = EVERY host's copy DELETED.** ⚠ At 5 hosts, a bundle only 2 compose is **not** done — 📌 the `SharedAiWindowRegistrar` failure mode with more places to hide |
| **`D3`** | ✅ ⓪ the two LOGIC duplicates *(no seam needed)* → ① **the diagnostics group as bundle #1** *(22 sites, 5 hosts)* → ② the editor/CGF-only shell surfaces |
| **`D4`** | ✅ **BOTH decomposition and de-drifting** — the drift risk is 5-way, so de-duplicating achieves both. ⛔ My earlier *"decomposition, NOT de-drifting"* framing is superseded |

### ⛔⛔⛔ 0.0d-① THE SAFETY NET — **how we know nothing broke.** *(design §5c.4e)*
> 🔒 **User:** *"how will you check that the unification does not destroy what now works? will you use current
> editor as something that should not change?"*

⭐ **Yes, current behaviour is the reference — but the editor ALONE is the wrong one, and the rail we had is
blind to the failure unification causes.**

| axis | state | ⛔ blind to |
|---|---|---|
| **A · cross-host parity** *(editor vs `--mode all`)* | ✅ phase 0 | 🔴🔴 **a change that hits BOTH hosts identically.** Drop a window on all 5 at once ⇒ **parity stays GREEN.** ⚠ Unification is exactly that class of change ⇒ **7th rail-blindness instance if relied on** |
| ⭐⭐⭐ **B · before/after, per host** | 🛠 **BUILT `2026-08-27`** — `TheUiBaselineIsPinnedPerHostRails` | pixels, layout, rendering |

⛔ **Why the editor alone is insufficient:** the ids are **host-prefixed**, so the editor's baseline covers
**4 of the 22** sites and proves nothing about `ig_system_profiler` still claiming perspective `"IG"`.

⭐⭐ **THREE captures cover all 22 sites** — 📐 `HrotRunnerConfiguration:124` expands `all` to
**`orchestrator,simhost,ig,excon,cgf`**, and `:181` **forbids the editor coexisting with IG/ExCon**:
| mode | covers |
|---|---|
| `editor` | the editor's 4 sites |
| ⭐⭐ `all` | **SimHost · IG · ExCon · CGF · Orchestrator — five hosts in ONE process** |
| `replaybrowser` | ReplayBrowser's 2 |

🛠 **The rail:** `Hrot.SystemTests/Conformance/TheUiBaselineIsPinnedPerHostRails.cs`. It pins, per mode, the
**`(panelId, kind, perspectives[])`** set from `GET /panels` `registered[]` + `list_perspectives`, through the
existing `GoldenStore`. Goldens live at `Hrot.SystemTests/Goldens/ui-baseline-{editor,all,replaybrowser}/`.
⭐ **Re-capture:** `PANEL_GOLDEN_CAPTURE=1 dotnet test … --filter TheUiBaselineIsPinnedPerHostRails`
⛔⛔ **and a re-capture must be INSPECTED and committed in the SAME commit as the code change** — never
separately, or it blesses whatever happened.

⚠⚠ **FOUR LIMITS — do NOT over-claim this net. ⛔ Two were found by INSPECTING the first capture:**
| ⚠ | |
|---|---|
| **`GET /panels` reports the INSTRUMENTED set** | ⛔ a window that never calls `PanelSnapshot.DeclareInstrumented` is **invisible** to the baseline. ⭐ `The_instrumentation_gap_is_measured_not_assumed` prints the real counts per mode — ⚠ **read them; the gap size is otherwise UNMEASURED** |
| **ids are NOT pixels** | ⭐ catches a dropped, renamed or added window; ⛔ **not** a panel that renders wrong ⇒ a windowed eyes pass stays in acceptance |
| 🔴🔴 **PERSPECTIVE IS NOT COVERED** | 📐 **`registered[]` is PROCESS-WIDE, not perspective-scoped** — 54 of the editor's 55 windows listed all 4 perspectives, because the field recorded which perspectives the CAPTURE VISITED. ⇒ ⛔⛔ **it would NOT catch `CE-071`'s `B1`**, which is what I first claimed for it. ⭐ The field was **REMOVED, not shipped** — false confidence is worse than a named gap. ⭐⭐ Stable source = **`focus_panel`** *(per-panel `{perspective, isOpen, isPinned}`)*, ⛔ but it has **side effects** ⇒ own pass. **FILED** |
| ⚠ **`kind` removed too** | 📐 empty for **18 of 55** — inverted from `kinds{}` which derives from `captured[]` ⇒ **frame-dependent** ⇒ spurious reds later |

🔒 **THE METHOD LESSON:** `GoldenStore` demands a capture be INSPECTED before commit *("a capture run is green
by construction")*. 📐 That inspection found **two defects in the RAIL, none in the product** — ⛔ and both
would have shipped GREEN. ⇒ ⭐⭐ **a golden that has never been read is not a baseline, it is a rumour.**
📐 Also measured while inspecting: **ReplayBrowser names its two `rb_*`** *(`rb_inspector`, `rb_events`)*, not
`*_fdp_*` — so all 22 sites ARE covered; my first grep pattern was wrong, not the capture.

### ⭐ THE ORDER — ⛔ **the baseline is FIRST, before any registration moves**
| # | slice | why here |
|---|---|---|
| **⓪** | 🛠 **capture + commit the three goldens on TODAY'S code** | ⚠⚠ a golden taken after bundle #1 lands **enshrines whatever that bundle did** |
| **①** | ✅✅ **DONE `2026-08-27` — `CE-078`.** Shared `AiAssetSavers` + `AiAssetReload` in `Hrot.Editor.AiShared/Documents/`; both hosts call them; `_btreeQuickReloadTrigger`/`_hsmQuickReloadTrigger` DELETED. 📄 design §5c.6 *(+ §5c.6.7 as-built)* | ✅ 12-fact equivalence rail, **3 inverse-edit red-proofs**; T3 baseline 5/5 with goldens **unchanged** |
| **②** | ✅✅ **DONE `2026-08-27` — `CE-082`.** `DiagnosticsWindowsBundle` + `DiagnosticsHostServices` in `Hrot.Presentation/Windows/`; all FOUR hosts compose it; IG/SimHost gained their first `Compose` call. 📐 **20 sites / 4 hosts, NOT 22 / 5** *(see the estimates list)*. 📄 design §5c.7 *(+ §5c.7.6 as-built)* | ✅ 9-fact equivalence rail, **3 inverse-edit red-proofs (8/9 red)**; ⭐⭐⭐ **T3 baseline 5/5 with the three goldens UNCHANGED** — the load-bearing proof for a registration move |
| **③** | ✅✅ **DONE `2026-08-27` — `CE-089`, and it was ALREADY 90% SHARED.** 📐 Menus: **0 hand-written registrations in either host**; perspective buttons/icons/AI-debug commands: already one implementation each. ⭐ What remained: `ShellTimeControlToolbar` *(4 lines × 2, the separator now a NAMED PARAMETER)* + two dead CGF guards. ⛔ No bundle — ceremony over 4 lines. 📄 design §5c.8 | ✅ 6-fact rail, 3 red-proofs (4/6 red); toolbar rails + all three goldens **UNCHANGED** |
| ⭐⭐⭐ **PHASE 2's SLICE LIST IS EMPTY — and the CLOSING INVENTORY is MEASURED** | 🔒 ⓪①②③ all done. 📄 **design §5c.9 carries the numbers, the four remaining clusters `J1`–`J4`, the recommended ORDER and a STOP CONDITION** — ⛔ read it instead of re-deriving. 📐 **Headline:** the arena is **~1 540 code lines**; **106 identical lines remain (~7 %)**, of which ~26 braces, ~12 field decls and ~8 the shared calls' own invocations ⇒ **~60 meaningful**, and **~35 of those are merely duplicated ARGUMENT LISTS to already-shared classes** ⇒ ⭐ **~24 lines of VERBATIM logic** — ⚠⚠ **but §5c.9.3b CORRECTS this: `J1`'s catalog-construction block is **~45 code lines PER HOST** of same-logic-different-SPELLING duplication *(editor inline, CGF wrapped in `BuildAssetCatalog()`)*, which the verbatim scan reported as ~5.** ⚠⚠ **That 106 is a FLOOR, not a ceiling — the method finds VERBATIM duplication only, and slice ①'s save delegates were semantically-identical-but-DRIFTED, so they would NOT have appeared in it.** ✅✅ **`J2` DONE `2026-08-27` (`CE-091`)** — ⭐ built `RefreshJsonContributors`, the method the builder's own doc promised and nobody had built; the 6-line lambda is gone from both hosts. ⛔⛔ **Its `K2` half was WITHDRAWN after implementation:** 📐 **11 tests inject those four delegates to assert the create SEQUENCE** ⇒ 🔒 **a repeated ARGUMENT LIST can be a TEST SEAM, not accidental duplication** — the verbatim metric counted 5 lambdas as duplication and 4 were load-bearing. ✅ **`J3` CONCLUDED NOT WORTH BUILDING (`CE-092`)** — all three document factories are behind the cycle, and the identical parts are already shared calls. ⭐⭐⭐ **`J1` IS DESIGNED (§5c.12) AND WAITING ON A NOD — and its prize is NOT the ~45 lines:** 🔴 **`CE-093`, the editor cannot load its own BTree/HSM JSON assets on a DEPLOYED node** *(it resolves roots with `ResolveProjectDir` — walk-up only, null off-tree — where CGF uses ruling 67's `ResolveBase`)*. ⛔ **If that behaviour change is declined, CLOSE `J1`** rather than build it for tidiness. ⭐ Order from here: `J1`** *(cheapest — AiShared, no cycle, changes NO UI output)* **→ `J3`** *(same clean home, ⚠ GOLDEN-SENSITIVE)* **→ `J1`** *(⛔ DESIGN PASS FIRST: cycle-bound like slice ①, and the design must be allowed to conclude it is NOT worth unifying)* **→ `J4`** *(never alone)*. ⛔⛔ **STOP after `J2`+`J3`** — ~30 lines of argument lists is not a bundle. ⭐ Open questions meanwhile: `CE-087` *(profiler missing from the shipped layout — needs YOUR windowed re-save)*, `CE-086`, `CE-090`, `CE-073`, and the flaky-suite pair `CE-084`/`CE-088` |

⛔⛔ **EVERY slice carries an EQUIVALENCE rail** — 🔒 `CE-072`'s lesson: *a wrapper needs an equivalence rail
the day it is introduced*, because **when a wrapper becomes the only production path to tested code, the
existing tests stop covering production.** ⚠ At 5 hosts this matters more: each host's ids and perspective
must come out **byte-identical**, or someone's saved layout resets.

### ⛔⛔⛔ 0.0d-② THE CONSTRAINT THAT SHAPES EVERY REMAINING SLICE — **a reference CYCLE** *(measured `2026-08-27`, `CE-078`)*

📐 **`Hrot.BTree.Editor`, `Hrot.Hsm.Editor` AND `Hrot.Blueprints.Editor` ALL reference
`Hrot.Editor.AiShared`.** ⇒ ⛔⛔ **AiShared can NEVER name `BehaviorTreeAsset` / `HsmAsset` /
`BlueprintAsset`** — that is a **circular project reference**, not a style preference.
📐 **And the only NON-TEST projects that see all three are the two hosts themselves** ⇒ ⛔ **there is no
existing shared home** for logic that needs the concrete asset types, and a **new project is the wrong
price** for a few dozen lines in a 149-project solution.

⭐⭐⭐ **THE WAY THROUGH, and it generalises: `DTO`-in, not asset-in.** The DTOs
*(`BehaviorTreeAssetDto`/`HsmAssetDto`)* live in **`Hrot.AiEditor.Persistence`**, which AiShared **does**
reference, and every serialize/emit step already takes a DTO. ⇒ ⭐ **only `ToDto(asset)` and the compiler
adapter stay host-side; AiShared owns everything after the map.**

⭐ **This was ALREADY WRITTEN DOWN and I nearly re-derived it from scratch** —
`SaveAllAiDocumentsCommand.cs:10`: *"Kind-specific serialization is injected as delegates to avoid
circular assembly references … design §PU-602."* ⇒ 📌 **the seam law once more:** those delegate
parameters looked like a style choice and were load-bearing. ⚠ **Slice ③ (menus/toolbar/perspectives) will
meet the same wall** — ⭐ check the reference direction BEFORE choosing where shared code lives.

### ⛔⛔ 0.0d-③ A T3 RAIL MUST BE GATED THROUGH THE SCRIPT AT LEAST ONCE *(`CE-081`, `2026-08-27`)*

🔴 **`scripts/run-system-tests.sh` filters `(Category=SystemSmoke|Category=SystemModes)`.** 📐 `CE-075`'s
baseline rails declared only `[Trait("lane","T3")]` ⇒ the script printed **"No test matches the given
testcase filter"** and exited **`0`** — ⛔⛔ **a silent ZERO-TEST GREEN**, and the whole phase-2 safety net
was unreachable from the project's own entry point for a day. ✅ Fixed with
`[Trait("Category","SystemModes")]` *(the bucket `ModeStartupRails` uses)*; 📐 the same command now runs
**5/5**.
⇒ ⭐⭐ **`dotnet test --filter` BYPASSES the category filter**, which is exactly how it hid — so a new T3
rail is not gated until **`run-system-tests.sh <Name>` has printed a non-zero test count.**

### ⛔⛔ 0.0d-④ TWO GATING TRAPS FOUND WHILE BUILDING SLICE ② *(`2026-08-27`)*

| ⚠ | |
|---|---|
| 🔴🔴 **`Hrot.Presentation.Tests` IS FLAKY AND THE IDENTITY ROTATES** *(`CE-084`)* | 📐 **3 of 6 runs failed** with the new rail EXCLUDED, a **different test each time** — `EntityDragGizmoTests` · `RouteWaypointGizmoTests` · `TheDragCommitsThroughTheWriteRouterTests` ×2, all `Hrot.ScenarioEditor.Tests`, all gizmo/ECS-write, all **green in isolation**. ⇒ ⛔⛔ **neither a red nor a green from this suite is evidence** — `--filter` the classes you touched and SAY SO, exactly as the `Fdp.Toolkits.Tests` / `DEBT-AIB-030` rule already requires |
| ⛔⛔ **A FILTERED GREEN IS NOT EVIDENCE A NEW TEST CLASS IS SAFE** | 📐 the slice-② rail passed **9/9 filtered** and the rest of the assembly passed **140/140**, but together **the test host CRASHED** — registering real windows touches the process-global `PanelSnapshot` singleton and the class was running parallel to the four that serialise on it. ⭐ **The convention existed** *(`PanelSnapshotTestCollection`, mirrored in two other assemblies)* and the rail was written without it. ⇒ ⭐⭐ **run the WHOLE project suite before believing a new rail** |

### ⚠ FIVE SIZE ESTIMATES I GOT WRONG THIS SESSION — **measure before quoting**
⛔ *"a 24-site cross-assembly rename"* → 📐 **19 hits, 9 files, one tree** *(the 24 was the graph's DEGREE)*.
⛔ *"`SharedAiWindowRegistrar` is the cheapest adopter"* → 📐 **CGF constructs 0 of its 7 windows.**
⛔ *"`CE-018`: three copies of a `.csproj` walk-up, ~190 lines"* → 📐 **already FIXED**; the sizing counted the
**comment recording the fix**.
⛔ *"the save cluster is the biggest prize, ~426 lines"* → 📐 **`SaveAllAiDocumentsCommand` is already shared
and both hosts already call it**; the lines were comment + shared calls.
⛔ *"phase 2 is editor-vs-CGF"* → 📐 **22 sites across 5 hosts.**
⛔ *"the BTree/HSM save delegates are LINE-FOR-LINE duplicates"* *(this doc said so)* → 📐 **semantically
identical, syntactically DRIFTED** — the editor used `as`+null-check+a `prettyJson` local, CGF used
`is not … return` + inlined flatten. ⭐ The drift had already happened; the claim was too strong in the
detail and too weak in the conclusion. ⚠ **The RELOAD arms genuinely were line-for-line.**
⛔ *"CGF has ONE dispatcher, the editor has three callbacks"* → 📐 **THREE dispatchers, not two**: CGF's
method, the editor's toolbar switch, **and the editor's MCP `reloadAsset` route** — each with its own
wording for the same condition.
⛔ *"the diagnostics group is 22 sites across 5 hosts"* *(this doc's own headline)* → 📐 **20 sites across
4.** ⭐ Each of the five call kinds is exactly **4**; ⛔ **ReplayBrowser is a DIFFERENT TYPE in a DIFFERENT
ASSEMBLY** *(`Fdp.Presentation.Windows.ReplayBrowser.*`)* with no profiler and no architecture window, so it
can never join that bundle. ⚠ **`search_graph` returned BOTH same-named classes — that is what caught it**;
grep for `new FdpEntityInspectorWindow` alone would have counted 5 hosts and been wrong about one.
⇒ 🔒 **measure CODE lines and read the call sites before naming a slice or a size.**

## ⛔ 0.0c — **HISTORY: the `CE-070`/`CE-071` way-forward** *(`2026-08-27`)* — ⚠ **SUPERSEDED by §0.0d; do NOT start here**

> 🔒 **USER, `2026-08-27`:** *"cgf==editor is still valid here (the goal of the whole programme), which
> should resolve the question"* ⇒ ✅ **the ROLE question is RESOLVED: CGF gets the AI shell.**
> ⭐⭐⭐ **And the finer distinction turned out to be real: CGF ALREADY HAS IT, so the next item is a
> DELETION, not an adoption.** 📄 design **§5b.5** carries the full measurement and the corpus citation.

### ⭐ THE QUEUE — in order
| # | item | state |
|---|---|---|
| ~~**1**~~ | ✅ **`CE-070` — `SharedAiWindowRegistrar` DELETED** *(`2026-08-27`)*. ⭐⭐ **The build found a stronger argument than the analysis had:** its windows declare **`WindowScope.PerspectiveBound`** and it was a **flat host-level** registrar ⇒ ⛔ **it could never have worked even if a host had called it**, which closes the *"an out-of-repo host might call it"* defence. ⭐ Its rail is replaced by **its inverse** *(`AddSharedAiEditor_Registers_No_Flat_Host_Level_WindowRegistrar`)*, because a flat registrar is the shape a session re-adds by reflex | ✅ **DONE** — as-built §5b.6 |
| ~~**1**~~ | ✅ **`CE-071` — the comparison result surfaces are LIVE on both hosts** *(`2026-08-27`)*. 🔒 The user's MCP question resolved it: the MCP obsoletes the **export** half *(an agent reads both revisions with `git show`)*, ⛔ but **no MCP tool annotates a graph node** ⇒ ⭐⭐ **the half that becomes more valuable is exactly the half nobody wired.** 📐 It was easy — nothing was unbuilt, six wiring sites were missing. ⭐⭐ **`D5` FLIPPED:** the canvas renderer was the design's *deferred* piece and turned out to be the **cheapest** — every factory already composes "built-in + extras" and ships 4–6 live renderers | ✅ **DONE** — as-built §9 |
| ~~**2**~~ | ✅ **`CE-072` — phase 1 CLOSED.** ⛔ **The item's premise was FALSE:** 📐 the only production caller of `RegisterCommonCore` is `ShellCommandCoreBundle:98` ⇒ **no remaining direct callers to migrate.** ⭐⭐ **But looking found a real gap — the 6th rail-blindness instance, in `CE-069`'s own code:** zero tests referenced the bundle, so all seven `TheToolbarLayoutIsOneListTests` rails call the STATIC while production goes through the WRAPPER ⇒ a mis-forwarding bundle would have left them all green. 🛠 `The_bundle_emits_exactly_what_the_direct_call_emits`, red-proved by two inverse edits | ✅ **DONE** — as-built §5b.7 |
| **2** | ⚠ **`CE-073` — `tracker-counts.py --check` counts ONLY `BP-` rows** *(448 of 760)*, so **72 `CE-` · 69 `ST-`/`QA-` · 58 `TM-`/`MX-` rows are ungated** ⇒ every *"tracker-counts OK"* in this programme's gate reports never looked at the rows the batch had just added. ⛔ **Not fixed unilaterally** — widening it re-baselines the Total in every future report ⇒ 🔒 **needs a nod**: widen + re-baseline, or rename the gate to say it covers `BP-` only | ⭐ measured, awaiting a decision |
| **3** | ⚠ **`CE-074` — `SKILL.md` has no "Capabilities & boundaries" section**, though the skill's own instructions cite one and tell you absence *"must be stated explicitly in the SKILL's boundaries section, not inferred from source"*. 📐 `skill-parts/` has six partials and none carries it. 📌 Hit for real by `CE-071`: *"can the MCP annotate a graph node?"* had to be derived from the tool list. 🛠 Add a `40-boundaries.md` partial + its assembly line — ⛔⛔ **NEVER edit `SKILL.md`; it is GENERATED** *(user ruling)*. ⚠ **MCP lane's call** | ⭐ filed |
| **3** | ⭐⭐ **phase 2** — one bundle per batch from the editor as specimen: **scenario panels → gizmos → map → AI shell → time transport**. ⛔ Each needs its **own inventory + UML before code** *(obligations ①/②)* | needs design per batch |
| **4** | open ids: `CE-062` *(blueprint live-value provider on CGF)* · `CE-063` *(`EditorMapPickAdapter` vs `CanvasMapPickAdapter` — ⛔ do not merge blind)* · `CE-047` · `CE-048` · `CE-050` *(rotating ALC flake)* · `MX-011` *(MCP lane: gizmo buffer into `PanelSnapshot`)* | unchanged |
| **5** | ⚪ **the "map shows no entities" symptom** — ⛔ still **unreproduced**, not fixed. The rail stands | watch |

### ⛔⛔ THE THREE TRAPS THIS SESSION PAID FOR — **do not re-pay them**
| # | trap | the guard |
|---|---|---|
| **①** | ⭐⭐⭐ **"the caller HAS the dependency and does not pass it"** — `BP-487` *(gizmo buffer)*, `CE-065` *(event registration)*, `CE-066` *(mission editor)*, **three times in one batch** | ⭐ before designing a shared abstraction, check whether the host **already holds** the thing and merely fails to hand it over. ⛔ Not a missing abstraction — a missing **argument** |
| **②** | ⭐⭐⭐ **THE INVERSE: a class that LOOKS like the shared thing while the shared thing is elsewhere** — `SharedAiWindowRegistrar` was DI-wired, cited in a design, and **superseded by `PerspectiveWorkspaceRegistrar`** *(⇒ DELETED, `CE-070`)* | 🔒 **before adopting any "unadopted shared" class, ask what the hosts ACTUALLY use for that job.** ⛔ In-degree 0 can mean *"somebody solved it better, over there"* |
| **④** | ⭐⭐ **A RESOLUTION RAIL PROVES A TYPE IS REGISTERED, NEVER THAT A FEATURE IS REACHED** — 5th rail-blindness instance. `AddSharedAiEditor_Resolves_…` kept a never-called class alive for months, and its container has **no production caller at all** ⇒ it asserted over a graph nobody walks | ⭐ **when deleting a rail, consider asserting its INVERSE** — the wrong shape is usually the reflex shape. ⛔ And check whether the container/graph a rail asserts over is one production actually walks |
| **⑥** | ⭐⭐⭐ **WHEN A WRAPPER BECOMES THE ONLY PRODUCTION PATH TO A TESTED FUNCTION, THE EXISTING TESTS STOP COVERING PRODUCTION** — `CE-072`: seven rails call `RegisterCommonCore`; since `CE-069` both hosts reach it only via `ShellCommandCoreBundle`, which **no test touched** | 🔒 **a wrapper needs an EQUIVALENCE rail the day it is introduced** *(same args ⇒ identical output, both halves)*. ⚠ Spy-based seam rails do **not** substitute: they prove `Compose` calls *a* bundle, never that *this* bundle forwards faithfully |
| **⑦** | ⚠⚠ **A GREEN GATE MAY BE SCOPED NARROWER THAN ITS NAME** — `CE-073`: `tracker-counts.py` counts only `BP-` rows *(448 of 760)*, so *"tracker-counts OK"* never covered the `CE-` rows each batch adds | ⭐ **read the gate's own filter once**, then cite it with its scope. ⛔ *"the gate is green"* is not *"the thing I changed was checked"* |
| **⑤** | ⭐⭐ **AN AS-BUILT NOTE IS NOT A DIAGRAM EDIT** — §5b.4 recorded `ReportUnserviceable`/the 3-arg ctor as *"not built"* and said *"the diagram above is corrected"*; ⛔ only the `classDiagram` had been touched, so the `sequenceDiagram` stayed false for a batch | 🔒 **obligation ⑤ is satisfied by CHANGING THE PICTURE**, not by describing the change beside it. ⭐ Re-read every diagram in the doc, not just the one you were thinking about |
| **③** | ⭐⭐ **`--no-build` prints PASSED over a STALE BINARY** — `CE-067`: `Hrot.Blueprints.Tests` *(3 983 tests)* had not compiled and every gate over it read green | ⭐ **build the project before trusting `--no-build`**; a *"pre-existing build error"* in a TEST project means **that whole suite is dark**, not that it is noise to route around |

### ⚠ Two of MY OWN estimates were wrong this session, both caught by tools
⛔ *"a 24-site cross-assembly rename"* → 📐 **19 hits, 9 files, one tree** *(the 24 was the graph's DEGREE)*.
⛔ *"`SharedAiWindowRegistrar` is the cheapest adopter"* → 📐 **CGF constructs 0 of its 7 windows.**
⇒ ⭐ **measure the edit surface before quoting a size, and measure adoption before calling something cheap.**

⚠ **A third, added `2026-08-27`:** my `CE-070` deletion argument rested on **adoption** *(in-degree 0, the job
done elsewhere)*. 📐 The **stronger** argument — that the class was the **wrong shape** for
`PerspectiveBound` windows and could never have worked — surfaced only while reading the windows' own
constructors during the build. ⇒ ⭐⭐ **read the CONSTRUCTOR of the thing being registered, not just the count
of who registers it**; the declared scope of a window is a fact about correctness, not about adoption.

⇒ ⭐⭐ Everything is bound by §3's standing constraint: ⛔⛔ **no bundle registers a module, system,
translator or participant** — and that is now **railed structurally**
*(`TheUiBundleSeamHoldsTests.A_bundle_cannot_reach_the_run_set`)*. ⚠ If that rail fails, it is a **DESIGN
question**, not a test to update.

## 0.1 🔒 THE PROBLEM, AND WHAT THE USER APPROVED

⭐ **The problem:** ~85% of each host's composition is the SAME shared pieces wired TWICE, independently ⇒ the *"CGF forgot to wire X"* bug class. 📐 **Every defect `CE-046`…`CE-064` is an instance**, and the user found six of them by eye.

| approved `2026-08-27` | |
|---|---|
| ✅ **`Q63-B` — per-feature BUNDLES**, ⛔ not one `ComposeEditorExperience(deps)` | a monolith forces `if (host==…)` *(ruling 58)* or a nullable-knob bag *(a silent-default generator)* |
| ✅ **`Q63-D` — DISSOLUTION, not extraction**, for `IEditorLogic` | 📐 it is 128 ln / ~15 members and `EditorApplication` is 297 ln of one-line delegations. `CE-060` dissolved one call in ONE LINE |
| ✅ **`Q63-E` — THIS SESSION owns EVERY composition root** | ⇒ ⭐ no cross-lane split needed; fix all roots together |
| ✅ **the parity rail goes FIRST** *(user: "for a refactor like this that rail is absolute must")* | |

## 0.2 🔒🔒🔒 CANON — the two USER RULINGS that constrain every batch

| ⭐ axis | reference | unify? |
|---|---|---|
| ⭐⭐⭐ **UI · scenario editing · monitoring · debugging** | **the EDITOR is the SOURCE AND SPECIMEN** | ✅ **aggressively** |
| ⛔⛔ **the RUN-SET** — modules · systems · services | **each host's ROLE** *(the editor runs almost everything; CGF/IG/SimHost run only what their role needs)* | ⛔ **NEVER** |
| ⛔⛔ **NETWORK** — translators · DDS · participant | **each host's ROLE** *(the sets are near-DISJOINT — measured)* | ⛔ **NEVER** |

⚠⚠ **THE TRAP:** *"editor is the specimen"* and *"editor runs almost everything"* are **TWO DIFFERENT AXES.** 📌 A *"map bundle"* that registered `MapCullingModule`+`StyleResolutionModule` because the editor does would **silently change what CGF computes every frame — and would look like a successful unification.**

### ⛔⛔ THE STANDING CONSTRAINT *(`AQ63` §10.5)*
> **No bundle may register a module, a global system, a DDS translator, an egress/ingress system, or a participant.**
⭐ A bundle **DECLARES** what its affordances need; the **HOST** decides what runs; an unserviceable affordance **REPORTS** it *(the `ToolActivationDrainSystem(reportUnserviceable:)` pattern)*. ⛔ A bundle that seems to need one has hit the role boundary ⇒ **STOP and report** *(`R-106`)*.

## 0.3 ⭐⭐⭐ PHASE 0 — the parity rail. **VENUE AND CHANNELS ARE SETTLED; NO PRODUCTION CHANGE**

| ⭐ | |
|---|---|
| **venue** | ⭐⭐⭐ **TWO WINDOWED PROCESSES under Xvfb, driven over MCP.** ⛔ **NEVER headless** — a panel publishes only when it DRAWS |
| **it already exists** | 📐 `ClusterConformanceRails.The_asset_panels_are_the_same_on_both_hosts` *(`:867`)* launches `StartAsync("…-editor")` + `StartAsync("…-all", mode:"all")`, captures by KIND, and asserts anti-vacuity **both** directions ⇒ ⭐ **phase 0 EXTENDS what it compares, not where it runs** |
| **channels** *(all exist — read `tools/ai-debug-mcp/SKILL.md`, ⛔ never derive MCP capability from engine source)* | ⭐ **`list_panels`** → `kinds` is *"the key a cross-host comparison uses"* · ⭐ **`get_panel`** → the view model, *"assert a field, do not parse prose"* · ⭐⭐⭐ **`get_gizmo_frame`** → *"what the map is drawing this frame, as data"* |
| ⛔⛔ **what it must NOT assert** | **run-set equality.** ⭐ It proves each host is INTERNALLY COHERENT and that shared **SURFACES** match — ⛔ never that two hosts RUN the same thing *(`AQ63` §10.4 — this CORRECTS the frame handoff's wording)* |
| **tier** | ⚠ **`T3`** *(two windowed processes, minutes)* ⇒ async / CI, ⛔ never a foreground blocker |

### ⭐ The phase-0 work items
| # | item |
|---|---|
| **①** | extend the two-host comparison to the **8 known drift instances**: scenario catalog non-empty · perspective icon keys resolve · `debug.*` group present · create-core single · `MutationInterceptor` set · perspective toolbar section present · scenario root · center/rotate routed |
| **②** | ⭐⭐⭐ **map parity via `get_gizmo_frame`** — the highest-value piece; reaches what no model-level rail can |
| **③** | the two NEW user symptoms *(`2026-08-27`, **`--mode all`** — ⚠ corrected `2026-08-27`: the user never runs `--mode cgf`)*: ① **the 2D map shows NO entities on some scenarios** *(e.g. `hill-attack` loads, map empty)* · ② **center-on-entity CRASHES** ⚠ **suspect: the `E3`/`CE-051` path is mine** |
| **④** | ⛔ **nothing in production** |
| ⭐ proof | each item must **redden on the pre-fix root** *(inverse edit)* |

## 0.4 ⭐ THE PHASE ORDER *(revised by ruling — `AQ63` §9.4 REVERSES the earlier node-first plan)*

**0** parity rail → **1** the bundle seam + **menus/toolbar** *(`CgfEditorShellToolbar` already IS the pattern)* → **2+** one bundle per batch, **extracted from the editor as specimen** → **N** *(optional, LATER)* node-bootstrap adoption, **CGF first**.
⚠ **Node adoption is deliberately LAST:** it is the only phase that touches orchestration/participant/time authority — the area the ruling says not to move blindly — and 📐 **not one** of `CE-046`…`CE-064` was a node-bootstrap gap.

## 0.5 ⛔⛔⛔ FACTS A LATER SESSION MUST NOT RE-DERIVE — **including FOUR claims I got WRONG**

| ⭐ fact | |
|---|---|
| ⛔⛔ **`--mode all` MUST run WINDOWED (Xvfb). Headless dumps come back EMPTY** | 🔴🔴 **THIS FILE'S §0-prev ALREADY SAID SO, flagged ⭐⭐⭐, and I still built `AQ63` §11 on a headless venue.** ⇒ ⭐ read §0.5 *before* designing a rail. **Xvfb IS installed** *(`/usr/bin/Xvfb`)* and `run-system-tests.sh` uses it |
| ⛔ **"this container has no display"** | ⚠⚠ **WRONG — I put it in two reports and a tracker row.** 📐 T3 ran **105 passed / 2 failed** here. I generalised ONE X11 `SIGSEGV` *(`ModeStartupRails(ig)`)* into a capability claim |
| ⛔ **"the map render path is eyes-only"** | ⚠ **WRONG** — `get_gizmo_frame` returns it as data |
| ⛔ **"`translatorPacks` unsupplied is a silent-default defect"** | ⚠ **RETRACTED** *(`AQ63` §9.3)* — External is a **network POSTURE** change; ingress comes from the factory. It is a **dead parameter**, not a missing dependency |
| ⭐⭐ **the god-facade is NOT a blocker** | 📐 `IEditorLogic` 128 ln / ~15 members; `AiShared` references it in **ZERO code** *(prose only)*; ~3 members genuinely editor-only |
| ⭐⭐ **the pre/post-`Kernel.Initialize()` line already IS the node/UI boundary** | 📐 editor `:1757` of 5325 · CGF `:850` of 2599 — same 33/67 ratio; **0** kernel-module registrations after it |
| ⭐⭐ **ExCon · ReplayBrowser · Orchestrator own NO kernel** | 📐 0 `ModuleHostKernel`, 0 `RegisterModule`; only `RegisterWindow` *(9/6/2)* ⇒ they are **pure bundle consumers** |
| ⭐⭐ **the seam for the UI half EXISTS, used backwards** | 📐 `IWindowRegistrar` has 10 impls and **8 ARE the subsystems**; the system half is **50 `IEcsModule`s**. ⭐ `SharedAiWindowRegistrar` = built, in-degree **0** |
| ⚠ **the rail-blindness pattern, THREE times** | `CE-049` asserted *present+enabled* not *has something to offer* · `CE-053` **supplied the input it tested** · `CE-064` had a correct but **UNREACHABLE** assertion *(a loop over an empty collection)* |

## 0.6 ⭐ GATES + the commands that matter

```bash
bash scripts/session-design-brief.sh              # RELEARN: ledger + 7-day digest + probes
python3 scripts/rulings-check.py                  # 25/25 expected
python3 scripts/design-digest.py --check          # STATUS headers + INVENTORY + UML
python3 scripts/tracker-counts.py --check         # open 102 / done 346 at CE-064
bash scripts/quick-check.sh <proj> [filter]       # T0, ~8 s
dotnet build <affected.csproj> --no-restore       # ⛔ NEVER the .sln in the fix loop (115 s vs 8 s)
bash scripts/run-system-tests.sh --no-build       # T3, ~11 min, ASYNC only
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs <file.md>
```

⚠ **Two known T3 reds, both PRE-EXISTING** *(proved against a base worktree)*: `/missions/*` capability classification *(**MCP lane's**, unfixed)* and `EntityBlueprintsEditModelTests` / `SimHostInstance` compile errors. ⚠ `TwoReloadCycles_OldAlcIsCollected` is the **known rotating ALC flake** *(`CE-050`)*.

## 0.7 ⭐ OPEN ids carried in
| id | |
|---|---|
| `CE-062` | blueprint live-value provider on CGF — ⭐ **unblocked** by `CE-059` |
| `CE-063` | `EditorMapPickAdapter` **duplicates** the shared `CanvasMapPickAdapter` — ⚠ possibly two capability levels; ⛔ do NOT merge blind |
| `CE-055`/`CE-056` | ⭐ **user confirmed NON-REPRO on a windowed box** *(`2026-08-27`)* ⇒ out of scope; ⚠ close as non-repro |
| `CE-047` `CE-048` `CE-050` | `MigrationAlertManager.Draw()` unwired · `DebugApiService.LoadScenarioLive` not routed via the session · the ALC flake |

## 0a. ✅ THE BATCH IMMEDIATELY BEFORE THIS — **`--mode all` parity** *(`CE-057`…`CE-064`, `2026-08-27`)*

📄 **Report: [`batches/REPORT_Cgf_Mode_All_Parity.md`](batches/REPORT_Cgf_Mode_All_Parity.md)** ·
designs [`../DESIGN_Cgf_Scenario_Windows_Slice.md`](../DESIGN_Cgf_Scenario_Windows_Slice.md) *(§10 = AS-BUILT)*.
⭐ Merged by the coordinator at `c67f1b2ae`; this lane then added `CE-064`.

⭐⭐ **Why it matters to the quest:** all four symptoms the user hit in `--mode all` were composition drift —
⇒ **the evidence base for `AQ63`.**

| id | what |
|---|---|
| `CE-057` | CGF resolved `{staging}/nodes/node-N/scenarios` — **a directory that does not exist**; the scenarios are in `{staging}/shared/scenarios`. `OrchestrationConstants.GetSharedScenariosRoot()` is now the ONE authority |
| `CE-058` | `PerspectiveIconKeys` — ⛔ **NOT a second toolbar**: both hosts build the same `PerspectiveToolbarSection`; the icon TABLE had one caller, so CGF took the documented text-button fallback |
| `CE-059` | CGF constructs `BlueprintDebugSession` *(it already held all 3 ctor args)* + the shared `ActiveDebugSessionMirror` ⇒ the `debug.*` group works rather than being present-and-dead |
| `CE-060` | `ScenarioOrbatAdapter.SelectEntity` **ignored its argument** on BOTH hosts; now publishes `ActivateEditorToolEvent` + `SelectEntityCommand` |
| `CE-061` | **E5** — four `ManagedWindow` wrappers became shared `Hrot.Presentation.Windows.*PanelWindow` types; four adapters moved as `Scenario*`; ⭐ editor window IDS unchanged and railed |
| `CE-064` | every catalogued scenario carries a real `SourceFilePath` — ⚠ found only because `CE-057` made the list non-empty and a T3 rail could finally fail |

## 0-prev. ⛔ HISTORY — **the cross-host conformance harness** *(`2026-08-24`)*

📄 **Designs *(the AS-BUILT records — read these FIRST)*:
[`../blueprints/Architect_Question_54_Cluster_Mcp_Contract.md`](Architect_Question_54_Cluster_Mcp_Contract.md) § AS-BUILT ·
[`../DESIGN_Headless_Testability.md`](../DESIGN_Headless_Testability.md) §6e + § conformance AS-BUILT.**
📄 **Report: [`batches/REPORT_Conformance_Harness.md`](batches/REPORT_Conformance_Harness.md)**.

⭐ **Ids: `HN-025`/`026`/`027` done; `HN-028`, `HN-029`, `MX-014` open.** ⭐ Next free: `HN-030` / `MX-015`.
⭐ **Suite: `76 → 80`, all green.** ⭐⭐ **`--mode all` answers MCP.**

### ⛔⛔ The six facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **`--mode all` MUST run WINDOWED (Xvfb), never headless** | 📐 a panel publishes only when it DRAWS and the headless runner loop never calls `DrawUIAll` ⇒ every dump would be empty. `EditorProcess.StartAsync(mode: "all")` does this |
| ⭐⭐⭐ **A PROVIDER'S DEPS MUST BE LAZY** | 📐 `_clusterTimeAdapter` is built in `RegisterWindows` — AFTER the composition root builds providers ⇒ a value-captured provider reported `time.drive:false` for SimHost and CGF, i.e. **the manifest lying in the safe-looking direction** |
| ⭐⭐⭐ **The conformance diff IGNORES `panelId`; the GOLDENS keep it** | 📐 a VM contains its own id ⇒ two hosts publishing one KIND can never be byte-identical *(a first cut reported 6 of 6 DIFFERENT entirely on the address)*. ⛔ Goldens are keyed BY id, so there it is content |
| 🔴🔴 **The cluster CANNOT be given the editor's scenario** | `POST /scenario/load` ⇒ `NOT_SUPPORTED_HERE(editor.authoring)` — a cluster loads via the orchestrator's 2PC. ⇒ the design's *"load S in both, then diff"* is not executable; only world-INDEPENDENT structure is comparable *(`HN-029`)* |
| 🔴🔴 **The ack-gate's cluster half is CROSS-LANE** | `MasterSyncController` is private in `OrchestratorSubsystem` *(TIME lane)* ⇒ `hasMaster:false` in the manifest, **asserted by a rail** so it reddens when the TIME lane exposes it *(`HN-028`)* |
| ⚠ **Comparing clocks on a FREE-RUNNING cluster measures harness latency** | 📐 the first lockstep attempt read a ~3-tick gap that was elapsed wall time. ⭐ Pause, then step, then read — CGF and SimHost are then bit-identical |

---

## 0b. ✅ **the regression net, part C** *(`N2`–`N6`)* is DONE *(`2026-08-24`)*

📄 **Design *(and the AS-BUILT — read §7b and §8b FIRST)*:
[`../DESIGN_Regression_Net.md`](../DESIGN_Regression_Net.md)** — now `BUILT`.
📄 **Report: [`batches/REPORT_Regression_Net_Part_C.md`](batches/REPORT_Regression_Net_Part_C.md)**.

⭐ **Ids: `HN-020`/`HN-021`/`HN-022` done; `HN-023`, `HN-024`, `MX-013` open.** ⭐ Next free: `HN-025` / `MX-014`.
⭐ **Suite: `58 → 76`, all green.**

### ⛔⛔ The five facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **A GOLDEN IS CAPTURED ON A FIRST LOAD IN A FRESH PROCESS** | ⛔ `HN-011`: a reload leaves entity `1000` carrying `BlueprintAssignments` ⇒ a golden captured after one **bakes the defect in**. `GoldenCaptureFixture` owns a private editor and loads **once**; ⛔ the shared collection fixture may not be used for captures |
| ⭐⭐⭐ **THE NORMALIZER'S IGNORE-LIST IS EMPTY, AND THAT IS MEASURED** | 📐 Across all 41 dumps a path and a `timestamp` appear in **one** panel *(`fdp_message_log`)* and a `frame` in one more — both already declared-volatile. ⛔ **Never widen it to go green**; a control rail re-derives the claim from the committed goldens |
| ⛔⛔ **A PANEL ID CAN CONTAIN A SLASH** | `editor/_gizmo` — it threw `DirectoryNotFoundException` on the first capture. Encoded `/`→`~`, with an injectivity rail |
| 🔴🔴 **THE SHARED EDITOR CAN HIDE A LIVE DEFECT** | 📐 With `9aa790d57` reverted, the `R-132` assertion **passed in the full suite** and **failed in its own process**. ⇒ ⭐ a falsifiable behaviour claim gets a **fresh process** *(design `Q1` overturned)* |
| ⚠⚠ **THE AUTHORING PERSPECTIVES CAN ONLY BE CAPTURED EMPTY** | 📐 **48 routes; none opens an AI asset** ⇒ 30 of 41 panels are pinned only in their no-asset shape. `MX-013` is the highest-value addition to the harness |

---

## 0c. ✅ **a preview leaves no trace** is DONE *(`HN-017`, `2026-08-24`)*

📄 **Design *(and the AS-BUILT record — read §4d FIRST)*:
[`../DESIGN_Deterministic_Network_Ids.md`](../DESIGN_Deterministic_Network_Ids.md)** — now `BUILT`.
📄 **Report: [`batches/REPORT_Preview_Leaves_No_Trace.md`](batches/REPORT_Preview_Leaves_No_Trace.md)**.

⭐ **Ids: `HN-017` done; `HN-018`, `HN-019` filed open; `HN-012`/`HN-013` closed.** ⭐ Next free: `HN-020`.

### ⛔⛔ The five facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **"what preview saves" lives in `Fdp.Toolkits/Orchestration/Preview/`** | ⛔ **NOT in either handler.** 📐 There are **two** preview handlers *(`HN-016`)* and the design named the **editor-only** one as the "one home" — that would have been exactly the hardwiring the user's steer forbids |
| ⭐⭐⭐ **A pooled allocator's issuing position IS ITS QUEUE** | ⇒ ⛔ `Reset(Read())` was never possible *(`BlockIdManager.Reset` ignores its argument; `DdsIdAllocator.Reset` writes a **global** `Req_Reset`)*. ⭐ All five allocators implement `IRestorableIdAllocator`, and ⛔ **none of them talks to the central authority** |
| ⭐⭐⭐ **The allocator may NEVER be restored without the map** | 📐 `NetworkEntityMap.Register` throws on a duplicate id and the editor never prunes ⇒ exact id repetition makes that throw **certain** on preview 2. ⭐ The drift was the only thing hiding the leak |
| ⭐⭐ **Cluster-wide needed NO new protocol** | both handlers answer `PrepareState(LoadingPreview/UnloadingPreview)`: the master broadcasts, **each node restores its own reservation locally** |
| ⚠⚠ **`Hrot.SimHost.Tests` and `Fdp.Toolkits.Tests` BOTH have rotating order-dependent reds** | 📐 Proved on a **stashed** tree: 4 then 11 failures over two identical runs. ⇒ ⛔⛔ **a full-suite red/green there is not evidence about your change** — isolate. `HN-019`, and `DEBT-AIB-030`'s shape |

---

## 0d. ✅ **the perspective model, Part A** is DONE *(`2026-08-23`)*

📄 **Design: [`../DESIGN_Perspective_Unification.md`](../DESIGN_Perspective_Unification.md) §3** — now
`BUILT`, with per-item **AS-BUILT** notes folded in *(obligation ⑤)*.
📄 **Handoff: [`batches/HANDOFF_Perspective_Model_Part_A.md`](batches/HANDOFF_Perspective_Model_Part_A.md)** ·
**Report: [`batches/REPORT_Perspective_Model_Part_A.md`](batches/REPORT_Perspective_Model_Part_A.md)**.

⭐ **`BP-488`–`BP-497`.** All items landed; ⛔ nothing descoped. ⭐ **Next free id: `BP-498`.**

### ⛔⛔ The four facts a later session must not re-derive

| ⭐ | |
|---|---|
| ⭐⭐⭐ **The editor's perspective id is `"Scenario"`** | ⛔ **not `"Editor"`** — `L6.1b` is DONE. ⚠ The **subsystem** is still named `"Editor"`, and so are its node/log names ⇒ 📌 **a perspective is not a subsystem name**, which was this batch's whole lesson |
| ⭐⭐⭐ **`SwitchPerspective` REFUSES an unclaimed perspective** | ⇒ ⛔ **a rail must REGISTER a claiming window BEFORE switching.** 📐 Four existing rails had the order backwards and passed only because no check existed |
| ⭐⭐ **CGF's perspective is `"Scenario"` too, and there is NO `CGF` perspective** | `perspectiveMap["Scenario"] = "CGF"` — the one entry whose key and value differ |
| ⭐⭐ **`FindResultsWindow`'s `owningPerspective` is REQUIRED**, and the scope is a parameter | ⛔ the `?? "Authoring"` default is gone, and the ctor refuses an anonymous `PerspectiveBound` window or a `Global` one that names a perspective |

⚠ **Two things left for the coordinator** *(§6 of the report)*: CLAUDE.md's coordinator-branch row looks
stale, and `Hrot.Presentation/Windows/FdpEntityInspectorHelper.cs` is on no lane's surface list although
`A1` had to touch it.

---

## 1. ✅ HISTORY — `BP-399` *("one shell")* is **DONE**

> ⚠ **This section and §2 are HISTORY as of `2026-08-23`.** ⭐ Read §0 for where the lane actually is.

📄 **The design: [`DESIGN_Details_Panel_View_Switching.md` §7](DESIGN_Details_Panel_View_Switching.md).**
📄 **The dispatch: [`batches/TASKS_One_Shell_BP399.md`](batches/TASKS_One_Shell_BP399.md).**

| # | what | state |
|---|---|---|
| **S0** | measure whether the Diagnostics/Blackboard `L3` rows were already satisfied | ✅ **yes, no code owed** |
| **S1** | Blueprint gets the real shell *(atomic; `BlueprintDetailsWindow` deleted)* | ✅ `BP-428`–`BP-430` |
| **S2** | `details.nodeproperties` on BTree + HSM at Rank 20 | ✅ `BP-431`–`BP-433` |
| **S2b** | the asset-scoped arms leave `InspectorWindow` **as menus, not views** | ✅ `BP-434`–`BP-437` |
| **S3** | `details.utility`, ported honestly as the stub it is | ✅ `BP-438` |
| **S4** | `details.parametersync`, Rank 15 | ✅ `BP-448` — `R-99` **satisfied**, not waived |
| **S5** | retire `InspectorWindow` | ✅ `BP-449`, `BP-450` — the class is **deleted** |

⛔ **`InspectorWindow` no longer exists.** All six arms are Details views or asset-row menu items.

### ⚠ Two corrections this lane made to its own claims — **do not re-introduce either**

| ⛔ the wrong claim | ⭐ the truth |
|---|---|
| *"`S5` is blocked on `S3` alone"* *(`S2b` report)* | **`BP-439`** — it was **`S4`**; §7.6's ④-before-⑤ order was right. ⚠ The **mirror error**: cleared one blocker, inferred the remainder instead of re-reading the sequence |
| *"`ai_inspector_*` is in no layout file"* *(`S5`)* | **`BP-450`** — ⛔ **FALSE.** My grep used `--include=*.cs`, excluding the very file types a layout lives in. `BP-103b`'s stale-layout rail caught it. ⚠ **An absence claim from grep is an absence in your PATTERN** |
| *"arms ① and ⑥ need a home in the Details panel"* *(`BP-431`)* | 🔒 The user routed all three **OUT**: collisions → Diagnostics, Rename…/Find References → the Asset Browser row menu, Go to Definition → **deleted**. §7.4a |

---

## 2. ⭐⭐⭐ THE NEXT TASK — **[`batches/HANDOFF_Panel_Observability.md`](batches/HANDOFF_Panel_Observability.md)**

> 🔒 **The user's instruction, `2026-08-22`:** *"then your task will be `HANDOFF_Panel_Observability.md`."*

⛔⛔ **READ THE DESIGN FIRST: [`../DESIGN_UI_Observability_Snapshot.md`](../DESIGN_UI_Observability_Snapshot.md)**,
whole — its **§UML** is the contract, and **§Invariant** *(the draw renders ONLY from the VM)* is the
load-bearing rule. ⭐ Umbrella context: [`../DESIGN_Headless_Testability.md`](../DESIGN_Headless_Testability.md).

| phase | what | how |
|---|---|---|
| **1 — `U-obs-1`** | `IPanelViewModel` + `PanelSnapshot` + the opt-in registry + **ONE pilot panel** end-to-end + a stable panel id | ⛔⛔ **HANDS-ON, do NOT fan out** — it is the pattern every later conversion mirrors. ⭐ Then **push a green checkpoint**: it unblocks the time lane's Group T |
| **2 — `U-obs-2+`** | the per-panel fan-out *(Details/blackboard/watch first, then the gizmo peer feed, then value-ordered)* | ⭐ **SONNET subagents**, Opus reviews the real diff and re-runs each panel's gates. ⛔ Review gate: **the INVARIANT** — any drawn value not from the VM is a defect |

⭐ **New tracker area `K` — Panel observability.** ⭐ Dispatch sha `5843055e7`; **scope frozen there.**
⭐ **Run freely — wait for nothing.**

### ⚠ Before writing code

1. **rule 7** — already done this session *(merged `2d95c419`)*; re-merge if the coordinator moves again.
2. **rule 1b** — push an empty `chore: started <batch> at <sha>` marker **immediately**, before any code.
3. **`U1a`'s open call:** the handoff *leans* to homing the contract in `Fdp.Diagnostics.Contracts`
   *(beside `DebugPrimitiveBuffer`)* — ⭐ **confirm the assembly by measurement and say so in the report.**

---

## 3. ⭐ THE STANDING PROTOCOL — **what this lane does every batch**

| | |
|---|---|
| ⭐⭐ **design before code** | `R-129`: intent is in `docs/` *(current)* and `.dev/<programme>/*-DESIGN.md` *(implemented)*, **never** in the code. Cite **doc + section** per item |
| ⭐⭐ **INVENTORY before design** | `search_graph` **first** — grep can only confirm a guess, never enumerate. Record the query + its `total` |
| ⭐⭐⭐ **revert-goes-red per item** | ⛔ un-apply with the **inverse edit**, ⛔⛔ **NEVER `git checkout --`** |
| ⭐ **tiers** | `T0` `scripts/quick-check.sh <csproj> [filter]` while working; the **full gate table ONCE, at the end** |
| ⭐⭐ **the gate report substitutes for the coordinator's run** | 8-row contract: per-gate command + counts + delta · a `--no-build` column · goldens as a **diff shape** · every red **confirmed pre-existing against the base sha** · clean tree · both quarantine counts · `tracker-counts.py --check` + ids allocated · the **integration suite** for a cross-cutting change *(or why it cannot gate)* |
| ⭐⭐⭐ **obligation ⑤** | a deviation goes **back into the owning DESIGN doc**, prior state marked SUPERSEDED — ⛔ the report is ephemeral, the design is not |
| ⭐ **I allocate the ids** | state them in the report. **Next free: `BP-498`** *(⚠ was `BP-453`; `BP-453`–`BP-497` are spent)* |
| ⛔ **no PR** unless the user asks | there has never been one in this programme |
| ⭐ **links for mobile** | `https://github.com/pjanec/HROT/blob/claude/reset-working-branch-qd1qpv/<path>` — ⚠ **push first** |
| ⛔ **plain-text questions** | never the multiple-choice widget |

### ⚠ Known pre-existing — **do not re-diagnose**

| | |
|---|---|
| `StructEdit.Tests` **1 red** | `DocumentBuilderTests.Build_CircularReference_…` — confirmed in a clean worktree at `5d1fd44d` |
| `Fdp.Presentation.Tests` | ⛔ cannot run whole *(`BP-419`, test-host crash)* — gate by `--filter` |
| `ClusterRunner.Integration.Tests` | ⛔ un-gateable, pre-existing DDS-allocator crash *(Batch 101)* |
| `Fdp.Toolkits.Tests` | ⚠ `DEBT-AIB-030` — the failing identity **rotates**; neither red nor green is evidence |
| `rulings-check.py` | ⚠ 1 staleness WARN on `.claude/CLAUDE.md` — pre-existing |
| ⭐ `design-digest.py --check` | ✅ **now fully green** — the coordinator's `8ad6d6aa` cleared the four long-standing failures |
| ⭐ `Hrot.ClusterRunner.Tests` **2 red** | `DataDrivenGizmoPredicateTests.D003_*` — `InvalidCastException` casting a test double to `DebugPrimitiveBuffer` at `DataDrivenGizmoSystem.cs:314`. ⛔ Confirmed pre-existing in a clean worktree at `c6f54318c` |
| ⚠ `Hrot.Editor.Tests` **1 FLAKY** | `AiHotReloadCoordinatorTests.TwoReloadCycles_OldAlcIsCollected` — asserts an `AssemblyLoadContext` was GC-collected. 📐 Green 2 of 3 whole-suite runs, 3 of 3 filtered ⇒ ⛔ **neither colour is evidence** |
| ⚠ a merge that adds a project | **`dotnet restore` first** — `Hrot.SystemTests` arrived this way and `--no-restore` failed on it |

---

## 4. ⭐ CARRIED OPEN

### ⭐ `S4` / `BP-399`'s tail — **both blockers are now CLOSED; one design call remains**

📄 **[`Architect_Question_49_…`](Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md)** —
written by the coordinator, **amended by this lane `2026-08-22`**:

| | |
|---|---|
| ✅ **`Q49` option C is BUILT** *(`BP-440`–`BP-442`)* | the identity is recomputed from the catalog resolver already wired at `PerspectiveWorkspaceRegistrar:289`, through ONE derivation *(`SubtreeSyncIdentity`)*, pulled from inside `Emit` so no path can forget *(`R-126`)* |
| ✅ **`Q50` option A + `Q49` option D are BUILT** *(`BP-444`–`BP-447`)* | 🔒 user: *"i hoped the editor automatically adds the subtree's data."* ⭐⭐ **A and D turned out to be the SAME change**: every input is persisted, so it is a **generator-side projection over a document** — no editor, no ordering. `SubtreeSyncProjection` does one walk yielding both the groups and the slice fields, so *"a group without its field"* is unrepresentable |
| ⛔⛔⛔ **RE-MEASURED `2026-08-22` — `BP-446`'s LIMIT WAS DESCRIBED WRONG. 📄 Read [`Q50`](Architect_Question_50_The_Master_Blackboard_Declares_The_Subtree_Slice.md) *"THE LIMIT — re-measured"* BEFORE reasoning about this area** | ⛔ *(was: "a generated Category-2 callee blackboard does not exist in the master's compilation")* — 📐 **all 15 managed assets declare `BrainBlackboard`, an ordinary resolvable type; that skip never fires.** ⭐⭐ **The real wall is the BYTE BUDGET** — 128 bytes vs a 100-byte inline budget ⇒ **"declare the slice as a field" can never hold a Category-2 callee.** ⚠ Architectural, ⛔ not a missing helper |
| 🔴 **THE REACH — the honest state of `S4`** | the panel can only author against a **Category-2** callee *(it needs `BlackboardVariables`)*, and the generator skips **every** Category-2 callee ⇒ ⛔ **the authorable and emittable sets are DISJOINT today.** ⭐ The panel is real and writes real persisted data; ⛔ no authorable binding reaches the runtime yet |
| ✅ **POSTPONED BY THE USER, ON THE RECORD** *(`BP-452`)* | 🔒 *"is that safely postponable, providing you record it thoroughly as such?"* ⇒ ⭐ **yes**: every failure is a **build-time skip**, never a partial emit or a bad runtime copy; 📐 **no corpus asset has a sync binding.** ⭐ Three routes with a lean *(`C′` — declare the slice `Role = State`, reusing the partition tier that already escapes the budget)* — ⚠ it moves the emitted body, so it wants a nod |
| ⛔ **ONE REAL DEFECT, not postponable indefinitely** *(`BP-451`)* | **nothing validates a binding's `FieldName` against the callee's type** ⇒ the generator **can emit CS1061** — 📌 `BP-306` re-armed. ⭐ Unreachable through the UI, reachable by hand-edited JSON. 🛠 Small, needs no design call ⇒ **do it in the next batch touching this generator** |
| ⚠ also required | the **master** blackboard must be `Managed`; a Category-1 master cannot gain a field *(the one claim of `BP-446` that survived)* |
| ⚠ still awaiting a nod | `Q49`'s open sub-question — a **MISSING** subtree at load. ⭐ Recommended: a diagnostic row. Built behaviour: identity left alone, never erased |

### ⚠ Other open `BP-` rows

`BP-405` · `BP-407` · `BP-411` · `BP-416` · `BP-418` · `BP-419` · `BP-426` *(needs a running editor)* ·
`BP-427` · `BP-342` · `BP-399` · ⭐ **`BP-451`** *(a real defect — see above)* · ⭐ **`BP-452`**
*(postponed on the record)*. ⭐ Tracker: **open 92 / done 295**.

### ⭐ The other lanes — **do not touch their files**

| lane | branch | owns |
|---|---|---|
| **coordinator** | ⚠ **`claude/blueprint-authoring-status-6sr5ld`** is where the live handoffs and designs are being pushed *(`2026-08-23`)*; CLAUDE.md's table still says `…-gm0akp` — ⭐ **confirm by ancestry, not by name** | handoffs, designs, the ledger |
| ⭐ **backend** *(new `2026-08-23`)* | `claude/blueprint-macro-feature-sdmspn` | project/reference structure · the Stride cleanup. ids **`ST-`**, tracker **Area I only**. ⚠ **Our only shared file is `Program.cs`** |
| **time / MCP** | see `RESUME_Time_Stride_Session.md` | `Fdp.Toolkits/Time/` · `Hrot.Orchestrator` · `ModuleHostKernel` · the MCP harness. ids **`TM-`**, tracker **Area H only** |

⛔ **A cross-lane edit is a STOP-and-report, not a judgement call.**
