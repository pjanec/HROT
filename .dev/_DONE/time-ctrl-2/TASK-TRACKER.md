# Time Control Phase 2 — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success conditions.

---

## Phase 1 — Fix Core Lockstep

**Goal:** Ensure `MasterSyncController` enforces ACK wait for the runtime slave roster rather than the empty construction-time set. Without this, lockstep has no actual synchronisation.

- [x] **TC2-P1-T1** Fix `MasterSyncController.SwitchToDeterministic` runtime slave capture [details](./TASK-DETAIL.md#tc2-p1-t1--fix-mastersynccollrollerswitchtodeterministic)
- [x] **TC2-P1-T2** Remove stale DT-003 comment from `OrchestratorSubsystem` [details](./TASK-DETAIL.md#tc2-p1-t2--update-orchestratorsubsystem-construction-and-comment)

---

## Phase 2 — Smooth SimTime Display on UI

**Goal:** Eliminate the 1 Hz visual stutter in the Time Control panel by reading sim time directly from the local `ITimeController` in `ClusterUiCache`.

- [x] **TC2-P2-T1** Add `ITimeController?` injection to `ClusterUiCache`; change `MasterSimTime` to direct read [details](./TASK-DETAIL.md#tc2-p2-t1--add-itimecontroller-injection-to-clusteruicache)
- [x] **TC2-P2-T2** Reorder `OrchestratorSubsystem.Initialize` and wire `_masterSync` into cache [details](./TASK-DETAIL.md#tc2-p2-t2--wire-mastersynccollroller-into-orchestratorsubsystems-ui-cache)
- [ ] **TC2-P2-T3** Wire slave controllers into SimHost / IG UI caches *(stretch)* [details](./TASK-DETAIL.md#tc2-p2-t3--wire-slave-controllers-into-simhost-and-ig-ui-caches-stretch) — **TD-001 (P3)**

---

## Phase 3 — ExCon Lockstep Participation

**Goal:** Give `ExConSubsystem` a `SlaveSyncController` so it participates in cluster lockstep (sends ACKs), and its UI shows smooth frame-rate sim time.

- [x] **TC2-P3-T1** Add `SlaveSyncController` + translators to `ExConSubsystem` [details](./TASK-DETAIL.md#tc2-p3-t1--add-slavesynccontroller-and-translators-to-exconsubsystem)
- [x] **TC2-P3-T2** Drive time pipeline in `ExConSubsystem.Update` [details](./TASK-DETAIL.md#tc2-p3-t2--drive-time-pipeline-in-exconsubsystemupdate)
- [x] **TC2-P3-T3** Wire `_slaveSyncController` into ExCon's `ClusterUiCache` [details](./TASK-DETAIL.md#tc2-p3-t3--wire-slavesynccontroller-into-excons-ui-cache)
- [x] **TC2-P3-T4** Remove redundant `TimePulseIngressHandler`/`TimeModeIngressHandler` from `ExConSubsystem` [details](./TASK-DETAIL.md#tc2-p3-t4--remove-redundant-time-ingress-handlers-from-exconsubsystem)
