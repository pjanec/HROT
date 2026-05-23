# Failing Test Tracker

Status: not-fixed | fixed

---

## FDP Solution (`FDP\FDP.sln`)

### Group FDP-G01: DebugGizmoLayer NullReferenceException
**Root cause:** NullReferenceException at `DebugGizmoLayer.Draw` line 102  
**Test project:** `Fdp.Presentation.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerHitTests.SC_GZ026_1_LineMidpoint_IsHit` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerHitTests.SC_GZ026_2_BeyondEndpoint_IsMiss` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerHitTests.SC_GZ026_3_SphereCenter_IsHit` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerHitTests.SC_GZ026_4_ScreenPixels_ZoomScalesHitRadius` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerActivationTests.SC_GZ025_1_HitPickable_PushesProxyTool` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerActivationTests.SC_GZ025_2_OnEnter_PublishesStartedEventOnce` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerActivationTests.SC_GZ025_3_MissedClick_NoToolPushed` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Layers.DebugGizmoLayerActivationTests.SC_GZ025_5_NullCanvas_FallbackPublishesEvent` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Gizmos.DebugGizmoLayerGizmoTests.SC_GZ013_1_Draw_WithInjectedRenderer_NoException` | not-fixed |
| `Fdp.Toolkit.Vis2D.Tests.Gizmos.DebugGizmoLayerGizmoTests.SC_GZ013_2_HandleInput_HitPrimitive_ReturnsTrueAndPublishesEvent` | not-fixed |

---

### Group FDP-G02: RecordingDumper CLI Integration
**Root cause:** CLI exit code 3 instead of 0  
**Test project:** `Fdp.Tools.RecordingDumper.Tests`

| Test | Status |
|------|--------|
| `Fdp.Tools.RecordingDumper.Tests.DumperTests.EX_T30_AllSwitches_MappedToCorrectOptions` | not-fixed |
| `Fdp.Tools.RecordingDumper.Tests.DumperTests.EX_T32_CliIntegration_MatchesDirectServiceOutput` | not-fixed |

---

### Group FDP-G03: ModuleHost SharedSnapshotProvider
**Root cause:** Expected `SharedSnapshotProvider`, got `OnDemandProvider` (convoy/SoD logic broken)  
**Test project:** `Fdp.ModuleHost.Tests`

| Test | Status |
|------|--------|
| `Fdp.ModuleHost.Tests.ConvoyAutoGroupingTests.AutoGrouping_SameTierAndFreq_SharesProvider` | not-fixed |
| `Fdp.ModuleHost.Tests.ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy` | not-fixed |
| `Fdp.ModuleHost.Tests.HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` | not-fixed |
| `Fdp.ModuleHost.Tests.HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically` | not-fixed |
| `Fdp.ModuleHost.Tests.ConvoyIntegrationTests.ConvoyIntegration_5Modules_ShareSnapshot` | not-fixed |
| `Fdp.ModuleHost.Tests.ConvoyIntegrationTests.ConvoyIntegration_MemoryUsage_Reduced` | not-fixed |
| `Fdp.ModuleHost.Tests.ResilienceIntegrationTests.Resilience_MultipleModulesFailing_SystemDegrades` | not-fixed |

---

### Group FDP-G04: Event type 2030 not registered
**Root cause:** `Event type 2030 not registered via RegisterEvent<T>()` - missing event registration in scenarios  
**Test project:** `Fdp.Examples.Scenarios.Tests`

| Test | Status |
|------|--------|
| `Fdp.Examples.Scenarios.Tests.UrbanCombatNewScenarioTests.UrbanCombatNew_RunToCompletion_ExitsZero` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.UrbanCombatNewScenarioTests.UrbanCombatNew_Latch1_InsurgentFiresWithin100Ticks` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.UrbanCombatNewScenarioTests.UrbanCombatNew_Latch2_ApcHaltsAfterAmbush` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.UrbanCombatNewScenarioTests.UrbanCombatNew_Latch4_InsurgentDies` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.UrbanCombatNewScenarioTests.UrbanCombatNew_Latch5_MissionResumes` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.BallisticsAndHitScenarioTests.BallisticsAndHit_RunToCompletion_ExitsZero` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.BallisticsAndHitScenarioTests.BallisticsAndHit_Phase1_BulletSpawnedWithCorrectVelocity` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.BallisticsAndHitScenarioTests.BallisticsAndHit_Phase3_TargetTakesDamage_NoBulletSwimthrough` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.BallisticsAndHitScenarioTests.BallisticsAndHit_Phase4_BulletDestroyedAfterImpact` | not-fixed |

