namespace Fdp.Core
{
    /// <summary>
    /// Central catalog of globally unique ECS component IDs, allocated in named blocks.
    ///
    /// <para>
    /// Each ID constant is used in conjunction with <see cref="ComponentIdAttribute"/> on
    /// the corresponding component struct, guaranteeing deterministic and collision-free ID
    /// assignment regardless of assembly load order.  This is a prerequisite for merging
    /// SimHost, IG, and ExCon into a single Runner process (Phase R0).
    /// </para>
    ///
    /// <para><b>ID block allocation</b></para>
    /// <list type="table">
    ///   <item><term>0â€“19</term>  <description>Fdp.Core core components</description></item>
    ///   <item><term>20â€“49</term> <description>FDP toolkit expansion: Behavior, Physics, Combat, CarKinem, Geographic</description></item>
    ///   <item><term>50â€“79</term> <description>FDP.Toolkit.Replication components</description></item>
    ///   <item><term>80â€“109</term><description>FDP.Toolkit.Vis2D components</description></item>
    ///   <item><term>110â€“139</term><description>IG components</description></item>
    ///   <item><term>140â€“159</term><description>ModuleHost network components (Cyclone)</description></item>
    ///   <item><term>160â€“199</term><description>Application-level descriptor components</description></item>
    ///   <item><term>200â€“255</term><description>Reserved â€” examples and future use</description></item>    ///   <item><term>256â€"299</term><description>Squad coordination components</description></item>    /// </list>
    ///
    /// <para>
    /// When adding a new component, pick the next unused ID within the appropriate block,
    /// add a constant here, and decorate the struct with
    /// <c>[ComponentId(GlobalComponentIds.YourNewComponent)]</c>.
    /// </para>
    /// </summary>
    public static class GlobalComponentIds
    {
        // â”€â”€ Fdp.Core (0â€“19) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Core simulation components present in every FDP application.

        /// <summary><see cref="SimTransform"/> â€” world position and orientation.</summary>
        public const int SimTransform        = 0;

        /// <summary><see cref="SimVelocity"/> â€” linear and angular velocity.</summary>
        public const int SimVelocity         = 1;

        /// <summary>Reserved (was <c>HealthData</c> mirror, removed by BUG2-A001). ID preserved for serialization compatibility.</summary>
        public const int HealthData          = 2;

        /// <summary><see cref="GlobalTime"/> â€” simulation time singleton.</summary>
        public const int GlobalTime          = 3;

        /// <summary>Reserved (was <c>IsActiveTag</c>, removed). ID preserved for serialization compatibility.</summary>
        public const int IsActiveTag         = 4;

        /// <summary>Reserved (was <c>LifecycleDescriptor</c>, removed). ID preserved for serialization compatibility.</summary>
        public const int LifecycleDescriptor = 5;

        /// <summary><see cref="HierarchyNode"/> â€” parent/child linked-list node.</summary>
        public const int HierarchyNode       = 6;

        /// <summary><see cref="PartDescriptor"/> â€” bitmask of present component parts (network sync).</summary>
        public const int PartDescriptor      = 7;

        // IDs 8â€“19 are reserved for future Fdp.Core core components.

        // â”€â”€ FDP.Toolkit expansion (20â€“49) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Toolkit components added after initial release: geographic, behavior, physics.

        /// <summary><c>GeoTransform</c> â€” geodetic position and orientation (WGS-84).</summary>
        public const int GeoTransform            = 20;

        /// <summary><c>GeoVelocity</c> â€” geodetic velocity and acceleration (ENU frame).</summary>
        public const int GeoVelocity             = 21;

        /// <summary><c>BehaviorState</c> â€” active behavior (behavior tree / HSM) for an entity.</summary>
        public const int BehaviorState           = 22;

        /// <summary><c>BrainBlackboard</c> â€” shared blackboard memory for behavior execution.</summary>
        public const int BrainBlackboard         = 23;

        /// <summary><c>LocomotionChannel</c> â€” active locomotion action slot for behavior control.</summary>
        public const int LocomotionChannel       = 24;

        /// <summary><c>WeaponChannel</c> â€” active weapon action slot for behavior control.</summary>
        public const int WeaponChannel           = 25;

        /// <summary><c>InteractionChannel</c> â€” active interaction action slot for behavior control.</summary>
        public const int InteractionChannel      = 26;

