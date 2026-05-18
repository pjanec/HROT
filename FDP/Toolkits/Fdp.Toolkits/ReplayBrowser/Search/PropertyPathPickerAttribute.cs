using System;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Marks a string property as requiring a dynamic property-path dropdown
    /// based on the contextual component or event type selected in the DTO.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class PropertyPathPickerAttribute : Attribute { }
}
