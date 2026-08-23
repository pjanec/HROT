<!--STATUS
state: LIVE
build-state: BUILT (S1-S5; no stops remain on this line)
updated: 2026-08-22
current-answer: the whole file — how to port the Bullet-based Stride from origin/stride-integ-1 onto the
  coordinator line, what the integration changed on the HROT/FDP shared side, and the one real breakage
  (the crowd movement-intent refactor). Awaiting user review before any code moves.
design-basis: origin/stride-integ-1 (measured 2026-08-22) · .dev/stride-1/Stride-Integration_v0_3.md ·
  STRANDED_FEATURES_AUDIT.md (user ruling: Bullet, not Bepu; Stride only).
known-conflict: none.
-->
# DESIGN — porting the (Bullet) Stride integration onto the current line

> 🔒 **User ruling:** we want the **branch's Bullet-based Stride**, not trunk's Bepu. This doc answers the
> question the user asked — *how did the integration change the HROT/FDP shared side, and does it break what
> we have?* — and gives a safe port strategy. **Not approved to build; review first.**

## Headline

⭐⭐⭐ **The brain → stride-node ANIMATION contract is CLEAN and ADDITIVE** — it is pre-existing shared ECS
data, **byte-identical on both branches**; the Stride node is just a new consumer behind the existing
`IAnimationBackend`. **The user's animation worry is unfounded — nothing on our side fights it.**

## ⚠ CORRECTION 2026-08-22 — the "crowd breakage" was OVERSTATED. The coordinator crowd is a NO-OP STUB.

> 🔒 **User challenge:** *"is there any crowd implemented elsewhere? isn't the Stride crowd the only
> implementation?"* — ⭐ **Correct.** 📐 Measured on the coordinator:
> - `EngineBackedDtCrowdProvider.cs` is a **36-line NO-OP STUB** — its own doc: *"No-op crowd provider stub…
>   `GetAgentVelocity` always returns Zero. Humanoid navigation in this mode is handled by
>   `LinearKinematicsSystem`."*
> - **No DotRecast library on the coordinator at all** — only a comment in `IDtCrowdProvider.cs`
>   *("eventually by a DotRecast/dtCrowd port for production")*. The **real** DotRecast crowd lives ONLY on
>   `stride-integ-1` (`Hrot.Stride.Core/DotRecastDtCrowdProvider`).
>
> ⇒ ⭐⭐ **The Stride crowd IS the only real crowd implementation.** The coordinator's `CrowdAgentUpdateSystem`
> is fed a stub that returns **zero velocity** — nothing moves through the crowd path today. ⇒ ⛔ **Porting
> the Stride crowd REPLACES a stub with the real thing — it is ADDITIVE, not a breakage.** The earlier
> "freezes crowd locomotion on non-Stride nodes" concern is largely MOOT: there is no live crowd locomotion to
> freeze. ⭐ Keeping the change **authority-conditional** is still tidy defensive hygiene *(so a future
> non-Stride node could get a real FDP crowd)*, but it is NOT the load-bearing risk it was framed as.

## INVENTORY — what was enumerated, and how *(the graph could not; the branch is disjoint + unindexed)*

⚠ **`search_graph` does not cover `origin/stride-integ-1`** — it is a disjoint-history branch, not indexed into
the codebase-memory graph. ⇒ the enumeration was by **git against the branch**, stated so it is checkable:

| query run | total | result |
|---|---|---|
| `git ls-tree -r --name-only origin/stride-integ-1 -- Stride/Hrot.Stride.Core Stride/Hrot.Stride.Animation` | **38 files** | the two new projects — 34 in `.Core` (Bullet motor + reverse-sync, DotRecast navmesh/crowd, vehicle nav, 3D debug render), 4 in `.Animation` |
| `git diff --stat origin/stride-integ-1 <coord> -- FDP/Toolkits/Fdp.Toolkits/Navigation FDP/Toolkits/Fdp.Toolkits/Physics Hrot.MuscleCharacter.Animation` | *(the seam table below)* | the shared-file deltas — the crowd/raycast/animation seams |
| grep for each shared symbol across trunk under any name *(the rename confounder)* | — | confirmed which "absent" pieces are truly new vs merely renamed |

