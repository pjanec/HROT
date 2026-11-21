# Census — node `simhost`, phases Simulation,PostSimulation — counts per EXECUTION of each system (its profiler count)

| class | systems | VM ms / frame |
|---|---|---|
| READY | 21 | 5.3 |
| CONVERTIBLE | 6 | 39.4 |
| GLOBAL | 8 | 0.4 |
| NOT SEEN | 31 | 1.1 |

| system | phase | ms | class | cross-entity writes / run | structural on others / run | last-tick reads (types) | 'none' writes / run | read-after-create |
|---|---|---|---|---|---|---|---|---|
| `CarKinematicsSystem` | PostSimulation | 37.15 | CONVERTIBLE |  |  | `SimTransform`, `SimVelocity` | 0.0 | 0 |
| `MapLayerAssignmentSystem` | PostSimulation | 0.85 | READY |  |  |  | 0.0 | 247347 |
| `LookFollowSystem` | PostSimulation | 0.79 | CONVERTIBLE |  |  | `SimTransform` | 0.0 | 0 |
| `DoorPassageSystem` | Simulation | 0.67 | READY |  |  |  | 0.0 | 0 |
| `PathTraceSystem` | PostSimulation | 0.50 | CONVERTIBLE |  |  |  | 3.5 | 0 |
| `SpatialHashSystem` | Simulation | 0.43 | READY |  |  |  | 0.0 | 41088 |
| `SubEntityCleanupSystem` | PostSimulation | 0.40 | READY |  |  |  | 0.0 | 1623 |
| `SoundEmissionSystem` | Simulation | 0.38 | CONVERTIBLE |  |  |  | 65.0 | 0 |
| `EqsResultUpdateSystem` | Simulation | 0.32 | CONVERTIBLE |  |  |  | 34.8 | 1042 |
| `NavigationExecutionSystem` | Simulation | 0.28 | READY |  |  |  | 0.0 | 0 |
| `BallisticsSystem` | PostSimulation | 0.26 | CONVERTIBLE | `UnderFire` 373.5 | StructAdd `UnderFire` 0.4 | `UnderFire` | 0.0 | 41088 |
| `ThermalHeatSystem` | Simulation | 0.25 | READY |  |  |  | 0.0 | 0 |
| `AnimationStateReporterSystem` | PostSimulation | 0.23 | READY |  |  |  | 0.0 | 0 |
| `BleedOutSystem` | Simulation | 0.23 | READY |  |  |  | 0.0 | 0 |
| `AnimationRuntimeBridgeSystem` | Simulation | 0.23 | READY |  |  |  | 0.0 | 0 |
| `DangerAreaSensorSystem` | Simulation | 0.22 | NOT SEEN |  |  |  | 0.0 | 0 |
| `LookAtDispatcherSystem` | Simulation | 0.21 | READY |  |  |  | 0.0 | 145 |
| `NotifyEventEmitterSystem` | PostSimulation | 0.19 | READY |  |  |  | 0.0 | 0 |
| `AirMotionSystem` | PostSimulation | 0.19 | READY |  |  |  | 0.0 | 0 |
| `HealthApplicationSystem` | Simulation | 0.17 | READY |  |  |  | 0.0 | 0 |
| `TrafficShadowFollowSystem` | Simulation | 0.17 | READY |  |  |  | 0.0 | 0 |
| `EjectionSystem` | Simulation | 0.16 | READY |  |  |  | 0.0 | 0 |
| `StanceTransitionSystem` | Simulation | 0.15 | READY |  |  |  | 0.0 | 0 |
| `LinearKinematicsSystem` | PostSimulation | 0.12 | READY |  |  |  | 0.0 | 13696 |
| `CountermeasureSystem` | PostSimulation | 0.11 | GLOBAL |  |  |  | 0.0 | 0 |
| `NavigationIntentBridgeSystem` | Simulation | 0.11 | GLOBAL |  |  |  | 9.3 | 0 |
| `DescentLandingSystem` | PostSimulation | 0.11 | NOT SEEN |  |  |  | 0.0 | 0 |
| `AreaEffectCloudSystem` | Simulation | 0.11 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DeadReckoningSyncSystem` | PostSimulation | 0.10 | NOT SEEN |  |  |  | 0.0 | 0 |
| `LightweightRoundIdentitySystem` | PostSimulation | 0.09 | GLOBAL |  |  |  | 13.0 | 0 |
| `GuidedRoundSystem` | PostSimulation | 0.09 | READY |  |  |  | 0.0 | 0 |
| `RouteTrajectorySyncSystem` | Simulation | 0.09 | READY |  |  |  | 0.0 | 0 |
| `AnimationDispatcherSystem` | Simulation | 0.08 | GLOBAL |  |  |  | 0.0 | 145 |
| `WorldSurveyApplySystem` | Simulation | 0.08 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DroneImpactSystem` | PostSimulation | 0.08 | READY |  |  |  | 0.0 | 0 |
| `WeaponDeploySystem` | Simulation | 0.08 | READY |  |  |  | 0.0 | 0 |
| `CoordinateTransformSystem` | PostSimulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SpawnLevelSystem` | PostSimulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `HandsFuzeSystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `CustodySystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `MontageQueueAdvanceSystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `FormationTargetSystem` | Simulation | 0.07 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DisposalMonitoringSystem` | PostSimulation | 0.06 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DamageCalculationSystem` | Simulation | 0.01 | GLOBAL |  |  |  | 0.0 | 0 |
| `CombatTraceSystem` | PostSimulation | 0.01 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EventHistoryCaptureSystem` | PostSimulation | 0.01 | NOT SEEN |  |  |  | 0.0 | 0 |
| `AnimationBackendCleanupSystem` | PostSimulation | 0.00 | GLOBAL |  |  |  | 0.0 | 145 |
| `CanvasMenuUpdateSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SelectionRequestSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `StaffInboxSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `AreaEffectSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `GroundClampWhilePausedSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `VehicleCommandSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `TrajectoryRestoreSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `BuildingDamageSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `BridgeClosureSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `UnitHierarchySystem` | Simulation | 0.00 | GLOBAL |  |  |  | 0.0 | 290 |
| `AirRouteCompileSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `SelectionNotificationSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EntityRequestFinalizationSystem` | PostSimulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `ChargeSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `DoorCommandSystem` | Simulation | 0.00 | GLOBAL |  |  |  | 0.0 | 0 |
| `SeedTargetSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `CarryStationSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `EmbarkationSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |
| `RoundPickUpSystem` | Simulation | 0.00 | NOT SEEN |  |  |  | 0.0 | 0 |

