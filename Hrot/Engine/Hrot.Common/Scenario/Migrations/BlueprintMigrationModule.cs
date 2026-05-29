using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Skeleton migration module for the HROT Blueprint format.
    /// Currently at version 1 with no migration chain. A migration chain will be
    /// added in JM-P3-003 when the Blueprint format is bumped to version 2.
    ///
    /// <para>
    /// Registered doc type: <see cref="HrotDocumentTypes.Blueprint"/> — version 1.
    /// </para>
    /// </summary>
    public static class BlueprintMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the Blueprint format.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Registers the Blueprint document type with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null)
                throw new System.ArgumentNullException(nameof(registry));

            registry.RegisterPassthroughDocType(HrotDocumentTypes.Blueprint, CurrentVersion);
        }
    }
}
