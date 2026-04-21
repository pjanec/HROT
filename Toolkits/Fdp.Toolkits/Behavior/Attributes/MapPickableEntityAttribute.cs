using System;

namespace Fdp.Toolkit.Behavior.Attributes
{
    /// <summary>
    /// Marks an int or long property of a behavior-param DTO as containing a network
    /// entity ID that can be picked interactively (e.g., from the map).
    ///
    /// <para>Optionally accepts filter presets to restrict the entity selection UI
    /// to entities matching certain criteria.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
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
