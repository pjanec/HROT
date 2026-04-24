[BUG] component editor for ActorcapabilityState.Capabilities offer just individual values of CanShoot, CanMove etc.
But in fact these are flags that can be combined. The StructEdit should support flags. And Imgui should render
a checkbox list instead of plain enum-combo.


----------- IOS/IG MAP related stuff -----------------

[BUG] When i delete the entity using its context menu on IG, it gets deleted just from IG but not from SimHost - DeleteEntityRequest is necessary!

[BUG] Entity inspector when deleting entititie must use the entity deletion request message so it always reached the entity
owner and the entity is deleted properly (the owner performs the ELM-based entity deletion procedure)

[BUG] on IG, 'Edit personal route' entity context menu does nothing


[BUG] in IOS ORBAT panel, the JUMP TO seems to do nothing.

[BUG] in IOS ORBAT panel, vehicle entity context menu 'Edit route' starts authoring a route entity (OK)
When committed, the route entity is created as a subordinate of the vehicle (EntiyInfo.CommanderId=vehicle id)

[BUG] The ECS component EntityInfo contains CommanderId = network entity id (int).
Should be CommanderId = local entity id (Entity struct)

[BUG] When i delete tank platoon unit entity, the subordinate (physical) units are not deleted. Shouldn't they?

[BUG] ContextMenuRequest not seen to be sent if clicked entity not yet configured with context menu from IOS.

[BUG] MapClickEvent does not recognize lef/right/middlele click!




-----------



[IDEA] Should we move the TransitionPlanner to FDP toolkit? Move whole cluster state machine the toolkit? Is the state machine separable
from the 






[BUG?] When in operatingLive and switched to unloadingLive, the UnloadingLive lasts forever. The Orchestrator should automatically go to Idle
one all nodes are finished with the unloadingLive.
Similar situation if at Idle and switched to loadingLive, orgestrator should automatically issue the transition to OperatingLive
once all nodes are finished loading.

[BUG] before OperatingLive is entered (during loadingLive), the exercise clock should be initialized to scenario-specified time in paused state.
Depending on the jsonPayload of the cluster transition request {"StartPaused": true/false} the clock should be unpaused when OperatingLive
transition is confirmed by all nodes. Another field in json payload {"DeterministicStepping":true/false} should
determine the exercise clock mode.



[ISSUES] collected during development - can be obsolete, needs revising

- **NLog in tests:** test logging relied on `NLog.LogManager.Configuration` / `MemoryTarget`; global config made `IsDebugEnabled` false and is not thread-safe for xUnit parallel runs.
- **Fragile log assertions:** tests assert literal log strings (e.g. "[TC3][Master] STEP"), breaking if format changes.
- **FdpLog API limit:** `FdpLog<T>.Debug` has no `params` overload (max 4 args), forcing callers to drop fields.
- **Pre-sync guards dropped needed events:** overly strict `_isTimeSynced` guards in `ProcessTimePulses` / `DrainModeSwitchEvents` caused mode-switch/time-pulse events to be dropped (integration failures when translators not wired).
- **Test infra mismatches:** many tests assumed implicit behavior (missing `SwapBuffers()`, `SwitchToDeterministic()` side effects); required explicit swaps and call sequences.
- **ReadOnlySpan vs xUnit:** `bus.Consume<T>()` returns `ReadOnlySpan<T>` which broke `Assert` helpers; tests needed `.ToArray()` conversions.

- **FdpLog.Debug overloads:** Fdp.Kernel.Logging.FdpLog.cs implements Debug overloads only up to 4 args — no `params` overload. (File: FdpLog.cs)
- **Test logging isolation:** Tests configure NLog.LogManager.Configuration globally (MasterSyncControllerTests.cs); risk of cross-test contamination remains. (File: MasterSyncControllerTests.cs)
- **Drain tests vacuous (missing SwapBuffers):** Some unit tests still publish managed intents without SwapBuffers() before ctrl.Update() — drain assertions remain effectively vacuous (see SlaveSyncControllerTests.cs, e.g. SlaveSyncController_ContinuousMode_DrainsStrayStepIntents). (File: SlaveSyncControllerTests.cs)
- **Log string fragility (STEP prefix):** Tests match literal `"[TC3][Master] STEP"` while controller emits that string inline — recommend exposing a shared constant used by both. (Files: MasterSyncController.cs, MasterSyncControllerTests.cs)
- **Periodic resync during long runs:** SyncRefreshIntervalTicks exists and long-frame tests may trigger periodic resyncs; tests/harness may need configuration or freezing to avoid unintended resyncs. (File: TimeConfig.cs)