---

### Group FDP-G05: ComponentDamageScenarioTests assertion failures
**Root cause:** Damage logic not working - health/flags not changing as expected  
**Test project:** `Fdp.Examples.Scenarios.Tests`

| Test | Status |
|------|--------|
| `Fdp.Examples.Scenarios.Tests.ComponentDamageScenarioTests.ComponentDamage_RunToCompletion_ExitsZero` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.ComponentDamageScenarioTests.ComponentDamage_Phase2_HealthDecreases_AfterHit` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.ComponentDamageScenarioTests.ComponentDamage_Phase3_MoveFlagStripped_AfterDamage` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.ComponentDamageScenarioTests.ComponentDamage_Phase4_LocomotionCleared_ByHSM` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.ComponentDamageScenarioTests.ComponentDamage_Phase5_WeaponStillFires_AfterMobilityKill` | not-fixed |

---

### Group FDP-G06: DistributedTankScenarioTests
**Root cause:** Scenario exits with code 1 (unexpected failure)  
**Test project:** `Fdp.Examples.Scenarios.Tests`

| Test | Status |
|------|--------|
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_PhaseA_RunToTick10_ExitsZero` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_PhaseB_BrainHullReachesActive_AtTick5` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_PhaseB_MuscleHasGhostForBrainHull` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_Phase2_MuscleNodeMovesOnCommand` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_Phase2_LocoMsgConsumedViaDds` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_Phase3_BrainTurretTracksHull_AtTick40` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.DistributedTankScenarioPhaseATests.DistributedTank_Phase4_SplitAuthorityBothChannelsActive` | not-fixed |

---

### Group FDP-G07: SensorGridScenarioTests
**Root cause:** Sensor/visibility logic failure - targets not detected/occluded properly  
**Test project:** `Fdp.Examples.Scenarios.Tests`

| Test | Status |
|------|--------|
| `Fdp.Examples.Scenarios.Tests.SensorGridScenarioTests.SensorGrid_RunToCompletion_ExitsZero` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.SensorGridScenarioTests.SensorGrid_Phase1_TargetDetectedInOpenField` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.SensorGridScenarioTests.SensorGrid_Phase2_TargetOccludedByWall` | not-fixed |
| `Fdp.Examples.Scenarios.Tests.SensorGridScenarioTests.SensorGrid_Phase3_TargetReacquiredAfterWall` | not-fixed |

---

### Group FDP-G08: GizmoSettingsPersistenceTests
**Root cause:** Float32 setting not persisted (returns 0 instead of 2.5)  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Diagnostics.Gizmos.Tests.GizmoSettingsPersistenceTests.Float32_Roundtrip_Via_Persistence` | not-fixed |

---

### Group FDP-G09: JsonExportOptionsTests
**Root cause:** Default `ExportMode` is `Incremental` instead of `AbsoluteState`  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.ReplayBrowser.Export.JsonExportOptionsTests.Defaults_MatchDesignSpec` | not-fixed |

---

### Group FDP-G10: NavigationIntentBridgeSystemTests
**Root cause:** `NoneIntent` not being skipped - nav state changes when it should not  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Navigation.Tests.NavigationIntentBridgeSystemTests.NoneIntent_IsSkipped_NavStateUnchanged` | not-fixed |

---

### Group FDP-G11: FastPathBenchmarks (performance test)
**Root cause:** Speedup too low (1.45x instead of expected significant speedup)  
**Test project:** `Fdp.Core.Tests`

| Test | Status |
|------|--------|
| `Fdp.Tests.Benchmarks.FastPathBenchmarks.Benchmark_HotPathOptimization` | not-fixed |

---

