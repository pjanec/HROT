using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Map.Common.Components;

/// <summary>⭐ CE-11006 (RR-6) — who is driving the suggested camera.</summary>
public enum CameraSource : byte { Operator = 0, Follow = 1, Director = 2 }

/// <summary>⭐ CE-11006 (RR-6) — how it is placed: the free camera; orbiting a unit; at a unit's eyes; over a unit's shoulder; chasing a unit (the director's
/// follow); a fixed key pose (the director's).</summary>
public enum CameraMode : byte { Free = 0, Orbit = 1, Eyes = 2, Shoulder = 3, Chase = 4, FixedKey = 5 }

/// <summary>
/// ⭐⭐ CE-11006 (RR-6, R-11000; docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md D-1) — THE CAMERA HROT WOULD SHOW NOW on this node, a world
/// SINGLETON written every frame by the map (<c>MapViewModeLayer</c>, the one place every map host passes through) from whichever driver holds its 3-D camera: the
/// film director, the follow camera, the operator's own (possessing or free). A node extension reads it with no Presentation reference and treats it as
/// ADVICE. ⛔ Node-local: never recorded (NoReplay), saved or replicated. Absent on a node with no map.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.CameraSuggestion)]
[DataPolicy(DataPolicy.NoReplay | DataPolicy.NoScenario | DataPolicy.NoPreview)]
public struct CameraSuggestion
{
    public CameraSource Source;
    public CameraMode Mode;
    /// <summary>The ± side of an over-the-shoulder view (+1 right, −1 left; 0 = none).</summary>
    public sbyte ShoulderSide;
    /// <summary>The unit it is bound to (Null = none).</summary>
    public Entity Bound;
    /// <summary>The director's follow target as a network id (0 = none) — it names units by network id.</summary>
    public int BoundNetId;
    /// <summary>The continuum distance (m): the follow / shoulder / chase distance; 0 at a unit's eyes; the free camera's orbit distance.</summary>
    public float Distance;
    /// <summary>The camera's eye, world metres.</summary>
    public Vector3 Position;
    /// <summary>Where it looks at (world metres).</summary>
    public Vector3 LookAt;
    /// <summary>Its orientation: +X its forward, +Y its left, +Z its up (HROT's body convention).</summary>
    public Quaternion Orientation;
    /// <summary>Vertical field of view, degrees.</summary>
    public float FovyDegrees;
    /// <summary>Grows by one at every HARD CUT the director makes (a renderer snaps instead of easing).</summary>
    public int Cut;
    /// <summary>The take / shot on screen (the director's segment id), empty otherwise.</summary>
    public FixedString64 Take;
}