        /// <summary><c>PreviousCapabilities</c> â€” shadow of last-frame actor capability bitmask.</summary>
        public const int PreviousCapabilities    = 27;

        /// <summary><c>ActorCapabilityState</c> â€” current actor capability bitmask.</summary>
        public const int ActorCapabilityState    = 28;

        /// <summary><c>BrainBTreeState</c> â€” behavior tree runtime state for an entity brain.</summary>
        public const int BrainBTreeState         = 29;

        /// <summary><c>VehicleState</c> â€” kinematic vehicle physics state (speed, steer, accel).</summary>
        public const int VehicleState            = 30;

        /// <summary><c>VehicleParams</c> â€” static vehicle configuration parameters.</summary>
        public const int VehicleParams           = 31;

        /// <summary><c>NavState</c> â€” navigation and locomotion controller state.</summary>
        public const int NavState                = 32;

        /// <summary><c>FormationController</c> â€” formation type and parameters (attached to leader).</summary>
        public const int FormationController     = 33;

        /// <summary><c>SimTier</c> â€” simulation tier level for entity brain prioritization.</summary>
        public const int SimTier                 = 34;

        /// <summary><c>BrainHsm64</c> â€” 64-slot HSM runtime state for entity brains.</summary>
        public const int BrainHsm64              = 35;

        /// <summary><c>BrainHsm128</c> â€” 128-slot HSM runtime state for entity brains.</summary>
        public const int BrainHsm128             = 36;

        /// <summary><c>PassengerBuffer</c> â€” fixed-capacity passenger roster on a vehicle entity.</summary>
        public const int PassengerBuffer         = 37;

        /// <summary><c>IsEmbarkedTag</c> â€” tag marking a soldier currently aboard a vehicle.</summary>
        public const int IsEmbarkedTag           = 38;

        /// <summary><c>MissionPlanQueue</c> â€” ordered queue of mission phases for the mission director.</summary>
        public const int MissionPlanQueue        = 39;

        /// <summary><c>PhysicsCollider</c> â€” bounding-circle collider for broadphase and raycast tests.</summary>
        public const int PhysicsCollider         = 40;

        /// <summary><c>RaycastBatchData</c> â€” singleton pre-allocated raycast request/result batch.</summary>
        public const int RaycastBatchData        = 41;

        /// <summary><c>WeaponState</c> â€” ammo count and cooldown state of a weapon attachment (Combat toolkit).</summary>
        public const int WeaponState             = 42;

        /// <summary><c>CombatHealth</c> â€” hit-point pool for combat entities (Combat toolkit).</summary>
        public const int CombatHealth            = 43;

        /// <summary><c>BallisticProjectile</c> â€” marks a bullet entity with shooter reference and sweep data.</summary>
        public const int BallisticProjectile     = 44;

        /// <summary><c>FormationFollower</c> â€” formation membership data for follower vehicles.</summary>
        public const int FormationFollower       = 45;

        /// <summary><c>FormationTarget</c> â€” transient scratchpad driving target written by FormationTargetSystem.</summary>
        public const int FormationTarget         = 46;

        /// <summary><c>SpatialGridData</c> â€” singleton spatial hash grid produced by SpatialHashSystem.</summary>
        public const int SpatialGridData         = 47;

        /// <summary><c>GeoPosition</c> â€” flat-earth 3-D position in the geographic module.</summary>
        public const int GeoPosition             = 48;

        /// <summary><c>GeoPositionGeodetic</c> â€” managed WGS-84 geodetic position in the geographic module.</summary>
        public const int GeoPositionGeodetic     = 49;

        // â”€â”€ FDP.Toolkit.Replication (50â€“79) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Network identity and replication state managed by the Replication toolkit.

        /// <summary><c>NetworkIdentity</c> â€” globally unique ID across the distributed system.</summary>
        public const int NetworkIdentity     = 50;

        /// <summary><c>NetworkAuthority</c> â€” ownership / authority for a networked entity.</summary>
        public const int NetworkAuthority    = 51;

        /// <summary><c>NetworkTransform</c> â€” shadow of the last-published or last-received position and orientation (no-record policy).</summary>
        public const int NetworkTransform    = 52;

        /// <summary><c>NetworkVelocity</c> â€” replicated velocity (no-record policy).</summary>
        public const int NetworkVelocity     = 53;

