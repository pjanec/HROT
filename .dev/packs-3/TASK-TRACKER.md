# Task Tracker — Scenario File Support, ACL Hardening & Network DRY Refactor (`packs-3`)

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.
**Design:** See [DESIGN.md](./DESIGN.md) for architecture overview and rationale.

---

## Phase 0: CGF Component Registry Hardening

**Goal:** Replace ad-hoc per-component registration in `CgfApplication` with a centralised
`CgfComponentRegistry`, matching the `SimHostComponentRegistry` pattern.

- [ ] **PACK3-C001** Create `CgfComponentRegistry` and replace ad-hoc registrations in `CgfApplication` [details](./TASK-DETAIL.md#pack3-c001--create-cgfcomponentregistry)

---

## Phase 1: Urban Combat Scenario Extraction & Shared Validation

**Goal:** Enable the Urban Combat demo to function as a data-driven JSON scenario; introduce
shared validation logic; prove the Editor Preview/Rewind and full cluster lifecycle.

- [ ] **PACK3-U001** Extract `UrbanCombatValidator` with `TkbIdentity`-based dynamic entity resolution [details](./TASK-DETAIL.md#pack3-u001--extract-urbancombatvalidator)
- [ ] **PACK3-U002** Simplify `UrbanCombatNewScenario` to delegate `EvaluateTick` to the new validator [details](./TASK-DETAIL.md#pack3-u002--simplify-urbancombatscenario)
- [ ] **PACK3-U003** Editor Preview/Rewind integration test (`EditorPreviewAndSaveIntegrationTests`) [details](./TASK-DETAIL.md#pack3-u003--editor-preview--rewind-integration-test)
- [ ] **PACK3-U004** Urban Combat file lifecycle integration test (auto-extract → full cluster → validate) [details](./TASK-DETAIL.md#pack3-u004--urban-combat-file-lifecycle-integration-test)

---

## Phase 2: Zone Definitions in Scenario Files

**Goal:** Allow scenario files to declare static environment assets (road networks, cylindrical
obstacles) that are loaded before entity spawning. Strict ACL: app-layer DTOs only; FDP
engine receives in-memory structs only.

- [ ] **PACK3-Z001** `ZoneEnvironmentData` ECS singleton + `CarKinematicsSystem` singleton refactor [details](./TASK-DETAIL.md#pack3-z001--zoneenvironmentdata-ecs-singleton--carkinematicssystem-refactor)
- [ ] **PACK3-Z002** App-layer DTOs: `HrotScenarioEnvelopeDto`, `ScenarioHeaderDto`, `ZoneDefinitionDto`, `ZoneObstacleDto`, `HrotJsonOptions` [details](./TASK-DETAIL.md#pack3-z002--application-layer-dtos-for-scenario-envelope)
- [ ] **PACK3-Z003** `IZoneManagerService` + `ZoneManagerService` (load road network + spawn obstacles) [details](./TASK-DETAIL.md#pack3-z003--izonmanagerservice-and-zonemanagerservice)
- [ ] **PACK3-Z004** Custom load handlers: `HrotScenarioLoadHandler` (LoadingLive), `HrotEditLoadHandler` (LoadingEdit) [details](./TASK-DETAIL.md#pack3-z004--custom-load-handlers-hrotscenarioloadhandler-hroteditloadhandler)
- [ ] **PACK3-Z005** `ScenarioFileService.SaveScenario` — serialise full `HrotScenarioEnvelopeDto` including Zones [details](./TASK-DETAIL.md#pack3-z005--scenariofileservice-save-with-zone-support)
- [ ] **PACK3-Z006** Zone scenario load integration test (`ZoneScenarioLoadIntegrationTests`) [details](./TASK-DETAIL.md#pack3-z006--zone-scenario-load-integration-test)

---

## Phase 3: ACL Backdoor Elimination

**Goal:** Remove the hidden `tryGetPrebuilt` side-channel that bypasses the FDP event bus.
Map tools must emit only pure domain events; the egress translator handles DDS translation.

- [ ] **PACK3-A001** Purge `tryGetPrebuilt` delegate field and bypass block from `SpawnEntityCommandEgressTranslator` [details](./TASK-DETAIL.md#pack3-a001--purge-trygetprebuilt-from-spawnentitycommand-egresstranslator)
- [ ] **PACK3-A002** Delete `_prebuiltRequests` cache and `TryDequeuePrebuilt` from `MapCommandController` [details](./TASK-DETAIL.md#pack3-a002--remove-dto-cache-from-mapcommandcontroller)
- [ ] **PACK3-A003** Clean `IgApplication` composition root — remove side-channel lambda wiring [details](./TASK-DETAIL.md#pack3-a003--igapplication-composition-root-cleanup)
- [ ] **PACK3-A004** Fix `AreaAuthoringTool` (and `RouteAuthoringTool`) to use `SpawnEntityCommand.InitialComponents` [details](./TASK-DETAIL.md#pack3-a004--fix-areaauthoringtool-to-use-initialcomponents)
- [ ] **PACK3-A005** ACL verification tests (boundary unit, E2E area authoring, offline editor isolation) [details](./TASK-DETAIL.md#pack3-a005--acl-verification-tests)

---

## Phase 4: NetworkGatewaySystem DRY Refactor

**Goal:** Eradicate the copy-pasted `NetworkGatewaySystem` from the Cyclone transport pack.
The canonical, transport-agnostic implementation lives in `FDP.Toolkit.Replication`.

- [ ] **PACK3-N001** Create canonical `NetworkGatewaySystem` in `FDP.Toolkit.Replication.Systems` [details](./TASK-DETAIL.md#pack3-n001--relocate-networkgatewaysystem-to-fdptoolkitreplication)
- [ ] **PACK3-N002** Delete Cyclone-local clones and legacy `ModuleHost.Core` originals [details](./TASK-DETAIL.md#pack3-n002--delete-clones-and-legacy-originals)
- [ ] **PACK3-N003** Rewire `CycloneNetworkModule` to reference the Replication toolkit system [details](./TASK-DETAIL.md#pack3-n003--rewire-cyclonenetworkmodule)
- [ ] **PACK3-N004** `NetworkGatewaySystem` integration test (SimHost + IG, `AllPeers` handshake, `EntityLifecycle.Active`) [details](./TASK-DETAIL.md#pack3-n004--networkgatewaysystem-integration-test)
