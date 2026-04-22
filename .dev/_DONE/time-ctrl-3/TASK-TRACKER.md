# Time Control Phase 3 — Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for detailed task descriptions and success conditions.

---

## Phase 1 — Message Types & Configuration

**Goal:** Define the new `TimeSyncRequest` / `TimeSyncResponse` DDS wire types and add the
three `TimeConfig` fields that control the NTP handshake behaviour.  No logic changes yet —
this phase is purely additive and provides the foundation for all subsequent phases.

- [x] **TC3-P1-T01** Add `TimeSyncRequest` and `TimeSyncResponse` structs to `TimeMessages.cs` [details](./TASK-DETAIL.md#tc3-p1-t01--add-timesyncrequestresponse-dds-structs)
- [x] **TC3-P1-T02** Add `MaxRttTicks`, `SyncRefreshIntervalTicks`, `SyncCorrectionWeight` to `TimeConfig` [details](./TASK-DETAIL.md#tc3-p1-t02--add-timeconfig-properties-for-ntp-sync)

---

## Phase 2 — MasterSyncController Bug Fixes

**Goal:** Fix the two single-line bugs in `MasterSyncController` that cause the 200 ms pause
drift and the per-step sim-time divergence.  Add structured debug logging throughout.

- [x] **TC3-P2-T01** Fix `MasterSyncController` constructor: `_totalWallTicks = now` [details](./TASK-DETAIL.md#tc3-p2-t01--fix-mastersynccollroller-constructor-initialise-_totalwallticks)
- [x] **TC3-P2-T02** Fix `MasterSyncController.Step()`: `TargetSimTime = _totalTime` [details](./TASK-DETAIL.md#tc3-p2-t02--fix-mastersynccollrollerstep-populate-targetsimtime)
- [x] **TC3-P2-T03** Add debug logging to `MasterSyncController` [details](./TASK-DETAIL.md#tc3-p2-t03--add-debug-logging-to-mastersynccollroller)
- [x] **TC3-P2-T04** Fix `SwitchToDeterministic` + `UpdateBarrierPending` to use physical `_getTick()` [details](./TASK-DETAIL.md#tc3-p2-t04--fix-switchtodeterministic-and-updatebarrierpending-to-use-the-physical-clock)

---

## Phase 3 — SlaveSyncController NTP Handshake

**Goal:** Equip `SlaveSyncController` with the NTP-style two-way handshake that computes
`_masterWallClockOffset`. Use `SyncedWallTicks` everywhere a master-domain time comparison
is needed (barrier evaluation, PLL transit time).  Add structured debug logging throughout.

- [x] **TC3-P3-T01** Add NTP fields, `SyncedWallTicks` property, `_isTimeSynced`, both event registrations, initial `SendTimeSyncRequest` [details](./TASK-DETAIL.md#tc3-p3-t01--add-ntp-fields-syncedwallticks-and-initial-sendtimesyncrequest)
- [x] **TC3-P3-T02** Implement `DrainTimeSyncResponses` with RTT filtering, offset update, and `_isTimeSynced = true` [details](./TASK-DETAIL.md#tc3-p3-t02--implement-draintimesyncresponses-with-rtt-calculation-and-offset-update)
- [x] **TC3-P3-T03** Fix `UpdateBarrierPending` to use `SyncedWallTicks` [details](./TASK-DETAIL.md#tc3-p3-t03--fix-updatebarrierpending-to-use-syncedwallticks)
- [x] **TC3-P3-T04** Fix `OnTimePulseReceived` to use `SyncedWallTicks` [details](./TASK-DETAIL.md#tc3-p3-t04--fix-ontimepulsereceived-to-use-syncedwallticks)
- [x] **TC3-P3-T05** Add pre-sync guards to `ProcessTimePulses` and `DrainModeSwitchEvents` [details](./TASK-DETAIL.md#tc3-p3-t05--add-pre-sync-guards-to-processtimepulses-and-drainmodeswitchevents)
- [x] **TC3-P3-T06** Drain stray `AdvanceFrameIntent` in `UpdateContinuous` and `UpdateBarrierPending` [details](./TASK-DETAIL.md#tc3-p3-t06--drain-stray-advanceframeintent-in-continuous-and-barrierpending-modes)

---

## Phase 4 — Translators & Network Module

**Goal:** Build the two DDS bridge translators that carry `TimeSyncRequest` / `TimeSyncResponse`
between nodes, and expose them through the `TimeNetworkModule` factory so application startup
code can wire them with a single call.

- [x] **TC3-P4-T01** Implement `MasterTimeSyncTranslator` [details](./TASK-DETAIL.md#tc3-p4-t01--implement-mastertimesynctranslator)
- [x] **TC3-P4-T02** Implement `SlaveTimeSyncTranslator` [details](./TASK-DETAIL.md#tc3-p4-t02--implement-slavetimesynctranslator)
- [x] **TC3-P4-T03** Add factory methods to `TimeNetworkModule` [details](./TASK-DETAIL.md#tc3-p4-t03--add-factory-methods-to-timenetworkmodule)

---

## Phase 5 — Autonomous Multi-Computer Unit Tests

**Goal:** Prove that the Phase 1–4 changes are correct for multi-process / multi-computer
scenarios by running a comprehensive suite of deterministic in-process tests that simulate
separate OS-clock domains using injected tick sources.  All tests must pass before any
application-layer wiring is attempted.

- [x] **TC3-P5-T01** `TimeSyncOffsetTests` — RTT formula, spike rejection, steering [details](./TASK-DETAIL.md#tc3-p5-t01--timesyncoffsettests-rtt-formula-and-offset-corner-cases)
- [x] **TC3-P5-T02** `PauseBarrierSyncTests` — barrier fires at same simtime with clock offsets [details](./TASK-DETAIL.md#tc3-p5-t02--pausebarriersynctests-barrier-fires-at-the-same-simtime)
- [x] **TC3-P5-T03** `LockstepSimTimeAccuracyTests` — step TotalTime is bit-identical across nodes [details](./TASK-DETAIL.md#tc3-p5-t03--lockstepsimtimeaccuracytests-step-simtime-is-bit-identical)
- [x] **TC3-P5-T04** `FullCycleMultiComputerSim` — full continuous→pause→step×5→resume with offsets [details](./TASK-DETAIL.md#tc3-p5-t04--fullcyclemulticomputersim-end-to-end-with-clock-offsets)
- [x] **TC3-P5-T05** `ClockSkewDriftTests` — periodic re-sync keeps accumulated drift bounded [details](./TASK-DETAIL.md#tc3-p5-t05--clockskewdrifttests-periodic-re-sync-keeps-drift-bounded)

---

## Phase 6 — Application Integration Validation

**Goal:** Confirm that the toolkit changes do not break any existing application-layer
behaviour.  No application-layer code changes are required for this phase.

- [x] **TC3-P6-T01** Verify `Hrot.ClusterRunner` builds cleanly; all `TimeControlIntegrationTests` pass [details](./TASK-DETAIL.md#tc3-p6-t01--api-compatibility-verification)
- [ ] **TC3-P6-T02** Application wiring guide (deferred — follow-on workstream) [details](./TASK-DETAIL.md#tc3-p6-t02--wire-the-new-translators-in-application-startup-integration-guide-task)
