using System;

namespace Fdp.Toolkit.Tkb.Attributes
{
    /// <summary>
    /// Marks a property or field as a reference to a 3-D model asset path or GUID.
    /// Used by the TKB Editor for picker UI and cross-entity validation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ModelRefAttribute : Attribute { }
}
