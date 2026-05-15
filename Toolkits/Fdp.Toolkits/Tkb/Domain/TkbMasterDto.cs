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
    }
}
