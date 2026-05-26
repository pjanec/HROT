# Replay Browser Frankenstein — TASK TRACKER

**Reference:** See [TASK-DETAILS.md](./TASK-DETAILS.md) for full task descriptions, success criteria, and DESIGN cross-references.
**Design:** [DESIGN.md](./DESIGN.md)
**Debt:** [DEBT-TRACKER.md](./DEBT-TRACKER.md)
**Onboarding:** [ONBOARDING.md](./ONBOARDING.md)

---

## Phase P1 — Metadata extension + validated group loading

**Goal:** Make `.fdp` recordings self-identifying (ExerciseId + NodeId) and load groups safely.

- [x] **RBF-P1T1** `RecordingMetadata` schema extension [details](./TASK-DETAILS.md#rbf-p1t1--recordingmetadata-schema-extension)
- [x] **RBF-P1T2** `RecordingConfiguration.NodeId` [details](./TASK-DETAILS.md#rbf-p1t2--recordingconfigurationnodeid)
- [x] **RBF-P1T3** `AsyncRecorder` stamps metadata [details](./TASK-DETAILS.md#rbf-p1t3--asyncrecorder-stamps-metadata)
- [x] **RBF-P1T4** `FederatedReplayManager.LoadGroup(string[] paths)` [details](./TASK-DETAILS.md#rbf-p1t4--federatedreplaymanagerloadgroupstring-paths)


## Phase P2 — Federation runtime infrastructure

**Goal:** Replace single-context replay with a manager that owns per-node contexts and coordinates wall-tick seeks.

- [x] **RBF-P2T1** `FederatedReplayManager` time state + `SeekAll` [details](./TASK-DETAILS.md#rbf-p2t1--federatedreplaymanager-time-state--seekall)
- [x] **RBF-P2T2** `FederatedReplayManager` lifecycle + dispose [details](./TASK-DETAILS.md#rbf-p2t2--federatedreplaymanager-lifecycle--dispose)
- [x] **RBF-P2T3** Subsystem wiring: `ReplayBrowserSubsystem` owns a manager [details](./TASK-DETAILS.md#rbf-p2t3--subsystem-wiring-replaybrowsersubsystem-owns-a-manager)


## Phase P3 — Frankenstein synthesis engine

**Goal:** Build a mathematically correct transient `EntityRepository` from authority-filtered slices, with graceful paradox handling.

- [x] **RBF-P3T1** `NetworkIdGuid` helper [details](./TASK-DETAILS.md#rbf-p3t1--networkidguid-helper)
- [x] **RBF-P3T2** `FederatedGuidResolver` [details](./TASK-DETAILS.md#rbf-p3t2--federatedguidresolver)
- [x] **RBF-P3T3** `ScenarioSerializer.DeserializeWith(IGuidResolver)` overload [details](./TASK-DETAILS.md#rbf-p3t3--scenarioserializerdeserializewithiguidresolver-overload)
- [x] **RBF-P3T4** Consensus-mask helper [details](./TASK-DETAILS.md#rbf-p3t4--consensus-mask-helper)
- [x] **RBF-P3T5** `TransientMasterBuilder.Build(manager)` [details](./TASK-DETAILS.md#rbf-p3t5--transientmasterbuilderbuildmanager)
- [x] **RBF-P3T6** Extract `PrimeAppDomainAndSandbox` to shared helper [details](./TASK-DETAILS.md#rbf-p3t6--extract-primeappdomainandsandbox-to-shared-helper)
- [x] **RBF-P3T7** Local-Entities Provider injection in `TransientMasterBuilder` [details](./TASK-DETAILS.md#rbf-p3t7--local-entities-provider-injection-in-transientmasterbuilder)


## Phase P4 — UI binding and paradox visualisation

**Goal:** Operator-facing controls and diagnostic feedback.

- [x] **RBF-P4T1** Multi-file open dialog [details](./TASK-DETAILS.md#rbf-p4t1--multi-file-open-dialog)
- [x] **RBF-P4T2** `FederationPanel` (new ImGui panel) [details](./TASK-DETAILS.md#rbf-p4t2--federationpanel-new-imgui-panel)
- [x] **RBF-P4T3** Subsystem mode swap + repo rebind [details](./TASK-DETAILS.md#rbf-p4t3--subsystem-mode-swap--repo-rebind)
- [x] **RBF-P4T4** Inspector field flagging for `Entity.Null` paradoxes [details](./TASK-DETAILS.md#rbf-p4t4--inspector-field-flagging-for-entitynull-paradoxes)
- [x] **RBF-P4T5** Documentation: severe stutter is expected [details](./TASK-DETAILS.md#rbf-p4t5--documentation-severe-stutter-is-expected)
- [x] **RBF-P4T6** Disable continuous playback in Merged View [details](./TASK-DETAILS.md#rbf-p4t6--disable-continuous-playback-in-merged-view)
- [x] **RBF-P4T7** Disable search in Merged View [details](./TASK-DETAILS.md#rbf-p4t7--disable-search-in-merged-view)


---

## Success-condition coverage map

| SC | Covered by tasks |
|----|------------------|
| **SC-1** Validated multi-file group loading | RBF-P1T1, RBF-P1T2, RBF-P1T3, RBF-P1T4, RBF-P4T1 |
| **SC-2** Mathematically correct ECS synthesis | RBF-P3T1, RBF-P3T2, RBF-P3T3, RBF-P3T4, RBF-P3T5, RBF-P3T6, RBF-P3T7 |
| **SC-3** Graceful relational paradox handling | RBF-P3T2, RBF-P3T3 (incl. inline-array handle + auto-serializer forwarding), RBF-P3T5 (`MissingTargetResolvesToEntityNull`) |
| **SC-4** Flawless gizmo / tool compatibility | RBF-P3T5, RBF-P4T3 (gizmos require no changes; structural) |
| **SC-5** Accurate diagnostic UI feedback | RBF-P4T2, RBF-P4T4 |
| **SC-6** Acceptance of performance degradation | RBF-P4T5 (documentation), RBF-P4T6 (Play disabled in Merged), RBF-P4T7 (Search disabled in Merged) |