- FdpEventBus.SwapBuffers silently discards all items still in the read buffer; events not consumed before `SwapBuffers` are permanently lost. Evaluate draining-queue mode for orchestration path to prevent silent data loss. — **Target Batch:** Backlog
- ClusterOpE2eScriptTests (OverlappingCheckpoints, RecordAndReplaySeek, PreviewStateRestore, LiveFromReplayBranch) time out; root cause appears unrelated to multi-intent queue fixes and needs dedicated investigation. — **Target Batch:** BATCH-08
- Several ClusterMaster test files (8) still exercise the DDS compatibility path rather than bus-mode; migrate these tests to bus-path to reduce maintenance and ensure bus-mode coverage. — **Target Batch:** BATCH-09


[DEBTS] — Logic Packs & Translator Packs Refactoring

| ID | Priority | Source | Description | Target Batch | Status |
|----|----------|--------|-------------|--------------|--------|
| DEBT-001 | P3 | PACK-M002 / BATCH-01 | AllInOne mode (`DamageSystem`) does not strip `CanMove` on non-lethal hits. Existing test contract (`Damage_StripsCapabilities_OnLethalHit` Part A) prohibits it. Design gap: AllInOne and Brain/CQRS paths have different non-lethal damage behavior. Future AllInOne parity pass needed if this matters. | TBD | Open |
| DEBT-002 | P3 | PACK-M001 / BATCH-01 | `IReadOnlyList<T>` lacks `FindIndex` — workaround `.ToList().FindIndex(...)` in `CognitiveRuntimeModuleTests`. Minor test ergonomics issue. | TBD | Open |
| DEBT-003 | P3 | PACK-P002 / BATCH-02 | `SimHostModule` constructor now has 9 optional parameters. A builder or options-object pattern would improve readability. Will worsen as more systems are added. | TBD | Open |
| DEBT-004 | P3 | PACK-P002 / BATCH-02 | `SstRequestFinalizationSystem.cs` file contains class `NedRequestFinalizationSystem` — file name mismatch is a maintenance hazard. | TBD | Open |
| DEBT-005 | P3 | General / BATCH-02 | 328 xUnit2013 style warnings (`Assert.Equal` on collection size vs `Assert.Empty/Single`). Adds noise. Could be fixed in a cleanup batch. | TBD | Open |
| DEBT-006 | P2 | PACK-P001 / BATCH-03 | `MissionControlRequestSystem` still exists in codebase but is no longer wired. Must be deleted to avoid confusion. | BATCH-04 | ✅ Resolved |
| DEBT-007 | P3 | PACK-P001 / BATCH-03 | `view as EntityRepository` cast in `MissionControlIngressTranslator` (and `EntityMissionIngressTranslator`) — silently no-op if view is wrapped. `ISimulationView` should expose `Bus`/`PublishManagedEvent`. | TBD | Open |
| DEBT-008 | P3 | PACK-P001 / BATCH-03 | `[EventId]` collision has no compile-time guard — only fails at runtime. A test enumerating all registered event type IDs and asserting uniqueness would catch it. | TBD | Open |
| DEBT-009 | P3 | PACK-P001 / BATCH-03 | `IDescriptorTranslator.Dispose(long)` contract undocumented. New bus-bridge translators implement as no-op (correct) but no guidance exists. | TBD | Open |
| DEBT-010 | P3 | PACK-C002 / BATCH-04 | `OrchestratorSubsystem.Update()` bridges `SwitchTimeModeEvent` between two buses per-frame. Could be eliminated by unifying buses. Low priority. | TBD | Open |
| DEBT-011 | P3 | PACK-C002 / BATCH-04 | `OrchestrationObserverTranslator.Tick()` parses JSON (asset inventory) every frame even if unchanged. Version/hash check could short-circuit. Not on hot path. | TBD | Open |
| DEBT-007 | P3 | PACK-P001 / BATCH-03 | `view as EntityRepository` cast in `MissionControlIngressTranslator` (and `EntityMissionIngressTranslator`) — silently no-op if view is wrapped. `ISimulationView` should expose `Bus`/`PublishManagedEvent`. | TBD | Open |
| DEBT-008 | P3 | PACK-P001 / BATCH-03 | `[EventId]` collision has no compile-time guard — only fails at runtime. A test enumerating all registered event type IDs and asserting uniqueness would catch it. | TBD | Open |
| DEBT-009 | P3 | PACK-P001 / BATCH-03 | `IDescriptorTranslator.Dispose(long)` contract undocumented. New bus-bridge translators implement as no-op (correct) but no guidance exists. | TBD | Open |
