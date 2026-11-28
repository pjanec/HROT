using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;

namespace Hrot.AI.Behaviors.Devices.Binoculars
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 D-42 / D-75, §3.1 SD-2′) — the BINOCULARS' state, a
    /// <c>[UnitMemory]</c> struct in the DEVICE entity's blackboard store: the zoom STEP (an index into <see cref="BinocularsZoom.Steps"/>) and the press
    /// tracker. Written ONLY by the device's own logic (<see cref="BinocularsBehavior.Tick"/>); read by its HUD projector and the scenario's <c>field</c> cue.
    /// </summary>
    [UnitMemory]
    [StructLayout(LayoutKind.Sequential)]
    public struct BinocularsState
    {
        /// <summary>The zoom step (index into <see cref="BinocularsZoom.Steps"/>). ⚠ A fresh state is step 0 until the first tick sets the default.</summary>
        public byte Zoom;
        /// <summary>1 once the first tick put the zoom at its default step.</summary>
        public byte Ready;
        /// <summary>The press counters last seen (R7-11 — the one tracker every device keeps).</summary>
        public DevicePressTracker Presses;
    }

    /// <summary>
    /// The binoculars' press channels (<c>DeviceInput</c>). ⚠ Placeholders until the TKB type names them (D-26). The station's device scope puts LMB on
    /// channel 0 and RMB on channel 1 (PossessionStation.DeviceKeys).
    /// </summary>
    public static class BinocularsChannels
    {
        public const int Count = 2;
        /// <summary>ZOOM IN — one step up (clamped at the top).</summary>
        public const int ZoomIn = 0;
        /// <summary>ZOOM OUT — one step down (clamped at the bottom).</summary>
        public const int ZoomOut = 1;
    }

    /// <summary>
    /// The binoculars' zoom steps — ⚠ placeholder constants until a TKB device type carries them (D-26 / R7-4, as S1c's channels): 5× / 7× / 10×, 7× at
    /// the start (the mockup's lit step). The field of view of a step is THE optic rule (<see cref="DeviceOptics.FovDegrees"/>: 10° · 7.1° · 5°).
    /// </summary>
    public static class BinocularsZoom
    {
        private static readonly float[] s_steps = { 5f, 7f, 10f };

        /// <summary>The magnifications, low to high.</summary>
        public static ReadOnlySpan<float> Steps => s_steps;

        /// <summary>The step a new device starts at (7×).</summary>
        public const int DefaultStep = 1;

        /// <summary>The magnification of <paramref name="step"/> (clamped).</summary>
        public static float Magnification(int step) => s_steps[Math.Clamp(step, 0, s_steps.Length - 1)];

        /// <summary>The field of view (degrees) a view bound to the binoculars shows at <paramref name="step"/>.</summary>
        public static float FovDegrees(int step) => DeviceOptics.FovDegrees(Magnification(step));
    }
}
