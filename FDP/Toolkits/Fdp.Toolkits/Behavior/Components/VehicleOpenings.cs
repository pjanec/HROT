using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>⭐ <c>CE-4670</c> (VA-4) — an opening's decision. Its animation is DERIVED from <see cref="OpeningSlot.Since"/> (VA-1): Opening since t is fully
    /// open at t + the type's open seconds, with no further write; Closing likewise ends closed.</summary>
    public enum OpeningState : byte
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3,
    }

    /// <summary>One opening's state and the sim time (s) it began.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OpeningSlot
    {
        public OpeningState State;
        /// <summary>⭐ VA-4 — a crewman has his head out of this hatch (a request to the arbiter, <c>OpeningRequestEvent</c>): it stays open, and he rides
        /// exposed. Cleared when he goes in or his seat changes hands. ⚠ Sits in what was padding — the slot stays 16 B.</summary>
        public bool CrewExposed;
        public double Since;
    }

    /// <summary>Inline array of <see cref="VehicleOpenings.Capacity"/> slots, in the type's opening order.</summary>
    [InlineArray(VehicleOpenings.Capacity)]
    public struct OpeningSlots
    {
        private OpeningSlot _element;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (VA-S2, VA-1 / VA-4; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — a vehicle's hatches, doors and ramps: per
    /// opening of its type (<c>VehicleOpeningsCatalog</c>, ≤ <see cref="Capacity"/>, in the catalogue's order) its <see cref="OpeningState"/> and <c>Since</c>. ONE
    /// writer: the boarding arbiter — the passenger list's owner (<c>VehicleOpeningsSystem</c>); replicated (<c>dtVehicleOpenings</c> 243), recorded, ⛔ not saved
    /// (a scenario starts closed). A renderer computes the animation's progress with <see cref="Progress"/> — never streamed.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.VehicleOpenings)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct VehicleOpenings
    {
        /// <summary>Openings per vehicle.</summary>
        public const int Capacity = 8;

        /// <summary>Openings in use (the type's count, capped at <see cref="Capacity"/>).</summary>
        public byte Count;

        public OpeningSlots Slots;

        /// <summary>
        /// ⭐ THE one progress rule (every reader — our map, a gateway, ui's <c>VehicleLook.OpeningProgress</c> — calls it): 0 = closed, 1 = fully open, at
        /// <paramref name="now"/>, from the decision and its time and the type's <paramref name="openSeconds"/> (≤ 0 = instant).
        /// </summary>
        public static float Progress(OpeningState state, double since, float openSeconds, double now)
        {
            float t = openSeconds <= 0f ? 1f : (float)Math.Clamp((now - since) / openSeconds, 0.0, 1.0);
            return state switch
            {
                OpeningState.Opening => t,
                OpeningState.Open => 1f,
                OpeningState.Closing => 1f - t,
                _ => 0f,
            };
        }

        /// <summary>Opening <paramref name="i"/>'s progress at <paramref name="now"/> (0 for an index out of range).</summary>
        public readonly float ProgressAt(int i, float openSeconds, double now)
            => (uint)i < Count ? Progress(Slots[i].State, Slots[i].Since, openSeconds, now) : 0f;

        /// <summary>True while opening <paramref name="i"/> is open or on its way open (the arbiter's "is it open for boarding").</summary>
        public readonly bool IsOpenOrOpening(int i) => (uint)i < Count && Slots[i].State is OpeningState.Opening or OpeningState.Open;
    }
}