### Group FDP-G12: IdAllocationTests
**Root cause:** IdAllocator monitor system not triggering / not processing responses correctly  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Replication.Tests.IdAllocationTests.MonitorSystem_PublishesRequest_WhenLowWaterMarkTriggers` | not-fixed |
| `Fdp.Toolkit.Replication.Tests.IdAllocationTests.MonitorSystem_ProcessesResponse_AndAddsBlock` | not-fixed |

---

### Group FDP-G13: Combat component struct sizes wrong
**Root cause:** Struct sizes larger than expected (4 bytes too big each)  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Combat.Tests.CombatComponentTests.WeaponFireIntent_IsUnmanaged_AndHasCorrectSize` | not-fixed |
| `Fdp.Toolkit.Combat.Tests.CombatComponentTests.WeaponFireNotification_IsUnmanaged_AndHasCorrectSize` | not-fixed |
| `Fdp.Toolkit.Combat.Tests.CombatComponentTests.DetonationNotification_IsUnmanaged_AndHasCorrectSize` | not-fixed |
| `Fdp.Toolkit.Combat.Tests.CombatComponentTests.DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize` | not-fixed |

---

### Group FDP-G14: AimAndFireExecutorTests cooldown logic
**Root cause:** Cooldown logic not working - fires when it should not  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Combat.Tests.AimAndFireExecutorTests.AimAndFire_DoesNotFire_WhenCooldownActive` | not-fixed |
| `Fdp.Toolkit.Combat.Tests.AimAndFireExecutorTests.AimAndFire_DrainsCooldown_ByDt_UntilCanFire` | not-fixed |

---

### Group FDP-G15: FdpAutoSerializer EntityInlineComp InlineArray
**Root cause:** `Component 'EntityInlineComp' has an [InlineArray] field with element type Entity, not supported by FdpAutoSerializer`  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T02_BasicRoundTrip_HeaderAndFrameCount` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T03_FirstFrame_IsKeyframe_EmptyDestroyedEntities` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T04_DeltaFrame_DestroyedEntities_PopulatedCorrectly` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T05_ComponentEntries_HaveCorrectSchema` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T06_HasAuthority_ReflectsComponentAuthorityMask` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T07_RelativeWallTimeSec_ZeroOnFirstFrame_MonotoneAfter` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T08_SimTimeSec_MatchesGlobalTimeTotalTime` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T09_SimFrameNumber_MatchesGlobalTimeFrameNumber` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T10_FileFrameOrdinal_IsDense` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T11_Tick_MatchesFrameMetadataTick` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T12_ByFrame_WindowsCorrectly` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T13_ByTime_WindowsCorrectly` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T14_ByTime_PastEof_EmitsEmptyFrames` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T15_FilterByEntityIndex_RestrictsEntitiesAndDestroyedEntities` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T16_FilterBySelection_EmitsOnlyTargetEntities` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T17_IncludeEventsFalse_OmitsEventsBlock` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T18_IncludeEntitiesFalse_OmitsEntitiesBlock` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T19_Minified_ProducesNoNewlines` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T20_NumericArrayPayloads_AreFlattenedToSingleLine` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T21_EntityFieldsInEvents_AreFormattedAsStrings` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T22_CustomTranslator_IsHonored_PayloadReflectsStubDto` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T23_ManagedEvents_EmittedWithIsManagedTrue` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T24_UnmanagedEvents_EmittedWithIsManagedFalse` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T25_LargeRecording_NoBigHeapAllocation` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T26_Export_DoesNotMutateParallelContext` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T27_ChangelogMode_EmitsExactlyThreeEntries_AtMutatedFrames` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T28_ChangelogMode_Epsilon_SuppressesSubEpsilonChanges` | not-fixed |
| `Fdp.Toolkit.ReplayBrowser.Export.RecordingExportServiceTests.EX_T29_ChangelogMode_EntityDestruction_NoEntriesAfterDeath` | not-fixed |

---

### Group FDP-G16: ComponentId collisions (test artifacts)
**Root cause:** `Component ID collision: NoRecordTestComponent and TestComponent both declare [ComponentId(240)]` and `AuditCompB and ZoneEnvironmentData both declare [ComponentId(201)]`  
**Test projects:** `Fdp.Core.Tests`, `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Tests.Benchmarks.ComponentOperationBenchmarks.Benchmark_SetRawObject_Performance` | not-fixed |
| `Fdp.Tests.Benchmarks.ComponentOperationBenchmarks.Benchmark_CommandBuffer_Playback` | not-fixed |
| `CarKinem.Tests.Systems.CarKinematicsSystemTests.System_FollowsTrajectory` | not-fixed |
| `CarKinem.Tests.Systems.CarKinematicsSystemTests.System_AvoidanceMovesVehicle` | not-fixed |
| `CarKinem.Tests.Systems.CarKinematicsSystemTests.System_UpdatesVehiclePosition` | not-fixed |
| `Fdp.Toolkit.CarKinem.Tests.VehicleStateRefactorTests.CarKinematicsSystem_WritesSimTransform_AfterUpdate` | not-fixed |

