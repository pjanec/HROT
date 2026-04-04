# Task Tracker — Logic Packs & Translator Packs Refactoring

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.  
**Design:** See [DESIGN.md](./DESIGN.md) for architecture overview and rationale.

---

## Phase 1: NavigationStatus CQRS — Fix RouteContextSystem

**Goal:** Decouple `RouteContextSystem` from Muscle-tier `NavState` so it works on Brain-only
nodes in a distributed cluster.

- [x] **PACK-N001** Extend NavigationStatus with ProgressS field [details](./TASK-DETAIL.md#pack-n001--extend-navigationstatus-with-progresss)
- [x] **PACK-N002** Populate ProgressS in NavigationExecutionSystem [details](./TASK-DETAIL.md#pack-n002--populate-progresss-in-navigationexecutionsystem)
- [x] **PACK-N003** Update NavigationStatus network translators for ProgressS [details](./TASK-DETAIL.md#pack-n003--update-navigationstatus-network-translators-for-progresss)
- [x] **PACK-N004** Refactor RouteContextSystem to Brain-only query [details](./TASK-DETAIL.md#pack-n004--refactor-routecontextsystem-brain-only-query)

---

## Phase 2: Module Realignment

**Goal:** Ensure every system executes on the node tier where its required ECS components reside.

- [x] **PACK-M001** Relocate HsmDamageBridgeSystem to CognitiveRuntimeModule [details](./TASK-DETAIL.md#pack-m001--relocate-hsmdamagebridgesystem-to-cognitiveruntimemodule)
- [x] **PACK-M002** Delete ApcMobilityTriggerSystem; absorb logic into HealthApplicationSystem [details](./TASK-DETAIL.md#pack-m002--delete-apcmobilitytriggersystem-absorb-logic-into-healthapplicationsystem)

---

## Phase 3: Enforce the Intent Bus

**Goal:** Route all vehicle movement requests through `NavigationIntent`; retire the legacy
`Cmd*` event backdoor.

- [x] **PACK-I001** Refactor PersonalRouteAuthoringSystem to use NavigationIntent [details](./TASK-DETAIL.md#pack-i001--refactor-personalrouteauthoringsystem-to-use-navigationintent)
- [x] **PACK-I002** Refactor SimHostVisualization right-click to use NavigationIntent [details](./TASK-DETAIL.md#pack-i002--refactor-simhostvisualization-right-click-to-use-navigationintent)
- [x] **PACK-I003** Remove legacy Cmd* movement commands from VehicleCommandSystem [details](./TASK-DETAIL.md#pack-i003--remove-legacy-commands-from-vehiclecommandsystem)

---

## Phase 4: Anti-Corruption Layer — Pluggability Violations

**Goal:** Remove residual direct DDS/JSON coupling from Logic Pack systems so the network layer
is a true plugin.

- [x] **PACK-P001** Split MissionControlRequestSystem into Translator + Logic [details](./TASK-DETAIL.md#pack-p001--split-missioncontrolrequestsystem-into-translator--logic)
- [x] **PACK-P002** Extract Create/DeleteEntityRequestSystem out of SimHostModule [details](./TASK-DETAIL.md#pack-p002--extract-spawning-request-systems-out-of-simhostmodule)
- [x] **PACK-P003** Strip NetworkEntityMap from HitResolutionSystem and AimAndFireExecutor [details](./TASK-DETAIL.md#pack-p003--strip-networkentitymap-from-hitresolutionsystem-and-aimandFireexecutor)
- [x] **PACK-P004** Relocate UpdateEntityDescriptorRequestSystem to Replication.Ingress [details](./TASK-DETAIL.md#pack-p004--relocate-updateentitydescriptorrequestsystem-to-replicationingress-namespace)

---

## Phase 5: Orchestration Domain CQRS Cleanup

**Goal:** `ClusterMaster` and `ClusterUiCache` operate exclusively via `FdpEventBus`. DDS
fallback paths are deleted with no backward compatibility.

- [x] **PACK-C001** Purify ClusterMaster — remove DDS constructors and fallback paths [details](./TASK-DETAIL.md#pack-c001--purify-clustermaster-remove-dds-constructors-and-fallback-paths)
- [x] **PACK-C002** Purify ClusterUiCache and create OrchestrationObserverTranslator [details](./TASK-DETAIL.md#pack-c002--purify-clusteruicache--create-orchestrationobservertranslator)

---

## Phase 6: ExCon Egress Anti-Corruption Layer

**Goal:** Eradicate all `DdsWriter<T>` and `System.Text.Json` references from ExCon UI panels
and services; all outbound commands from ExCon travel exclusively via `FdpEventBus`.

- [ ] **PACK-E001** Eradicate DdsWriter from ClusterScenarioPanel; create ClusterOpEgressTranslator [details](./TASK-DETAIL.md#pack-e001--eradicate-ddswriter-from-clusterscenariiopanel)
- [ ] **PACK-E002** Eradicate IDdsWriter from MissionEditorService; create MissionControlEgressTranslator [details](./TASK-DETAIL.md#pack-e002--eradicate-iddswriter-from-missioneditorservice)

---

## Phase 7: Remaining Combat and Perception ACL Leaks

**Goal:** Eliminate (a) a combat event carrying a network ID on the in-process bus, (b) a
Muscle-tier perception system mutating a Brain-tier component, and (c) ECS components that
embed raw DDS-generated structs.

- [ ] **PACK-D001** Purify DamageAssessedEvent — replace long HitEntityId with Entity HitEntity [details](./TASK-DETAIL.md#pack-d001--purify-damageassessedevent)
- [ ] **PACK-A001** Fix AudioPerceptionSystem split-brain — define TargetHeardEvent, extend ThreatEvaluationSystem [details](./TASK-DETAIL.md#pack-a001--fix-audioperceptionsystem-split-brain)
- [ ] **PACK-M003** Remove DDS structs from ECS components — replace EntityMissionHolder and IgMissionHolder with ActiveMissionPlan POCO [details](./TASK-DETAIL.md#pack-m003--remove-dds-structs-from-ecs-components-mission-holders)
