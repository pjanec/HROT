# Task Tracker — Commander-Subordinate Infrastructure

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: Core Component Definitions

**Goal:** Define all new ECS components, enums, and component IDs before touching any existing code.

- [x] **TASK-CS001** TacticalDesignation dual-enum definitions [details](./TASK-DETAIL.md#task-cs001--tacticaldesignation-dual-enum-definitions)
- [x] **TASK-CS002** UnitSubordinate component [details](./TASK-DETAIL.md#task-cs002--unitsubordinate-component)
- [x] **TASK-CS003** UnitRoster component [details](./TASK-DETAIL.md#task-cs003--unitroster-component)
- [x] **TASK-CS004** Component ID registration [details](./TASK-DETAIL.md#task-cs004--component-id-registration)

---

## Phase 2: Formation Component Refactor

**Goal:** Rename and split existing `FormationRoster`/`FormationMember` into generic and formation-specific parts.

- [x] **TASK-CS005** Rename FormationRoster to FormationController [details](./TASK-DETAIL.md#task-cs005--rename-formationroster-to-formationcontroller)
- [x] **TASK-CS006** Rename FormationMember to FormationFollower [details](./TASK-DETAIL.md#task-cs006--rename-formationmember-to-formationfollower)
- [x] **TASK-CS007** Update VehicleCommandSystem for new component names [details](./TASK-DETAIL.md#task-cs007--update-vehiclecommandsystem-for-new-component-names)

---

## Phase 3: Network Anti-Corruption Layer

**Goal:** Update DDS descriptor and translators so the network boundary converts between integer IDs and ECS Entity handles cleanly.

- [x] **TASK-CS008** Extend EntityInfo DDS descriptor [details](./TASK-DETAIL.md#task-cs008--extend-entityinfo-dds-descriptor)
- [x] **TASK-CS009** Remove CommanderId from Fdp.Core.EntityInfo [details](./TASK-DETAIL.md#task-cs009--remove-commanderid-from-fdpcoreentityinfo)
- [x] **TASK-CS010** Update EntityInfoEgressTranslator [details](./TASK-DETAIL.md#task-cs010--update-entityinfoegresstranslator)
- [x] **TASK-CS011** Update EntityInfoIngressTranslator (with deferred queue) [details](./TASK-DETAIL.md#task-cs011--update-entityinfoingresstranslator-with-deferred-queue)

---

## Phase 4: Scenario Serialization

**Goal:** Make command hierarchy survive scenario save/load cycles without persisting volatile entity handles.

- [x] **TASK-CS012** InitialUnitSubordinateIntent component [details](./TASK-DETAIL.md#task-cs012--initialunitsubordinateintent-component)
- [x] **TASK-CS013** UnitSubordinateTranslator (IEntityScenarioTranslator) [details](./TASK-DETAIL.md#task-cs013--unitsubordinatetranslator-ientityscenariotranslator)
- [x] **TASK-CS014** GenesisMaterializationSystem: MaterializeUnitSubordinate [details](./TASK-DETAIL.md#task-cs014--genesismaterializationsystem-materializeunitsubordinate)

---

## Phase 5: Runtime Hierarchy Management

**Goal:** Provide a single, event-driven system that is the sole authority for mutating command relationships at runtime.

- [x] **TASK-CS015** CmdAssignSubordinate and CmdRemoveSubordinate events [details](./TASK-DETAIL.md#task-cs015--cmdassignsubordinate-and-cmdremovesubordinate-events)
- [x] **TASK-CS016** UnitHierarchySystem [details](./TASK-DETAIL.md#task-cs016--unithierarchysystem)

---

## Phase 6: ORBAT UI Drag-Drop Subordination

**Goal:** Allow operators to reassign command hierarchy via drag-drop in both the offline Editor and the distributed ExCon.

- [x] **TASK-CS017** OrbatNodeViewModel: CanAcceptSubordinates flag [details](./TASK-DETAIL.md#task-cs017--orbatnodeviewmodel-canacceptsubordinates-flag)
- [x] **TASK-CS018** IOrbatController: subordination methods [details](./TASK-DETAIL.md#task-cs018--iorbatcontroller-subordination-methods)
- [x] **TASK-CS019** SharedOrbatPanel: subordination drag-drop [details](./TASK-DETAIL.md#task-cs019--sharedorbatpanel-subordination-drag-drop)
- [x] **TASK-CS020** EditorOrbatAdapter full implementation [details](./TASK-DETAIL.md#task-cs020--editororbatadapter-full-implementation)
- [x] **TASK-CS021** ExConOrbatAdapter full implementation [details](./TASK-DETAIL.md#task-cs021--exconorbatadapter-full-implementation)

---

## Phase 3 (cross-cut): ExCon Patch Routing

**Goal:** Ensure the ExCon's JSON `CommanderId` patch reaches `UnitHierarchySystem` after the ECS field is removed.

- [x] **TASK-CS024** EntityDataAttributeInstaller CommanderId interception [details](./TASK-DETAIL.md#task-cs024--entitydataattributeinstaller-commanderid-interception)

---

## Phase 4 (addendum): Scenario Load Correctness

**Goal:** Ensure loaded scenarios arrive in the live cluster with all commander network IDs intact and all intent components resolved before physical simulation starts.

- [x] **TASK-CS026** Cluster load handlers: InitialUnitSubordinateIntent drain guard [details](./TASK-DETAIL.md#task-cs026--cluster-load-handlers-initialunitsubordinateintent-drain-check)
- [x] **TASK-CS027** StagingEntityExtractor: remap CommanderNetworkId on load [details](./TASK-DETAIL.md#task-cs027--stagingentityextractor-remap-commandernetworkid-on-load)

---

## Phase 7: TKB Composite Definition Update

**Goal:** Replace the string-based `RoleTag` in TKB blueprints with the typed `TacticalDesignation` enum.

- [x] **TASK-CS022** TkbChildSlot: replace RoleTag with Designation [details](./TASK-DETAIL.md#task-cs022--tkbchildslot-replace-roletag-with-designation)

---

## Cross-Cutting

**Goal:** Keep the test suite green throughout the refactor and validate distributed boundary correctness.

- [x] **TASK-CS023** Component registry integration test update [details](./TASK-DETAIL.md#task-cs023--component-registry-integration-test-update)
- [x] **TASK-CS025** Integration tests: distributed boundary validation [details](./TASK-DETAIL.md#task-cs025--integration-tests-distributed-boundary-validation)
