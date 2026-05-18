using System;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// Marks a field or property whose value is an entity reference that should
    /// offer a "Pick Entity" button in the component editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MapPickableEntityAttribute : Attribute
    {
        /// <summary>
        /// Optional filter presets passed to the entity picker to narrow the
        /// selectable set (e.g. "tanks", "infantry").
        /// </summary>
        public string[] FilterPresets { get; }

        public MapPickableEntityAttribute(params string[] filterPresets)
        {
            FilterPresets = filterPresets;
        }
    }

    /// <summary>
    /// Marks a field or property whose value is a world coordinate that should
    /// offer a "Pick Map" button in the component editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MapPickableWorldLocationAttribute : Attribute
    {
    }
}
