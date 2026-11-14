using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario.Migrations.Migrators.Scenario;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Migration module for the HROT Scenario format.
    /// Currently at version 3 with a v1&lt;-&gt;v2&lt;-&gt;v3 migration chain.
    ///
    /// <para>
    /// Registered doc type: <see cref="HrotDocumentTypes.Scenario"/> — version 3.
    /// </para>
    /// <para>
    /// Migration chain:
    /// v1 -&gt; v2: <see cref="V1ToV2_EntityInfo_AddTags"/> (adds Tags field to EntityInfo).
    /// v2 -&gt; v1: <see cref="V2ToV1_EntityInfo_RemoveTags"/> (removes Tags field; lossy).
    /// v2 -&gt; v3: <see cref="V2ToV3_RemoveBrainBlackboard"/> (drops the retired component; lossy).
    /// v3 -&gt; v2: <see cref="V3ToV2_RestoreBrainBlackboard"/> (no-op; absence is valid on v2).
    /// </para>
    /// <para>
    /// v3 exists because P4 retired BrainBlackboard without migrating the corpus, which made
    /// every scenario still carrying the key unloadable — ScenarioSerializer throws on a
    /// component name it cannot resolve. See <see cref="V2ToV3_RemoveBrainBlackboard"/>.
    /// </para>
    /// </summary>
    public static class ScenarioMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the Scenario format.</summary>
        public const int CurrentVersion = 3;

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
                    new V2ToV3_RemoveBrainBlackboard(),
                    new V3ToV2_RestoreBrainBlackboard(),
                });
        }
    }
}