⭐ **The seam table in §1 IS this inventory's shared-side half**; §3 is the delta half. ⛔ The as-built §6
records the two enumeration MISSES the port surfaced *(`IRaycastBackend`, the id-265 reservation)* — proof that a
git-diff enumeration, like a graph one, is only as complete as the paths it was pointed at.

## 1. The integration seams — all shared, all already on our branch (byte-identical)

| seam | type | file (same path both branches) | on coord? |
|---|---|---|---|
| pose / velocity | `SimTransform`, `SimVelocity` | `Fdp.Core/CoreComponents/SimComponents.cs` | ✅ identical |
| behaviour→muscle channels | `LocomotionChannel` · `WeaponChannel` · `InteractionChannel` | `Fdp.Toolkits/Behavior/Components/ChannelComponents.cs` | ✅ identical |
| discrete animation intent | `AnimationChannel` (PlayMontage) · `LookAtChannel` · `StanceIntent` | `Hrot.MuscleCharacter.Animation/Components/ReplicatedComponents.cs` | ✅ identical |
| animation backend contract | `IAnimationBackend.UpdateLocomotionInputs(...)` + montage APIs | `Hrot.MuscleCharacter.Animation/Contracts/IAnimationBackend.cs` | ✅ identical |
| brain-side writer | `AnimationRuntimeBridgeSystem` (SimVelocity → UpdateLocomotionInputs) | `…Animation/Systems/…` | ✅ identical |
| discrete-montage trigger | `OffMeshTraversalStartedEvent` / `OffMeshLinkDetectionSystem` | `Fdp.Toolkits/Navigation/PathfindingEvents.cs` | ✅ present |
| model/collider descriptor | `StrideRenderModelDefDto` + `CollisionShapeKind` | `Fdp.Toolkits/Tkb/Domain/…` | ✅ identical |

⇒ **The animation/pose contract was NOT touched by the integration** (the `.dev/stride-1` design says it
*reuses* `AnimationRuntimeBridgeSystem`/`IAnimationBackend`/`AnimationChannel.PlayMontage`; only the backend
implementation is new).

## 2. Animation control path — PORTABLE (plain ECS data, no Stride types crossing)

```mermaid
graph TD
    B["brain / behaviour"] -->|LocomotionChannel MoveTo| NIB["NavigationIntentBridgeSystem"]
    NIB -->|register agent, set target| CROWD["DotRecast crowd solves desired velocity"]
    CROWD --> CAUS["CrowdAgentUpdateSystem"]
    CAUS -->|"branch: CrowdMotorIntent.Velocity"| MOT["BulletCharacterMotor (Hrot.Stride.Core)"]
    MOT --> RS["BulletReverseSyncSystem (post-physics)"]
    RS -->|"writes SimTransform + SimVelocity"| SV["SimVelocity / SimTransform (authoritative)"]
    SV -->|"plain floats"| SAB["StrideAnimationBridge.PumpLocomotion"]
    SAB -->|"UpdateLocomotionInputs(horizX,horizZ,vertical,grounded)"| BK["StrideAnimationBackend"]
    BK --> LB["LocomotionBlend idle/walk/run"]
    LB --> PBT["PerEntityBlendTreeBuilder -> Stride AnimationComponent"]
    EV["OffMeshTraversalStartedEvent"] -->|discrete| SAB2["StrideAnimationBridge.DispatchTraversals"]
    SAB2 -->|"PlayMontageOnSlot Jump_Start/Loop/End"| BK
```

⭐ `StrideAnimationBridge` reads `SimVelocity` as plain floats (*"no Stride engine types appear here"*);
Stride types are confined to `PerEntityBlendTreeBuilder`, attached by visual binding. Same `IAnimationBackend`
that the shared `FakeAnimationBackend` implements. ⇒ **the Stride node is a clean additional consumer.**

