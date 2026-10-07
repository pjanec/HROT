using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Mandatory master descriptor present on every TKB entity.
    /// Provides the human-readable name and DIS entity type classification.
    /// </summary>
    [TkbDescriptor("TkbMaster")]
    public record TkbMasterDto
    {
        /// <summary>Human-readable display name for the entity.</summary>
        public string CustomName { get; init; } = string.Empty;

        /// <summary>SISO-REF-010-2015 DIS Entity Type (e.g. 1.1.225.1.1.1.0).</summary>
        [Description("SISO-REF-010-2015 DIS Entity Type (e.g. 1.1.225.1.1.1.0)")]
        public string DisType { get; init; } = string.Empty;

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — true for a type an operator never places by hand (an internal part, a sensor or mount
        /// child, a test fixture): the Add Entity picker leaves it out. 📄 docs/DESIGN_Add_Entity_Picker.md D7.
        /// </summary>
        [Description("Hide this type from the Add Entity picker (internal parts, children, fixtures)")]
        public bool HideFromPalette { get; init; }
    }
}
