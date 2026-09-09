using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Migrators.Blueprint;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Migration module for the HROT Blueprint format.
    /// At version 2 with a v1&lt;-&gt;v2 migration chain.
    ///
    /// <para>
    /// Registered doc type: <see cref="HrotDocumentTypes.Blueprint"/> — version 2.
    /// </para>
    /// <para>
    /// Migration chain:
    /// v1 -&gt; v2: <see cref="V1ToV2_Blueprint_UnifyDeclarations"/> (three declaration lists become one
    /// tagged array).
    /// v2 -&gt; v1: <see cref="V2ToV1_Blueprint_SplitDeclarations"/> (splits them back; lossless).
    /// </para>
    /// <para>
    /// NOTE: this class previously said "a migration chain will be added in JM-P3-003". That was a
    /// stale forward-reference — JM-P3-003 shipped, for the Scenario format, and never covered
    /// Blueprint (see .dev/_DONE/json-migration/TASK-DETAILS.md). The chain below is U-10 / D2, and
    /// the seam that made it writable is Architect Question #31.
    /// </para>
    /// <para>
    /// WARNING: RegisterDocType, NOT RegisterPassthroughDocType. MigrationPipeline.MigrateTo returns
    /// from the passthrough arm BEFORE the fromVersion == targetVersion comparison, reporting the file
    /// at its own version; combined with MigrateMode's "already at target" skip, a passthrough
    /// registered at 2 would mean no transform ever runs while CurrentVersion advertises 2 — every v1
    /// file silently never visited.
    /// </para>
    /// </summary>
    public static class BlueprintMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the Blueprint format.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Registers the Blueprint document type with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null)
                throw new System.ArgumentNullException(nameof(registry));

            registry.RegisterDocType(
                HrotDocumentTypes.Blueprint,
                currentVersion: CurrentVersion,
                migrators: new IJsonDocumentMigrator[]
                {
                    new V1ToV2_Blueprint_UnifyDeclarations(),
                    new V2ToV1_Blueprint_SplitDeclarations(),
                });
        }
    }
}
