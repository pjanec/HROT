# Squad Coordination — Onboarding

Welcome. This workstream builds the **Brain-resident squad coordination layer**: role assignment, shared situational awareness, coordinated maneuvers (urban + open-field + tank-platoon), and danger-area sensing. It sits on top of Utility AI and the existing commander/subordinate hierarchy.

If you only have time for one document, read [`Squad_Coordination_Design_v1_1.md`](./Squad_Coordination_Design_v1_1.md). It is the destination of the squad-tactics design thread and the source of truth for every task here.

---

## What we're building

**Five Brain-resident primitives** (§2 of the design), unit-type-agnostic, all on the commander entity:

1. **Element partition** — split the `UnitRoster` into N elements (moving vs. covering, etc.).
2. **Tactical-feature reference** — handles on danger areas / features (a street, a crest, a choke).
3. **Role / slot assignment** — assign elements to slots (suppress, cross, sector-of-fire) via the Utility allocation matrix, re-run on phase changes.
4. **Phase sequencer with turn-taking** — the squad HSM substrate.
5. **Exposed-slot rotation with burn/reuse** — generalized from hill-attack's `BurnedSlotsMask` / `WaveUsedSlotsMask`.

**Plus:**
- A new EQS-shaped **danger-area sensor** (§5, child-entity lifecycle, bespoke result schema).
- A new **`SquadPerceptionMergeSystem`** (§4, the one genuinely new mechanism).
- **`ManeuverSelect`** — a commander-tier `[UtilityDecision]` that recurses the Utility core one level (§8.0).
- A **maneuver catalog** (§8) — danger-area crossing, bounding overwatch, suppress-and-maneuver, hill-crest hull-down, stack-and-room-entry, travelling overwatch — each a configuration of the five primitives.

**Out of scope** (deliberately): formation movement and cover-aware path *shaping* — that is **Muscle's** job; the squad layer only sets a `MovementMode` intent (§6.1).

---

## Where the documents live

```
.dev/group-maneuvers/
├── Squad_Coordination_Design_v1_1.md         ← architecture (read first)
├── Step_1_5_TargetMemory_3D_Reconciliation.md ← pre-step gate (must be merged green before squad work)
├── TASK-TRACKER.md                            ← phases + tasks, progress checkboxes
├── TASK-DETAIL.md                             ← per-task scope, constraints, success conditions
├── DEBT-TRACKER.md                            ← debt items (P2/P3); start empty
└── ONBOARDING.md                              ← this file
```

Cross-references into Utility AI:
```
.dev/utility-ai/
├── Utility_AI_Design_v1_1.md                  ← the scoring core; squad reuses §4 (aggregator), §10 (allocation matrix)
├── Utility_AI_Editor_Design_v1_2.md           ← visual authoring (ManeuverSelect rides this)
├── Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md  ← overlays (SquadAssignmentOverlaySource is the extension point)
└── TASK-TRACKER.md                            ← Utility's own work; P3–P6 must complete before squad starts
```

---

## Project layout — where the components are

### Existing infrastructure the squad layer reuses (already built — do not duplicate)