## 3. The one conflict, and the safe strategy

| what | coordinator today | branch | verdict |
|---|---|---|---|
| `CrowdMotorIntent` (component **id 265**) | **ABSENT** (id 265 is FREE — coord ids stop at 264) | new movement-intent component | additive, no id collision |
| `CrowdAgentUpdateSystem` | writes `SimVelocity` **and integrates** `SimTransform += v·dt` — **owns** crowd position *(LinearKinematicsSystem excludes `CrowdAgent`)* | writes **only** `CrowdMotorIntent`; **stops** writing SimVelocity/SimTransform (delegates to Bullet motor + reverse-sync) | ⛔ **SAME FILE, DIFFERENT BEHAVIOUR — the risk** |
| `BulletCharacterMotor` · `BulletReverseSyncSystem` · `StrideVisualBindingSystem` | absent | in `Hrot.Stride.Core` | additive (new project) |

⚠ **Naive-port consequence, RE-SCOPED by the correction above:** the coordinator's crowd provider is a **no-op
stub returning zero** *(§ CORRECTION)*, so the refactored `CrowdAgentUpdateSystem` would not "freeze" any *live*
movement — there is none through the crowd path today. The change is effectively **replacing a dormant stub with
the real DotRecast+Bullet crowd**, i.e. additive.

⭐ **Still tidy to do it authority-conditionally** *(defensive, not load-bearing)*: keep a `SimVelocity`+
`SimTransform` integration branch for a future FDP-authoritative (non-Stride) crowd, and route through
`CrowdMotorIntent`→Bullet where `BulletReverseSyncSystem` is present. The additive pieces (`CrowdMotorIntent`
id 265, the `IDtCrowdProvider.RegisterAgent(entity, params, startPos)` overload, `NavigationIntentBridgeSystem`'s
deferred-retry robustness) port as-is. ⇒ **the whole Stride port is now assessed as ADDITIVE** — animation clean,
crowd a stub-replacement — with no live functionality on the coordinator that it would break.

## 4. Task breakdown (a FUTURE port — for review, not scheduled)

| # | task | note |
|---|---|---|
| **S1** | port the two new projects `Stride/Hrot.Stride.Core` (Bullet) + `Stride/Hrot.Stride.Animation`, into the solution | new paths, additive; keep Bullet |
| **S2** | add the additive nav pieces: `CrowdMotorIntent` (id 265), the `RegisterAgent(…startPos)` overload, the `EngineBackedDtCrowdProvider`/`FakeDtCrowdProvider`/`NavigationContractsComponentIds` deltas | additive |
| **S3** | ⛔ **make `CrowdAgentUpdateSystem` authority-conditional** (§3 strategy) — the ONE behaviour change; rail that non-Stride crowd locomotion still integrates SimVelocity/SimTransform | the risk item; gate carefully |
| **S4** | wire the Stride visual binding + animation backend on a Stride host; confirm `StrideAnimationBridge` reads `SimVelocity` and drives the blend tree | additive |
| **S5** | verify: non-Stride nodes (SimHost/editor/fake) still move crowd agents (regression); a Stride host animates from brain output | S3 is the thing to prove green |

## 5. Decisions for review

| # | decision | lean |
|---|---|---|
| **SD1** | port target branch | the coordinator line (as with MCP) |
| **SD2** | physics backend | **Bullet** (user ruling) — do NOT adopt trunk's Bepu for this |
| **SD3** | the crowd system | **authority-conditional** (§3), never wholesale replace | 
| **SD4** | scope of v1 | S1–S3 + a regression rail (S5); the Stride host wiring (S4) can follow |
| **SD5** | relationship to trunk's `Stride/BepuSample` | leave it; the ported Bullet Stride is a separate host path — decide later whether Bepu sample stays |


---