## Cross-entity write sites (sampled)

- `EqsSolverSystem` Write `EqsCognitiveBuffer`: EqsSolverSystem.Commit < EqsSolverSystem.CommitAndPersist < EqsSolverSystem.Execute < SystemScheduler.ExecuteSystem
- `PathTraceSystem` StructAdd `PathTrace`: PathTraceSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `PathTraceSystem` Write `PathTrace`: PathTraceSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `FireProcessingSystem` StructAdd `SimTransform`: FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `FireProcessingSystem` StructAdd `SimVelocity`: FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `FireProcessingSystem` StructAdd `BallisticProjectile`: FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `FireProcessingSystem` StructAdd `ShotOrdinal`: HitModel.NextOrdinal < FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `FireProcessingSystem` StructAdd `PhysicsCollider`: FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `FireProcessingSystem` StructAdd `LightweightRound`: FireProcessingSystem.MarkIfVisible < FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `FireProcessingSystem` StructAdd `TkbIdentity`: FireProcessingSystem.MarkIfVisible < FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `FireProcessingSystem` StructAdd `ScenarioIgnoreTag`: FireProcessingSystem.MarkIfVisible < FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `FireProcessingSystem` Write `ShotOrdinal`: FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem · HitModel.NextOrdinal < FireProcessingSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `SoundEmissionSystem` Write `AcousticEmitter`: SoundEmissionSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `SoundEmissionSystem` Write `DoorHeard`: SoundEmissionSystem.HearDoors < SoundEmissionSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup
- `EqsResultUpdateSystem` Write `EqsCognitiveBuffer`: EqsResultUpdateSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `BallisticsSystem` StructAdd `UnderFire`: HitModel.StampUnderFire < BallisticsSystem.ReportNearMisses < BallisticsSystem.Execute < SystemScheduler.ExecuteSystem
- `BallisticsSystem` Write `UnderFire`: HitModel.StampUnderFire < BallisticsSystem.ReportNearMisses < BallisticsSystem.Execute < SystemScheduler.ExecuteSystem
- `NavigationIntentBridgeSystem` Write `NavState`: NavigationIntentBridgeSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `NavigationIntentBridgeSystem` Write `NavigationApplied`: NavigationIntentBridgeSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `LightweightRoundIdentitySystem` StructAdd `NetworkAuthority`: LightweightRoundIdentitySystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
- `LightweightRoundIdentitySystem` StructAdd `NetworkIdentity`: LightweightRoundIdentitySystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
- `HitResolutionSystem` StructAdd `UnderFire`: HitResolutionSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `HitResolutionSystem` StructAdd `HitMarks`: HitResolutionSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `HitResolutionSystem` Write `UnderFire`: HitResolutionSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `HitResolutionSystem` Write `HitMarks`: HitResolutionSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecuteGroup < SystemScheduler.ExecuteSystem
- `CycloneNetworkIngressSystem` StructAdd `DescriptorOwnership`: OwnershipApplier.Apply < EqsSensorConfigIngressTranslator.Apply < EqsSensorConfigIngressTranslator.ApplyPending < EqsSensorConfigIngressTranslator.PollIngress
- `CycloneNetworkIngressSystem` StructAdd `GhostStateTracker`: GhostCreationSystem.CreateGhost < GeoSpatialIngressTranslator.Decode < CycloneTranslator`2.PollIngress < CycloneNetworkIngressSystem.Execute
- `CycloneNetworkIngressSystem` StructAdd `NetworkIdentity`: GhostCreationSystem.CreateGhost < GeoSpatialIngressTranslator.Decode < CycloneTranslator`2.PollIngress < CycloneNetworkIngressSystem.Execute
- `CycloneNetworkIngressSystem` Write `(entity lifecycle state)`: GhostCreationSystem.CreateGhost < GeoSpatialIngressTranslator.Decode < CycloneTranslator`2.PollIngress < CycloneNetworkIngressSystem.Execute
- `CycloneNetworkIngressSystem` Write `DescriptorOwnership`: OwnershipApplier.Apply < EqsSensorConfigIngressTranslator.Apply < EqsSensorConfigIngressTranslator.ApplyPending < EqsSensorConfigIngressTranslator.PollIngress
- `EngineBackedPathResponseSystem` Write `NavState`: EngineBackedPathResponseSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
- `PathfindingResultMaterializationSystem` StructAdd `NavigationCorridorMuscle`: PathfindingResultMaterializationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
- `PathfindingResultMaterializationSystem` Write `NavState`: PathfindingResultMaterializationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
- `PathfindingResultMaterializationSystem` Write `NavigationCorridorMuscle`: PathfindingResultMaterializationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
- `PathfindingResultMaterializationSystem` Write `NavigationStatus`: PathfindingResultMaterializationSystem.Execute < SystemScheduler.ExecuteSystem < SystemScheduler.ExecutePhase < ModuleHostKernel.UpdateInternalCore
