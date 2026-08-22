<!--STATUS
state: LIVE
build-state: DESIGN (porting-risk map + strategy — NOT approved to build)
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

⛔ **The ONE real breakage is the movement/locomotion-intent refactor of the shared `CrowdAgentUpdateSystem`.**
Ported wholesale it would **freeze all crowd-driven locomotion on every non-Stride node** (SimHost, editor,
fake). It must be applied **authority-conditionally**, not as a replacement.

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

⛔ **Naive port consequence:** the refactored `CrowdAgentUpdateSystem` stops writing `SimVelocity`/`SimTransform`;
`LinearKinematicsSystem` still excludes `CrowdAgent`; and no Bullet motor exists on a non-Stride node to consume
`CrowdMotorIntent` ⇒ **crowd agents get intent but nothing moves them — frozen locomotion everywhere but Stride.**

⭐⭐ **Safe strategy — authority-conditional, not replacement:** keep the `SimVelocity`+`SimTransform` integration
path in `CrowdAgentUpdateSystem` for FDP-authoritative (non-Stride) nodes, and route through
`CrowdMotorIntent`→Bullet **only where `BulletReverseSyncSystem` is present** (i.e. a Stride node owns physics).
The additive pieces (`CrowdMotorIntent` id 265, the `IDtCrowdProvider.RegisterAgent(entity, params, startPos)`
overload, `NavigationIntentBridgeSystem`'s deferred-retry robustness) port as-is.

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