        /// <summary><c>NetworkSpawnRequest</c> â€” REMOVED (replaced by <c>TkbIdentity</c>). ID 54 is reserved.</summary>
        [System.Obsolete("NetworkSpawnRequest has been removed. Use TkbIdentity instead.")]
        public const int NetworkSpawnRequest = 54;

        /// <summary><c>PartMetadata</c> â€” back-reference to parent entity and descriptor ordinal.</summary>
        public const int PartMetadata        = 55;

        // IDs 56â€“79 are reserved for future Replication toolkit components.

        /// <summary><c>BinaryGhostStore</c> â€” REMOVED (ghost data now stored as real ECS components). ID 56 is reserved.</summary>
        [System.Obsolete("BinaryGhostStore has been removed. Ghost entities now accumulate real ECS components directly.")]
        public const int BinaryGhostStore        = 56;

        /// <summary><c>TkbIdentity</c> â€” permanent blueprint type identity component replacing <c>NetworkSpawnRequest</c>.</summary>
        public const int TkbIdentity             = 65;

        /// <summary><c>GhostStateTracker</c> â€” tracks the birth frame of a ghost entity for promotion and timeout logic.</summary>
        public const int GhostStateTracker       = 66;

        // 67â€“68 freed â€” moved to FDP.Toolkit.Navigation.Contracts (NavigationContractsComponentIds).

        /// <summary><c>ChildMap</c> â€” maps sub-entity instance IDs to local ECS entities.</summary>
        public const int ChildMap                = 57;

        /// <summary><c>EgressPublicationState</c> â€” smart-egress dirty-tracking state per entity.</summary>
        public const int EgressPublicationState  = 58;

        /// <summary><c>DescriptorOwnership</c> â€” per-descriptor ownership map for split-authority.</summary>
        public const int DescriptorOwnership     = 59;

        /// <summary><c>ITkbDatabase</c> â€” TKB database singleton injected into ECS world.</summary>
        public const int ITkbDatabase            = 60;

        /// <summary><c>INetworkTopology</c> â€” network topology singleton injected into ECS world.</summary>
        public const int INetworkTopology        = 61;

        /// <summary><c>BlockIdManager</c> â€” network ID block allocator service singleton.</summary>
        public const int BlockIdManager          = 62;

        /// <summary><c>ISerializationRegistry</c> â€” ghost-protocol serialization registry singleton.</summary>
        public const int ISerializationRegistry  = 63;

        /// <summary><c>NetworkEntityMap</c> â€” bidirectional map between network IDs and ECS entities.</summary>
        public const int NetworkEntityMap        = 64;

        // IDs 65â€“79 are reserved for future toolkit components.
        // 67â€“68 freed â€” moved to FDP.Toolkit.Navigation.Contracts (NavigationContractsComponentIds).

        /// <summary>
        /// <c>FrustrationTicks</c> â€” per-entity frustration counter used by
        /// <c>NavigationExecutionSystem</c> to detect stuck vehicles.  Replaces the
        /// previous dictionary-based counter to allow automatic reclamation on entity
        /// destruction.  See <c>FrustrationTicks.cs</c> in <c>FDP.Toolkit.CarKinem</c>.
        /// </summary>
        public const int FrustrationTicks = 69;

        // â”€â”€ FDP.Toolkit expansion â€” additional toolkit IDs (70â€“79) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Toolkit components added after the 20â€“49 block was exhausted.

        /// <summary><c>InFormationTag</c> â€” tag added to an entity that has successfully joined a formation slot (FDP.Toolkit.Navigation).</summary>
        public const int InFormationTag          = 70;

        /// <summary><c>Faction</c> â€” REMOVED. Entity side identifier (replaced by <see cref="EntityInfo"/> from <c>Hrot.IG.Components</c>). ID reserved for serialization compatibility.</summary>
        [System.Obsolete("Faction has been removed. Use Hrot.IG.Components.EntityInfo.ForceId instead.")]
        public const int Faction                 = 71;

        /// <summary><c>PerceptionReceptor</c> â€” combined sensor range/FOV parameters (FDP.Toolkit.Perception).</summary>
        public const int PerceptionReceptor      = 72;

        /// <summary><c>TargetMemory</c> â€” fixed-size threat table for perceived targets (FDP.Toolkit.Perception).</summary>
        public const int TargetMemory            = 73;

