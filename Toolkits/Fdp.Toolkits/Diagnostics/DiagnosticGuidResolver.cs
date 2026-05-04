using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Diagnostic-mode <see cref="IGuidResolver"/> that formats entity handles as
    /// human-readable strings rather than stable GUIDs.
    ///
    /// <para>
    /// The format is <c>[Index, vGeneration]</c>, e.g. <c>[42, v3]</c>.
    /// Used by the entity inspector clipboard path and the NAS diagnostic dump path
    /// so that entity cross-references in JSON output are readable without a GUID
    /// look-up table.
    /// </para>
    /// </summary>
    public sealed class DiagnosticGuidResolver : IGuidResolver
    {
        /// <inheritdoc/>
        public string Resolve(Entity entity)
            => entity == Entity.Null
                ? "null"
                : $"[{entity.Index}, v{entity.Generation}]";

        /// <summary>
        /// Load-time resolution is not supported in diagnostic mode.
        /// Always returns <see cref="Entity.Null"/>.
        /// </summary>
        public Entity Resolve(string guidStr) => Entity.Null;
    }
}
