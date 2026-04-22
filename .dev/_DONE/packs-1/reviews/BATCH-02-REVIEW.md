# BATCH-02 Review

**Batch:** BATCH-02
**Tasks:** PACK-I001, PACK-I002, PACK-I003, PACK-P002, PACK-P004
**Verdict:** ✅ APPROVED

---

## Verification Summary

| Project | Result |
|---------|--------|
| `Hrot.SimHost.Tests` | ✅ 0 failed / 412 passed |
| `FDP.Toolkit.CarKinem.Tests` | ✅ 0 failed / 127 passed |
| `FDP.Toolkit.Navigation.Tests` | ✅ 0 failed / 41 passed |
| `dotnet build IOS-IG-SimHost.sln` | ✅ 0 errors (328 pre-existing xUnit warnings) |

## Task Verification

### PACK-I001 ✅
- `PersonalRouteAuthoringSystem` writes `NavigationIntent` component (increments IntentId).
- `CmdFollowTrajectory` only appears in a comment — zero functional references.

### PACK-I002 ✅
- `SimHostVisualization` brain-dead path writes `NavigationIntent` correctly.
- `DdsParticipant` / NavState not mutated in right-click path.

### PACK-I003 ✅
- 5 Cmd movement structs deleted from `CommandEvents.cs`.
- 5 processing methods deleted from `VehicleCommandSystem`.
- `VehicleAPI` cleaned of 5 legacy methods.
- `NavigationIntentBridgeSystemTests` written and passing.
- Build succeeds — no remaining callers.

### PACK-P002 ✅
- DDS adapters extracted to `Hrot.SimHost/Network/SimHostNetworkAdapters.cs`.
- `SimHostModule.cs` — zero `DdsParticipant`/`DdsReader`/`DdsWriter` references (grep confirmed).
- Offline instantiation test passing.

### PACK-P004 ✅
- `UpdateEntityDescriptorRequestSystem` at `Hrot.Map.Common/Replication/Ingress/` (file verified).
- Old `Hrot.Map.Common/Systems/` location has no file.
- Old namespace `Hrot.Map.Common.Systems.UpdateEntityDescriptorRequestSystem` — zero grep results.

## Issues / Debt Recorded

### P3 — SimHostModule 9-parameter constructor growing wide
The constructor now has 9 optional parameters (DEBT-003). A builder or options-object pattern
would improve readability for callers that need only a subset of DDS systems. Currently not
fragile but will become maintenance hazard as more systems are added.

### P3 — NedRequestFinalizationSystem file/class name mismatch (DEBT-004)
File `SstRequestFinalizationSystem.cs` contains class `NedRequestFinalizationSystem`.
Latent maintenance hazard — should be renamed in a cleanup pass.

### P3 — xUnit2013 style warnings (DEBT-005)
328 warnings about `Assert.Equal(count, collection.Count)` vs `Assert.Empty/Single`. Not
breaking but adds noise. Could be fixed in a cleanup batch.

---

## Suggested Git Commit Messages

### Main repo (`d:\Work\IOS-IG-SimHost-FDP-2`)
```
feat(packs-1): BATCH-02 — Enforce Intent Bus + Extract Spawning Systems

PACK-I001: PersonalRouteAuthoringSystem writes NavigationIntent instead of CmdFollowTrajectory
PACK-I002: SimHostVisualization right-click writes NavigationIntent (remove NavState mutation)
PACK-I003: Delete 5 legacy Cmd* movement structs + VehicleCommandSystem handlers
PACK-P002: Extract DDS adapters from SimHostModule to SimHostNetworkAdapters.cs
PACK-P004: Relocate UpdateEntityDescriptorRequestSystem to Replication.Ingress namespace

Build: 0 errors. Tests: 580 passing across targeted projects.
```

### FDP submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP`)
```
feat(packs-1): BATCH-02 — delete legacy Cmd* movement events; routing via NavigationIntent

- Delete CmdNavigateToPoint, CmdFollowTrajectory, CmdNavigateViaRoad, CmdStop, CmdSetSpeed
- Remove 5 processing methods from VehicleCommandSystem and VehicleAPI
- Add NavigationIntentBridgeSystemTests
- Fix CarKinem example cascade callers (HeadlessCarKinemApp, CarKinemApp, ScenarioManager)
```