## 6. ⭐⭐⭐ AS-BUILT — **what the port measured, including two premises of this map that were FALSE**

> 📄 Batch: [`blueprints/batches/BATCH_ST101_The_Stride_Port.md`](blueprints/batches/BATCH_ST101_The_Stride_Port.md) ·
> ids **`ST-001`…`ST-009`**, tracker **Area I**.

### ⭐⭐ 6.1 The headline held — **and is now proven, not asserted**

⭐ *"The animation contract is CLEAN and ADDITIVE"* — ✅ **proven by a ZERO DIFF**: after the whole
port, `git status` reports **no change** under `Hrot.MuscleCharacter.Animation/`,
`Fdp.Core/CoreComponents/` or `Fdp.Toolkits/Behavior/Components/`. Suites **195 / 0 · 15 / 0 · 31 / 0**.

### ⛔⛔ 6.2 CORRECTION — **`id 265` was NOT free** *(`ST-005`)*

📐 §3 says *"id 265 is FREE — coord ids stop at 264"*. ⛔ **It looked only at `GlobalComponentIds` and
`NavigationContractsComponentIds`.** `NavFakeIds` declares its block as **262–279** and had RESERVED
**265** for `FakeVolumetricState`. ⭐ Measured: that constant has **no component attached** — its own
declaration is its only reference — so it is a reservation, not a live claim. ⇒ **moved to 269**;
265 now has exactly one claimant. ⚠ Left alone, a fake volumetric state built later would have
collided with a production component, silently.

### ⛔⛔ 6.3 CORRECTION — **the §1 seam table is INCOMPLETE: `IRaycastBackend` is a third piece** *(`ST-003`)*

📐 It surfaced as the **only** compile drift in `Hrot.Stride.Core`. `Fdp.Toolkits/Physics/IRaycastBackend.cs`
exists on the branch and **not at all** on the coordinator line, and `RaycastSolverSystem` gains a
nullable `RaycastBackend` property plus an early-return branch. ⭐⭐ **Additive and null on every
non-Stride node** ⇒ identical behaviour; Physics **31 / 0**. ⇒ ⚠ **`S2` should have named it.**

### ⭐⭐⭐ 6.4 `S3` AS BUILT — **the authority marker is the COMPONENT, per entity** *(`ST-004`)*

⭐ §3 asked for *"authority-conditional"* without saying what selects the arm. ⇒ **the entity's
`CrowdMotorIntent`**: present ⇒ write the intent only *(physics owns the pose)*; absent ⇒ the
pre-port `SimVelocity` + `SimTransform` integration, unchanged.

⭐⭐ **Per ENTITY, not per node**, and that is the substantive design choice: the component is added
only by the host that also runs the motor and the reverse-sync, so **its presence IS the marker**. A
node-level flag would be a second thing to keep in step with the first, and its failure mode is
silent — an agent that stops moving with nothing to point at. 📐 Railed with a **mixed world**: one
agent of each kind in one repository, both resolved correctly.

### ✅ 6.5 `S4` — **the hosted-real-editor mode BUILDS. My first verdict was wrong** *(`ST-007` → `ST-010`)*

⛔⛔ **I first reported this as "cross-lane, stop": the mode needs twelve `EditorSubsystem` members
this line lacks, so I guarded it out.** 🔒 **The user challenged that** — *"were they added on the
stride branch? what does that have to do with the UI branch, can't you do it yourself?"* — ⭐⭐ **and
the measurement vindicated the challenge.** 📌 I had applied the file-level lane rule **without
measuring what the change actually was.**

