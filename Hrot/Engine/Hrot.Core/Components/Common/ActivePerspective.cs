using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.Common;

/// <summary>
/// ECS managed singleton that tracks the currently active UI/world-space perspective.
/// Stored via <c>EntityRepository.SetSingletonManaged&lt;ActivePerspective&gt;()</c>.
/// A class (not struct) because <c>Name</c> is a managed string, which prevents
/// use as an unmanaged ECS singleton.
/// </summary>
[ComponentId(HrotComponentIds.ActivePerspective)]
public sealed class ActivePerspective
{
    /// <summary>Name of the active perspective (e.g. "IG", "SimHost").</summary>
    public string Name { get; set; } = string.Empty;
}
