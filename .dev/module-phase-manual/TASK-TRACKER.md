# Task Tracker: Network Architecture Cleanup and Module Phase Manual

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.
**Design:** See [DESIGN.md](./DESIGN.md) for architecture rationale and context.


## Phase 1: Dead Code Purge

**Goal:** Remove all dead code that creates confusion, violates ACL constraints, or pollutes the diagnostic UI.

- [x] **MPM-P1-T01** Delete legacy perception systems (PerceptionBroadphaseSystem, ThreatEvaluationAdapterSystem) [details](./TASK-DETAIL.md#mpm-p1-t01---delete-legacy-perception-systems)
- [x] **MPM-P1-T02** Delete INetworkReplayTarget and strip from translator base classes [details](./TASK-DETAIL.md#mpm-p1-t02---delete-inetworkreplaytarget-and-strip-from-translators)
- [x] **MPM-P1-T03** Delete AutoCycloneTranslators, ReplicationBootstrap, and NetworkDemo project [details](./TASK-DETAIL.md#mpm-p1-t03---delete-autocyclonetranslators-replicationbootstrap-and-networkdemo)


## Phase 2: Descriptor Ordinal Cleanup

**Goal:** Replace all magic integer literals in DescriptorOrdinal properties with named constants.

- [x] **MPM-P2-T01** Extend EDescriptorType enum with missing NED entries [details](./TASK-DETAIL.md#mpm-p2-t01---extend-edescriptortype-enum)
- [x] **MPM-P2-T02** Fix NED translator magic ordinals (EntityMission, EntityMaster, MapEntitySymbol, others) [details](./TASK-DETAIL.md#mpm-p2-t02---fix-ned-translator-magic-ordinals)
- [x] **MPM-P2-T03** Create TimeDescriptorType enum and update five time translators [details](./TASK-DETAIL.md#mpm-p2-t03---create-timedescriptortype-enum-and-update-time-translators)
- [x] **MPM-P2-T04** Create BdcDescriptorType enum and update two BDC translators [details](./TASK-DETAIL.md#mpm-p2-t04---create-bdcdescriptortype-enum-and-update-bdc-translators)


## Phase 3: Network Interface Segregation

**Goal:** Separate INetworkTranslator (base) from IDescriptorTranslator (persistent state); give event translators a clean INetworkEventTranslator contract.

- [x] **MPM-P3-T01** Create INetworkTranslator base interface [details](./TASK-DETAIL.md#mpm-p3-t01---create-inetworktranslator-base-interface)
- [x] **MPM-P3-T02** Refactor IDescriptorTranslator to extend INetworkTranslator [details](./TASK-DETAIL.md#mpm-p3-t02---refactor-idescriptortranslator-to-extend-inetworktranslator)
- [x] **MPM-P3-T03** Create INetworkEventTranslator and update event translator base classes [details](./TASK-DETAIL.md#mpm-p3-t03---create-inetworkeventtranslator-and-update-event-translator-base-classes)
- [x] **MPM-P3-T04** Update ingress/egress systems and remove GetDirectionLabel hack from ArchitectureDiagnosticsPanel [details](./TASK-DETAIL.md#mpm-p3-t04---update-ingressegress-systems-and-diagnostic-panel)


## Phase 4: SystemPhase.Manual

**Goal:** First-class diagnostic visibility for modules using the direct-execution pattern.

- [ ] **MPM-P4-T01** Add SystemPhase.Manual = 255 to enum and guard ExecutePhase [details](./TASK-DETAIL.md#mpm-p4-t01---add-systemphasemanual-to-enum)
- [ ] **MPM-P4-T02** Add RegisterManualSystem to ISystemRegistry and implement ProfiledManualSystemWrapper in SystemScheduler [details](./TASK-DETAIL.md#mpm-p4-t02---add-registermanualsystem-to-isystemregistry-and-implement-in-systemscheduler)
- [ ] **MPM-P4-T03** Update CapturingSystemRegistry in ModuleHostKernel [details](./TASK-DETAIL.md#mpm-p4-t03---update-capturingsystemregistry-in-modulehostkernel)
- [ ] **MPM-P4-T04** Tag four perception systems with [UpdateInPhase(SystemPhase.Manual)] [details](./TASK-DETAIL.md#mpm-p4-t04---tag-perception-systems-with-updateinphasesystephasemanual)
- [ ] **MPM-P4-T05** Refactor AutonomousPerceptionModule to use RegisterManualSystem [details](./TASK-DETAIL.md#mpm-p4-t05---refactor-autonomousperceptionmodule)


## Phase 5: Doctrine Auto-Registration

**Goal:** Eliminate all doctrine behavior-ID magic strings; make the parameter DTO the Single Source of Truth.

- [ ] **MPM-P5-T01** Create DoctrineCategory enum and DoctrineContractAttribute in Hrot.Core [details](./TASK-DETAIL.md#mpm-p5-t01---create-doctrinecategory-and-doctrinecontractattribute)
- [ ] **MPM-P5-T02** Decorate existing parameter DTOs and create empty marker DTOs [details](./TASK-DETAIL.md#mpm-p5-t02---decorate-dtos-and-create-empty-marker-dtos)
- [ ] **MPM-P5-T03** Create DoctrineSchemaDiscovery auto-registration utility [details](./TASK-DETAIL.md#mpm-p5-t03---create-doctrineschemariscovery)
- [ ] **MPM-P5-T04** Replace BehaviorUiSetup and CgfDoctrineSetup manual registrations [details](./TASK-DETAIL.md#mpm-p5-t04---replace-behavioruisetup-and-cgfdoctrinesetup-manual-registrations)
- [ ] **MPM-P5-T05** Rebuild DoctrineCatalog using reflection [details](./TASK-DETAIL.md#mpm-p5-t05---rebuild-doctrinecatalog-using-reflection)
- [ ] **MPM-P5-T06** Update CgfNodes.cs AI tree JSON to use DTO BehaviorId constants [details](./TASK-DETAIL.md#mpm-p5-t06---update-cgfnodescs-to-use-dto-behaviorid-constants)
- [ ] **MPM-P5-T07** Create DoctrineTestHelper and eliminate magic strings from unit tests [details](./TASK-DETAIL.md#mpm-p5-t07---create-doctrinetesthelper-and-update-tests)