---

### Group FDP-G17: FdpAutoSerializerFixedBufferTests
**Root cause:** `MissionPlanQueue` component not found after deserialization  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Scenario.Tests.FdpAutoSerializerFixedBufferTests.RoundTrip_MissionPlanQueue_PreservesPhaseData` | not-fixed |

---

### Group FDP-G18: ReferenceHandlerTests staging path
**Root cause:** Staging directory path format differs (extra `scenarios\` segment)  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Orchestration.Tests.ReferenceHandlerTests.LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir` | not-fixed |

---

### Group FDP-G19: RecordingSearchServiceTests
**Root cause:** GreaterThan search finds 1 result instead of 3  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.ReplayBrowser.Search.RecordingSearchServiceTests.SR_T03_GreaterThan_FindsMultipleFrames` | not-fixed |

---

### Group FDP-G20: DataDrivenGizmoSystemRoutingTests
**Root cause:** Gizmo system routing not dispatching to correct gizmo  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Diagnostics.Gizmos.Tests.DataDrivenGizmoSystemRoutingTests.SC_GZ066_2_StructUpdate_RoutesTo_MatchingGizmo` | not-fixed |
| `Fdp.Toolkit.Diagnostics.Gizmos.Tests.DataDrivenGizmoSystemRoutingTests.SC_GZ066_5_MenuAction_RoutesTo_MatchingGizmo_Only` | not-fixed |

---

### Group FDP-G21: BicycleModel negative speed
**Root cause:** Negative speed not clamped to 0  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `CarKinem.Tests.Algorithms.BicycleModelTests.BicycleModel_NegativeSpeed_ClampsToZero` | not-fixed |

---

### Group FDP-G22: SimTransformBridgeSystemTests rotation
**Root cause:** Rotation-to-heading/pitch-roll conversion incorrect  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Geographic.Tests.SimTransformBridgeSystemTests.RotationToPitchRollDeg_Combined_PitchAndRollIndependent` | not-fixed |
| `Fdp.Toolkit.Geographic.Tests.SimTransformBridgeSystemTests.RotationToHeadingDeg_DegenerateRotation_Returns0` | not-fixed |

---

### Group FDP-G23: ReplayModuleTests async seek
**Root cause:** `SeekToFrameAsync` completes synchronously instead of on background thread  
**Test project:** `Fdp.Toolkits.Tests`

| Test | Status |
|------|--------|
| `Fdp.Toolkit.Replay.Tests.ReplayModuleTests.ReplayModule_SeekToFrameAsync_IsOffMainThread` | not-fixed |

---

## HROT Solution (`IOS-IG-SimHost.sln`)

### Group HROT-G01: NavigationIntentEgressTranslatorTests
**Root cause:** NavigationIntent egress translator publishing when it should not / wrong count  
**Test project:** `Hrot.Map.Common.Tests`

| Test | Status |
|------|--------|
| `Hrot.Map.Common.Tests.Replication.Egress.NavigationIntentEgressTranslatorTests.ModeNone_NeverPublished` | not-fixed |
| `Hrot.Map.Common.Tests.Replication.Egress.NavigationIntentEgressTranslatorTests.NewCommand_PublishedExactlyOnce` | not-fixed |

---

### Group HROT-G02: CreateEntityRequestSystemTests
**Root cause:** NullReferenceException in child entity allocation  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.CreateEntityRequestSystemTests.C013_ChildOverride_KeyAbsent_AllocatorCalledForChild` | not-fixed |

---

### Group HROT-G03: LogArchiveExtractionServiceTests
**Root cause:** Log extraction service filtering/collection issues  
**Test project:** `Hrot.Core.Tests`

