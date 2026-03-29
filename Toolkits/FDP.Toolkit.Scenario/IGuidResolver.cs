using Fdp.Kernel;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Translates volatile <see cref="Entity"/> handles to stable persistent GUID strings
    /// (save) and back (load).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ScenarioSerializer</c> builds two concrete implementations of this interface —
    /// one for save (backed by <c>Dictionary&lt;Entity, Guid&gt;</c>) and one for load
    /// (backed by <c>Dictionary&lt;Guid, Entity&gt;</c>). Both are populated during the
    /// first entity-enumeration pass before any <see cref="IEntityScenarioTranslator"/>
    /// or <c>FdpAutoSerializer</c> delegates run.
    /// </para>
    /// </remarks>
    public interface IGuidResolver
    {
        /// <summary>
        /// Save-time resolution: converts a live <see cref="Entity"/> handle to a stable,
        /// persistent GUID string that can be written to the scenario JSON.
        /// </summary>
        string Resolve(Entity entity);

        /// <summary>
        /// Load-time resolution: converts a persistent GUID string (previously written by
        /// <see cref="Resolve(Entity)"/>) to the live <see cref="Entity"/> handle
        /// created during this load pass.
        /// </summary>
        Entity Resolve(string guidStr);
    }
}
