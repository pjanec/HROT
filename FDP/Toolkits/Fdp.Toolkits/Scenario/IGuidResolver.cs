using Fdp.Core;

namespace Fdp.Toolkit.Scenario
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

        /// <summary>
        /// ⭐ <c>CE-4250</c> — save-time resolution that may decline: false when <paramref name="entity"/> is not one this save holds. For a
        /// translator saving a RUNTIME reference (a sensing cache such as target memory) that may name an entity the save leaves out — one a
        /// Preview spawned — which it then drops instead of failing the whole save. ⛔ Authored references keep <see cref="Resolve(Entity)"/>,
        /// which throws: an authored link that does not resolve is a real error. Default: <see cref="Resolve(Entity)"/>, never declining.
        /// </summary>
        bool TryResolve(Entity entity, out string guid)
        {
            guid = Resolve(entity);
            return true;
        }
    }
}
