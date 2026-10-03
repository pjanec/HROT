using System;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// Marks a field or property whose value is a world coordinate that should
    /// offer a "Pick Map" button in the component editor.
    /// </summary>
    /// <remarks>⭐ An entity reference needs no attribute: a field of type <c>Fdp.Toolkit.Replication.EntityRef</c> gets the
    /// entity picker, and <c>Fdp.Toolkit.Behavior.Attributes.MapPickableEntityAttribute</c> narrows it
    /// (<c>DESIGN_Entity_Reference.md</c> D5 — this file's own copy of that attribute was deleted).</remarks>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MapPickableWorldLocationAttribute : Attribute
    {
    }
}
