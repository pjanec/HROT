# Task Tracker: CGF Scenario Serialization Correctness (cgf-scn-2)

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: DataPolicy Cleanup and Execution-State Exclusion

**Goal:** Fix a misleading XML comment and prevent execution-tier components from appearing
in scenario JSON.

- [ ] **TASK-S101** Fix DataPolicy.NoSave/NoRecord XML Comments [details](./TASK-DETAIL.md#task-s101-fix-datapolicynosave-xml-comment)
- [ ] **TASK-S102** Add DataPolicy.NoSave to Execution Channel Components [details](./TASK-DETAIL.md#task-s102-add-datapolicynosave-to-execution-channel-components)
- [ ] **TASK-S103** Add DataPolicy.NoSave to Brain Execution Components [details](./TASK-DETAIL.md#task-s103-add-datapolicynosave-to-brain-execution-components)
- [ ] **TASK-S104** Add DataPolicy.NoSave to Transient Perception Components [details](./TASK-DETAIL.md#task-s104-add-datapolicynosave-to-transient-perception-components)
- [ ] **TASK-S105** Delete WeaponChannelTranslator and Unregister It [details](./TASK-DETAIL.md#task-s105-delete-weaponchanneltranslator-and-unregister-it)

---

## Phase 2: MissionPlan Scenario Serialization

**Goal:** Persist active mission plans (e.g., FireAtTarget) through scenario save/load.

- [ ] **TASK-S201** Implement MissionPlanTranslator [details](./TASK-DETAIL.md#task-s201-implement-missionplantranslator)
- [ ] **TASK-S202** Register MissionPlanTranslator at All Serializer Sites [details](./TASK-DETAIL.md#task-s202-register-missionplantranslator-at-all-serializer-sites)

---

## Phase 3: FdpAutoSerializer Upgrade for Unmanaged Memory Layouts

**Goal:** Teach the auto-serializer to correctly iterate fixed buffers and InlineArrays.

- [ ] **TASK-S301** FdpAutoSerializer - fixed Buffer Expression Trees [details](./TASK-DETAIL.md#task-s301-fdpautoserializer-fixed-buffer-expression-trees)
- [ ] **TASK-S302** FdpAutoSerializer - InlineArray Expression Trees [details](./TASK-DETAIL.md#task-s302-fdpautoserializer-inlinearray-expression-trees)

---

## Phase 4: Intent Components for Cross-Entity Reference Safety

**Goal:** Prevent dangling-pointer bugs during distributed genesis for components with
cross-entity references.

- [ ] **TASK-S401** Define Intent DTO Components [details](./TASK-DETAIL.md#task-s401-define-intent-dto-components)
- [ ] **TASK-S402** Translators for VisHierarchyNode, IsEmbarkedTag, PersonalRouteRef [details](./TASK-DETAIL.md#task-s402-translators-for-vishierarchynode-isembarkedtag-personalrouteref)
- [ ] **TASK-S403** Update PassengerBufferTranslator to Emit Intent [details](./TASK-DETAIL.md#task-s403-update-passengerbuffertranslator-to-emit-intent)
- [ ] **TASK-S404** Implement GenesisMaterializationSystem [details](./TASK-DETAIL.md#task-s404-implement-genesismaterializationsystem)
- [ ] **TASK-S405** Patch StagingEntityExtractor for Intent NetworkId Remapping [details](./TASK-DETAIL.md#task-s405-patch-stagingentityextractor-for-intent-networkid-remapping)
- [ ] **TASK-S406** Refactor TargetMemoryTranslator to Emit Intent [details](./TASK-DETAIL.md#task-s406-refactor-targetmemorytranslator-to-emit-initialtargetsintent)

---

## Phase 5: Checkpoint Event Preservation

**Goal:** Persist in-flight FDP events into binary checkpoint files for complete state restoration.

- [ ] **TASK-S501** Add PopulateCurrentStreams to FdpEventBus [details](./TASK-DETAIL.md#task-s501-add-populatecurrentstreams-to-fdpeventbus)
- [ ] **TASK-S502** Update RecorderSystem.WriteEvents with Buffer-Selection Flag [details](./TASK-DETAIL.md#task-s502-update-recordersystemwriteevents-with-buffer-selection-flag)
- [ ] **TASK-S503** Wire EventAccumulator into ReferenceCheckpointHandler [details](./TASK-DETAIL.md#task-s503-wire-eventaccumulator-into-referencecheckpointhandler)
- [ ] **TASK-S504** Patch CheckpointIOWorker to Pass Event Bus [details](./TASK-DETAIL.md#task-s504-patch-checkpointioworker-to-pass-event-bus)
