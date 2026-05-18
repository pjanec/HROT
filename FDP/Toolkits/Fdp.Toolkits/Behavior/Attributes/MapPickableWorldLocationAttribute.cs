using System;

namespace Fdp.Toolkit.Behavior.Attributes
{
    /// <summary>
    /// Marks a numeric property of a behavior-param DTO as containing a geographic
    /// coordinate that can be picked interactively on a map.
    ///
    /// <para>Applied to latitude and longitude fields in behaviors like MoveToLocation.</para>
    /// <para>Marker only -- no data members.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class MapPickableWorldLocationAttribute : Attribute
    {
    }
}