        /// <summary><c>Blackboard1024</c> â€” 1024-byte generic heavy blackboard for behavior-specific large payloads.
        /// Reusable across different behaviors to avoid exhausting the 256 component-type limit.
        /// Projected into behavior-specific DTOs via <c>Unsafe.As</c> inside generated action thunks.</summary>
        public const int Blackboard1024          = 74;

        /// <summary><c>PathfindingBatchData</c> â€” zero-allocation singleton for batched pathfinding requests/results (FDP.Toolkit.Navigation).</summary>
        public const int PathfindingBatchData    = 76;

        // IDs 77â€“79 are defined in GeographicComponentIds (Fdp.Toolkit.Geographic).
        // GroundClampingConfig = 77, TerrainClampBaseline = 78, TerrainQueryBatchData = 79.

        // â”€â”€ FDP.Toolkit.Vis2D (80â€“109) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 2-D visualisation and map-layer components.

        /// <summary><c>MapDisplayComponent</c> â€” layer-mask controlling map visibility.</summary>
        public const int MapDisplayComponent = 80;

        /// <summary><c>VisHierarchyNode</c> â€” parent/child relationships for ORGBAT entities.</summary>
        public const int VisHierarchyNode    = 81;

        /// <summary><c>AggregateState</c> â€” centroid and bounding box of a logical node's children.</summary>
        public const int AggregateState      = 82;

        /// <summary><c>AggregateRoot</c> â€” tag marking root entities in the Vis2D hierarchy.</summary>
        public const int AggregateRoot       = 83;

        // IDs 84â€“109 are reserved for future Vis2D toolkit components.

        // â”€â”€ IG (110â€“139) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Image Generator ECS components used for rendering and interaction.

        /// <summary><c>ResolvedStyle</c> â€” computed visual rendering state (texture, tint, label).</summary>
        public const int ResolvedStyle       = 110;

        /// <summary><c>CullingState</c> â€” viewport-culling and LOD state written by MapCullingSystem.</summary>
        public const int CullingState        = 111;

        /// <summary><c>SelectionState</c> â€” operator selection state written by StandardInteractionTool.</summary>
        public const int SelectionState      = 112;

        /// <summary><c>VisualEffectState</c> â€” lifecycle and colour state of a temporary visual effect entity.</summary>
        public const int VisualEffectState   = 113;

        /// <summary><c>TracerTarget</c> â€” world-space endpoint of a tracer-line effect.</summary>
        public const int TracerTarget        = 114;

        // IDs 115â€“139 are reserved for future IG components.

        /// <summary><c>HistoryTrail</c> â€” circular-buffer of recent world-space positions for trail rendering.</summary>
        public const int HistoryTrail            = 115;

        /// <summary><c>ContextMenuState</c> â€” managed component holding active context-menu actions.</summary>
        public const int ContextMenuState        = 116;

        /// <summary><c>EditablePolyline</c> â€” managed component storing vertex list of a user-editable overlay.</summary>
        public const int EditablePolyline        = 117;

        /// <summary><c>VisualData</c> â€” runtime TKB visual data (symbol, model path, colour) cached on entity.</summary>
        public const int VisualData              = 118;

        // ID 119 was IgSymbolOverride; moved to project specific IgSymbolOverride = 167 (DB-MOD1-22).

        /// <summary><c>MapOverlayStyle</c> â€” rendering style (fill colour, border colour, line thickness) for a map visual overlay.</summary>
        public const int MapOverlayStyle         = 120;

        /// <summary><c>SimCombatDef</c> â€” TKB combat definition used by SimHost.</summary>
        public const int SimCombatDef            = 121;

        /// <summary><c>TkbCompositionDef</c> â€” TKB composite unit definition.</summary>
        public const int TkbCompositionDef       = 122;

        /// <summary><c>IgMissionHolder</c> â€” managed component caching decoded EntityMission for rendering.</summary>
        public const int IgMissionHolder             = 123;

        // IDs 124â€“139 are reserved for future IG components.

        // â”€â”€ ModuleHost Network (140â€“159) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Network ownership and coordination components managed by CycloneNetworkModule.

        /// <summary><c>NetworkOwnership</c> â€” tracks primary network owner and local node ID.</summary>
        public const int NetworkOwnership        = 140;

        /// <summary><c>PendingNetworkAck</c> â€” transient tag for entities awaiting reliable-init acknowledgment.</summary>
        public const int PendingNetworkAck       = 141;

