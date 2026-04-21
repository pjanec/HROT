using System;

namespace Fdp.Toolkit.Behavior.Attributes
{
    /// <summary>
    /// Marks a property of a behavior-param DTO as containing a network entity ID that
    /// must be remapped when a staging scenario is loaded into a live session.
    ///
    /// <para>Only <c>long</c> and <c>int</c> properties are remapped.
    /// For <c>int</c> properties the replacement <c>long</c> value is narrowing-cast to <c>int</c>.</para>
    ///
    /// <para>Marker only — no data members.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class RemapNetworkIdAttribute : Attribute
    {
    }
}
