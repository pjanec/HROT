using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Migrators.Scenario;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Migration module for the HROT Scenario format.
    /// Currently at version 2 with a v1&lt;-&gt;v2 migration chain.
    ///
    /// <para>
    /// Registered doc type: <see cref="HrotDocumentTypes.Scenario"/> — version 2.
    /// </para>
    /// <para>
    /// Migration chain:
    /// v1 -&gt; v2: <see cref="V1ToV2_EntityInfo_AddTags"/> (adds Tags field to EntityInfo).
    /// v2 -&gt; v1: <see cref="V2ToV1_EntityInfo_RemoveTags"/> (removes Tags field; lossy).
    /// </para>
    /// </summary>
    public static class ScenarioMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the Scenario format.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Registers the Scenario document type with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null)
                throw new System.ArgumentNullException(nameof(registry));

            registry.RegisterDocType(
                HrotDocumentTypes.Scenario,
                currentVersion: CurrentVersion,
                migrators: new IJsonDocumentMigrator[]
                {
                    new V1ToV2_EntityInfo_AddTags(),
                    new V2ToV1_EntityInfo_RemoveTags(),
                });
        }
    }
}
