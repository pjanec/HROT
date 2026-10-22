using System;
using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — what the 3-D view's sky, sun and air look like this frame: the sky gradient, the
    /// direction TOWARD the sun (HROT, unit), the light the <see cref="LitShader"/> lights with (ambient + sun), how far one can see
    /// (the fog), and the sun disc. <see cref="Default"/> is the free camera's clear, timeless day — the look the map always had. A host
    /// that knows the weather hands <see cref="MapCamera3D.Atmosphere"/> one built from it; this assembly knows no weather.
    /// </summary>
    public readonly record struct SceneAtmosphere(
        Color SkyTop, Color SkyHorizon, Vector3 ToSun, float Ambient, float SunLight, float VisibilityM, Color SunDisc)
    {
        /// <summary>The free camera's look: clear day, sun high from the south-west, the camera-distance haze (no visibility).</summary>
        public static readonly SceneAtmosphere Default = new(
            LitShader.SkyTop, LitShader.SkyHorizon, Vector3.Normalize(new Vector3(-0.45f, -0.6f, 1f)), 0.42f, 0.58f, 0f, default);

        /// <summary>The <see cref="LitShader"/>'s fog constant for a visibility: its fog is <c>exp(−(d·k)²)</c>, and visibility is the
        /// distance at which contrast falls to 5 % (Koschmieder) ⇒ <c>k = √(−ln 0.05) / V</c>.</summary>
        public static float FogFor(float visibilityM) => 1.7308f / MathF.Max(1f, visibilityM);

        /// <summary>True when the sun disc is drawn (a colour was given and the sun is not below the horizon).</summary>
        public bool SunVisible => SunDisc.A > 0 && ToSun.Z > -0.03f;

        /// <summary>⭐ CE-1146 (§6aa.CL) — the cloud layers, drawn as background by <see cref="MapCamera3D.BeginMode"/>
        /// (<see cref="CloudDeckRenderer"/>); null or empty ⇒ a clear sky.</summary>
        public SceneCloudLayer[]? Clouds { get; init; }
    }
}
