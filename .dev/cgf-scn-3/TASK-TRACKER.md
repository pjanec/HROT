# Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions

---

## Phase 1: Core ECS Correctness

**Goal:** Fix the four immediate implementation bugs that cause mission data loss and scenario pollution.

- [x] **TASK-S301** Fix SetManagedComponent/RemoveManagedComponent for ActiveMissionPlan [details](./TASK-DETAIL.md#task-s301--fix-setmanagedcomponent--removemanagedcomponent-for-activemissionplan)
- [x] **TASK-S302** Fix InlineArray Span mutation in TryBuildQueue [details](./TASK-DETAIL.md#task-s302--fix-inlinearray-span-mutation-in-trybuildqueue)
- [x] **TASK-S303** Add DataPolicy.NoSave to BrainBlackboard [details](./TASK-DETAIL.md#task-s303--add-datapolicynosave-to-brainblackboard)
- [x] **TASK-S304** Fix SteppingTimeController.GetMode() [details](./TASK-DETAIL.md#task-s304--fix-steppingtimecontrollergetmode)

---

## Phase 2: CGF Multi-Phase Architecture

**Goal:** Split CgfLogicPack and its sub-modules into explicit Input/Simulation phase groups for correct distributed and editor behavior.

- [x] **TASK-S305** MissionControlModule two-group registration overload [details](./TASK-DETAIL.md#task-s305--missioncontrolmodule-two-group-registration-overload)
- [x] **TASK-S306** CgfLogicPack two-group registration overload [details](./TASK-DETAIL.md#task-s306--cgflogicpack-two-group-registration-overload)
- [x] **TASK-S307** CgfInputGroupAdapter in Hrot.Common [details](./TASK-DETAIL.md#task-s307--cgfinputgroupadapter-in-hrotcommon)
- [x] **TASK-S308** CgfSubsystem registration update [details](./TASK-DETAIL.md#task-s308--cgfsubsystem-registration-update)

---

## Phase 3: Editor Composition Root

**Goal:** Fix the broken module registration in EditorSubsystem/EditorHarness and replace SteppingTimeController with MasterSyncController for correct authoring/preview time modes.

- [ ] **TASK-S309** EditorSubsystem system group wiring [details](./TASK-DETAIL.md#task-s309--editorsubsystem-system-group-wiring)
- [ ] **TASK-S310** EditorSubsystem MasterSyncController replacement [details](./TASK-DETAIL.md#task-s310--editorsubsystem-mastersyncontroller-replacement)
- [ ] **TASK-S311** EditorPreviewController time mode wiring [details](./TASK-DETAIL.md#task-s311--editorpreviewcontroller-time-mode-wiring)
- [ ] **TASK-S312** EditorHarness fix [details](./TASK-DETAIL.md#task-s312--editorharness-fix)

---

## Phase 4: Distributed Load Safety

**Goal:** Fix silent network-ID corruption in multi-part entity loads during distributed scenario launch.

- [ ] **TASK-S313** StagingEntityExtractor child entity remapping [details](./TASK-DETAIL.md#task-s313--stagingentityextractor-child-entity-remapping)