        /// <summary><c>ForceNetworkPublish</c> â€” tag forcing immediate descriptor publication, bypassing dirty-check.</summary>
        public const int ForceNetworkPublish     = 142;

        /// <summary><c>NetworkOrientation</c> â€” replicated orientation (quaternion) for Cyclone-networked entities.</summary>
        public const int NetworkOrientation      = 143;

        /// <summary><c>PendingAuthorityGrants</c> â€” transient component caching descriptor ownership intents from a DeferredTakeOwnership message. Stripped once the entity enters Constructing.</summary>
        public const int PendingAuthorityGrants  = 144;

        // IDs 145â€“159 are reserved for future ModuleHost network components.

        // â”€â”€ Application-level Descriptors (160â€“199) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // These IDs are now declared in project specific ComponentIds.
        // ID 161 (EntityDamage) is kept here only as a cross-reference comment.
        // All other application-level IDs (162â€“166) have been migrated to project specific Ids.
        //
        // DO NOT add new project specific component IDs here â€” use project specific ComponentIds instead.

        // IDs 160â€“199 are reserved for project specific application-level components.

        /// <summary><c>EntityInfo / IgEntityData</c> â€” IG-internal entity metadata (name, force affiliation, commander). Moved to Fdp.Core to remove the Faction component.</summary>
        public const int EntityInfo            = 164;

        // â”€â”€ Commander-Subordinate hierarchy components (AI tier, IDs 182-184 reserved in HrotComponentIds) â”€

        /// <summary><c>UnitRoster</c> â€” fixed-capacity subordinate list on the commanding entity (AI tier); NoSave (derived from UnitSubordinate records).</summary>
        public const int UnitRoster = 182;

        /// <summary><c>UnitSubordinate</c> â€” generation-safe commander reference and tactical designation on subordinate entities (AI tier).</summary>
        public const int UnitSubordinate = 183;

        /// <summary><c>InitialUnitSubordinateIntent</c> â€” genesis intent DTO storing network commander ID at scenario load.</summary>
        public const int InitialUnitSubordinateIntent = 184;

        // â”€â”€ Zone toolkit (201+) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // NOTE: IDs 20â€“79 are fully allocated across toolkit expansion blocks.
        // ID 200 is reserved by FDP.Toolkit.Scenario (ScenarioComponentIds.ScenarioIgnoreTag).
        // Zone-environment components start at 201.

        /// <summary>
        /// <c>ZoneEnvironmentData</c> â€” ECS singleton carrying the active zone's static environment
        /// (road network blob, terrain reference).  Written by <c>ZoneManagerService</c> on scenario
        /// load; read by <c>CarKinematicsSystem</c> to obtain road-graph data without constructor
        /// injection.  Defined in <c>FDP.Toolkit.CarKinem</c>.
        /// </summary>
        public const int ZoneEnvironmentData = 201;

        /// <summary><c>AreaQueryBatchData</c> â€” ECS singleton for batched area query requests and results (EQS pipeline).</summary>
        public const int AreaQueryBatchData = 202;

        /// <summary><c>EqsTargetPool</c> â€” ECS singleton native array pool for packed entity handles returned by area queries.</summary>
        public const int EqsTargetPool = 203;

        /// <summary><c>BlueprintBlackboard1024</c> - 1024-byte tier component for Blueprint Instance state.</summary>
        public const int BlueprintBlackboard1024 = 204;

        /// <summary><c>BlueprintBlackboard4096</c> - 4096-byte tier component for Blueprint Instance state.</summary>
        public const int BlueprintBlackboard4096 = 205;

        /// <summary><c>BlueprintBlackboard16384</c> - 16384-byte tier component for Blueprint Instance state.</summary>
        public const int BlueprintBlackboard16384 = 206;

        /// <summary><c>EqsSensor</c> — standing query configuration replicated from Brain to Muscle (EQS v1.3).</summary>
        public const int EqsSensor = 207;

        /// <summary><c>EqsCognitiveBuffer</c> — Brain-side Top-K result cache written by <c>EqsResultUpdateSystem</c> (EQS v1.3).</summary>
        public const int EqsCognitiveBuffer = 208;

        /// <summary><c>EqsResultPool</c> — Muscle-side native ring-buffer pool for packed EQS results (EQS v1.3).</summary>
        public const int EqsResultPool = 209;

