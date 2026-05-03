using System.Collections.Generic;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Component data extracted from a single entity, keyed by component type name.
    /// </summary>
    public sealed class EntityStateDumpDto
    {
        public long NetworkId { get; init; }
        public int LocalIndex { get; init; }
        public int LocalGeneration { get; init; }

        /// <summary>
        /// Component data, keyed by component type short name.
        /// Values are the raw component structs/classes from the repository.
        /// </summary>
        public Dictionary<string, object> Components { get; init; } = new();
    }

    /// <summary>
    /// Headless service that extracts entity state from the ECS world for diagnostic/dump purposes.
    /// Does not touch any ImGui or Presentation code.
    /// </summary>
    public interface IEntityStateExtractionService
    {
        /// <summary>
        /// Extracts component data for all network-identifiable entities, optionally filtered by
        /// a set of network IDs.
        /// </summary>
        /// <param name="networkIds">
        /// When non-null, only entities whose <c>NetworkIdentity.Value</c> is in this list are
        /// included.  When null, all entities with a NetworkIdentity are included.
        /// </param>
        IReadOnlyList<EntityStateDumpDto> ExtractEntities(IReadOnlyList<long>? networkIds = null);
    }
}