| Test | Status |
|------|--------|
| `Hrot.Core.Tests.Diagnostics.LogArchiveExtractionServiceTests.ExtractLogsAsync_AllLinesPass_WritesAllLines` | not-fixed |
| `Hrot.Core.Tests.Diagnostics.LogArchiveExtractionServiceTests.ExtractLogsAsync_SeverityFilter_ExcludesLowSeverityLines` | not-fixed |
| `Hrot.Core.Tests.Diagnostics.LogArchiveExtractionServiceTests.ExtractLogsAsync_MultipleMatchingFiles_CollectsAllLines` | not-fixed |
| `Hrot.Core.Tests.Diagnostics.LogArchiveExtractionServiceTests.ExtractLogsAsync_FileWithinMaxAge_IsIncluded` | not-fixed |
| `Hrot.Core.Tests.Diagnostics.LogArchiveExtractionServiceTests.ExtractLogsAsync_BracketSeverityFilter_WorksCorrectly` | not-fixed |

---

### Group HROT-G04: ScenarioSaveLoadTests
**Root cause:** Scenario save/load not preserving context properly  
**Test project:** `Hrot.Orchestrator.Integration.Tests`

| Test | Status |
|------|--------|
| `Hrot.Orchestrator.Integration.Tests.ScenarioSaveLoadTests.OnContextLoaded_FiresWithCorrectValues_AfterCommitLoad` | not-fixed |
| `Hrot.Orchestrator.Integration.Tests.ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad` | not-fixed |
| `Hrot.Orchestrator.Integration.Tests.ScenarioSaveLoadTests.RoundTrip_SimHost_EntitiesMatchAfterLoad` | not-fixed |

---

### Group HROT-G05: DataDrivenGizmoPredicateTests
**Root cause:** `InvalidCastException` - `D003NoOpDrawBuilder` cannot be cast to `DebugPrimitiveBuffer`  
**Test project:** `Hrot.ClusterRunner.Tests`

| Test | Status |
|------|--------|
| `Hrot.ClusterRunner.Tests.DataDrivenGizmoPredicateTests.D003_Predicate_False_SkipsUpdateAndDraw_ForFilteredEntity` | not-fixed |
| `Hrot.ClusterRunner.Tests.DataDrivenGizmoPredicateTests.D003_Predicate_True_AllowsUpdateAndDraw` | not-fixed |

---

### Group HROT-G06: EpisodeInjectionTests
**Root cause:** Episode entity injection/deletion not working  
**Test project:** `Hrot.SimHost.Integration.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Integration.Tests.EpisodeInjectionTests.StartEpisode_EntitiesSpawnedWithEpisodeTag` | not-fixed |
| `Hrot.SimHost.Integration.Tests.EpisodeInjectionTests.StopEpisode_EntitiesDestroyedByEpisodeTag` | not-fixed |
| `Hrot.SimHost.Integration.Tests.EpisodeInjectionTests.MultipleEpisodesCoexist_IndependentDeletion` | not-fixed |

---

### Group HROT-G07: NavigationIntent not registered in Hrot.IG tests
**Root cause:** `StatelessGizmoRegistry.Register: required component type 'NavigationIntent' is not registered` - missing `repo.RegisterComponent<NavigationIntent>()` in bootstrapper  
**Test project:** `Hrot.IG.Tests`, `Hrot.Presentation.Tests`

