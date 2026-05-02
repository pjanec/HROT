# Onboarding — Tactical Intent Distribution System

## What Is Being Built

This workstream adds a **Group Cognitive Layer** to the HROT simulation engine. It lets
a Commander AI (or a human scenario author) issue a generic, unit-type-agnostic behavioral
order such as `"DefendArea"` to a group of mixed subordinates (APCs, infantry, drones).
A new receiver-side system translates each generic intent into the specific behavior that
fits the subordinate's capabilities, then hands off to the existing `BehaviorIngressSystem`
unchanged.

The same event pipeline is also wired into the existing `MissionAdapterSystem` so that
human-authored mission plans with generic behavior IDs go through the same resolution
path.

---

## Planning Artifacts

| File | Purpose |
|---|---|
| [DESIGN.md](./DESIGN.md) | Architecture phases, component designs, and decision log |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, and success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist |

---

## Folder Layout

### New files to create

| Project | Path | Description |
|---|---|---|
| `Fdp.Toolkits` | `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs` | New managed event |
| `Fdp.Toolkits` | `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs` | Mapper interface |
| `Fdp.Toolkits` | `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/TacticalIntentMapperRegistry.cs` | Mapper registry |
| `Hrot.CGF` | `Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs` | Receiver resolution system |
| `Hrot.Core` | `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/Intents/DefendAreaIntentDto.cs` | Example intent DTO |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED/TacticalIntentMessages.cs` | DDS wire struct |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentEgressTranslator.cs` | DDS egress |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentIngressTranslator.cs` | DDS ingress |
| `Hrot.AI.Behaviors` | `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/DefendAreaMapper.cs` | First concrete mapper |
| `Hrot.AI.Behaviors` | `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CommanderNodes.cs` | Reference BTree action |

### Existing files to modify

| Project | File | Change summary |
|---|---|---|
| `Hrot.Core` | `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorCategory.cs` | Add `Commander = 1 << 4` |
| `Hrot.Core` | `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs` | Add intent ID constants (range 1000+) |
| `Hrot.Core` | `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BehaviorCatalog.cs` | Handle `Commander` category in `GetValidBehaviors` |
| `Hrot.CGF` | `Hrot/Subsystems/Hrot.CGF/Systems/MissionAdapterSystem.cs` | Emit `AssignTacticalIntentEvent`; remove `BehaviorRegistry` dependency |
| `Hrot.CGF` | `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | Add `TacticalIntentResolutionSystem`; add `TacticalIntentMapperRegistry` param |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs` | Add `dtTacticalIntentRequest = 92` |
| `Hrot.Network.NED` | `Hrot/Network/Hrot.Network.NED/SimHost/SimHostAuxiliaryTranslatorPack.cs` | Register egress and ingress translators |

---

## Project Dependency Notes

- `AssignTacticalIntentEvent` and `ITacticalOrderMapper` live in `Fdp.Toolkits`, which
  has no Hrot dependencies. This keeps the interface usable in any FDP project.
- `TacticalIntentResolutionSystem` lives in `Hrot.CGF`, which already depends on
  `Fdp.Toolkits`, `Hrot.Core`, and `Hrot.Common`. No new project references are needed
  for `Hrot.CGF`.
- `DefendAreaMapper` lives in `Hrot.AI.Behaviors`, which already depends on `Hrot.Core`
  and `Fdp.Toolkits`. No new project references are needed.
- The DDS translator pair lives in `Hrot.Network.NED`, which depends on `Fdp.Toolkits`
  via an existing `ProjectReference`. `AssignTacticalIntentEvent` is therefore reachable
  without adding new references.
- No circular dependencies are introduced by these placements.

---

## Build and Run

```powershell
# Build the full solution
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet

# Run all unit tests
dotnet test IOS-IG-SimHost.sln --no-build --nologo

# Run only the CGF and toolkit tests (faster feedback loop)
dotnet test Hrot/Subsystems/Hrot.CGF/ --no-build --nologo
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --nologo
```

---

## Development Workflow

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand the batch-based development
workflow used in this project. Work is delivered in batches, each referencing specific
TASK-IDs from [TASK-DETAIL.md](./TASK-DETAIL.md). Complete one batch at a time, verify
all tests pass, then commit.
