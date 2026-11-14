# Census — node `cgf`, phases Simulation,PostSimulation — counts per EXECUTION of each system (its profiler count)

| class | systems | VM ms / frame |
|---|---|---|
| READY | 18 | 6.5 |
| CONVERTIBLE | 11 | 128.1 |
| GLOBAL | 6 | 0.3 |
| NOT SEEN | 36 | 1.4 |

| system | phase | ms | class | cross-entity writes / run | structural on others / run | last-tick reads (types) | 'none' writes / run | read-after-create |
|---|---|---|---|---|---|---|---|---|
| `BrainTickSystem` | Simulation | 69.83 | CONVERTIBLE | `CalloutState` 0.1, `Utterance` 0.1 | StructAdd `PartMetadata` 58.2, StructAdd `Utterance` 0.0 | `Health`, `WeaponState`, `BehaviorState`, `Gesture`, `UnitRoster` (+7) | 0.0 | 162681 |
| `WeaponDispatcherSystem` | Simulation | 15.01 | CONVERTIBLE |  |  | `PartMetadata`, `Health`, `StanceIntent`, `WeaponPosture`, `SimTransform` (+3) | 33.4 | 25896 |
| `StressSystem` | Simulation | 14.57 | CONVERTIBLE |  |  | `Health`, `WeaponState`, `SimTransform`, `BehaviorState`, `Gesture` (+2) | 40.2 | 0 |
| `CalloutSystem` | Simulation | 13.07 | CONVERTIBLE |  |  | `Utterance`, `SimTransform`, `Health`, `BehaviorState` | 0.0 | 32031 |
| `CgfThreatEvaluationSystem` | Simulation | 5.59 | CONVERTIBLE |  |  | `SimTransform`, `Health`, `StanceIntent` | 0.0 | 0 |
| `SquadCoordinationSystem` | Simulation | 4.47 | CONVERTIBLE |  |  | `SimTransform`, `TargetMemory`, `Health`, `SquadMemberSense`, `WeaponState` (+3) | 74.4 | 74 |
| `StaffComposerSystem` | Simulation | 3.29 | CONVERTIBLE |  |  | `Health`, `BehaviorState`, `UnitRoster`, `PartDamage` | 0.0 | 0 |
| `RestlessSystem` | Simulation | 1.45 | CONVERTIBLE |  |  | `Health` | 0.0 | 0 |
| `WeaponAmmoMirrorSystem` | Simulation | 1.16 | READY |  |  |  | 0.0 | 0 |
| `MapLayerAssignmentSystem` | PostSimulation | 0.81 | READY |  |  |  | 0.0 | 51961 |
| `ProficiencyFillSystem` | Simulation | 0.71 | READY |  |  |  | 0.0 | 0 |
| `ChannelArbitrationSystem` | Simulation | 0.60 | READY |  |  |  | 0.0 | 0 |
| `BlueprintTickSystem` | Simulation | 0.56 | READY |  |  |  | 0.0 | 0 |
| `SubEntityCleanupSystem` | PostSimulation | 0.44 | READY |  |  |  | 0.0 | 5364 |
| `UnitCentroidSystem` | Simulation | 0.44 | CONVERTIBLE |  |  | `Health`, `SimTransform`, `UnitRoster` | 0.0 | 0 |
| `CrowdSystem` | Simulation | 0.42 | READY |  |  |  | 0.0 | 0 |
| `MissionAdapterSystem` | Simulation | 0.23 | READY |  |  |  | 0.0 | 0 |
| `CognitiveInterruptSystem` | Simulation | 0.23 | READY |  |  |  | 0.0 | 0 |
| `LocomotionDispatcherSystem` | Simulation | 0.23 | READY |  |  |  | 0.0 | 0 |
| `BleedOutSystem` | Simulation | 0.22 | READY |  |  |  | 0.0 | 0 |
| `DangerAreaSensorSystem` | Simulation | 0.21 | NOT SEEN |  |  |  | 0.0 | 0 |
| `UnderFireMoveSystem` | Simulation | 0.20 | CONVERTIBLE |  |  | `Health` | 0.0 | 0 |
| `HealthApplicationSystem` | Simulation | 0.20 | CONVERTIBLE |  |  |  | 68.6 | 0 |
| `InteractionDispatcherSystem` | Simulation | 0.18 | READY |  |  |  | 0.0 | 0 |
| `GrenadeDangerSystem` | Simulation | 0.17 | READY |  |  |  | 0.0 | 0 |
| `CognitiveCleanupSystem` | Simulation | 0.17 | READY |  |  |  | 0.0 | 0 |
| `CivilianFearSystem` | Simulation | 0.16 | GLOBAL |  |  |  | 0.0 | 0 |
| `DisposalMonitoringSystem` | PostSimulation | 0.16 | NOT SEEN |  |  |  | 0.0 | 0 |
| `RuleWakeSystem` | Simulation | 0.16 | NOT SEEN |  |  |  | 0.0 | 0 |
| `TrafficShadowFollowSystem` | Simulation | 0.15 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EjectionSystem` | Simulation | 0.15 | READY |  |  |  | 0.0 | 0 |
| `MissionDirectorSystem` | Simulation | 0.11 | READY |  |  |  | 0.0 | 0 |
| `AreaEffectCloudSystem` | Simulation | 0.09 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DeadReckoningSyncSystem` | PostSimulation | 0.09 | NOT SEEN |  |  |  | 0.0 | 0 |
| `WeaponDeploySystem` | Simulation | 0.08 | READY |  |  |  | 0.0 | 0 |
| `OneManCommandSystem` | Simulation | 0.08 | NOT SEEN |  |  |  | 0.0 | 0 |
| `RuleEditSystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `LightweightRoundIdentitySystem` | PostSimulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `CustodySystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SpawnLevelSystem` | PostSimulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `HandsFuzeSystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `CoordinateTransformSystem` | PostSimulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `RouteContextSystem` | Simulation | 0.06 | READY |  |  |  | 0.0 | 0 |
| `WorldSurveyApplySystem` | Simulation | 0.04 | GLOBAL |  |  |  | 6.4 | 0 |
| `EqsResultUpdateSystem` | Simulation | 0.03 | GLOBAL |  |  |  | 28.0 | 0 |
| `ActiveSensorTracksUpdateSystem` | Simulation | 0.02 | GLOBAL |  |  |  | 0.0 | 0 |
| `StaffInboxSystem` | Simulation | 0.01 | GLOBAL |  |  |  | 28.8 | 0 |
| `DetonationMemorySystem` | Simulation | 0.01 | NOT SEEN |  |  |  | 0.0 | 0 |
| `NearMissSensingSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EventHistoryCaptureSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `UnitHierarchySystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `CanvasMenuUpdateSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `ExercisePauseOnEndSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SeedTargetSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `BridgeClosureSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EmbarkationSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `MissileWarningSensingSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SelectionRequestSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DataBreakpointSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `RoundPickUpSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `TacticalIntentResolutionSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DoorCommandSystem` | Simulation | 0.00 | GLOBAL |  |  |  | 0.0 | 0 |
| `CarryStationSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `ChargeSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `BehaviorFrameSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `ToolActivationDrainSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SelectionInteractionSystemAdapter` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SelectionNotificationSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EntityRequestFinalizationSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `RuleLeafSampler` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `CenterOnEntitySystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |

## Cross-entity write sites (sampled)

- `BrainTickSystem` StructAdd `BehaviorOwnedPart`: EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.ApproachSensors · EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.PostureSensors
- `BrainTickSystem` StructAdd `Utterance`: Callouts.Say < Callouts.OnIssue < SquadManeuverRunner.Issue < SquadManeuverRunner.Tick
- `BrainTickSystem` StructAdd `PartMetadata`: EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.ApproachSensors · EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.PostureSensors
- `BrainTickSystem` StructAdd `ScenarioIgnoreTag`: EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.ApproachSensors · EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.PostureSensors
- `BrainTickSystem` StructAdd `EqsCognitiveBuffer`: EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.ApproachSensors · EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.PostureSensors
- `BrainTickSystem` StructAdd `EqsSensor`: EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.ApproachSensors · EqsChildSensor.Ensure < PostureNodes.Keep < PostureNodes.KeepPair < PostureNodes.PostureSensors
- `BrainTickSystem` Write `Utterance`: Callouts.Say < Callouts.OnIssue < SquadManeuverRunner.Issue < SquadManeuverRunner.Tick
- `BrainTickSystem` Write `EqsCognitiveBuffer`: EqsChildSensor.Apply < EqsChildSensor.Refresh < PostureNodes.Keep < PostureNodes.KeepPair
- `BrainTickSystem` Write `EqsSensor`: EqsChildSensor.Apply < EqsChildSensor.Refresh < PostureNodes.Keep < PostureNodes.KeepPair
- `BrainTickSystem` Write `CalloutState`: Callouts.Say < Callouts.OnIssue < SquadManeuverRunner.Issue < SquadManeuverRunner.Tick
- `WeaponDispatcherSystem` Write `WeaponState`: ShotLedger.Settle < WeaponDispatcherSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `StressSystem` Write `Stress`: StressModel.Add < StressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `SquadCoordinationSystem` StructAdd `BehaviorOwnedPart`: EqsChildSensor.Ensure < WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` StructAdd `SensorConfigPayload`: WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute
- `SquadCoordinationSystem` StructAdd `SensorTag`: WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute
- `SquadCoordinationSystem` StructAdd `WorldSurveyAnswer`: WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute
- `SquadCoordinationSystem` StructAdd `WorldSurveyRequest`: WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute
- `SquadCoordinationSystem` StructAdd `WorldSurveyStatus`: EqsChildSensor.Ensure < WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` StructAdd `PartMetadata`: EqsChildSensor.Ensure < WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` StructAdd `ScenarioIgnoreTag`: EqsChildSensor.Ensure < WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` StructAdd `EqsSensor`: EqsChildSensor.Ensure < WorldSurveyChildSensor.Ensure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` StructAdd `SquadMemberSense`: SquadSenseChanges.Store < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute < SystemScheduler.ExecuteSystem
- `SquadCoordinationSystem` StructAdd `SquadUnitSense`: SquadSenseChanges.Store < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute
- `SquadCoordinationSystem` Write `SensorConfigPayload`: WorldSurveyChildSensor.SetRequest < WorldSurveyChildSensor.Configure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` Write `WorldSurveyAnswer`: EqsChildSensor.Apply < EqsChildSensor.Refresh < WorldSurveyChildSensor.Configure < SquadSenseChanges.AskStructure
- `SquadCoordinationSystem` Write `WorldSurveyRequest`: WorldSurveyChildSensor.SetRequest < WorldSurveyChildSensor.Configure < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds
- `SquadCoordinationSystem` Write `WorldSurveyStatus`: EqsChildSensor.Apply < EqsChildSensor.Refresh < WorldSurveyChildSensor.Configure < SquadSenseChanges.AskStructure
- `SquadCoordinationSystem` Write `EqsSensor`: EqsChildSensor.Apply < EqsChildSensor.Refresh < WorldSurveyChildSensor.Configure < SquadSenseChanges.AskStructure
- `SquadCoordinationSystem` Write `SquadMemberSense`: SquadSenseChanges.Store < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute < SystemScheduler.ExecuteSystem
- `SquadCoordinationSystem` Write `SquadUnitSense`: SquadSenseChanges.Store < SquadSenseChanges.AskStructure < SquadSenseChanges.Rounds < SquadCoordinationSystem.Execute
- `OwnershipRecomputeSystem` StructRemove `OutgoingGrantsPending`: OwnershipRecomputeSystem.Recompute < OwnershipRecomputeSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase
- `HealthApplicationSystem` StructAdd `PartDamage`: HealthApplicationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `HealthApplicationSystem` Write `Health`: HealthApplicationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `HealthApplicationSystem` Write `PartDamage`: HealthApplicationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `BehaviorIngressSystem` EntityDestroy `(entity)`: BehaviorOwnedParts.ReleaseCore < BehaviorOwnedParts.Release < BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute
- `BehaviorIngressSystem` StructAdd `BehaviorStartRecord`: BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `BehaviorIngressSystem` StructAdd `PausedTask`: BehaviorIngressSystem.AfterAdmitted < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup · BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `BehaviorIngressSystem` StructRemove `BehaviorStartRecord`: BehaviorIngressSystem.Clear < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `BehaviorIngressSystem` StructRemove `PausedTask`: BehaviorIngressSystem.DropPausedTask < BehaviorIngressSystem.AfterAdmitted < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem · BehaviorIngressSystem.DropPausedTask < BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `BehaviorIngressSystem` StructRemove `SubordinateOrders`: SubordinateOrders.Release < BehaviorOwnedParts.ReleaseCore < BehaviorOwnedParts.Release < BehaviorIngressSystem.Start
- `BehaviorIngressSystem` Write `BehaviorStartRecord`: BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `BehaviorIngressSystem` Write `BehaviorState`: BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `BehaviorIngressSystem` Write `BrainJournal`: BrainJournalOf.Write < BehaviorIngressSystem.JournalStarted < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `BehaviorIngressSystem` Write `BlueprintBlackboard1024`: <>c__31`1.<For>b__31_1 < BehaviorIngressSystem.Clear < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem · <>c__31`1.<For>b__31_1 < BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `BehaviorIngressSystem` Write `BlueprintBlackboard256`: <>c__31`1.<For>b__31_1 < BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `BehaviorIngressSystem` Write `BlueprintBlackboard4096`: <>c__31`1.<For>b__31_1 < BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem · BehaviorIngressSystem.Start < BehaviorIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `WorldSurveyApplySystem` Write `WorldSurveyAnswer`: WorldSurveyApplySystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `WorldSurveyApplySystem` Write `WorldSurveyStatus`: WorldSurveyApplySystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `EqsResultUpdateSystem` Write `EqsCognitiveBuffer`: EqsResultUpdateSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `RecentSensesSystem` StructAdd `RecentSenses`: RecentSensesSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `RecentSensesSystem` Write `RecentSenses`: RecentSensesSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `StaffInboxSystem` Write `CommandPicture`: StaffInboxSystem.Fold < StaffInboxSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `StaffInboxSystem` Write `CommandReportHistory`: CommandHistoryOf.Report < StaffInboxSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `CycloneEgressSystem` StructAdd `DescriptorOwnership`: OwnershipApplier.Apply < EqsSensorConfigEgressTranslator.RecordSolver < EqsSensorConfigEgressTranslator.ScanAndPublish < CycloneEgressSystem.Execute
- `CycloneEgressSystem` Write `DescriptorOwnership`: OwnershipApplier.Apply < EqsSensorConfigEgressTranslator.RecordSolver < EqsSensorConfigEgressTranslator.ScanAndPublish < CycloneEgressSystem.Execute
- `CycloneNetworkIngressSystem` EntityDestroy `(entity)`: EntityMasterIngressTranslator.ProcessDispose < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `CycloneNetworkIngressSystem` StructAdd `GhostStateTracker`: GhostCreationSystem.CreateGhost < EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute
- `CycloneNetworkIngressSystem` StructAdd `LightweightRound`: EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `CycloneNetworkIngressSystem` StructAdd `NetworkIdentity`: GhostCreationSystem.CreateGhost < EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute
- `CycloneNetworkIngressSystem` StructAdd `ScenarioIgnoreTag`: EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `CycloneNetworkIngressSystem` StructRemove `GhostStateTracker`: EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute < SystemScheduler.ExecuteSystem
- `CycloneNetworkIngressSystem` Write `(entity lifecycle state)`: EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute < SystemScheduler.ExecuteSystem · GhostCreationSystem.CreateGhost < EntityMasterIngressTranslator.ProcessSample < EntityMasterIngressTranslator.PollIngress < CycloneNetworkIngressSystem.Execute
- `OwnershipIngressSystem` StructAdd `DescriptorOwnership`: OwnershipApplier.Apply < OwnershipIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase
- `OwnershipIngressSystem` Write `DescriptorOwnership`: OwnershipApplier.Apply < OwnershipIngressSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase
- `DoorCommandSystem` Write `DoorState`: DoorCommandSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
