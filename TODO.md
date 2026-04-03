x[BUG] 'clusterrunner -m all' shows 'not bootstrapped'. The subsystem state reporting is failing.

x[BUG] ScenarioSerializer inside FDP now "knows" the scenario file format (see PeekSubsystemType). This is leaking the application level knowledge
dowen the FDP.

x[BUG] ReferenceEpisodeLoadHandler uses StartEpisodeOperationId = 20; as number which must match NodeOpType.StartEpisode -fragile, breaks if i reorder
operationd ids. We should use enums intead, no primitive types. Similarly for other handlers.

x[BUG] StatusCode in NodeOpCompletedEvent is int. Should be enum. OrchestrationStatusCode.cs should define enums, not pure int.
I know that the primitive int allows for open-ended status codes. But i want you to convert the int constants in OrchestrationStatusCode.cs
into enum (keep the numerical values and the comments about numeric ranges) and use that enum for the statuscode fields in the internal FDP events
to improve the debugging experience.

x[BUG] NodeOpCompletedEvent should contain operation id so that the translator know how to convert  the "object? ResultPayload;" to dds message.

x[BUG] ClusterStateTransitionedEvent now contain int state id NewStateId. Should be enum!

y[BUG] ClusterUiCache is tied to network messages. After the cluster management revamp to CQRS, it should work purely with fdp events.
Only the translator should work with network, everything inside the system must be communicated via FdpEvents. this 

y[BUG] ClusterMaster ctor exists in 2 versions - with DDS and without it. The DDS one is undersired after we revamped the one with FdpEvents.
No fallback and backward compatibility please!


[BUG] The IDL codegen has a bug with non-sequential enums — value gap at 3 (where dtGeoSpatialDR was) causes the enum entries after to use `@value()` annotations which confuses the idlc union case generator.
The IDL generator is using the field's position in the struct (0, 1, 2, 3, 4) instead of the actual discriminant values (0, 1, 2, 4, 5). So when it encounters `MapVisualOverlay` at index 3, it's grabbing the wrong discriminant value, and when it gets to `MapRoute` at index 4, it's using the numeric value 5 as a fallback. The gap from removing `dtGeoSpatialDR` is causing the indices and values to misalign.

[IDEA] Should we move the TransitionPlanner to FDP toolkit? Move whole cluster state machine the toolkit? Is the state machine separable
from the 



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





----------
# Scene tree graph in ECS?
Invent ECS components for scene graph implementation in ECS
 - parent component (contains parent entity id)
 - child component (contains entity id of first child)
 - sibling component (containd entity id of prev and next sibling)
Queue for structural change commands
 - reparent command
Optimized recalculation of transforms every frame if something changes
(in case we need to calculate the transforms of child entities - like aircraft on board of a carrier)
etc.
----------
# sample.IsValid issue
Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!
-----------

We have two identical components
 - EntityMissionholder component
 - IgMissionHolder component
why? can't we unify them?







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
