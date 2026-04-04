# Task Tracker — Scenario Editor Pack & HROT Editor Refactoring (`packs-2`)

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.  
**Design:** See [DESIGN.md](./DESIGN.md) for architecture overview and rationale.

---

## Phase 0: Formalize Pack Composite Wrappers

**Goal:** Create named composite `IEcsModule` wrappers grouping existing modules by architectural
tier. Prerequisite for Phase 5 composition roots and the Feature Switch.

- [x] **PACK2-P001** Create Logic Pack composite wrappers (`SimHostCoreLogicPack`, `CgfLogicPack`, `OrchestrationLogicPack`) [details](./TASK-DETAIL.md#pack2-p001--create-logic-pack-composite-wrappers)
- [ ] **PACK2-P002** Create Translator Pack composite wrappers (`ActuatorIntentsEgressPack`, `EntityStatesIngressPack`) [details](./TASK-DETAIL.md#pack2-p002--create-translator-pack-composite-wrappers-for-feature-switch)

---

## Phase 1: Decouple Map Tools from the Network Edge

**Goal:** Strip all DDS/DTO coupling from IG map tools so they emit pure FDP domain events,
making them portable and network-agnostic.

- [ ] **PACK2-D001** Refactor `CreationTool` to emit `SpawnEntityCommand` [details](./TASK-DETAIL.md#pack2-d001--refactor-creationtool-to-emit-spawnentitycommand)
- [ ] **PACK2-D002** Refactor `EditTool` and `RouteEditTool` to emit `UpdateEntityCommand` [details](./TASK-DETAIL.md#pack2-d002--refactor-edittool-and-routeedittool-to-emit-updateentitycommand)
- [ ] **PACK2-D003** Remove network branching from context menus; always emit `DestroyEntityCommand` [details](./TASK-DETAIL.md#pack2-d003--remove-network-branching-from-context-menus-and-delete-hotkeys)
- [ ] **PACK2-D004** Sever `IDdsWriter<CreateEntityRequest>` from `MapCommandController` [details](./TASK-DETAIL.md#pack2-d004--sever-iddswritercreateentityrequest-from-mapcommandcontroller)
- [ ] **PACK2-D005** Create ACL egress translators for Spawn / Update / Destroy commands [details](./TASK-DETAIL.md#pack2-d005--create-acl-egress-translators-for-spawn-update-and-destroy-commands)

---

## Phase 2: Extract the Shared Scenario Interaction Logic Pack

**Goal:** Create `Hrot.ScenarioEditor` — a shared, DDS-free module housing the map tools and
render layers reusable by IG, ExCon, and the new HROT Editor.

- [ ] **PACK2-E001** Scaffold `Hrot.ScenarioEditor` project (new IEcsModule) [details](./TASK-DETAIL.md#pack2-e001--scaffold-hrotscenarioeditor-project)
- [ ] **PACK2-E002** Migrate core interaction tools into `Hrot.ScenarioEditor` [details](./TASK-DETAIL.md#pack2-e002--migrate-core-interaction-tools-into-hrotscenarioeditor)
- [ ] **PACK2-E003** Extract visual rendering layers into `Hrot.ScenarioEditor` [details](./TASK-DETAIL.md#pack2-e003--extract-visual-rendering-layers-into-hrotscenarioeditor)
- [ ] **PACK2-A001** Define `WorldResetEvent`; flush cached entity handles in tools and SelectionManager [details](./TASK-DETAIL.md#pack2-a001--define-worldresetevent-and-hook-into-selection--tool-state)
- [ ] **PACK2-E004** Wire local scenario file operations in `ScenarioEditorModule` [details](./TASK-DETAIL.md#pack2-e004--wire-local-scenario-file-operations-in-scenarioeditormodule)

---

## Phase 3: Formalize Host-Specific UI Packs

**Goal:** Enforce "dumb view" separation; formalize UI packs for IG and ExCon; scaffold the
bespoke HROT Editor UI Pack.

- [ ] **PACK2-U001** Enforce UI-Logic separation — audit and fix residual DDS calls in panels [details](./TASK-DETAIL.md#pack2-u001--enforce-ui-logic-separation-in-existing-panels)
- [ ] **PACK2-U002** Formalize ExCon UI Pack — confirm IExConLogic facade boundary [details](./TASK-DETAIL.md#pack2-u002--formalize-excon-ui-pack)
- [ ] **PACK2-U003** Formalize IG UI Pack — confirm event-driven tool activation in panels [details](./TASK-DETAIL.md#pack2-u003--formalize-ig-ui-pack)
- [ ] **PACK2-U004** Scaffold HROT Editor UI Pack (`ScenarioBrowserPanel`, `EditorToolbarPanel`, etc.) [details](./TASK-DETAIL.md#pack2-u004--scaffold-hrot-editor-ui-pack)

---

## Phase 4: Implement Local Scenario File Operations

**Goal:** Validate Save / Load / New operations on the local `EntityRepository` without DDS,
integrated into the Editor composition.

- [ ] **PACK2-F001** Instantiate purified `ScenarioSerializer` in the Editor composition root [details](./TASK-DETAIL.md#pack2-f001--instantiate-the-purified-serializer-in-the-editor-composition-root)
- [ ] **PACK2-F002** Validate "Load Empty" (New Scenario) integration [details](./TASK-DETAIL.md#pack2-f002--validate-load-empty-in-the-editor-composition)
- [ ] **PACK2-F003** Validate "Save Scenario" round-trip [details](./TASK-DETAIL.md#pack2-f003--validate-save-scenario-round-trip)
- [ ] **PACK2-F004** Validate "Load Scenario" round-trip [details](./TASK-DETAIL.md#pack2-f004--validate-load-scenario-round-trip)

---

## Phase 5: Assemble Composition Roots & Implement the Feature Switch

**Goal:** Build the HROT Editor All-In-One executable and implement the runtime Feature Switch
(Internal FDP SimHost ↔ External HROT SimHost over DDS).

- [ ] **PACK2-C001** Assemble HROT Editor All-In-One composition root (offline state) [details](./TASK-DETAIL.md#pack2-c001--assemble-hrot-editor-all-in-one-composition-root)
- [ ] **PACK2-C002** Feature Switch — eject local Logic Packs (External State) [details](./TASK-DETAIL.md#pack2-c002--implement-feature-switch--eject-local-logic-packs)
- [ ] **PACK2-C003** Feature Switch — snap-in ACL Translator Packs + toggle UI [details](./TASK-DETAIL.md#pack2-c003--implement-feature-switch--snap-in-the-acl-translator-packs)

---

## Phase 6: CGF Subsystem Execution Profile & Headless Integration Tests

**Goal:** Complete the CGF Brain-role deployment profile; add `RunMode.Editor` / `RunMode.Demo`;
add harnesses and four integration test suites for offline editing, Feature Switch RCU, and
distributed Brain/Muscle execution.

- [x] **PACK2-R001** Add `RunMode.Editor` (= 64) and `RunMode.Demo` macro; add `"editor"` / `"demo"` CLI cases; validation guard [details](./TASK-DETAIL.md#pack2-r001--extend-runmode-with-editor-and-demo-update-configuration-validation)
- [ ] **PACK2-R002** Complete `CgfSubsystem.Initialize` with `CgfLogicPack` + `EntityStatesIngressPack(Ingress)` + `ActuatorIntentsEgressPack(Egress)` [details](./TASK-DETAIL.md#pack2-r002--complete-cgfsubsystem-brain-role-pack-installation)
- [ ] **PACK2-R003** Scaffold `CgfHarness` (domain-isolated + shared-domain ctor) and `EditorHarness` (offline, no DDS) [details](./TASK-DETAIL.md#pack2-r003--scaffold-cgfharness-and-editorharness-test-infrastructure)
- [ ] **PACK2-R004** `OfflineEditorIntegrationTests` (IT-1): spawn / edit / delete via memory bus; assert zero DDS writes [details](./TASK-DETAIL.md#pack2-r004--offlineeditorintegrationtests-it-1)
- [ ] **PACK2-R005** `EditorFileIOIntegrationTests` (IT-2) + `FeatureSwitchRcuIntegrationTests` (IT-3) [details](./TASK-DETAIL.md#pack2-r005--editorfileiointegrationtests-it-2-and-featureswitchrcuintegrationtests-it-3)
- [ ] **PACK2-R006** `DistributedBrainMuscleIntegrationTests` (IT-4): CGF + SimHost in shared loopback domain [details](./TASK-DETAIL.md#pack2-r006--distributedbrainmuscleintegrationtests-it-4)