| Test | Status |
|------|--------|
| `Hrot.IG.Tests.Gizmos.HillAttackGizmoTests.SC_GZ021_HA_6_GizmoRegistrar_RegistersShowSlotsSetting` | not-fixed |
| `Hrot.IG.Tests.Gizmos.HealthBarGizmoTests.SC_GZ021_HB_5_GizmoRegistrar_RegistersSettingsForBothKeys` | not-fixed |
| `Hrot.IG.Tests.Gizmos.EntityRotationGizmoTests.SC_GZ021_ROT_4_GizmoRegistrar_RegistersEntityRotationArrowLengthSetting` | not-fixed |
| `Hrot.IG.Tests.Gizmos.GizmoRendererWiringTests.SC_GZ020_3_RegisterHealthBarGizmo_DoesNotThrow` | not-fixed |
| `Hrot.IG.Tests.AreaAuthoringTests.AreaTool_AfterCommit_ToolIsPopped` | not-fixed |
| `Hrot.IG.Tests.AreaAuthoringTests.AreaRequest_Overlay_PointsAreRelativeOffsets_FromCentroid` | not-fixed |
| `Hrot.IG.Tests.AreaAuthoringTests.AreaRequest_EntityMaster_TkbType_IsArea` | not-fixed |
| `Hrot.IG.Tests.ContinuousDragTests.ContinuousDragOff_RepeatMoves_NoGatewayCalls` | not-fixed |
| `Hrot.IG.Tests.ContinuousDragTests.DragEnd_AlwaysSendsExactlyOneUpdate` | not-fixed |
| `Hrot.IG.Tests.IgApplicationTests.ExecuteLocalContextAction_IgDeleteEntity_PublishesDestroyCommand` | not-fixed |
| `Hrot.IG.Tests.IgApplicationTests.CommitHandler_EntityDestroyedBeforeCommit_DropsUpdateSilently` | not-fixed |
| `Hrot.IG.Tests.ShiftRightClickTests.PlainRightClick_DoesNotEmitWaypointEvent` | not-fixed |
| `Hrot.IG.Tests.RouteAuthoringTests.AfterFinish_PointSequenceToolIsNoLongerActive` | not-fixed |
| `Hrot.IG.Tests.RouteAuthoringTests.FinishCallback_1Point_DoesNotEmitRequest` | not-fixed |
| `Hrot.IG.Tests.CommandHandling.DrawPersonalRouteCommandTests.ValidPoints_GatewayCalledWithCorrectDescriptors` | not-fixed |

---

### Group HROT-G08: UnitSubordinateTranslatorTests JSON deserialization
**Root cause:** `designation` field is a Number in JSON but deserialized as String  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.UnitSubordinateTranslatorTests.Inject_WithValidGuid_WritesInitialUnitSubordinateIntent` | not-fixed |
| `Hrot.SimHost.Tests.UnitSubordinateTranslatorTests.Extract_WithCommander_ProducesCommanderGuidAndDesignation` | not-fixed |
| `Hrot.SimHost.Tests.UnitSubordinateTranslatorTests.Inject_WithUnresolvableGuid_WritesIntentWithZeroNetworkId` | not-fixed |

---

### Group HROT-G09: ClusterMasterContextHandlerTests
**Root cause:** State transition not invoking local context handler  
**Test project:** `Hrot.Orchestrator.Tests`

| Test | Status |
|------|--------|
| `Hrot.Orchestrator.Tests.ClusterMasterContextHandlerTests.TransitionState_LoadingLive_InvokesLocalContextHandler` | not-fixed |

---

### Group HROT-G10: MissionExecutionFlowTests
**Root cause:** Entity not moving - mission execution failure (expected movement > 50m, got 0m)  
**Test project:** `Hrot.SimHost.Integration.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Integration.Tests.MissionExecutionFlowTests.EntityMission_MovesEntity` | not-fixed |
| `Hrot.SimHost.Integration.Tests.MissionExecutionFlowTests.MoveToLocation_TankNavigates_GeoSpatialChangesAfter10s` | not-fixed |

---

### Group HROT-G11: RecordReplayIntegrationTests
**Root cause:** Recording files not produced / module not installed  
**Test project:** `Hrot.SimHost.Integration.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Integration.Tests.RecordReplayIntegrationTests.EpisodeRecording_WithConcurrentGlobalRecorder_BothFilesProduced` | not-fixed |
| `Hrot.SimHost.Integration.Tests.RecordReplayIntegrationTests.RecordingLifecycle_InstallUninstall_ModuleInstalledThenGone` | not-fixed |

---

### Group HROT-G12: ScenarioFileServiceTests SaveablePosition
**Root cause:** `Unknown component type 'SaveablePosition'` - component name changed or not registered  
**Test project:** `Hrot.Presentation.Tests`

| Test | Status |
|------|--------|
| `Hrot.ScenarioEditor.Tests.ScenarioFileServiceTests.SaveLoad_RoundTrip_PreservesEntitiesAndComponents` | not-fixed |

---

### Group HROT-G13: EntityDragGizmoTests
**Root cause:** Drag/commit not writing correct position / firing callbacks  
**Test project:** `Hrot.Presentation.Tests`

| Test | Status |
|------|--------|
| `Hrot.ScenarioEditor.Tests.EntityDragGizmoTests.OnCommit_WritesFinalPositionAndFiresCallback` | not-fixed |
| `Hrot.ScenarioEditor.Tests.EntityDragGizmoTests.OnDragUpdate_WritesToSimTransformPosition` | not-fixed |

---

### Group HROT-G14: EntityInfoTranslatorTests
**Root cause:** Commander info not being set/resolved correctly  
**Test project:** `Hrot.IG.Tests`

| Test | Status |
|------|--------|
| `Hrot.IG.Tests.EntityInfoTranslatorTests.CS011_CommanderPresent_ImmediateCmdAssignSubordinate` | not-fixed |
| `Hrot.IG.Tests.EntityInfoTranslatorTests.CS011_CommanderIdZero_WithExistingUnitSubordinate_PublishesCmdRemove` | not-fixed |
| `Hrot.IG.Tests.EntityInfoTranslatorTests.CS011_DeferredResolvedOnEntityRegistered` | not-fixed |
| `Hrot.IG.Tests.EntityInfoTranslatorTests.CS011_CommanderUpdate_ScrubsOldPendingQueue` | not-fixed |

---

### Group HROT-G15: MissionPlanTranslatorTests
**Root cause:** Mission plan serialization/deserialization failure  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.MissionPlanTranslatorTests.Extract_EntityWithActiveMissionPlan_ReturnsMissionPlanDomObject` | not-fixed |
| `Hrot.SimHost.Tests.MissionPlanTranslatorTests.Inject_WithExtractedDom_RestoresActivePlanAndQueue` | not-fixed |

