namespace Hrot.Common.Scenario
{
    /// <summary>
    /// Well-known document type identifiers for HROT-owned JSON formats.
    /// Used in the <c>$meta.docType</c> envelope field and in
    /// <see cref="Fdp.Core.Serialization.Migrations.MigrationRegistry"/> registrations.
    ///
    /// <para>
    /// This class is the successor to <see cref="HrotSubsystemTypes"/> for the migration
    /// system. <c>HrotSubsystemTypes</c> remains unchanged for backward compatibility with
    /// callers that rely on it directly (e.g. <c>ScenarioSerializer</c>).
    /// </para>
    /// </summary>
    public static class HrotDocumentTypes
    {
        // ── Versioned customer-authored documents (migration chain registered) ──

        /// <summary>Cross-node, engine-agnostic scenario payload.</summary>
        public const string Scenario      = "Hrot.Scenario";

        /// <summary>Compiled blueprint asset.</summary>
        public const string Blueprint     = "Hrot.Blueprints";

        /// <summary>
        /// Behavior-tree (BTree) persisted asset (*.btree.json).
        /// Added in BATCH-01 of the Persistence Unification thread.
        /// </summary>
        public const string BTree         = "Hrot.BTree";

        /// <summary>
        /// Hierarchical state machine (HSM) persisted asset (*.hsm.json).
        /// Added in BATCH-01 of the Persistence Unification thread.
        /// </summary>
        public const string Hsm           = "Hrot.Hsm";

        /// <summary>
        /// Behavior-tree format constant.
        /// Registered as a passthrough at version 1 in <c>BehaviorTreeMigrationModule</c>.
        /// </summary>
        public const string BehaviorTree  = "Hrot.BehaviorTree";

        /// <summary>TKB entity definition file.</summary>
        public const string TkbDefinition = "Hrot.Tkb";

        // ── Engine-shipped-only formats (passthrough registration only) ──

        /// <summary>StructEdit session state (stable schema; passthrough).</summary>
        public const string StructEdit           = "Hrot.StructEdit";

        /// <summary>ExCon map interaction configuration payload (passthrough).</summary>
        public const string MapInteractionConfig = "Hrot.MapInteractionConfig";

        /// <summary>
        /// Orchestrator global-context file (<c>Orchestrator.json</c>).
        /// Registered at version 2 because existing disk files already carry
        /// <c>schemaVersion: 2</c> (correction C-4).
        /// </summary>
        public const string OrchestratorContext  = "Hrot.OrchestratorContext";

        /// <summary>Automated test script (passthrough).</summary>
        public const string TestScript           = "Hrot.TestScript";

        /// <summary>Node runtime configuration (<c>config.json</c>) (passthrough).</summary>
        public const string NodeConfiguration    = "Hrot.NodeConfiguration";

        // ── Subsystem routing identifiers ──
        // These mirror values from HrotSubsystemTypes and are included here for
        // routing consistency in migration bootstrappers that work with subsystem roles.

        /// <summary>SimHost-authoritative subsystem identifier.</summary>
        public const string SimHostSubsystem = "Hrot.SimHost";

        /// <summary>CGF-authoritative subsystem identifier.</summary>
        public const string CgfSubsystem     = "Hrot.CGF";

        /// <summary>IG-specific subsystem identifier.</summary>
        public const string IgSubsystem      = "Hrot.IG";
    }
}
