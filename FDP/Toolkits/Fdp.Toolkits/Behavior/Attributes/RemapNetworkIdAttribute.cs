using System;

namespace Fdp.Toolkit.Behavior.Attributes
{
    /// <summary>
    /// Marks a property or field of a behavior-param DTO as containing a network entity ID that
    /// must be remapped when a staging scenario is loaded into a live session.
    ///
    /// <para>Only <c>long</c> and <c>int</c> members are remapped.
    /// For <c>int</c> members the replacement <c>long</c> value is narrowing-cast to <c>int</c>.</para>
    ///
    /// <para>Marker only — no data members.</para>
    /// </summary>
    /// <para>⭐ <c>CE-2054</c> — a FIELD too, as <c>docs/designs/cgf-scn/DESIGN.md</c> C005a specified: a generated
    /// behaviour <c>Params</c> struct has fields, not properties.</para>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class RemapNetworkIdAttribute : Attribute
    {
    }
}
