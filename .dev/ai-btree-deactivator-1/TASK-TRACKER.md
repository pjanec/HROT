# Task Tracker — EQS Sensor Lifecycle / BTree Hybrid Lifecycle Hook

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions.

---

## Phase 1: FastBTree Library (Fbt.Kernel — isolated)

**Goal:** Add complete deactivator support to FastBTree with proof-of-concept tests; no engine
dependencies touched.

- [x] **TASK-EQL-001** NodeDeactivatorDelegate + BTreeDeactivatorAttribute [details](./TASK-DETAIL.md#task-eql-001--nodedeactivatordelegate-and-btreedeactivatorattribute) *(BATCH-01)*
- [x] **TASK-EQL-002** ActionRegistry deactivator support [details](./TASK-DETAIL.md#task-eql-002--actionregistry-deactivator-support) *(BATCH-01 + BATCH-02)*
- [x] **TASK-EQL-003** Interpreter delta tracking + deactivator invocation [details](./TASK-DETAIL.md#task-eql-003--interpreter-deactivator-array-and-delta-tracking) *(BATCH-01)*

---

## Phase 2: Roslyn Generator Extension (Fdp.Toolkits.Analyzers)

**Goal:** Automate deactivator registration through the existing source generator so no
manual ActionRegistry wiring is ever needed.

- [x] **TASK-EQL-004** BTreeActionGenerator deactivator detection and emission [details](./TASK-DETAIL.md#task-eql-004--btreeactiongenerator-deactivator-detection-and-emission) *(BATCH-02)*

---

## Phase 3: Engine Integration

**Goal:** Replace all existing manual channel-cleanup workarounds with deactivators, and fill
the gaps where cleanup was previously absent.

- [x] **TASK-EQL-005** WeaponChannel deactivator — InsurgentNodes.Action_AimAndFire [details](./TASK-DETAIL.md#task-eql-005--weaponchannel-deactivator-for-insurgentnodesaction_aimandfire) *(BATCH-03)*
- [x] **TASK-EQL-006** LocomotionChannel deactivator — HillAttackTankNodes.Action_CreepToAndBeyondSlot [details](./TASK-DETAIL.md#task-eql-006--locomotionchannel-deactivator-for-hillattacktanknodesaction_creeptoandbeyondslot) *(BATCH-03)*
- [x] **TASK-EQL-007** WeaponChannel deactivator — HillAttackTankNodes.Action_AimAndFireSpecific [details](./TASK-DETAIL.md#task-eql-007--weaponchannel-deactivator-for-hillattacktanknodesaction_aimandFirespecific) *(BATCH-03)*
- [x] **TASK-EQL-008** EqsRequestId deactivator — HillAttackCommanderNodes.Action_RequestAreaQuery [details](./TASK-DETAIL.md#task-eql-008--eqsrequestid-deactivator-for-hillattackcommandernodes-action_requestareaquery) *(BATCH-03)*

---

## Phase 5: AOT Bit-Flag Optimization (post Phase 3)

**Goal:** Replace the temporary parallel-delegate-array with an AOT-compiled `IsResourceOwning`
bit baked into the `BehaviorTreeBlob`, achieving L1 cache locality and eliminating the
conditional `NodeType` guard from the hot path.

- [x] **TASK-EQL-009** NodeDefinition bit-flag layout + temporary Interpreter patching *(BATCH-04)*
- [x] **TASK-EQL-010** AOT compilation pipeline — TreeCompiler + BTreeBuilder *(BATCH-04)*
- [x] **TASK-EQL-011** Binary serialization versioning + V1 legacy fallback *(BATCH-04)*
- [x] **TASK-EQL-012** Interpreter cleanup + editor integration [details](./TASK-DETAIL.md#task-eql-012--interpreter-cleanup-and-editor-integration) *(BATCH-05)*
