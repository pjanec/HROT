using Fdp.Core.Serialization.Migrations;

namespace Hrot.Common.Scenario.Migrations
{
    /// <summary>
    /// Registers all engine-internal (passthrough) HROT document formats with a
    /// <see cref="MigrationRegistry"/>. These formats have stable schemas that never
    /// need version migration; only the <c>$meta</c> envelope wraps them.
    ///
    /// <para>
    /// Formats registered:
    /// <list type="bullet">
    ///   <item><see cref="HrotDocumentTypes.StructEdit"/> — version 1</item>
    ///   <item><see cref="HrotDocumentTypes.MapInteractionConfig"/> — version 1</item>
    ///   <item><see cref="HrotDocumentTypes.OrchestratorContext"/> — version 2 (C-4: disk files already at v2)</item>
    ///   <item><see cref="HrotDocumentTypes.TestScript"/> — version 1</item>
    ///   <item><see cref="HrotDocumentTypes.NodeConfiguration"/> — version 1</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class PassthroughFormatsModule
    {
        /// <summary>
        /// Registers all passthrough document types with <paramref name="registry"/>.
        /// </summary>
        /// <param name="registry">The registry to register into. Must not be sealed.</param>
        public static void RegisterAll(MigrationRegistry registry)
        {
            if (registry == null)
                throw new System.ArgumentNullException(nameof(registry));

            registry.RegisterPassthroughDocType(HrotDocumentTypes.StructEdit,           currentVersion: 1);
            registry.RegisterPassthroughDocType(HrotDocumentTypes.MapInteractionConfig,  currentVersion: 1);
            // OrchestratorContext registers at version 2: existing Orchestrator.json files
            // already carry schemaVersion: 2 (correction C-4).
            registry.RegisterPassthroughDocType(HrotDocumentTypes.OrchestratorContext,  currentVersion: 2);
            registry.RegisterPassthroughDocType(HrotDocumentTypes.TestScript,           currentVersion: 1);
            registry.RegisterPassthroughDocType(HrotDocumentTypes.NodeConfiguration,    currentVersion: 1);
        }
    }
}
