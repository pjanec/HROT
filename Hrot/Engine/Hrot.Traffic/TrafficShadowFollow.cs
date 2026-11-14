using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ <c>R-3039</c> S1 — a handed-over body's follow of its SUMO shadow (was <c>TrafficShadowFollowSystem._follow</c>): the shadow has been
/// seen, and where and when it was last seen (the speed the body is given is the step between two sightings). On the body, on the node
/// that moves it; recorded, so a checkpoint restored into a fresh process still knows the shadow was seen — 📐 empty, a body whose shadow
/// is between two frames would read "never seen" and hold, and one whose shadow is gone would hold forever instead of being taken back.
/// 📄 docs/DESIGN_System_State_Inventory.md §6 #14.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.TrafficShadowFollow)]
[DataPolicy(DataPolicy.NoScenario)]
public struct TrafficShadowFollow
{
    /// <summary>1 once the shadow has been seen (before: a deliberate hold; after, its absence means it is gone).</summary>
    public byte Seen;
    public float LastX;
    public float LastY;
    /// <summary>The sim time of the last sighting.</summary>
    public double At;
}
