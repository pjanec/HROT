using System;

namespace Fdp.Toolkit.Behavior.Attributes
{
    /// <summary>
    /// NARROWS the entity picker of an <see cref="Fdp.Toolkit.Replication.EntityRef"/> field or property to the given
    /// filter presets. ⭐ <c>DESIGN_Entity_Reference.md</c> D5 — the TYPE makes a member pickable (and remapped at scenario
    /// load); this attribute is optional and only filters. It is the ONE such attribute (the Fdp.Presentation copy was
    /// deleted).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class MapPickableEntityAttribute : Attribute
    {
        /// <summary>
        /// Optional filter preset strings to restrict entity picking.
        /// May be <c>null</c> when no filter is required.
        /// </summary>
        public string[]? FilterPresets { get; }

        /// <summary>
        /// Creates a pickable entity marker with optional filter presets.
        /// </summary>
        /// <param name="filterPresets">Variable number of filter preset strings.</param>
        public MapPickableEntityAttribute(params string[] filterPresets)
        {
            FilterPresets = filterPresets?.Length > 0 ? filterPresets : null;
        }
    }
}
