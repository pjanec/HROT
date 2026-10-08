namespace Hrot.IG;

/// <summary>
/// Named constants for IG network/DDS configuration.
/// Changing a value here propagates to all call sites (§CODE-STANDARDS §1).
/// </summary>
public static class IgNetworkConstants
{
    // --- DDS topology ---

    /// <summary>DDS domain that all Hrot simulation nodes use.</summary>
    public const int DdsDomain = 0;

    /// <summary>
    /// IG application instance ID (used in <c>NodeIdMapper</c>).
    /// Must be unique per process in the exercise; IG is assigned 300.
    /// </summary>
    public const int InstanceId = 300;

    /// <summary>
    /// Internal local node ID returned by <c>NodeIdMapper</c> for this IG process.
    /// <c>NodeIdMapper</c> always maps the local instance to internal ID 1.
    /// </summary>
    public const int LocalNodeId = 1;

    /// <summary>
    /// Map-group ID used to scope <c>MapEntitySymbol</c> overrides to this IG instance.
    /// </summary>
    public const int MapGroupId = 1;

    // ⛔ CE-3126 (R-229) — the default exercise-area origin (Berlin) that lived here is gone: nothing read it, and the origin
    //   now comes from the terrain file ("No default berlin. Missing geo = zeros."). 📄 docs/DESIGN_Geo_Origin.md.
}
