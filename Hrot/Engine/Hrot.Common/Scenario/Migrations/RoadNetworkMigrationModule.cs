using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Skeleton migration module for the FDP Road Network format.
    /// Currently at version 1 with no migration chain. A migration chain will be
    /// added when the Road Network format is bumped to version 2.
    ///
    /// <para>
    /// The doc type constant lives in <see cref="FdpDocumentTypes"/> (not
    /// <see cref="HrotDocumentTypes"/>) because Road Network is an FDP-owned format.
    /// This module is placed in <c>Hrot.Common</c> because it participates in the
    /// HROT-side bootstrap that wires all simulation formats together.
    /// </para>
    ///
    /// <para>
    /// Registered doc type: <see cref="FdpDocumentTypes.RoadNetwork"/> — version 1.
    /// </para>
    /// </summary>
    public static class RoadNetworkMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the Road Network format.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Registers the Road Network document type with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null)
                throw new System.ArgumentNullException(nameof(registry));

            registry.RegisterPassthroughDocType(FdpDocumentTypes.RoadNetwork, CurrentVersion);
        }
    }
}