        /// <summary><c>IEqsTemplateRegistry</c> — managed singleton registry for compiled EQS query templates (EQS v1.3).</summary>
        public const int IEqsTemplateRegistry = 210;

        /// <summary><c>ICoverProvider</c> — managed singleton cover database for EQS positional queries (EQS v1.3).</summary>
        public const int ICoverProvider = 211;

        /// <summary><c>INavmeshProvider</c> — managed singleton for navmesh queries (EQS v1.3).</summary>
        public const int INavmeshProvider = 212;

        /// <summary>Per-sensor cross-tick evaluation state (EQS v1.3 Phase 5).</summary>
        public const int SensorEvalState = 213;
        /// <summary>Global EQS solver budget singleton (EQS v1.3 Phase 5).</summary>
        public const int EqsSolverGlobalState = 214;
        /// <summary><c>IPathRegistry</c> — managed singleton path cache (NAV-P6).</summary>
        public const int IPathRegistry = 215;

        /// <summary><c>WeaponMountInfo</c> — identifies a weapon mount child entity; carries mount index, weapon GUID, and effective range.</summary>
        public const int WeaponMountInfo = 216;

        // ── Animation subsystem (220–249) ────────────────────────────────────────
        // Animation components: replicated channels, internal executors, queues.
        // DD-Fake §11.1 allocation block.

        /// <summary><c>AnimationChannel</c> – animation playback intent channel (replicable, NoSave).</summary>
        public const int AnimationChannel = 220;

        /// <summary><c>LookAtChannel</c> – aim/look-at targeting overlay (replicable, NoSave).</summary>
        public const int LookAtChannel = 221;

        /// <summary><c>StanceIntent</c> – desired stance transition descriptor (replicable, NoSave).</summary>
        public const int StanceIntent = 222;

        /// <summary><c>StanceStatus</c> – current stance and transition progress (replicable, NoSave).</summary>
        public const int StanceStatus = 223;

        /// <summary><c>AnimationMontageQueue</c> – chained montage sequence buffer (replicable, NoSave).</summary>
        public const int AnimationMontageQueue = 224;

        /// <summary><c>AnimationMontageQueueState</c> – queue playback progress (replicable, NoSave).</summary>
        public const int AnimationMontageQueueState = 225;

        /// <summary><c>LookAtExecutorState</c> – internal look-at execution state (not replicable, NoSave).</summary>
        public const int LookAtExecutorState = 237;

        /// <summary><c>CharacterAnimationDefRuntime</c> – baked animation definition handle (not replicable, NoSave).</summary>
        public const int CharacterAnimationDefRuntime = 238;

        /// <summary><c>AnimationExecutorState</c> – internal animation slot table (not replicable, NoSave).</summary>
        public const int AnimationExecutorState = 239;

        /// <summary><c>FakeAnimBackendState</c> – fake backend per-entity state (not replicable, NoSave). Placeholder for Phase 1.</summary>
        public const int FakeAnimBackendState = 240;

        // IDs 215–219, 226–236, 241–255 are reserved for future animation/toolkit components.

        // ---- Squad coordination components (256–299) ----------------------------

        /// <summary><c>SquadStateMarker</c> — zero-data ECS tag marking an entity whose
        /// <see cref="Blackboard1024"/> is projected as a <c>SquadCognitiveState</c> (Squad P0).</summary>
        public const int SquadStateMarker = 256;

        // NOTE: IDs 257-261 are reserved by NavigationContractsComponentIds (NavAgentProfile, NavigationCorridorMuscle,
        // NavigationCorridorPreview, NavigationPathDetailsBuffer, CrowdAgent). Squad IDs begin at 262.

        /// <summary><c>DangerAreaSensor</c> — standing query config on a sensor child entity
        /// (squad danger-area pipeline, §5.1).</summary>
        public const int DangerAreaSensor = 262;

        /// <summary><c>DangerAreaCognitiveBuffer</c> — Brain-side result cache written by
        /// <c>DangerAreaRefreshSystem</c> (squad danger-area pipeline, §5.2).</summary>
        public const int DangerAreaCognitiveBuffer = 263;

        /// <summary><c>MovementModeIntent</c> — per-member movement mode intent broadcast by the squad (Squad toolkit).</summary>
        public const int MovementModeIntent = 264;
    }
}
