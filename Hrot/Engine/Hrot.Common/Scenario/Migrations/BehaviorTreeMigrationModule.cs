using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Skeleton migration module for the HROT BehaviorTree format.
    /// Currently at version 1 with no migration chain. A migration chain will be
    /// added in a later phase when the BehaviorTree format is bumped.
    /// <para>Registered doc type: <see cref="HrotDocumentTypes.BehaviorTree"/> -- version 1.</para>
    /// </summary>
    public static class BehaviorTreeMigrationModule
    {
        /// <summary>Current (highest understood) schema version for the BehaviorTree format.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Registers the BehaviorTree document type with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null) throw new System.ArgumentNullException(nameof(registry));
            registry.RegisterPassthroughDocType(HrotDocumentTypes.BehaviorTree, CurrentVersion);
        }
    }
}
