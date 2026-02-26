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
    ///   <item><term>20–49</term> <description>Reserved for future Fdp.Kernel expansion</description></item>
    ///   <item><term>50–79</term> <description>FDP.Toolkit.Replication components</description></item>
    ///   <item><term>80–109</term><description>FDP.Toolkit.Vis2D components</description></item>
    ///   <item><term>110–139</term><description>Bagira.IG components</description></item>
    ///   <item><term>140–199</term><description>Reserved for future toolkit expansion</description></item>
    ///   <item><term>200–255</term><description>Reserved — future use</description></item>
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

        // ── FDP.Toolkit.Replication (50–79) ──────────────────────────────────────
        // Network identity and replication state managed by the Replication toolkit.

        /// <summary><c>NetworkIdentity</c> — globally unique ID across the distributed system.</summary>
        public const byte NetworkIdentity     = 50;

        /// <summary><c>NetworkAuthority</c> — ownership / authority for a networked entity.</summary>
        public const byte NetworkAuthority    = 51;

        /// <summary><c>NetworkPosition</c> — replicated position (no-record policy).</summary>
        public const byte NetworkPosition     = 52;

        /// <summary><c>NetworkVelocity</c> — replicated velocity (no-record policy).</summary>
        public const byte NetworkVelocity     = 53;

        /// <summary><c>NetworkSpawnRequest</c> — signals that master descriptor has arrived.</summary>
        public const byte NetworkSpawnRequest = 54;

        /// <summary><c>PartMetadata</c> — back-reference to parent entity and descriptor ordinal.</summary>
        public const byte PartMetadata        = 55;

        // IDs 56–79 are reserved for future Replication toolkit components.

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

        // ── Reserved (140–255) ───────────────────────────────────────────────────
        // IDs 140–199: reserved for additional toolkit expansion.
        // IDs 200–255: reserved — future use.
    }
}
