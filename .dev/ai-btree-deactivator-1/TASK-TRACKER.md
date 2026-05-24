# Task Tracker — EQS Sensor Lifecycle / BTree Hybrid Lifecycle Hook

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: FastBTree Library (Fbt.Kernel — isolated)

**Goal:** Add complete deactivator support to FastBTree with proof-of-concept tests; no engine
dependencies touched.

- [ ] **TASK-EQL-001** NodeDeactivatorDelegate + BTreeDeactivatorAttribute [details](./TASK-DETAIL.md#task-eql-001--nodedeactivatordelegate-and-btreedeactivatorattribute)
- [ ] **TASK-EQL-002** ActionRegistry deactivator support [details](./TASK-DETAIL.md#task-eql-002--actionregistry-deactivator-support)
- [ ] **TASK-EQL-003** Interpreter delta tracking + deactivator invocation [details](./TASK-DETAIL.md#task-eql-003--interpreter-deactivator-array-and-delta-tracking)

---

## Phase 2: Roslyn Generator Extension (Fdp.Toolkits.Analyzers)

**Goal:** Automate deactivator registration through the existing source generator so no
manual ActionRegistry wiring is ever needed.

- [ ] **TASK-EQL-004** BTreeActionGenerator deactivator detection and emission [details](./TASK-DETAIL.md#task-eql-004--btreeactiongenerator-deactivator-detection-and-emission)

---

## Phase 3: Engine Integration

**Goal:** Replace all existing manual channel-cleanup workarounds with deactivators, and fill
the gaps where cleanup was previously absent.

- [ ] **TASK-EQL-005** WeaponChannel deactivator — InsurgentNodes.Action_AimAndFire [details](./TASK-DETAIL.md#task-eql-005--weaponchannel-deactivator-for-insurgentnodesaction_aimandfire)
- [ ] **TASK-EQL-006** LocomotionChannel deactivator — HillAttackTankNodes.Action_CreepToAndBeyondSlot [details](./TASK-DETAIL.md#task-eql-006--locomotionchannel-deactivator-for-hillattacktanknodesaction_creeptoandbeyondslot)
- [ ] **TASK-EQL-007** WeaponChannel deactivator — HillAttackTankNodes.Action_AimAndFireSpecific [details](./TASK-DETAIL.md#task-eql-007--weaponchannel-deactivator-for-hillattacktanknodesaction_aimandFirespecific)
- [ ] **TASK-EQL-008** EqsRequestId deactivator — HillAttackCommanderNodes.Action_RequestAreaQuery [details](./TASK-DETAIL.md#task-eql-008--eqsrequestid-deactivator-for-hillattackcommandernodes-action_requestareaquery)

---

## Phase 5: AOT Bit-Flag Optimization (post Phase 3)

**Goal:** Replace the temporary parallel-delegate-array with an AOT-compiled `IsResourceOwning`
bit baked into the `BehaviorTreeBlob`, achieving L1 cache locality and eliminating the
conditional `NodeType` guard from the hot path.

- [ ] **TASK-EQL-009** NodeDefinition bit-flag layout + temporary Interpreter patching [details](./TASK-DETAIL.md#task-eql-009--nodedefinition-bit-flag-layout-and-temporary-interpreter-patching)
- [ ] **TASK-EQL-010** AOT compilation pipeline — TreeCompiler + BTreeBuilder [details](./TASK-DETAIL.md#task-eql-010--aot-compilation-pipeline)
- [ ] **TASK-EQL-011** Binary serialization versioning + V1 legacy fallback [details](./TASK-DETAIL.md#task-eql-011--binary-serialization-versioning-and-v1-legacy-fallback)
- [ ] **TASK-EQL-012** Interpreter cleanup + editor integration [details](./TASK-DETAIL.md#task-eql-012--interpreter-cleanup-and-editor-integration)
