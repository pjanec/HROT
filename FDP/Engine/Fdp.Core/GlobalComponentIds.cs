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

        /// <summary>
        /// ⛔⛔ <b>RESERVED — <c>BrainBlackboard</c> was retired by <c>P4</c> (2026-09-22).</b>
        /// ⚠ <b>Do NOT reuse 23.</b> A stale recording or scenario carrying this id must never bind to
        /// a different component; the same reason 74 (<c>Blackboard1024</c>) stays reserved. 📄 §30.28.
        /// </summary>
        public const int BrainBlackboard_RESERVED = 23;

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

        /// ⛔⛔ <b>RESERVED — <c>BrainBTreeState</c> was retired by <c>O7c</c>-② (2026-09-22).</b>
        /// ⭐ The root tree cursor moved into an occurrence slot so a HOSTED subtree can own its own
        /// — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.
        /// ⚠ <b>The id is BURNED, not freed</b>, as 23, 35 and 74 are.
        public const int BrainBTreeState_RESERVED = 29;

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

        /// ⛔⛔ <b>RESERVED — <c>BrainHsm64</c> was retired by <c>O7c</c>-① (2026-09-22).</b>
        /// 📐 Zero production attach sites; its tick query could never match. 📄
        /// <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.5.
        /// ⚠ <b>The id is BURNED, not freed</b> — same reason as 23 and 74: a stale recording or a
        /// replayed stream must not bind id 35 to a different component.
        /// ⭐ The 64-byte HSM TIER survives; only the ECS wrapper is gone (§9.4).
        public const int BrainHsm64_RESERVED     = 35;

        /// <summary>⛔ <b>RESERVED — was <c>BrainHsm128</c></b>, deleted by <c>O7c</c>-④d (2026-09-23).
        /// The root HSM instance is an occurrence slot keyed by the behaviour hash, sized by
        /// <c>HsmInstanceManager.SelectTier</c> at attach — 64, 128 or 256 bytes — and reached through
        /// <c>RootHsmAccess</c>. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §31.19.
        /// ⚠ <b>The id is BURNED, not freed</b> — same reason as 23, 31, 35 and 74: a stale recording or
        /// a replayed stream must not bind id 36 to a different component.
        /// ⭐ The 128-byte HSM TIER survives; only the ECS wrapper is gone (§9.4).</summary>
        public const int BrainHsm128_RESERVED    = 36;

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

        /// <summary>⛔⛔ <b>RESERVED — id 74 was <c>Blackboard1024</c>, retired by <c>P4</c>-①
        /// (<c>2026-09-22</c>).</b> Its three tenants all moved: AiPrimitive working state to the
        /// Blueprint tier ladder (<c>SLICE2</c>), squad state to its own component (<c>O1</c>), and the
        /// <c>HeavyDtoType</c> overflow path was never adopted.
        /// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §30.13.
        /// <para>⛔ <b>DO NOT REUSE THIS ID.</b> Ids are explicit, so removing the constant drifts
        /// nothing — but a recording or scenario written before the retirement still names 74, and
        /// binding it to a different component would decode those bytes as the wrong type.</para>
        /// </summary>
        public const int Reserved_WasBlackboard1024 = 74;

        /// <summary><c>IGeographicTransform</c> â€” geographic⇄Cartesian coordinate transform service singleton (FDP.Toolkit).</summary>
        public const int IGeographicTransform    = 75;

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

        // RESERVED — id 140 held the retired NetworkOwnership component (CE-281, merged into
        // NetworkAuthority). Kept reserved so the id is never reused; no live component maps to it.
        public const int NetworkOwnership        = 140;

        /// <summary><c>PendingNetworkAck</c> â€” transient tag for entities awaiting reliable-init acknowledgment.</summary>
        public const int PendingNetworkAck       = 141;

        /// <summary><c>ForceNetworkPublish</c> â€” tag forcing immediate descriptor publication, bypassing dirty-check.</summary>
        public const int ForceNetworkPublish     = 142;

        /// <summary><c>NetworkOrientation</c> â€” replicated orientation (quaternion) for Cyclone-networked entities.</summary>
        public const int NetworkOrientation      = 143;

        /// <summary><c>PendingAuthorityGrants</c> â€” transient component caching descriptor ownership intents from a DeferredTakeOwnership message. Stripped once the entity enters Constructing.</summary>
        public const int PendingAuthorityGrants  = 144;

        /// <summary><c>NetworkAckPeerSet</c> â€” managed sibling of PendingNetworkAck carrying the immutable snapshot of peer node ids the creator must collect Active acks from (reliable-init barrier). See DESIGN_Cross_Node_Construction_Barrier.md §3a.3.</summary>
        public const int NetworkAckPeerSet       = 145;

        // ⚠ IDs 146â€“151 are NOT free despite the historical "145â€“159 reserved" note: the Behavior
        // (BehaviorApplicationComponentIds: 146 BTreeTrace, 147 HsmTrace, 148 DebugState) and Utility
        // (UtilityApplicationComponentIds: 149â€“151) subsystems allocate their own component ids from
        // this same shared space. Enumerate real [ComponentId] usage, never trust this comment's range.

        /// <summary><c>ReportLifecycleOnActive</c> â€” transient tag on a reliable remote ghost: publish its lifecycle status when it reaches Active (reliable-init barrier peer side). See DESIGN_Cross_Node_Construction_Barrier.md §3a.2.</summary>
        public const int ReportLifecycleOnActive = 152;

        /// <summary><c>ConstructionResults</c> — CE-290 (C4): the creator-local poll-store singleton (dict keyed by
        /// NetworkId, TTL + evict-on-read) the local requestor polls for a reliable spawn's Success/Failed outcome.
        /// See DESIGN_Cross_Node_Construction_Barrier.md §3b.4.</summary>
        public const int ConstructionResults = 153;

        // ⚠ IDs 154–156 are taken by BehaviorApplicationComponentIds (BehaviorStartRecord, BehaviorOwnedPart,
        // BehaviorFaultLatch). 157–158 are free. Enumerate real [ComponentId] usage before taking one.

        /// <summary><c>OutgoingGrantsPending</c> — creator-local: the descriptors this node granted away at creation
        /// whose takeover it has not seen confirmed yet (the F7 window). The record recompute leaves them alone.
        /// See docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S5.</summary>
        public const int OutgoingGrantsPending = 159;

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

        /// <summary><c>UnitRoster</c> â€” fixed-capacity subordinate list on the commanding entity (AI tier); NoScenario (derived from UnitSubordinate records).</summary>
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

        /// <summary><c>AnimationChannel</c> – animation playback intent channel (replicable, NoScenario).</summary>
        public const int AnimationChannel = 220;

        /// <summary><c>LookAtChannel</c> – aim/look-at targeting overlay (replicable, NoScenario).</summary>
        public const int LookAtChannel = 221;

        /// <summary><c>StanceIntent</c> – desired stance transition descriptor (replicable, NoScenario).</summary>
        public const int StanceIntent = 222;

        /// <summary><c>StanceStatus</c> – current stance and transition progress (replicable, NoScenario).</summary>
        public const int StanceStatus = 223;

        /// <summary><c>AnimationMontageQueue</c> – chained montage sequence buffer (replicable, NoScenario).</summary>
        public const int AnimationMontageQueue = 224;

        /// <summary><c>AnimationMontageQueueState</c> – queue playback progress (replicable, NoScenario).</summary>
        public const int AnimationMontageQueueState = 225;

        /// <summary><c>AnimationChannelStatus</c> – the Muscle's report on the AnimationChannel request (replicable, NoScenario). CE-513 / R-180.</summary>
        public const int AnimationChannelStatus = 226;

        /// <summary><c>LookAtChannelStatus</c> – the Muscle's report on the LookAtChannel request (replicable, NoScenario). CE-513 / R-180.</summary>
        public const int LookAtChannelStatus = 227;

        /// <summary><c>LookAtExecutorState</c> – internal look-at execution state (not replicable, NoScenario).</summary>
        public const int LookAtExecutorState = 237;

        /// <summary><c>CharacterAnimationDefRuntime</c> – baked animation definition handle (not replicable, NoScenario).</summary>
        public const int CharacterAnimationDefRuntime = 238;

        /// <summary><c>AnimationExecutorState</c> – internal animation slot table (not replicable, NoScenario).</summary>
        public const int AnimationExecutorState = 239;

        /// <summary><c>FakeAnimBackendState</c> – fake backend per-entity state (not replicable, NoScenario). Placeholder for Phase 1.</summary>
        public const int FakeAnimBackendState = 240;

        // IDs 215–219, 228–236, 241–255 are reserved for future animation/toolkit components.

        // ---- Squad coordination components (256–299) ----------------------------

        /// <summary><c>SquadStateMarker</c> — ⛔ <b>RETIRED by `O1` (2026-09-20).</b> It marked an entity
        /// whose <c>Blackboard1024</c> was PROJECTED as a <c>SquadCognitiveState</c>; the state is now a
        /// component of its own (<see cref="SquadCognitiveState"/>), so its PRESENCE is the marker.
        /// ⚠ The id stays allocated and is NOT recycled — ids are ABI (`R-44`).</summary>
        public const int SquadStateMarker = 256;

        // NOTE: IDs 257-261 are reserved by NavigationContractsComponentIds (NavAgentProfile, NavigationCorridorMuscle,
        // NavigationCorridorPreview, NavigationPathDetailsBuffer, CrowdAgent). Squad IDs begin at 262.

        /// <summary><c>DangerAreaSensor</c> — standing query config on a sensor child entity
        /// (squad danger-area pipeline, §5.1). ⭐ <c>QA-037</c> / <c>CE-3072</c> B0 (<c>2026-10-06</c>): moved 262 → 271 — 262
        /// is <c>NavFakeIds.FakeNavmeshState</c>.</summary>
        public const int DangerAreaSensor = 271;

        /// <summary><c>DangerAreaCognitiveBuffer</c> — Brain-side result cache written by
        /// <c>DangerAreaRefreshSystem</c> (squad danger-area pipeline, §5.2). ⭐ <c>QA-037</c>: moved 263 → 272 — 263 is
        /// <c>NavFakeIds.FakeCrowdGlobalState</c>.</summary>
        public const int DangerAreaCognitiveBuffer = 272;

        /// <summary><c>MovementModeIntent</c> — per-member movement mode intent broadcast by the squad (Squad toolkit).
        /// ⭐ <c>QA-037</c>: moved 264 → 273 — 264 is <c>NavFakeIds.FakeCrowdAgentState</c>.</summary>
        public const int MovementModeIntent = 273;

        /// <summary><c>SquadCognitiveState</c> — ⭐ <b>the commander's squad state, as its OWN 1024-byte
        /// component</b> (`O1`, 2026-09-20). It used to be a PROJECTION over the commander's
        /// <c>Blackboard1024</c>, which made "has a blackboard" an accidental proxy for "is a commander
        /// with squad state" — the same accidental-filter shape `D1′` retired for blueprint slots.
        /// ⚠ 1024 B is exactly <c>EntityCommandBuffer.MaxComponentSize</c>: it fits, with NO headroom.</summary>
        public const int SquadCognitiveState = 270;

        // 🔴🔴 DO NOT ALLOCATE 262-269 WITHOUT READING THIS. Measured 2026-09-20 while O1 needed an id:
        //   the 256-299 block's comment says Navigation reserves only 257-261, and that is STALE —
        //   NavigationContractsComponentIds now reaches 265 (CrowdMotorIntent) and NavFakeIds occupies
        //   262-269. THREE ids are already allocated TWICE:
        //     262 = DangerAreaSensor (here)          AND FakeNavmeshState      (NavFakeIds)
        //     263 = DangerAreaCognitiveBuffer (here) AND FakeCrowdGlobalState  (NavFakeIds)
        //     264 = MovementModeIntent (here)        AND FakeCrowdAgentState   (NavFakeIds)
        //   ⚠ My first attempt took 265 and collided with CrowdMotorIntent. The tests PASSED IN
        //     ISOLATION and failed only in the full suite, because ComponentTypeRegistry is
        //     process-global — the same shape QA-008 measured. Filed for the backend lane.
        //   ⇒ 270 is clear of the whole contested band. ⛔ An id census must read EVERY *Ids.cs file,
        //     not just this one (R-44: ids are globally unique across all of them).
        //   ✅ FIXED 2026-10-06 (QA-037, CE-3072 B0 — the danger sensor needs both registered in production): the three
        //     squad ids moved to 271 / 272 / 273 (a census of every `const int` 250–349 in FDP/ and Hrot/ found them free).
        //     262–269 now belong to the navigation fakes alone.

        // ---- Terrain / zone loading (300–319) -----------------------------------
        // NOTE: starts at 300 deliberately. The "Zone toolkit (201+)" block above is full (202–216 went
        // to EQS/Blueprint), and 256–299 is the squad block with 257–261 reserved by
        // NavigationContractsComponentIds. A fresh block avoids stepping on either.

        /// <summary><c>TerrainAssetLoadState</c> — NODE-LOCAL marker recording whether this node has the
        /// terrain data for a zone entity resident, and the footprint it was loaded for.
        /// ⛔ Never replicated and never persisted: it is <c>[DataPolicy(NoScenario | NoReplay)]</c>
        /// because it describes THIS NODE's cache, not the entity. Nodes may legitimately disagree.</summary>
        public const int TerrainAssetLoadState = 300;

        /// <summary><c>TerrainDefinition</c> — ECS SINGLETON holding the parsed terrain definition asset
        /// for the terrain named in the scenario header.
        /// ⛔ <c>[DataPolicy(NoScenario | NoReplay)]</c>: it is re-derived from the named asset on every
        /// load, so persisting it would create a second place the truth can live — and singletons ARE
        /// written to a recording unless the policy excludes them.</summary>
        public const int TerrainDefinition = 301;

        /// <summary><c>TerrainWorld</c> — ECS SINGLETON holding the parsed terrain WORLD (prisms, floor slabs,
        /// ramps, surface areas) every derived query reads: navmesh build, movement surface Z, line of sight,
        /// the 2D map. ⛔ <c>[DataPolicy(NoScenario | NoReplay)]</c> for the same reason as
        /// <see cref="TerrainDefinition"/>. 304 is free by a census of every <c>*Ids*.cs</c> and every
        /// <c>[ComponentId(30x)]</c> (only a test uses 310) — <c>R-44</c>.
        /// 📄 docs/DESIGN_Terrain_World.md §3.</summary>
        public const int TerrainWorld = 304;

        /// <summary><c>SensorMount</c> — an entity's per-posture sensor EYE heights (standing / crouched / prone),
        /// projected from the TKB <c>SensorCapabilitiesDto</c>; the 3-D sight line starts there
        /// (🔒 R-182 <i>"sensor height must follow posture"</i>). 305 is free by a census of every
        /// <c>*Ids*.cs</c> and every <c>[ComponentId(30x)]</c> — <c>R-44</c>. 📄 docs/DESIGN_Terrain_World.md §7.1 W5.</summary>
        public const int SensorMount = 305;

        /// <summary><c>BrainInterrupts</c> — ⭐ <b>the entity-fact tail split out of <c>BrainBlackboard</c></b>
        /// by `O2` (2026-09-20): <c>ExpectedThreatLevel</c> and the edge-triggered interrupt registers.
        /// They are PER ENTITY and never per occurrence, so they must stop travelling inside a struct
        /// whose head is per-occurrence behaviour params.
        /// ⚠ Placed at 302 rather than beside <c>BrainBlackboard</c> (23) because the low behaviour block
        /// is dense and the block comments are measurably stale (`QA-037`). 302 is free by a census of
        /// EVERY <c>*Ids*.cs</c>, which is the only census that counts (`R-44`).</summary>
        public const int BrainInterrupts = 302;

        /// <summary><c>BlueprintBlackboard256</c> — ⭐ <b>the SMALLEST occurrence-store tier</b>, added by
        /// <c>O3b</c> / task <c>B4</c> (2026-09-20) to price the simple case: one root occurrence and at
        /// most a couple of stateful slots. 📐 25 of 30 generated behaviours (83 %) fit it.
        /// ⚠ <b>NOT beside its siblings at 204–206, and that is deliberate</b>: the 200–216 range is
        /// fully allocated, so the tier family cannot stay contiguous. 303 is free by a census of EVERY
        /// <c>*Ids*.cs</c>, which is the only census that counts (<c>R-44</c>, and <c>QA-037</c> is why).</summary>
        public const int BlueprintBlackboard256 = 303;
        /// <summary><c>SensorTag</c> — a sensor child's KIND and, for a TKB sensor, its index in the unit's sensor list
        /// (part id <c>1000 + index</c>). 306–308 are free by a census of every <c>*Ids*.cs</c> and every literal
        /// <c>[ComponentId(n)]</c>, <c>2026-10-04</c> (only a test uses 310) — <c>R-44</c>.
        /// 📄 docs/DESIGN_Sensors_And_Doctrine.md §4.</summary>
        public const int SensorTag = 306;

        /// <summary><c>SensorCapability</c> (managed) — a sensor's per-kind parameters (the TKB's own
        /// <c>SensorEntryDto</c>): the default it was built from and the one in force. The sensing tests read it.</summary>
        public const int SensorCapability = 307;

        /// <summary><c>SensorConfigPayload</c> (managed) — on the Brain, the JSON config a sensor's wire sample carries:
        /// a behaviour-made sensor's per-kind config, or an OVERRIDE of a TKB sensor (R-186 M′, R-187 N′).</summary>
        public const int SensorConfigPayload = 308;

        /// <summary><c>Roe</c> — a unit's rules of engagement (<c>CE-2074</c>, R-200). 311–313 are free by a census of every
        /// <c>*Ids*.cs</c> and every literal <c>[ComponentId(n)]</c>, <c>2026-10-04</c> (309 left free; 310 is a test's) — <c>R-44</c>.
        /// 📄 docs/DESIGN_Decision_Layer.md §4.4.</summary>
        public const int Roe = 311;

        /// <summary><c>RecentSenses</c> — the last tick of each <c>SensorChange</c> kind per unit, so a condition can ask
        /// "was hit within N s" (<c>CE-2076</c>). 📄 docs/DESIGN_Decision_Layer.md §4.3.</summary>
        public const int RecentSenses = 312;

        /// <summary><c>SopState</c> — the unit's SOP slot, beside <c>BehaviorState</c> (<c>CE-3035</c>, R-189, R-198).
        /// 📄 docs/DESIGN_Sensors_And_Doctrine.md §6.</summary>
        public const int SopState = 313;

        /// <summary><c>SopStartRecord</c> (managed) — what the SOP slot was started with, for a restart (<c>CE-3035</c>);
        /// 314 is free by the same census.</summary>
        public const int SopStartRecord = 314;

        /// <summary><c>PausedTask</c> (managed) — the task a running reaction paused, restarted when it ends (<c>CE-2078</c>,
        /// R-199); 315 is free by the same census (grep of every <c>*Ids*.cs</c> and literal <c>[ComponentId(315)]</c>, <c>2026-10-04</c>).</summary>
        public const int PausedTask = 315;

        /// <summary><c>InitialBrainIntent</c> (managed, transient) — a unit's AI as the scenario saved it, started through the
        /// ingress at load (<c>CE-3042</c>, R-192); 316 is free by the same census (<c>2026-10-04</c>).</summary>
        public const int InitialBrainIntent = 316;

        /// <summary><c>ReplicatedBrainIntent</c> (managed, transient) — the last AI intent another node published for a unit,
        /// started by the node that gains its Brain (<c>CE-3048</c>, V7); 317 is free by the same census (grep of every
        /// <c>*Ids*.cs</c> and literal <c>[ComponentId(317)]</c>, <c>2026-10-05</c>).</summary>
        public const int ReplicatedBrainIntent = 317;

        /// <summary><c>ThermalState</c> — an entity's heat and thermal signature parameters (<c>CE-3061</c>, R-205). ⚠ 330/331 leave a gap
        /// after 317 on purpose: this table has no lane blocks, and the behaviors lane allocates from 318 up.</summary>
        public const int ThermalState = 330;

        /// <summary><c>AcousticEmitter</c> — how far an entity's sounds carry (<c>CE-3062</c>, R-205).</summary>
        public const int AcousticEmitter = 331;

        /// <summary><c>WorldEpoch</c> — which world this is; bumped at every world boundary (<c>CE-2101</c>).</summary>
        public const int WorldEpoch = 332;

        /// <summary><c>UtilityDecisionLog</c> — what each utility decision of an OBSERVED unit chose, one slot per decision, for
        /// <c>GET /entities/{id}/utility</c> (<c>CE-3069</c> G2). 333 is free by a census of every <c>*Ids*.cs</c> and literal
        /// <c>[ComponentId(333)]</c>, <c>2026-10-05</c> — <c>R-44</c>.</summary>
        public const int UtilityDecisionLog = 333;

        /// <summary><c>MobilityKill</c> — a unit TYPE that a non-lethal hit immobilises, and below what fraction of its HP
        /// (<c>CE-3092</c>). Stamped from the TKB only on types that opt in; absent = only death stops the unit. 334 is free by a
        /// census of every <c>*Ids*.cs</c> and literal <c>[ComponentId(334)]</c> on backend, behaviors and ui, <c>2026-10-06</c>.</summary>
        public const int MobilityKill = 334;

        /// <summary><c>ShotOrdinal</c> — how many rounds a unit has fired, the index of the fixed deflection sequence (<c>AQ85</c> A,
        /// R-216). Muscle-local. 335/336 free by a census of backend, behaviors and ui, <c>2026-10-07</c>.</summary>
        public const int ShotOrdinal = 335;

        /// <summary><c>UnderFire</c> — when a unit was last hit or nearly missed (<c>AQ85</c> E, R-216): suppression spoils its aim.
        /// Muscle-local.</summary>
        public const int UnderFire = 336;

        /// <summary><c>DoorState</c> — a door entity's live state, replicated (buildings Stage 5b, 📄 docs/DESIGN_Building_Interiors.md
        /// §3j). 337/338 free by a census of backend, behaviors and ui, <c>2026-10-07</c> — <c>R-44</c>.</summary>
        public const int DoorState = 337;

        /// <summary><c>TerrainObjectKey</c> — the terrain-provided string key of the object an entity stands for (§3b K2).</summary>
        public const int TerrainObjectKey = 338;

        /// <summary><c>ShotTraces</c> — the last shots as the map draws them, recorded so a replay seek restores them (<c>CE-3117</c>,
        /// R-226, 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a). 339–342 free by a census of backend, behaviors and ui, <c>2026-10-08</c>
        /// — <c>R-44</c>.</summary>
        public const int ShotTraces = 339;

        /// <summary><c>DetonationTraces</c> — the last bursts with their fragment rays (<c>CE-3117</c>).</summary>
        public const int DetonationTraces = 340;

        /// <summary><c>HeardTraces</c> — a listener's last heard estimates (<c>CE-3117</c>).</summary>
        public const int HeardTraces = 341;

        /// <summary><c>PathTrace</c> — a mover's planned path as a polyline (<c>CE-3117</c>).</summary>
        public const int PathTrace = 342;

        /// <summary><c>RoadNetworkHolder</c> — the node's road graph carrier as a managed world singleton, so a background
        /// reader can LEASE the graph (<c>CE-3128</c>: the danger sensor's route and classifier).</summary>
        public const int RoadNetworkHolder = 343;

        /// <summary><c>ActionStatus</c> — why each of a brain unit's action channels is (not) acting, as its executor last saw it
        /// (<c>CE-3136</c>, 📄 docs/DESIGN_Ai_Action_Status_Gizmo.md). 344 is free by a census of backend, behaviors and ui,
        /// <c>2026-10-09</c> — <c>R-44</c>.</summary>
        public const int ActionStatus = 344;

        /// <summary><c>StaticObstacle</c> — marks an entity of a static-obstacle TKB type (a parked car, a sandbag wall …) and names its
        /// wall-library material: it is TERRAIN, baked into each node's world (<c>CE-3136</c> P-7a, R-243). 345/346 free by a census of
        /// backend, behaviors and ui, <c>2026-10-09</c> (the other lanes end at 334) — <c>R-44</c>.</summary>
        public const int StaticObstacle = 345;

        /// <summary><c>ObstacleShape</c> — a static obstacle's box (length × width × height), the TKB's or a per-instance override
        /// (<c>CE-3136</c> P-7a O3).</summary>
        public const int ObstacleShape = 346;

        /// <summary><c>StaticObstacleBakery</c> — a node's terrain-residency handle for the obstacle bake, as a managed world singleton
        /// (<c>CE-3136</c> P-7a, R-243). Absent ⇒ this node holds no terrain, and an obstacle is acked at once.</summary>
        public const int StaticObstacleBakery = 347;
    }
}