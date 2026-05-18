using System;

namespace Fdp.Toolkit.Tkb.Attributes
{
    /// <summary>
    /// Marks a property or field as a reference to a weapon entity TKB GUID.
    /// Used by the TKB Editor for picker UI and cross-entity validation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class WeaponRefAttribute : Attribute { }
}