| Component | Location |
|---|---|
| `UnitRoster` (commander; capacity 16) | [`FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs`](../../FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs) |
| `UnitSubordinate` (back-pointer) | `FDP/Engine/Fdp.Core/CommandHierarchy/UnitSubordinate.cs` |
| `UnitHierarchySystem` | `Hrot/Subsystems/Hrot.SimHost/Systems/UnitHierarchySystem.cs` |
| `Blackboard1024` (1024 B shared block) | [`FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs) |
| `Blackboard1024.Project<T>` helper | same file (P0.5 of Utility AI) |
| `AssignTacticalIntentEvent` rail | [`FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs`](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs) |
| `TacticalIntentResolutionSystem` | [`Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs`](../../Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs) |
| `ITacticalOrderMapper` | [`FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs`](../../FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs) |
| `BehaviorIngressSystem` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs` |
| `TargetMemory` (3D after promotion) | [`FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs`](../../FDP/Toolkits/Fdp.Toolkits/Perception/Components/PerceptionComponents.cs) |
| `EqsSensor`, `EqsCognitiveBuffer`, `EqsResult` (32 B, 3D) | [`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs`](../../FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsComponents.cs) |
| `PartMetadata` (child-entity routing) | `FDP/Toolkits/Fdp.Toolkits/Replication/Components/PartMetadata.cs` |
| `HillAttackMutableState` (template precedent) | [`Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs`](../../Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs) |
| `HillAttackCommanderNodes` (parity target for P5-04) | [`Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`](../../Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs) |
| `FakeNavmeshProvider` (pattern precedent) | [`FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs`](../../FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs) |
| Utility AI scoring core + readers | [`FDP/Toolkits/Fdp.Toolkits/Utility/`](../../FDP/Toolkits/Fdp.Toolkits/Utility/) |
| `ThreatMatrixAssignmentSystem` (shrunk + migrated in P0-01) | [`FDP/Toolkits/Fdp.Toolkits/Utility/Group/`](../../FDP/Toolkits/Fdp.Toolkits/Utility/Group/) |
| `SquadState` Blueprint precedent | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/Recipes/SquadState.bp.json` |

### New code lives here

```
FDP/Toolkits/Fdp.Toolkits/Squad/
├── State/
│   └── SquadCognitiveState.cs                ← single contiguous projection onto commander Blackboard1024
├── Primitives/
│   ├── ElementPartitionPrimitive.cs
│   ├── TacticalFeatureRef.cs
│   ├── RoleSlotAssignmentPrimitive.cs        ← calls into ThreatMatrixAssignmentSystem adapter
│   ├── PhaseSequencer.cs
│   └── SlotRotation.cs
├── DangerArea/
│   ├── DangerAreaDescriptor.cs                ← 3D-native, 2.5D extent (OBB + Z band), §5.2
│   ├── DangerAreaSensor.cs                    ← child component
│   ├── DangerAreaCognitiveBuffer.cs           ← child component (InlineArray<8>)
│   ├── DangerAreaRefreshSystem.cs
│   └── Fake/
│       └── FakeDangerAreaProvider.cs          ← hand-authored descriptors (mirrors FakeNavmeshProvider)
├── Inputs/
│   └── SquadInputs.cs                         ← SquadKnowsContact, SquadStrengthRatio, ActiveFeatureKindIs, AssignedRole, …
├── Mappers/
│   └── ForceManeuverMapper.cs                 ← ITacticalOrderMapper for mission override (§8.0)
└── StarterPack/
    └── ManeuverSelectStarterDecision.cs

Hrot/Subsystems/Hrot.AI.Brain/Squad/
├── SquadPerceptionMergeSystem.cs              ← §4
├── CommanderUtilityTickSystem.cs              ← §8.0 commander-tier scoring
├── SquadVetoDetectionSystem.cs                ← §6, §9
├── SquadEventIngressSystem.cs                 ← §9 hybrid event/timer
└── SquadMovementModeBroadcastSystem.cs        ← §6.1

Hrot/Subsystems/Hrot.AI.Behaviors/Squad/Maneuvers/
├── DangerAreaCrossingManeuver.cs              ← §8.1
├── BoundingOverwatchManeuver.cs               ← §8.2
├── SuppressAndManeuverManeuver.cs             ← §8.3
├── HillCrestHullDownManeuver.cs               ← §8.4 (parity with HillAttackCommanderNodes)
├── StackAndRoomEntryManeuver.cs               ← §8.6
└── TravellingOverwatchManeuver.cs             ← §8.6

Hrot.Diagnostics/Hrot.Diagnostics.Overlays/
└── SquadCoordinationOverlaySource.cs          ← §10 (extends SquadAssignmentOverlaySource)
```

(Exact file locations should be verified against the existing folder conventions at implementation time. The naming above tracks the design.)

---

## How to build

The squad workstream lives inside the main solution. Standard commands:

```powershell
# from the repo root
dotnet build IOS-IG-SimHost-FDP-2.sln
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
# integration tests live in Hrot.IG.Tests and Hrot.ClusterRunner.Integration.Tests
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
```

The Utility-AI sub-suite is the closest precedent for the squad tests — its fixture pattern (`UtilityTestWorld`) is what the squad integration tests should mirror. See [`PREREQ_Phase0_Bundle.md`](../utility-ai/PREREQ_Phase0_Bundle.md) §P0.6 for the helper's surface.

---

## Order of work

1. **Verify pre-step gates are green:**
   - Utility AI Phases 0–6 are landed (check [`../utility-ai/TASK-TRACKER.md`](../utility-ai/TASK-TRACKER.md)).
   - 3D Cognitive Spatial Awareness Promotion is merged.
   - [`Step_1_5_TargetMemory_3D_Reconciliation.md`](./Step_1_5_TargetMemory_3D_Reconciliation.md) is merged.
2. **Phase 0** — state layout + `ManeuverSelect` kind + fake provider. **This is corrective work** to the already-merged Utility AI P1-07: `AssignmentSlot` shrinks from 64 B to 16 B and embeds into `SquadCognitiveState`. Treat as one atomic PR.
3. **Phase 1** — primitives library.
4. **Phase 2** — perception merge + danger-area sensor (+ fake provider integration).
5. **Phase 3** — commander-tier `ManeuverSelect` + mission-override mapper + starter-pack worked example.
6. **Phase 4** — authority (member considerations + veto detection) + rotation engine + `MovementMode` intent.
7. **Phase 5** — maneuver catalog: infantry first (8.1, 8.2, 8.3), then 8.4 hill-crest parity, then 8.6 briefer entries.
8. **Phase 6** — three-way authoring shells (HSM preferred, Blueprint, dedicated script).
9. **Phase 7** — overlays.

---

## Hot-reload, debugging, observability

- The Utility trace buffer (`UtilityTraceWorkingMemory1024`) is **the same component** on commanders that it is on agents — the commander-tier `ManeuverSelect` writes into it exactly like a posture decision. The inspector already renders it.
- The squad coordination overlay (Phase 7) is gated by `AiOverlayFlags.SquadAssignment` (already defined in the Utility AI overlay set) and uses the same per-frame budget arbiter.
- The `TuningConsoleGizmo` automatically picks up tunables on the new `ManeuverSelectStarterDecision` — no extra wiring; the `[Tunable]` discovery is source-gen driven (see `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md` §3.2).

---

## Developer guide

Read [`DEV-GUIDE.md`](../../DEV-GUIDE.md) (if present at repo root) and [`docs/AI_DEV_GUIDE.md`](../../docs/AI_DEV_GUIDE.md) — they cover repo conventions, `BrainBlackboard` discipline, `[SharedAiHeavyAction]` rules, batch reporting expectations, and the per-batch debt-tracker discipline.

A few squad-specific rules that aren't elsewhere:

- **The 1024 B is one claim, not two.** Anything that wants to live on a commander's `Blackboard1024` must extend `SquadCognitiveState` (a new sub-region with a pinned offset) — do not introduce a second projection on the same block. The collision check in P0-02 will catch it.
- **Primitives are pure C# over the SoA.** No `EntityRepository` reads inside the primitive bodies; the squad HSM (or whichever shell) feeds them inputs and consumes outputs. Same discipline as the Utility scoring core.
- **The danger-area sensor is not an EQS solver.** It reuses the child-entity lifecycle (`PartMetadata + ParentEntity`) and the `Epoch` cache-invalidation precedent, but its result schema is bespoke. Do not try to fit `DangerAreaDescriptor` into `EqsResult`.

---

*Welcome aboard. The architecture is small and consistent on purpose: one set of primitives, one scoring core (recursed), one shared-memory discipline, one event-driven rotation engine. If something feels like it needs a second mechanism, re-read the design — it probably already fits.*
