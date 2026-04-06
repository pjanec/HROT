# Task Tracker — Shared UI Library & Hrot.Editor Feature Completion (`edit-1`)

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.
**Design:** See [DESIGN.md](./DESIGN.md) for architecture overview and rationale.

---

## Phase 0: `Hrot.UI.Common` — Shared Library Foundation

**Goal:** Create the new shared ImGui project and define all Port interfaces, shared DTOs,
`DoctrineCatalog`, and `DoctrineRegistry` extension — the contracts every other phase depends on.

- [x] **EDIT1-L001** Create `Hrot.UI.Common` project + all nine Facade interfaces + shared DTOs [details](./TASK-DETAIL.md#edit1-l001--create-hrotuicommon-project--all-facade-interfaces)
- [x] **EDIT1-L002** `DoctrineCatalog` in `Hrot.Map.Definitions` (TKB-to-doctrine mapping) [details](./TASK-DETAIL.md#edit1-l002--doctrinecatalog-in-hrotmapdefinitions)
- [x] **EDIT1-L003** `DoctrineRegistry.GetRegisteredNames()` + `TryGetId()` methods [details](./TASK-DETAIL.md#edit1-l003--doctrineregistrygetregisterednames)

---

## Phase 1: Migrate Core Panels to `Hrot.UI.Common`

**Goal:** Move `SpawnerPanel`, `MissionPanel`, and `ConfigPanel` from `Hrot.ExCon` to the
shared library, replacing `IExConLogic` dependencies with focused Port interfaces.

- [x] **EDIT1-P001** Migrate `SpawnerPanel` → `Hrot.UI.Common` (wire to `ISpawnController`) [details](./TASK-DETAIL.md#edit1-p001--migrate-spawnerpanel-to-hrotuicommon)
- [x] **EDIT1-P002** Migrate `MissionPanel` → `Hrot.UI.Common` (dynamic doctrine catalog via `IMissionEditorService`) [details](./TASK-DETAIL.md#edit1-p002--migrate-missionpanel-with-dynamic-doctrine-catalog)
- [x] **EDIT1-P003** Migrate `ConfigPanel` → `Hrot.UI.Common` (wire to `IMapConfigController`) [details](./TASK-DETAIL.md#edit1-p003--migrate-configpanel-to-hrotuicommon)

---

## Phase 2: New Shared Panels

**Goal:** Create the four panels that have no ExCon predecessor — `SharedOrbatPanel` (with
embarkation drag-and-drop), `PreviewPanel`, `ZoneEditorPanel`, and `SharedContextMenuPopulator`.

- [x] **EDIT1-N001** `SharedOrbatPanel` with ImGui embarkation drag-and-drop [details](./TASK-DETAIL.md#edit1-n001--sharedorbatpanel-with-embarkation-drag-and-drop)
- [x] **EDIT1-N002** `PreviewPanel` (`IPreviewController` — Edit/Preview toggle) [details](./TASK-DETAIL.md#edit1-n002--previewpanel-ipreviewcontroller)
- [x] **EDIT1-N003** `ZoneEditorPanel` (`IZoneAuthoringController` — road network + obstacle authoring) [details](./TASK-DETAIL.md#edit1-n003--zoneeditorpanel-izoneauthoringcontroller)
- [x] **EDIT1-N004** `SharedContextMenuPopulator` + `IEntityActionController` (entity + empty-map menus) [details](./TASK-DETAIL.md#edit1-n004--sharedcontextmenupopulator--ientityactioncontroller)

---

## Phase 3: New Domain Events

**Goal:** Define the pure FDP domain commands (`EmbarkEntityCommand`, `SeedTargetCommand`,
`SpawnZoneObstacleCommand`, `UpdateZoneConfigCommand`) needed by all Editor authoring systems.

- [x] **EDIT1-E001** `EmbarkEntityCommand` + `DisembarkEntityCommand` (FDP.Toolkit.Behavior) [details](./TASK-DETAIL.md#edit1-e001--embarkentitycommand-and-disembarkentitycommand)
- [x] **EDIT1-E002** `SeedTargetCommand` (FDP.Toolkit.Perception) [details](./TASK-DETAIL.md#edit1-e002--seedtargetcommand)
- [x] **EDIT1-E003** `SpawnZoneObstacleCommand` + `UpdateZoneConfigCommand` (Hrot.Map.Common) [details](./TASK-DETAIL.md#edit1-e003--spawnzoneobstaclecommand-and-updatezoneconfigcommand)
- [x] **EDIT1-E004** Register all new events in `CognitiveComponentRegistry`, `CombatComponentRegistry`, `HrotSharedComponentRegistry` [details](./TASK-DETAIL.md#edit1-e004--register-new-events-in-component-registries)

---

## Phase 4: Hrot.Editor Adapters & ECS Systems

**Goal:** Implement all Editor-specific adapter classes (8) and execution systems / rendering
layers (4) that translate UI intents into domain events and ECS mutations.

- [x] **EDIT1-A001** `EditorSpawnAdapter` (`ISpawnController` — tool push adapter) [details](./TASK-DETAIL.md#edit1-a001--editorspawnadapter-ispawncontroller)
- [x] **EDIT1-A002** `EditorMissionService` (`IMissionEditorService` — doctrine filtering + TAP commit) [details](./TASK-DETAIL.md#edit1-a002--editormissionservice-imissioneditorservice)
- [x] **EDIT1-A003** `EditorOrbatAdapter` (`IOrbatDataProvider` + `IOrbatController` + embark intents) [details](./TASK-DETAIL.md#edit1-a003--editororbatadapter-iorbatdataprovider--iorbatcontroller)
- [x] **EDIT1-A004** `EditorMapPickAdapter` (`IMapPickService` — location, entity, area picks) [details](./TASK-DETAIL.md#edit1-a004--editormappickadapter-imappickservice)
- [x] **EDIT1-A005** `EditorZoneAdapter` (`IZoneAuthoringController`) [details](./TASK-DETAIL.md#edit1-a005--editorzonéadapter-izoneauthoringcontroller)
- [x] **EDIT1-A006** `EditorEntityContextMenuHandler` (multi-select target seeding + rename + edit) [details](./TASK-DETAIL.md#edit1-a006--editorentitycontextmenuhandler-ientitycontextmenuhandler)
- [x] **EDIT1-A007** `EditorPreviewAdapter` (`IPreviewController` — offline snapshot/rewind) [details](./TASK-DETAIL.md#edit1-a007--editorpreviewadapter-ipreviewcontroller)
- [x] **EDIT1-A008** `EditorMapConfigAdapter` (`IMapConfigController` — direct singleton mutation) [details](./TASK-DETAIL.md#edit1-a008--editormapconfigadapter-imapconfigcontroller)
- [x] **EDIT1-A009** `EditorCargoSystem` (embark/disembark ECS execution + capacity check) [details](./TASK-DETAIL.md#edit1-a009--editorcargosystem)
- [x] **EDIT1-A010** `EditorPerceptionSetupSystem` (`SeedTargetCommand` execution, unsafe TargetMemory) [details](./TASK-DETAIL.md#edit1-a010--editorperceptionsetupsystem)
- [x] **EDIT1-A011** `EditorZoneAuthoringSystem` (obstacle ECS spawn + road network singleton swap) [details](./TASK-DETAIL.md#edit1-a011--editorzoneauthoringsystem)
- [x] **EDIT1-A012** `PerceptionMapLayer` (`IMapLayer` — TargetMemory link visualization) [details](./TASK-DETAIL.md#edit1-a012--perceptionmaplayer-imaplayer)

---

## Phase 5: Hrot.Editor Composition Root Wiring

**Goal:** Connect every panel, adapter, system, and rendering layer together in the Editor's
application startup; add project reference to `Hrot.UI.Common`; wire zone save pipeline.

- [x] **EDIT1-W002** `ScenarioFileService` zone save — inject `IZoneManagerService`, populate `HrotScenarioEnvelopeDto.Zones` [details](./TASK-DETAIL.md#edit1-w002--scenariofileservice-zone-save-integration)
- [x] **EDIT1-W001** `Hrot.Editor` full composition root wiring (systems, adapters, panels, canvas, window manager) [details](./TASK-DETAIL.md#edit1-w001--hroteditor-full-composition-root)

---

## Phase 6: ExCon Adapters

**Goal:** Make `Hrot.ExCon` consume the shared panels from `Hrot.UI.Common` via network-aware
NED adapters — completing DRY for the C2 subsystem.

- [x] **EDIT1-X001** `ExConOrbatAdapter` (`IOrbatDataProvider` + `IOrbatController` via `IDerRepo`) [details](./TASK-DETAIL.md#edit1-x001--exconorbatadapter-iorbatdataprovider--iorbatcontroller)
- [x] **EDIT1-X002** `ExConLogic : ISpawnController` declaration (zero logic change) [details](./TASK-DETAIL.md#edit1-x002--exconlogic-implements-ispawncontroller)
- [x] **EDIT1-X003** `MissionEditorService` NED adapter — `GetAvailableBehaviors` via `DoctrineCatalog` [details](./TASK-DETAIL.md#edit1-x003--missioneditorservice-ned-adapter--dynamic-doctrine-filter)
- [x] **EDIT1-X004** ExCon composition root: wire all shared panels to NED adapters [details](./TASK-DETAIL.md#edit1-x004--excon-composition-root-wire-shared-panels)
- [x] **EDIT1-X005** ExCon `ContextMenuLogic` refactor — `JsonContextMenuBuilder` + `ExConEntityActionAdapter` + `SharedContextMenuPopulator` [details](./TASK-DETAIL.md#edit1-x005--excon-contextmenulogic-refactor-via-sharedcontextmenupopulator)

---

## Phase 7: Headless Integration Tests

**Goal:** Prove every new authoring feature in a deterministic, GPU-free, DDS-free test suite
running inside `EditorHarness`.

- [x] **EDIT1-T001** Embarkation & cargo tests (valid embark, capacity limit, disembark restore) [details](./TASK-DETAIL.md#edit1-t001--embarkation--cargo-integration-tests)
- [x] **EDIT1-T002** Target memory seeding tests (single, N-to-1, 1-to-N) [details](./TASK-DETAIL.md#edit1-t002--target-memory-seeding-integration-tests)
- [x] **EDIT1-T003** Zone obstacle authoring + full save pipeline tests [details](./TASK-DETAIL.md#edit1-t003--zone-obstacle-authoring--save-pipeline-tests)
- [x] **EDIT1-T004** Doctrine catalog filtering tests (per TkbType + registry cross-check) [details](./TASK-DETAIL.md#edit1-t004--doctrine-catalog-filtering-tests)