| 📐 measured | |
|---|---|
| **① they are the PORT's own seam** | the branch's block header: *"Public host-integration surface … for external host assemblies (e.g. `HrotStrideApp.Game`) to reach the live ECS world, kernel, and time controller **without reflection**"* ⇒ ⛔ **not UI-lane work that merely shares a file** |
| **② five of twelve ALREADY EXIST here, as `internal`** | `World` · `Kernel` · `EditorLogic` · `TimeController` · `PreviewController` ⇒ the change is **`internal` → `public`**, and the branch says so itself: *"behavior is identical to the former internal accessors"* |
| **③ the UI lane has NO live edit to collide with** | `git diff HEAD origin/claude/hrot-implementation-j1jvin -- EditorSubsystem.cs` is **EMPTY**, and their batch is Details/menus — a different region |

⇒ ⭐⭐⭐ **the guard is removed and the mode compiles.** ⭐ Two pieces the "twelve" had missed came with
it: **`MuscleModuleContext`** *(a 2-field record)* and **`DefaultSelectionState.Version`**
*(a monotonic counter in `Fdp.Presentation`)*.

⭐⭐ **The one member with a behaviour branch is `MuscleModuleFactory`, and its null arm is kept
BYTE-FOR-BYTE** — an editor that sets nothing cannot be affected by it. ⛔ **Deliberate deviation from
the branch**: it registers every muscle module in a `foreach` on BOTH arms, which on the default path
would register **one module more** than this line does *(HEAD registers `perceptionMod` only and
splices `simHostCorePack`'s system lists)*. ⇒ the `foreach` runs on the **injected arm only**.

📐 **Proof it breaks nothing**: `Hrot.Blueprints.Tests` Editor namespace — the suites that actually
**construct** `EditorSubsystem` — **1032 / 0**; `BreakpointSubsystemWiringTests` **25 / 0**;
`TimeControlIntegrationTests` **9 / 0**.

### ✅ 6.5b THE "REMAINING STOP" WAS A FALSE NEGATIVE OF MINE *(`ST-011`)*

⛔⛔ **I wrote that the `CharacterAnimationDefDto` family *"does not exist on this line at all"*.
📐 It does — byte-identically.** `git diff` on
`Hrot.MuscleCharacter.Animation/Descriptors/CharacterAnimationDefDto.cs` is **EMPTY**, and all eight
types are present. ⛔ **I had grepped only `FDP/Toolkits/`; the family lives in `Hrot/Subsystems/`.**

⇒ 📌 **A SCOPED grep read as an ABSENCE claim** — the failure CLAUDE.md names in one line
*("an absence claim from grep is an absence in your pattern, not in the repo")* — ⚠⚠ **and the second
time in this batch I asserted a design-blocking absence I had not enumerated.** *(The first was
§6.2's id 265, where the design made the same mistake and I caught it; here I made it myself.)*

⭐ **The real gap was one method.** `UrbanCombatNewScenario.BuildMannequinAnimationDef()` — a static
factory returning the mannequin's descriptor *(6 montages: idle/walk/run on slot 0 = Locomotion,
Jump_Start/Loop/End on slot 100 = FullBody; 2 slots; 2 footstep notify markers)* — plus **four**
`AddDescriptor` call sites on the **`InfantrySoldier`** and **`Insurgent`** templates, and a
`ProjectReference` from `Fdp.Examples.Scenarios` → `Hrot.MuscleCharacter.Animation` *(the same one the
branch added; `HrotStrideApp.Game` already depends on that project, so the direction is consistent)*.

⇒ ⭐⭐ **`HrotStrideApp.Game.Tests` now compiles with ZERO exclusions** — the whole ported test surface
is present. ⚠ **`ST-013`**: `CivilianPedestrian` renders as a mannequin but gets **no** descriptor —
the branch attaches it to the two combatant templates only, and I matched the branch rather than
deviating unprompted.

### ⚠⚠ 6.6 THE ENVIRONMENT LIMIT — **compile-verified, NOT run-verified** *(`ST-006`)*

| 📐 measured | |
|---|---|
| `net8.0-windows` builds on Linux | ⭐ **only** with `-p:EnableWindowsTargeting=true`, on **restore AND build** |
| the Stride suites **cannot run** | ⛔ the test host needs the `Microsoft.WindowsDesktop.App` **runtime**; there is no linux-x64 build *("No frameworks were found")* |
| `HrotStrideApp.Windows` **cannot build** | ⛔ `Stride.Core.Assets.CompilerApp` runs `--platform=Windows --compile-property:StrideGraphicsApi=Direct3D11`, exit **150** |

⭐⭐⭐ **The third was CONFIRMED PRE-EXISTING** by building the base commit `128eb68c` in a worktree —
same failure, before a single port file existed. ⇒ ⛔ **the port did not regress it.**

⇒ 📄 **[`Stride_Host_Visual_Test.md`](Stride_Host_Visual_Test.md)** carries the Windows launch
command and what a human should see.

---

## ⭐⭐⭐ 7. AS-BUILT — **the StrideMock removal, and where `StrideNodeBootstrapper` lives now**

> 📄 Dispatch: [`blueprints/batches/HANDOFF_Stride_Cleanup.md`](blueprints/batches/HANDOFF_Stride_Cleanup.md).
> ids `ST-014`…`ST-018`, tracker Area I. 🔒 **User, `2026-08-23`:** *"StrideMock is not needed anymore, it
> was just a temporary submodule used in place of the real stride. we can remove whole subsystem (unless
> you find something great it provides we should keep)."*

### ⛔⛔ 7.1 The relocation target in `1f`/`S1` was **IMPOSSIBLE**, not merely second-best *(`ST-014`)*

Both this doc and the dispatch leaned on *"beside `SharedApplicationBootstrapper` in
`Hrot.Common.Infrastructure`"*, on the strength of that class's own *"eliminating duplication across
SimHost, IG, and StrideMock"* comment. ⛔ **The comment points at the base class; it does not license
moving the concrete root.**

| 📐 measured | |
|---|---|
| `StrideNodeBootstrapper` uses | `Hrot.SimHost{,.Modules,.Serializers,.Systems}` · `Hrot.IG.{Components,Systems}` · `Hrot.Map.Common` · `Hrot.Network.Infrastructure` |
| `Hrot.SimHost` → `Hrot.Common` | ✅ already |
| `Hrot.IG` → `Hrot.Common` | ✅ already |
| ⇒ `Hrot.Common` → `Hrot.SimHost` | 🔴 **project-reference CYCLE** |

⭐⭐ **The rule this establishes:** a **composition root sits ABOVE the subsystems it composes**, so it can
never live in the engine layer those subsystems depend on. The *abstract base* belongs in `Hrot.Common`;
the *concrete root* cannot follow it down. ⚠ Any future "move it next to its base class" instinct hits the
same wall.

⇒ ✅ **As built:** `Hrot.StrideMock` was **renamed** to **`Hrot.NodeComposition`**
(`Hrot/Subsystems/Hrot.NodeComposition/`, `git mv` so history follows) and gutted to the single surviving
type. ⭐ This keeps the exact reference set that already compiled — no new edge anywhere in the graph —
and gives the type a home the Stride app may reference. ⭐ References **trimmed to what the type uses**:
`Fdp.Presentation`, `Fdp.Examples.Scenarios`, `Raylib-cs` and `rlImGui-cs` were the *mock's* rendering
shell and demo scenario. ⚠ The class comment has always read *"must not reference Raylib, ImGui"* — so
those four were untrue of **it** even before the mock went.

### ⭐⭐ 7.2 `S2` as written would have deleted tests of a SURVIVING type *(`ST-014`)*

📐 `Hrot.StrideMock.Tests` held **44** facts, and **22 of them test types that survive** —
`StrideNodeBootstrapperTests` (12, the very type `S1` preserves) and `SharedApplicationBootstrapperTests`
(10, a `Hrot.Common` type). ⇒ ⭐ both kept, in the renamed `Hrot.NodeComposition.Tests` (**22 / 0**); only
the 22 mock-only facts went. ⚠ Splitting `SharedApplicationBootstrapperTests` out into
`Hrot.Common.Tests` was **rejected**: both survivors need `OfflineNetworkFactory`, which would have forced
`Hrot.Common.Tests` → `Hrot.Editor` (Raylib) — a heavy edge onto a low-level test project.

### 📐 7.3 What was removed, and the numbers

| | |
|---|---|
| deleted types | `StrideMockSubsystem` · `FakeStrideEntity` · `FakeStrideEffect` · `FakeStrideScript` · `SyncFdpToStrideScript` |
| deleted projects | `Hrot.FakeStrideApp` + `.Tests` (outright) |
| renamed projects | `Hrot.StrideMock` → `Hrot.NodeComposition`; `.Tests` likewise (**GUIDs kept**, so solution configs and nesting stayed valid) |
| solution | **122 → 120** projects — 4 entries touched: **2 removed**, **2 renamed in place** |
| mode token | `stridemock` gone from **6** sites, not the 4 the dispatch measured — see `ST-015` |
| grants | both `InternalsVisibleTo Hrot.StrideMock.Tests` dropped (`Hrot.Common`, `Hrot.Presentation`) |

### ⚠⚠ 7.4 What `S1` could and could NOT be verified against *(extends §6.6 / `ST-006`)*

⭐⭐ **Better than the dispatch expected.** §3 of the handoff asked for the `HrotStrideApp.Game` reference
update to be reported as *"REVIEWED, NOT COMPILED"*. 📐 It **compiles**: with
`-p:EnableWindowsTargeting=true`, **`HrotStrideApp.Game` and `Hrot.Stride.Core.Tests` both build 0 errors**
— and those are precisely the two projects whose `ProjectReference` was repointed. ⇒ the relocation is
**compile-verified end to end on Linux.**

⛔ **Still owed a Windows check:** *running* the Stride suites. Re-confirmed here — the test host wants
`Microsoft.WindowsDesktop.App` 8.0.0 and reports *"No frameworks were found"*. ⇒ `StrideGameReferenceTests`
was **updated to the new home and compiles, but has not been executed anywhere.**

## 8. ✅ `ST-018` RESOLVED — **the desyncing tick path is gone** *(`ST-021`, Batch runner-tick)*

> ⭐⭐ **Outcome:** `StrideNodeBootstrapper.Tick` now calls `Context.Kernel.Update()` and the
> `#pragma warning disable CS0618` is **deleted**. ⭐ With `TreatWarningsAsErrors=true` on
> `Hrot.NodeComposition`, the obsolete overload would now be a hard **error** there, not a warning — the
> suppression cannot creep back silently.
>
> ⛔⛔ **AND ONE PREMISE OF THE RE-GRADE'S OWN CONTEXT WAS FALSE.** The code comment being replaced said
> *"`SlaveSyncController` needs network sync events to advance deterministically, which are absent in
> headless/offline mode"* — 📐 **measured FALSE**: `AdvanceContinuousTime` derives elapsed from
> `SyncedWallTicks = _getTick() + _masterWallClockOffset`, where `_getTick` defaults to the local
> `HighResUtcClock` and the offset is **0** until a master answers. ⇒ ⭐⭐ **sync events CORRECT the
> offset; they do not GATE advancement.** Offline the node advances on its own wall clock and starts
> tracking a master when one appears.
>
> ⭐⭐⭐ **This is the only reason `T1` was a clean fix rather than a STOP.** The dispatch said *"if the
> stepping path needs state this bootstrapper does not have, STOP and report — do not invent a time
> source."* ⚠ Had the old comment been true, `Update()` would have frozen an offline Stride node and the
> right answer would have been to report, not to patch. ⇒ 📌 **the premise had to be measured before the
> one-line change could be justified**, and `SteppingTimeController` was NOT needed: the controller the
> node already has does the right thing.
>
> ⛔ **Gate limit:** `HrotStrideApp.Game` **compiles** (`-p:EnableWindowsTargeting=true`) but the Stride
> suites cannot RUN here (`ST-006`) ⇒ **owed a Windows check**.

### ⛔ HISTORY — the re-grade as written

⭐ **The batch filed `ST-018` correctly and left the call byte-for-byte** — ⭐⭐ that was the right call for a
batch whose remit was to MOVE the type. ⛔ **But the finding is more serious than its report implies, and the
reason is in the attribute the `#pragma` suppresses:**

```csharp
[Obsolete("Use Update() utilizing SteppingTimeController instead. "
        + "This legacy overload will cause deterministic desync.", false)]
public void Update(float deltaTime)          // ModuleHostKernel.cs:464-465
```

| 📐 | |
|---|---|
| ⛔⛔ **the engine itself says the overload causes DETERMINISTIC DESYNC** | ⇒ this is a **correctness** hazard, not a lint |
| 🔴 **and the type it sits in is now the REAL Stride app's composition root** | 📌 exactly the premise change `ST-018` identified — *"not a live DDS-connected node"* is what died with the mock |
| ⭐⭐⭐ **it collides with charter `D6`** | the whole regression net rests on determinism; ⛔ **a composition root that deterministically desyncs is precisely what the net cannot tolerate** |

⚠ **Stated fairly — severity is LATENT, not live:** `HrotStrideApp` cannot even build on Linux *(`ST-006`)*, so
the Stride node is not in daily distributed use. ⇒ ⭐ **the hazard arms itself the moment a Stride node joins a
cluster**, and that is before, not after, we would notice.

⇒ ⭐⭐ **Scheduled as its own item** *(with the runner's mode rails — they share a gate: "does each mode start
AND tick correctly?")*, ⛔ **not folded into the test-infrastructure batch**: changing a kernel tick path is a
behaviour change and wants the time/integration suites, not a golden.


---

## ⭐⭐ 9. THE MODE RAILS — **where "every mode starts and ticks" now lives** *(`ST-019`)*

📄 **`Hrot/Runner/Hrot.SystemTests/ModeStartupRails.cs`** — eight cases, each booting the **real**
`Hrot.ClusterRunner` in one mode and holding it for a window (10 s default, `HROT_MODE_RAIL_WINDOW`).
⭐ Replaces a MANUAL gate row that had been asked for and not delivered, which is how `--mode all` came to
die on frame one in front of the user with every unit rail green.

| ⭐ | |
|---|---|
| **reuse, not a second launcher** | the Xvfb-ownership logic was **private** to `EditorProcessFixture`; it is extracted to **`XvfbDisplay.cs`** and the fixture delegates. ⛔ Two copies of the orphan-avoidance was the alternative — ⚠ `xvfb-run` stops its server from an EXIT trap that `Process.Kill` never runs, so each copy that got it wrong would leak a display per run |
| **why a WINDOW and not a launch check** | under `--mode all` each subsystem gets its **own isolated `FdpEventBus`** — the documented design, and the reason that crash existed. The fault appears on the first **publish**, not at startup ⇒ a launch-only check would have stayed green through it |
| ⭐⭐ **the banner is expected PER MODE** | `--mode all` is an **alias**: the runner echoes what it **expanded to** (`mode=orchestrator,simhost,ig,excon,cgf`). ⇒ this **pins the five-subsystem expansion at process level**, the fact the charter had to measure by hand. 📌 The rail's first version asserted `mode=all` and reddened a healthy mode |
| ⚠ **what it does NOT prove** | a mode **composes and survives**; ⛔ **not forward progress.** A subsystem that ticks once then silently stalls passes — frame counting needs a per-mode control plane and only the editor has one (`GET /status`, covered by `PanelSnapshotTests`) |
| ⭐ **`ig` is quarantined WITH A TRIPWIRE** | `R-131` forbids a permanent filter-around ⇒ a case asserts `ig` is **still** broken and **FAILS the day it is fixed**, naming `ST-020`. ⛔ `stridemock` is absent by design — that token now throws (`ST-015`) |
