using Fdp.Core;
using Fdp.Toolkit.Combat;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1211</c> (docs/DESIGN_Entity_Control.md §8j Z-1) — the first-person ZOOM while the right button is held: through a scoped mount
/// its own field of view (<c>WeaponMountDto.ScopeFovDegrees</c>, else <see cref="HeldItems.ScopeFallbackFovDegrees"/>), otherwise iron sights
/// (<see cref="IronSightsFovDegrees"/>, ≈ 1.5×); the mouse turns the view slower in proportion, so a pixel covers the same share of the
/// picture. ⭐ Camera only: a possessed man is already Aimed (his operator fire reports Aiming whenever the trigger is up), so the sim's own
/// scope rule (<see cref="HeldItems.ScopeUp"/>, BR-9) already sees through the scope he looks through.
/// </summary>
public static class FpsZoom
{
    /// <summary>The 3-D map's own field of view (degrees) — <c>MapCamera3D.FovyDegrees</c>'s default.</summary>
    public const float NormalFovDegrees = 45f;
    /// <summary>A zoom with no scope: looking down the iron sights (degrees).</summary>
    public const float IronSightsFovDegrees = 30f;

    /// <summary>The zoomed field of view (degrees) for <paramref name="unit"/> aiming with weapon slot <paramref name="slot"/> (an own mount
    /// index, or a controlled weapon system's — its mount 0).</summary>
    public static float FovFor(EntityRepository world, Entity unit, int slot)
    {
        Entity platform = unit; int mount = slot;
        if (WeaponSystems.IsControlledIndex(slot)) { platform = WeaponSystems.ControlledAt(world, unit, slot); mount = 0; }
        if (platform == Entity.Null || !world.IsAlive(platform)) return IronSightsFovDegrees;
        return CombatTkb.MountOf(world, platform, mount) is { ScopeMagnification: > 1f } scoped
            ? (scoped.ScopeFovDegrees > 0f ? scoped.ScopeFovDegrees : HeldItems.ScopeFallbackFovDegrees)
            : IronSightsFovDegrees;
    }

    /// <summary>The look's mouse sensitivity at <paramref name="fovDegrees"/>: the normal one scaled by the field of view.</summary>
    public static float SensitivityFor(float fovDegrees) => FpsCameraDriverSensitivity * fovDegrees / NormalFovDegrees;

    private const float FpsCameraDriverSensitivity = FpsCameraDriver.LookSensitivity;
}
