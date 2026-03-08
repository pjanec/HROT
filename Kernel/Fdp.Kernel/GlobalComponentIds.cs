namespace Fdp.Kernel
{
    /// <summary>
    /// Central catalog of globally unique ECS component IDs, allocated in named blocks.
    ///
    /// <para>
    /// Each ID constant is used in conjunction with <see cref="ComponentIdAttribute"/> on
    /// the corresponding component struct, guaranteeing deterministic and collision-free ID
    /// assignment regardless of assembly load order.  This is a prerequisite for merging
    /// SimHost, IG, and IOS into a single Runner process (Phase R0).
    /// </para>
    ///
    /// <para><b>ID block allocation</b></para>
    /// <list type="table">
    ///   <item><term>0–19</term>  <description>Fdp.Kernel core components</description></item>
    ///   <item><term>20–49</term> <description>FDP toolkit expansion: Behavior, Physics, Combat, CarKinem, Geographic</description></item>
    ///   <item><term>50–79</term> <description>FDP.Toolkit.Replication components</description></item>
    ///   <item><term>80–109</term><description>FDP.Toolkit.Vis2D components</description></item>
    ///   <item><term>110–139</term><description>Bagira.IG components</description></item>
    ///   <item><term>140–159</term><description>ModuleHost.Core network components (Cyclone)</description></item>
    ///   <item><term>160–199</term><description>Application-level descriptor components</description></item>
    ///   <item><term>200–255</term><description>Reserved — examples and future use</description></item>
    /// </list>
    ///
    /// <para>
    /// When adding a new component, pick the next unused ID within the appropriate block,
    /// add a constant here, and decorate the struct with
    /// <c>[ComponentId(GlobalComponentIds.YourNewComponent)]</c>.
    /// </para>
    /// </summary>
    public static class GlobalComponentIds
    {
        // ── Fdp.Kernel (0–19) ────────────────────────────────────────────────────
        // Core simulation components present in every FDP application.

        /// <summary><see cref="SimTransform"/> — world position and orientation.</summary>
        public const byte SimTransform        = 0;

        /// <summary><see cref="SimVelocity"/> — linear and angular velocity.</summary>
        public const byte SimVelocity         = 1;

        /// <summary><see cref="HealthData"/> — current / max hit-points mirror.</summary>
        public const byte HealthData          = 2;

        /// <summary><see cref="GlobalTime"/> — simulation time singleton.</summary>
        public const byte GlobalTime          = 3;

        /// <summary><see cref="IsActiveTag"/> — tag marking a fully initialised entity.</summary>
        public const byte IsActiveTag         = 4;

        /// <summary><see cref="LifecycleDescriptor"/> — entity initialisation state machine.</summary>
        public const byte LifecycleDescriptor = 5;

        /// <summary><see cref="HierarchyNode"/> — parent/child linked-list node.</summary>
        public const byte HierarchyNode       = 6;

        /// <summary><see cref="PartDescriptor"/> — bitmask of present component parts (network sync).</summary>
        public const byte PartDescriptor      = 7;

        // IDs 8–19 are reserved for future Fdp.Kernel core components.

        // ── FDP.Toolkit expansion (20–49) ────────────────────────────────────────
        // Toolkit components added after initial release: geographic, behavior, physics.

        /// <summary><c>GeoTransform</c> — geodetic position and orientation (WGS-84).</summary>
        public const byte GeoTransform            = 20;

        /// <summary><c>GeoVelocity</c> — geodetic velocity and acceleration (ENU frame).</summary>
        public const byte GeoVelocity             = 21;

        /// <summary><c>DoctrineState</c> — active doctrine (behavior tree / HSM) for an entity.</summary>
        public const byte DoctrineState           = 22;

        /// <summary><c>BrainBlackboard</c> — shared blackboard memory for behavior execution.</summary>
        public const byte BrainBlackboard         = 23;

        /// <summary><c>LocomotionChannel</c> — active locomotion action slot for behavior control.</summary>
        public const byte LocomotionChannel       = 24;

        /// <summary><c>WeaponChannel</c> — active weapon action slot for behavior control.</summary>
        public const byte WeaponChannel           = 25;

        /// <summary><c>InteractionChannel</c> — active interaction action slot for behavior control.</summary>
        public const byte InteractionChannel      = 26;

        /// <summary><c>PreviousCapabilities</c> — shadow of last-frame actor capability bitmask.</summary>
        public const byte PreviousCapabilities    = 27;

        /// <summary><c>ActorCapabilityState</c> — current actor capability bitmask.</summary>
        public const byte ActorCapabilityState    = 28;

        /// <summary><c>BrainBTreeState</c> — behavior tree runtime state for an entity brain.</summary>
        public const byte BrainBTreeState         = 29;

        /// <summary><c>VehicleState</c> — kinematic vehicle physics state (speed, steer, accel).</summary>
        public const byte VehicleState            = 30;

        /// <summary><c>VehicleParams</c> — static vehicle configuration parameters.</summary>
        public const byte VehicleParams           = 31;

        /// <summary><c>NavState</c> — navigation and locomotion controller state.</summary>
        public const byte NavState                = 32;

        /// <summary><c>FormationRoster</c> — fixed-capacity formation member list (attached to leader).</summary>
        public const byte FormationRoster         = 33;

        /// <summary><c>SimTier</c> — simulation tier level for entity brain prioritization.</summary>
        public const byte SimTier                 = 34;

        /// <summary><c>BrainHsm64</c> — 64-slot HSM runtime state for entity brains.</summary>
        public const byte BrainHsm64              = 35;

        /// <summary><c>BrainHsm128</c> — 128-slot HSM runtime state for entity brains.</summary>
        public const byte BrainHsm128             = 36;

        /// <summary><c>PassengerBuffer</c> — fixed-capacity passenger roster on a vehicle entity.</summary>
        public const byte PassengerBuffer         = 37;

        /// <summary><c>IsEmbarkedTag</c> — tag marking a soldier currently aboard a vehicle.</summary>
        public const byte IsEmbarkedTag           = 38;

        /// <summary><c>MissionPlanQueue</c> — ordered queue of mission phases for the mission director.</summary>
        public const byte MissionPlanQueue        = 39;

        /// <summary><c>PhysicsCollider</c> — bounding-circle collider for broadphase and raycast tests.</summary>
        public const byte PhysicsCollider         = 40;

        /// <summary><c>RaycastBatchData</c> — singleton pre-allocated raycast request/result batch.</summary>
        public const byte RaycastBatchData        = 41;

        /// <summary><c>WeaponState</c> — ammo count and cooldown state of a weapon attachment (Combat toolkit).</summary>
        public const byte WeaponState             = 42;

        /// <summary><c>CombatHealth</c> — hit-point pool for combat entities (Combat toolkit).</summary>
        public const byte CombatHealth            = 43;

        /// <summary><c>BallisticProjectile</c> — marks a bullet entity with shooter reference and sweep data.</summary>
        public const byte BallisticProjectile     = 44;

        /// <summary><c>FormationMember</c> — formation membership data for follower vehicles.</summary>
        public const byte FormationMember         = 45;

        /// <summary><c>FormationTarget</c> — transient scratchpad driving target written by FormationTargetSystem.</summary>
        public const byte FormationTarget         = 46;

        /// <summary><c>SpatialGridData</c> — singleton spatial hash grid produced by SpatialHashSystem.</summary>
        public const byte SpatialGridData         = 47;

        /// <summary><c>GeoPosition</c> — flat-earth 3-D position in the geographic module.</summary>
        public const byte GeoPosition             = 48;

        /// <summary><c>GeoPositionGeodetic</c> — managed WGS-84 geodetic position in the geographic module.</summary>
        public const byte GeoPositionGeodetic     = 49;

        // ── FDP.Toolkit.Replication (50–79) ──────────────────────────────────────
        // Network identity and replication state managed by the Replication toolkit.

        /// <summary><c>NetworkIdentity</c> — globally unique ID across the distributed system.</summary>
        public const byte NetworkIdentity     = 50;

        /// <summary><c>NetworkAuthority</c> — ownership / authority for a networked entity.</summary>
        public const byte NetworkAuthority    = 51;

        /// <summary><c>NetworkTransform</c> — shadow of the last-published or last-received position and orientation (no-record policy).</summary>
        public const byte NetworkTransform    = 52;

        /// <summary><c>NetworkVelocity</c> — replicated velocity (no-record policy).</summary>
        public const byte NetworkVelocity     = 53;

        /// <summary><c>NetworkSpawnRequest</c> — REMOVED (replaced by <c>TkbIdentity</c>). ID 54 is reserved.</summary>
        [System.Obsolete("NetworkSpawnRequest has been removed. Use TkbIdentity instead.")]
        public const byte NetworkSpawnRequest = 54;

        /// <summary><c>PartMetadata</c> — back-reference to parent entity and descriptor ordinal.</summary>
        public const byte PartMetadata        = 55;

        // IDs 56–79 are reserved for future Replication toolkit components.

        /// <summary><c>BinaryGhostStore</c> — REMOVED (ghost data now stored as real ECS components). ID 56 is reserved.</summary>
        [System.Obsolete("BinaryGhostStore has been removed. Ghost entities now accumulate real ECS components directly.")]
        public const byte BinaryGhostStore        = 56;

        /// <summary><c>TkbIdentity</c> — permanent blueprint type identity component replacing <c>NetworkSpawnRequest</c>.</summary>
        public const byte TkbIdentity             = 65;

        /// <summary><c>GhostStateTracker</c> — tracks the birth frame of a ghost entity for promotion and timeout logic.</summary>
        public const byte GhostStateTracker       = 66;

        /// <summary><c>ChildMap</c> — maps sub-entity instance IDs to local ECS entities.</summary>
        public const byte ChildMap                = 57;

        /// <summary><c>EgressPublicationState</c> — smart-egress dirty-tracking state per entity.</summary>
        public const byte EgressPublicationState  = 58;

        /// <summary><c>DescriptorOwnership</c> — per-descriptor ownership map for split-authority.</summary>
        public const byte DescriptorOwnership     = 59;

        /// <summary><c>ITkbDatabase</c> — TKB database singleton injected into ECS world.</summary>
        public const byte ITkbDatabase            = 60;

        /// <summary><c>INetworkTopology</c> — network topology singleton injected into ECS world.</summary>
        public const byte INetworkTopology        = 61;

        /// <summary><c>BlockIdManager</c> — network ID block allocator service singleton.</summary>
        public const byte BlockIdManager          = 62;

        /// <summary><c>ISerializationRegistry</c> — ghost-protocol serialization registry singleton.</summary>
        public const byte ISerializationRegistry  = 63;

        /// <summary><c>NetworkEntityMap</c> — bidirectional map between network IDs and ECS entities.</summary>
        public const byte NetworkEntityMap        = 64;

        // IDs 65–79 are reserved for future Replication toolkit components.

        // ── FDP.Toolkit.Vis2D (80–109) ───────────────────────────────────────────
        // 2-D visualisation and map-layer components.

        /// <summary><c>MapDisplayComponent</c> — layer-mask controlling map visibility.</summary>
        public const byte MapDisplayComponent = 80;

        /// <summary><c>VisHierarchyNode</c> — parent/child relationships for ORGBAT entities.</summary>
        public const byte VisHierarchyNode    = 81;

        /// <summary><c>AggregateState</c> — centroid and bounding box of a logical node's children.</summary>
        public const byte AggregateState      = 82;

        /// <summary><c>AggregateRoot</c> — tag marking root entities in the Vis2D hierarchy.</summary>
        public const byte AggregateRoot       = 83;

        // IDs 84–109 are reserved for future Vis2D toolkit components.

        // ── Bagira.IG (110–139) ──────────────────────────────────────────────────
        // Image Generator ECS components used for rendering and interaction.

        /// <summary><c>ResolvedStyle</c> — computed visual rendering state (texture, tint, label).</summary>
        public const byte ResolvedStyle       = 110;

        /// <summary><c>CullingState</c> — viewport-culling and LOD state written by MapCullingSystem.</summary>
        public const byte CullingState        = 111;

        /// <summary><c>SelectionState</c> — operator selection state written by StandardInteractionTool.</summary>
        public const byte SelectionState      = 112;

        /// <summary><c>VisualEffectState</c> — lifecycle and colour state of a temporary visual effect entity.</summary>
        public const byte VisualEffectState   = 113;

        /// <summary><c>TracerTarget</c> — world-space endpoint of a tracer-line effect.</summary>
        public const byte TracerTarget        = 114;

        // IDs 115–139 are reserved for future Bagira.IG components.

        /// <summary><c>HistoryTrail</c> — circular-buffer of recent world-space positions for trail rendering.</summary>
        public const byte HistoryTrail            = 115;

        /// <summary><c>ContextMenuState</c> — managed component holding active context-menu actions.</summary>
        public const byte ContextMenuState        = 116;

        /// <summary><c>EditablePolyline</c> — managed component storing vertex list of a user-editable overlay.</summary>
        public const byte EditablePolyline        = 117;

        /// <summary><c>VisualData</c> — runtime TKB visual data (symbol, model path, colour) cached on entity.</summary>
        public const byte VisualData              = 118;

        /// <summary><c>IgSymbolOverride</c> — IOS-sourced per-entity visual override (style-set, affiliation).</summary>
        public const byte IgSymbolOverride        = 119;

        /// <summary><c>MapOverlayStyle</c> — rendering style (fill colour, border colour, line thickness) for a map visual overlay.</summary>
        public const byte MapOverlayStyle         = 120;

        /// <summary><c>SimCombatDef</c> — TKB combat definition used by SimHost.</summary>
        public const byte SimCombatDef            = 121;

        /// <summary><c>TkbCompositionDef</c> — TKB composite unit definition.</summary>
        public const byte TkbCompositionDef       = 122;

        // IDs 123–139 are reserved for future Bagira.IG components.

        // ── ModuleHost.Core Network (140–159) ────────────────────────────────────
        // Network ownership and coordination components managed by CycloneNetworkModule.

        /// <summary><c>NetworkOwnership</c> — tracks primary network owner and local node ID.</summary>
        public const byte NetworkOwnership        = 140;

        /// <summary><c>PendingNetworkAck</c> — transient tag for entities awaiting reliable-init acknowledgment.</summary>
        public const byte PendingNetworkAck       = 141;

        /// <summary><c>ForceNetworkPublish</c> — tag forcing immediate descriptor publication, bypassing dirty-check.</summary>
        public const byte ForceNetworkPublish     = 142;

        /// <summary><c>NetworkOrientation</c> — replicated orientation (quaternion) for Cyclone-networked entities.</summary>
        public const byte NetworkOrientation      = 143;

        // IDs 144–159 are reserved for future ModuleHost.Core network components.

        // ── Application-level Descriptors (160–199) ──────────────────────────────
        // DDS-sourced descriptor types repurposed as ECS components in FDP applications.

        /// <summary><c>EntityDamage</c> — DDS damage descriptor stored as an ECS component for IG rendering.</summary>
        public const byte EntityDamage            = 161;

        /// <summary><c>EntityMissionHolder</c> — managed wrapper carrying an <c>EntityMission</c> payload. [Obsolete]</summary>
        public const byte EntityMissionHolder     = 162;

        /// <summary><c>InFormationTag</c> — tag added to an entity that has successfully joined a formation slot.</summary>
        public const byte InFormationTag          = 163;

        /// <summary><c>IgEntityData</c> — IG-internal entity metadata from EntityInfo.</summary>
        public const byte IgEntityData            = 164;

        /// <summary><c>IgHealthState</c> — IG-internal health state derived from EntityDamage.</summary>
        public const byte IgHealthState           = 165;

        // IDs 166–199 are reserved for future application-level components.

        // ── Reserved (200–255) ───────────────────────────────────────────────────
        // IDs 200–255: reserved — future use.
    }
}
