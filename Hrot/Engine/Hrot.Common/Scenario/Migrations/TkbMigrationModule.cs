using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Skeleton migration module for the HROT TKB Definition format.
    /// Currently at version 1 with no migration chain. A migration chain will be
    /// added when the TKB format is bumped to version 2.
    ///
    /// <para>
    /// Registered doc type: <see cref="HrotDocumentTypes.TkbDefinition"/> — version 1.
    /// </para>
    /// </summary>
    public static class TkbMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the TKB Definition format.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Registers the TKB Definition document type with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null)
                throw new System.ArgumentNullException(nameof(registry));

            registry.RegisterPassthroughDocType(HrotDocumentTypes.TkbDefinition, CurrentVersion);
        }
    }
}