---

### Group HROT-G16: HillAttackIntegrationTests
**Root cause:** Hill attack commander logic failure - BehaviorFinished not published  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.HillAttackIntegrationTests.SC_HA015_1_FullEndToEnd_CommanderFinishes_WhenAreaIsEmpty` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackIntegrationTests.SC_HA015_2_DispatchWaveWithTargets_AssignsUniqueSlots_ViaBTreeSystem` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackIntegrationTests.SC_HA015_3_IsWaveCompleted_BurnsSlotOfKilledTank_ViaBTreeSystem` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackIntegrationTests.SC_HA015_4_DispatchWaveWithTargets_AssignsTargetsRoundRobin_ViaBTreeSystem` | not-fixed |

---

### Group HROT-G17: HillAttackNodeTests
**Root cause:** Various hill attack node logic failures  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.HillAttackNodeTests.SC_HA008_1_AimAndFireSpecific_WritesWeaponChannel_AndIncrementsId` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackNodeTests.SC_HA011_4_IsAreaQueryResolved_ReturnsFailure_WhenReadyWithZeroTargets` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackNodeTests.SC_HA012_6b_IsWaveCompleted_RunComplete_WhenHashNoLongerMatchesAfterStarted` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackNodeTests.SC_HA016_1_ParsePlatoonHillAttackParams_DeserializesFromJson` | not-fixed |
| `Hrot.SimHost.Tests.HillAttackNodeTests.SC_HA016_3_ParsePlatoonHillAttackParams_ResolvesTargetArea_WhenValid` | not-fixed |

---

### Group HROT-G18: AreaQueryTranslatorTests
**Root cause:** Area query pipeline result not reaching back with targets  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.AreaQueryTranslatorTests.SC_HA004_1_AreaQueryPipeline_BrainRequestReachesBack_WithTargets` | not-fixed |

---

### Group HROT-G19: EditorDependencyTests
**Root cause:** `HrotEditor` assembly has `CycloneDDS.Schema` dependency it should not have  
**Test project:** `Hrot.Editor.Tests`

| Test | Status |
|------|--------|
| `Hrot.Editor.Tests.EditorDependencyTests.HrotEditor_HasNoCycloneDdsDependency` | not-fixed |

---

### Group HROT-G20: ContextActionIngressSystemTests
**Root cause:** Rotate context action not publishing expected event  
**Test project:** `Hrot.SimHost.Tests`

| Test | Status |
|------|--------|
| `Hrot.SimHost.Tests.Gizmos.ContextActionIngressSystemTests.SC_ER007_ValidActionName_KnownEntity_PublishesGlobalActionRequestedEvent` | not-fixed |

---
